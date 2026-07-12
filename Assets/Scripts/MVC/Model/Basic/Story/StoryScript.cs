using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class StoryScript
{
    public List<StoryCommand> commands = new List<StoryCommand>();
    public Dictionary<string, int> labels = new Dictionary<string, int>();
    public Dictionary<string, string> resourceSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<StoryChoiceHistoryEntry> choiceHistory = new List<StoryChoiceHistoryEntry>();
    public StoryLayoutDocument layout;
    public StoryTextStyleDocument textStyle;

    public int GetLabelIndex(string label)
    {
        return labels.TryGetValue(label, out int index) ? index : -1;
    }

    public string GetResourceSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !resourceSources.TryGetValue(path, out string source))
            return "auto";

        return source;
    }
}

public class StoryCommand
{
    public string commandId;
    public StoryCommandType type;
    public string args;
    public string actorId;
    public string choiceId;
    public string speaker;
    public string text;
    public StoryActorDocument actorInfo;
    public StoryLayoutDocument layout;
    public int mapId;
    public string bgmResourcePath;
    public List<StoryChoice> choices = new List<StoryChoice>();
    public ConditionGroupDocument condition;
    public ConditionGroupDocument displayCondition;
}

public class StoryChoiceHistoryEntry
{
    public string pointId;
    public string commandId;
    public string choiceId;
    public string optionId;
}

public class StoryChoice
{
    public string choiceId;
    public string optionId;
    public string text;
    public string label;
}

[Serializable]
public class StoryResourceDefinition
{
    public string path;
    public string kind;
    public string source = "auto";
    public string metadata;
}

[Serializable]
public class StoryActorReferenceDocument
{
    public string actorId;
}

[Serializable]
public class StorySceneDocument
{
    public string id;
    public int mapId;
    public string bgmResourcePath;
    public string[] actorIds;
    public StoryLayoutDocument layout;
}

[Serializable]
public class StoryConditionDocument
{
    public string type;
    public string pointId;
    public string commandId;
    public string choiceId;
    public string optionId;
    public string[] optionSequence;
    public string flag;
    public string value;
    public int missionId;
    public string missionState;
}

[Serializable]
public class ConditionGroupDocument
{
    public string operatorType = "AND";
    public StoryConditionDocument[] conditions;
}

[Serializable]
public class StoryTextStyleDocument
{
    public string font;
    public int fontSize;
    public string textColor;
    public string outlineColor;
    public float outlineWidth;
    public bool bold = false;
}

[Serializable]
public class StoryDocument
{
    public int schemaVersion = 1;
    public string id;
    public string title;
    public string entry = "start";
    public StoryLayoutDocument layout;
    public StoryTextStyleDocument style;
    public StoryMissionDocument mission;
    public bool replayable = true;
    public StoryResourceDefinition[] resourceDefinitions;
    public StoryActorDocument[] actors;
    public StoryNodeDocument[] nodes;

    public bool IsValid => StoryValidator.Validate(this, out _);

    public MissionInfo ToMissionInfo()
    {
        if (!IsValid)
            return null;

        return new MissionInfo
        {
            id = mission.id,
            typeId = (int)MissionType.Mod,
            replayable = replayable && mission.replayable,
            title = string.IsNullOrEmpty(mission.title) ? title : mission.title,
            checkpoints = new List<MissionCheckpoint>
            {
                new MissionCheckpoint
                {
                    id = "default",
                    mapId = mission.mapId,
                    storyId = "mod:" + id,
                    intro = mission.summary,
                }
            },
            rewards = new List<Item>(),
        };
    }

    public StoryScript ToScript()
    {
        StoryScript script = new StoryScript
        {
            layout = layout,
            textStyle = style
        };
        foreach (StoryResourceDefinition resource in resourceDefinitions ?? Array.Empty<StoryResourceDefinition>())
        {
            if (resource == null || string.IsNullOrWhiteSpace(resource.path))
                continue;

            script.resourceSources[resource.path] = string.IsNullOrWhiteSpace(resource.source)
                ? "auto"
                : resource.source.Trim().ToLower();
        }
        if (nodes == null)
            return script;

        foreach (StoryNodeDocument node in GetOrderedNodes())
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;

            script.labels[node.id] = script.commands.Count;
            if (node.commands == null)
                continue;

            for (int commandIndex = 0; commandIndex < node.commands.Length; commandIndex++)
            {
                StoryCommand parsed = node.commands[commandIndex]?.ToCommand(this, node);
                if (parsed != null)
                {
                    if (string.IsNullOrEmpty(parsed.commandId))
                        parsed.commandId = node.id + ":" + commandIndex;
                    script.commands.Add(parsed);
                }
            }
        }

        return script;
    }

    public StoryActorDocument GetActor(string actorId)
    {
        if (string.IsNullOrEmpty(actorId) || actors == null)
            return null;

        return actors.FirstOrDefault(x => x != null && x.id == actorId);
    }

    private IEnumerable<StoryNodeDocument> GetOrderedNodes()
    {
        StoryNodeDocument entryNode = nodes.FirstOrDefault(x => x != null && x.id == entry);
        if (entryNode != null)
            yield return entryNode;

        foreach (StoryNodeDocument node in nodes)
        {
            if (node == null)
                continue;

            if (entryNode != null && node.id == entryNode.id)
                continue;

            yield return node;
        }
    }
}

public static class StoryValidator
{
    private static readonly HashSet<string> commandTypes = new HashSet<string>
    {
        "scene",
        "show",
        "hide",
        "say",
        "narrate",
        "choice",
        "jump",
        "mission",
        "teleport",
        "end",
    };

    private static readonly HashSet<string> resourceKinds = new HashSet<string>
    {
        "sprite",
        "actorSprite",
        "actorIcon",
        "mapBackground",
        "audio",
        "map",
        "ui",
    };

    private static readonly HashSet<string> resourceSources = new HashSet<string>
    {
        "auto",
        "mod",
        "builtin",
    };

    public static bool Validate(StoryDocument document, out string error)
    {
        List<string> errors = new List<string>();

        if (document == null)
        {
            error = "剧情文件为空或不是合法 JSON";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.id))
            errors.Add("story.id 不能为空");

        if (document.mission == null)
        {
            errors.Add("mission 不能为空");
        }
        else
        {
            if (document.mission.id >= 0)
                errors.Add("mission.id 必须是负数，避免和官方任务 ID 冲突");

            if (document.mission.mapId <= 0)
                errors.Add("mission.mapId 必须是有效地图 ID");

            if (string.IsNullOrWhiteSpace(document.mission.title) && string.IsNullOrWhiteSpace(document.title))
                errors.Add("mission.title 或 story.title 至少需要填写一个");
        }

        Dictionary<string, StoryActorDocument> actorDict = ValidateActors(document, errors);
        ValidateResources(document, errors);
        Dictionary<string, StoryNodeDocument> nodeDict = ValidateNodes(document, errors);

        string entry = string.IsNullOrWhiteSpace(document.entry) ? "start" : document.entry;
        if (nodeDict.Count > 0 && !nodeDict.ContainsKey(entry))
            errors.Add("entry 指向的节点不存在：" + entry);

        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            ValidateNodeScenes(node, actorDict, errors);
            ValidateNodeCommands(node, actorDict, nodeDict, errors);
        }

        error = string.Join("\n", errors);
        return errors.Count == 0;
    }

    private static void ValidateNodeScenes(StoryNodeDocument node, Dictionary<string, StoryActorDocument> actorDict, List<string> errors)
    {
        if (node?.scenes == null)
            return;

        HashSet<string> sceneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StorySceneDocument scene in node.scenes)
        {
            if (scene == null)
                continue;

            string location = "node[" + node.id + "].scenes[" + (scene.id ?? string.Empty) + "]";
            if (string.IsNullOrWhiteSpace(scene.id) || !sceneIds.Add(scene.id))
                errors.Add(location + " 存在重复或为空的场景 id");

            if (scene.mapId <= 0)
                errors.Add(location + ".mapId 必须是有效地图 ID");

            ValidateResourcePath("Maps/bg/" + scene.mapId, "mapBackground", "auto", errors, location + ".mapId");

            ValidateResourcePath(scene.bgmResourcePath, "audio", "auto", errors, location + ".bgmResourcePath");
            foreach (string actorId in scene.actorIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(actorId) || !actorDict.ContainsKey(actorId))
                    errors.Add(location + ".actorIds 引用了不存在的角色：" + actorId);
            }
        }
    }

    private static void ValidateResources(StoryDocument document, List<string> errors)
    {
        HashSet<string> resourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryResourceDefinition resource in document.resourceDefinitions ?? Array.Empty<StoryResourceDefinition>())
        {
            if (resource == null)
                continue;

            string path = resource.path?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                errors.Add("resourceDefinitions 中存在 path 为空的资源");
                continue;
            }

            if (!resourcePaths.Add(path))
                errors.Add("resourceDefinitions 存在重复资源路径：" + path);

            if (!string.IsNullOrEmpty(resource.kind) && !resourceKinds.Contains(resource.kind))
                errors.Add("资源 kind 不支持：" + resource.kind + "，路径：" + path);

            string source = string.IsNullOrWhiteSpace(resource.source) ? "auto" : resource.source.Trim().ToLower();
            if (!resourceSources.Contains(source))
                errors.Add("资源 source 不支持：" + resource.source + "，路径：" + path);

            ValidateResourcePath(path, resource.kind, source, errors, "resourceDefinitions[" + path + "]");
        }

    }

    private static void ValidateResourcePath(string path, string kind, string source, List<string> errors, string location)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            return;

        string normalizedPath = path.Replace('\\', '/').TrimStart('/');
        string[] roots = source == "mod"
            ? new[] { Application.persistentDataPath + "/Mod/" }
            : source == "builtin"
                ? new[] { Application.persistentDataPath + "/Resources/" }
                : new[]
                {
                    Application.persistentDataPath + "/Mod/",
                    Application.persistentDataPath + "/Resources/",
                };

        string[] extensions = GetResourceExtensions(kind, normalizedPath);
        bool exists = roots.Any(root => extensions.Any(extension => File.Exists(root + normalizedPath + extension)));
        if (!exists)
            errors.Add(location + " 引用的资源不存在：" + path + "，source=" + source);
    }

    private static string[] GetResourceExtensions(string kind, string path)
    {
        if (Path.HasExtension(path))
            return new[] { string.Empty };

        if (string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase))
            return new[] { ".mp3" };

        if (string.Equals(kind, "actorSprite", StringComparison.OrdinalIgnoreCase))
            return new[] { ".png", ".gif" };

        return new[] { ".png", ".gif" };
    }

    private static Dictionary<string, StoryActorDocument> ValidateActors(StoryDocument document, List<string> errors)
    {
        Dictionary<string, StoryActorDocument> actorDict = new Dictionary<string, StoryActorDocument>();
        foreach (StoryActorDocument actor in document.actors ?? Array.Empty<StoryActorDocument>())
        {
            if (actor == null)
                continue;

            if (string.IsNullOrWhiteSpace(actor.id))
            {
                errors.Add("actors 中存在 id 为空的角色");
                continue;
            }

            if (actorDict.ContainsKey(actor.id))
            {
                errors.Add("actors 存在重复角色 id：" + actor.id);
                continue;
            }

            actorDict[actor.id] = actor;
            ValidateResourcePath(actor.sprite, "actorSprite", "auto", errors, "actors[" + actor.id + "].sprite");
            ValidateResourcePath(actor.icon, "actorIcon", "auto", errors, "actors[" + actor.id + "].icon");
            ValidateResourcePath(actor.battleSprite, "actorSprite", "auto", errors, "actors[" + actor.id + "].battleSprite");
        }

        return actorDict;
    }

    private static Dictionary<string, StoryNodeDocument> ValidateNodes(StoryDocument document, List<string> errors)
    {
        Dictionary<string, StoryNodeDocument> nodeDict = new Dictionary<string, StoryNodeDocument>();
        if (document.nodes == null || document.nodes.Length == 0)
        {
            errors.Add("nodes 不能为空");
            return nodeDict;
        }

        foreach (StoryNodeDocument node in document.nodes)
        {
            if (node == null)
                continue;

            if (string.IsNullOrWhiteSpace(node.id))
            {
                errors.Add("nodes 中存在 id 为空的节点");
                continue;
            }

            if (nodeDict.ContainsKey(node.id))
            {
                errors.Add("nodes 存在重复节点 id：" + node.id);
                continue;
            }

            nodeDict[node.id] = node;
        }

        return nodeDict;
    }

    private static void ValidateNodeCommands(
        StoryNodeDocument node,
        Dictionary<string, StoryActorDocument> actorDict,
        Dictionary<string, StoryNodeDocument> nodeDict,
        List<string> errors)
    {
        if (node == null || node.commands == null)
            return;

        for (int i = 0; i < node.commands.Length; i++)
        {
            StoryCommandDocument command = node.commands[i];
            string location = "node[" + node.id + "].commands[" + i + "]";
            if (command == null)
            {
                errors.Add(location + " 不能为空");
                continue;
            }

            string type = (command.type ?? string.Empty).Trim().ToLower();
            if (string.IsNullOrEmpty(type))
            {
                errors.Add(location + ".type 不能为空");
                continue;
            }

            if (!commandTypes.Contains(type))
            {
                errors.Add(location + ".type 不支持：" + command.type);
                continue;
            }

            switch (type)
            {
                case "scene":
                    if (string.IsNullOrWhiteSpace(command.sceneId)
                        && command.mapId <= 0
                        && string.IsNullOrWhiteSpace(command.bg)
                        && string.IsNullOrWhiteSpace(command.args))
                        errors.Add(location + " 需要 sceneId、mapId、bg 或 args");
                    if (command.mapId < 0)
                        errors.Add(location + ".mapId 不能小于 0");
                    if (command.mapId > 0)
                        ValidateResourcePath("Maps/bg/" + command.mapId, "mapBackground", "auto", errors, location + ".mapId");
                    ValidateResourcePath(command.bgmResourcePath, "audio", "auto", errors, location + ".bgmResourcePath");
                    ValidateConditionGroup(command.condition, errors, location + ".condition");
                    ValidateConditionGroup(command.displayCondition, errors, location + ".displayCondition");
                    break;
                case "show":
                    ValidateActorReference(command, actorDict, errors, location, true);
                    break;
                case "hide":
                    if ((command.actor ?? string.Empty).Trim().ToLower() != "all")
                        ValidateActorReference(command, actorDict, errors, location, false);
                    break;
                case "say":
                    ValidateActorReference(command, actorDict, errors, location, false);
                    if (string.IsNullOrWhiteSpace(command.text))
                        errors.Add(location + ".text 不能为空");
                    break;
                case "narrate":
                    if (string.IsNullOrWhiteSpace(command.text))
                        errors.Add(location + ".text 不能为空");
                    break;
                case "choice":
                    ValidateChoices(command, nodeDict, errors, location);
                    ValidateConditionGroup(command.condition, errors, location + ".condition");
                    break;
                case "jump":
                    ValidateTarget(command.target, nodeDict, errors, location);
                    ValidateConditionGroup(command.condition, errors, location + ".condition");
                    break;
                case "mission":
                    if (string.IsNullOrWhiteSpace(command.args))
                        errors.Add(location + " 需要 args，例如：-10001 complete");
                    break;
                case "teleport":
                    if (!int.TryParse(command.args, out _))
                        errors.Add(location + " 需要数字地图 ID args");
                    break;
            }
        }
    }

    private static void ValidateConditionGroup(ConditionGroupDocument group, List<string> errors, string location)
    {
        if (group == null)
            return;

        string op = string.IsNullOrWhiteSpace(group.operatorType) ? string.Empty : group.operatorType.Trim().ToUpper();
        if (op != "AND" && op != "OR")
            errors.Add(location + ".operatorType 必须是 AND 或 OR");

        foreach (StoryConditionDocument condition in group.conditions ?? Array.Empty<StoryConditionDocument>())
        {
            if (condition == null || string.IsNullOrWhiteSpace(condition.type))
                errors.Add(location + ".conditions 中存在无效条件");
        }

    }

    private static void ValidateActorReference(
        StoryCommandDocument command,
        Dictionary<string, StoryActorDocument> actorDict,
        List<string> errors,
        string location,
        bool requireVisual)
    {
        if (string.IsNullOrWhiteSpace(command.actor))
        {
            errors.Add(location + ".actor 不能为空");
            return;
        }

        if (!actorDict.TryGetValue(command.actor, out StoryActorDocument actor))
        {
            errors.Add(location + ".actor 指向不存在的角色：" + command.actor);
            return;
        }

        if (requireVisual && string.IsNullOrWhiteSpace(actor.displaySprite) && string.IsNullOrWhiteSpace(command.args))
            errors.Add(location + " 的角色缺少 sprite/icon，且命令没有提供 args");
    }

    private static void ValidateChoices(
        StoryCommandDocument command,
        Dictionary<string, StoryNodeDocument> nodeDict,
        List<string> errors,
        string location)
    {
        if (command.choices == null || command.choices.Length == 0)
        {
            errors.Add(location + ".choices 不能为空");
            return;
        }

        for (int i = 0; i < command.choices.Length; i++)
        {
            StoryChoiceDocument choice = command.choices[i];
            string choiceLocation = location + ".choices[" + i + "]";
            if (choice == null)
            {
                errors.Add(choiceLocation + " 不能为空");
                continue;
            }

            if (string.IsNullOrWhiteSpace(choice.text))
                errors.Add(choiceLocation + ".text 不能为空");

            if (!string.IsNullOrWhiteSpace(choice.target))
                ValidateTarget(choice.target, nodeDict, errors, choiceLocation);
        }
    }

    private static void ValidateTarget(
        string target,
        Dictionary<string, StoryNodeDocument> nodeDict,
        List<string> errors,
        string location)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            errors.Add(location + ".target 不能为空");
            return;
        }

        if (!nodeDict.ContainsKey(target))
            errors.Add(location + ".target 指向不存在的节点：" + target);
    }
}

[Serializable]
public class StoryMissionDocument
{
    public int id;
    public string title;
    public bool replayable = true;
    public int mapId;
    public int[] mapIds;
    public string summary;
}

[Serializable]
public class StoryActorDocument
{
    public string id;
    public string actorType = "pet";
    public string petId;
    public string npcId;
    public string name;
    public string sprite;
    public string icon;
    public string battleSprite;
    public string side = "left";
    public int slot;
    public bool faceLeft = true;
    public bool flipIcon = false;
    public float scale = 1f;

    public string displayName => string.IsNullOrEmpty(name) ? id : name;
    public string displaySprite => string.IsNullOrEmpty(sprite) ? icon : sprite;
    public string normalizedSide => string.Equals(side, "right", StringComparison.OrdinalIgnoreCase) ? "right" : "left";
}

[Serializable]
public class StoryLayoutDocument
{
    public float actorSpacing;
    public float actorHeight;
    public float actorBottom;
    public float centerGap;
    public float stackOffset;
}

[Serializable]
public class StoryNodeDocument
{
    public string id;
    public string displayName;
    public StoryActorReferenceDocument[] actorReferences;
    public StorySceneDocument[] scenes;
    public StoryTextStyleDocument style;
    public StoryCommandDocument[] commands;

    public StorySceneDocument GetScene(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId) || scenes == null)
            return null;

        return scenes.FirstOrDefault(x => x != null && string.Equals(x.id, sceneId, StringComparison.OrdinalIgnoreCase));
    }
}

[Serializable]
public class StoryCommandDocument
{
    public string commandId;
    public string type;
    public string choiceId;
    public string sceneId;
    public int mapId;
    public string bgmResourcePath;
    public string bg;
    public string actor;
    public string text;
    public string target;
    public string args;
    public StoryLayoutDocument layout;
    public float actorSpacing;
    public float actorHeight;
    public float actorBottom;
    public float centerGap;
    public float stackOffset;
    public StoryChoiceDocument[] choices;
    public ConditionGroupDocument condition;
    public ConditionGroupDocument displayCondition;

    public StoryCommand ToCommand(StoryDocument document, StoryNodeDocument node = null)
    {
        string commandType = (type ?? string.Empty).Trim().ToLower();
        StoryCommand command = new StoryCommand();
        command.commandId = commandId;
        command.choiceId = choiceId;
        command.condition = condition;
        command.displayCondition = displayCondition;
        switch (commandType)
        {
            case "scene":
                command.type = StoryCommandType.Scene;
                StorySceneDocument scene = node?.GetScene(sceneId);
                command.mapId = scene?.mapId > 0 ? scene.mapId : mapId;
                command.bgmResourcePath = string.IsNullOrEmpty(scene?.bgmResourcePath)
                    ? bgmResourcePath
                    : scene.bgmResourcePath;
                command.args = string.IsNullOrEmpty(bg)
                    ? (command.mapId > 0 ? "Maps/bg/" + command.mapId : args)
                    : bg;
                command.layout = scene?.layout ?? GetSceneLayout();
                return command;
            case "show":
                command.type = StoryCommandType.Show;
                command.actorId = actor;
                command.actorInfo = document?.GetActor(actor);
                command.args = GetActorShowArgs(document);
                return command.actorInfo == null && string.IsNullOrEmpty(command.args) ? null : command;
            case "hide":
                command.type = StoryCommandType.Hide;
                command.actorId = actor;
                command.args = string.IsNullOrEmpty(actor) ? args : actor;
                return command;
            case "say":
                command.type = StoryCommandType.Say;
                command.actorId = actor;
                command.actorInfo = document?.GetActor(actor);
                command.speaker = GetActorName(document);
                command.text = text;
                return command;
            case "narrate":
                command.type = StoryCommandType.Narrate;
                command.text = text;
                return command;
            case "choice":
                command.type = StoryCommandType.Choice;
                command.choices = (choices ?? Array.Empty<StoryChoiceDocument>())
                    .Select(x => x?.ToChoice())
                    .Where(x => x != null)
                    .ToList();
                return command;
            case "jump":
                command.type = StoryCommandType.Jump;
                command.args = target;
                return command;
            case "mission":
                command.type = StoryCommandType.Mission;
                command.args = args;
                return command;
            case "teleport":
                command.type = StoryCommandType.Teleport;
                command.args = args;
                return command;
            case "end":
                command.type = StoryCommandType.End;
                return command;
            default:
                return null;
        }
    }

    private string GetActorName(StoryDocument document)
    {
        StoryActorDocument actorDocument = document?.GetActor(actor);
        return actorDocument == null ? actor : actorDocument.displayName;
    }

    private string GetActorShowArgs(StoryDocument document)
    {
        StoryActorDocument actorDocument = document?.GetActor(actor);
        if (actorDocument == null)
            return args;

        string sprite = actorDocument.displaySprite;
        return string.IsNullOrEmpty(sprite) ? string.Empty : actorDocument.displayName + " " + sprite;
    }

    private StoryLayoutDocument GetSceneLayout()
    {
        if (layout != null)
            return layout;

        if (actorSpacing <= 0f && actorHeight <= 0f && actorBottom <= 0f && centerGap <= 0f && stackOffset <= 0f)
            return null;

        return new StoryLayoutDocument
        {
            actorSpacing = actorSpacing,
            actorHeight = actorHeight,
            actorBottom = actorBottom,
            centerGap = centerGap,
            stackOffset = stackOffset,
        };
    }
}

[Serializable]
public class StoryChoiceDocument
{
    public string choiceId;
    public string optionId;
    public string text;
    public string target;

    public StoryChoice ToChoice()
    {
        return new StoryChoice
        {
            choiceId = choiceId,
            optionId = optionId,
            text = text,
            label = target,
        };
    }
}

public enum StoryCommandType
{
    None,
    Scene,
    Show,
    Hide,
    Say,
    Narrate,
    Choice,
    Jump,
    Mission,
    Teleport,
    End,
}

public static class StoryParser
{
    public static StoryScript Parse(string source)
    {
        StoryScript script = new StoryScript();
        if (string.IsNullOrEmpty(source))
            return script;

        string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                continue;

            if (trimmed.StartsWith("#"))
            {
                string label = trimmed.TrimStart('#').Trim();
                if (!string.IsNullOrEmpty(label))
                    script.labels[label] = script.commands.Count;
                continue;
            }

            if (!trimmed.StartsWith("@"))
                continue;

            StoryCommand command = ParseCommand(trimmed);
            if (command == null)
                continue;

            if ((command.type == StoryCommandType.Say) || (command.type == StoryCommandType.Narrate))
                command.text = ReadTextBlock(lines, ref i);
            else if (command.type == StoryCommandType.Choice)
                command.choices = ReadChoiceBlock(lines, ref i);

            script.commands.Add(command);
        }

        return script;
    }

    private static StoryCommand ParseCommand(string line)
    {
        string commandLine = line.TrimStart('@');
        int spaceIndex = commandLine.IndexOf(' ');
        string commandName = (spaceIndex < 0) ? commandLine : commandLine.Substring(0, spaceIndex);
        string args = (spaceIndex < 0) ? string.Empty : commandLine.Substring(spaceIndex + 1).Trim();

        StoryCommand command = new StoryCommand { args = args };
        switch (commandName.ToLower())
        {
            case "scene":
                command.type = StoryCommandType.Scene;
                break;
            case "show":
                command.type = StoryCommandType.Show;
                break;
            case "hide":
                command.type = StoryCommandType.Hide;
                break;
            case "say":
                command.type = StoryCommandType.Say;
                command.speaker = args;
                break;
            case "narrate":
                command.type = StoryCommandType.Narrate;
                break;
            case "choice":
                command.type = StoryCommandType.Choice;
                break;
            case "jump":
                command.type = StoryCommandType.Jump;
                break;
            case "mission":
                command.type = StoryCommandType.Mission;
                break;
            case "teleport":
                command.type = StoryCommandType.Teleport;
                break;
            case "end":
                command.type = StoryCommandType.End;
                break;
            default:
                return null;
        }

        return command;
    }

    private static string ReadTextBlock(string[] lines, ref int index)
    {
        List<string> block = new List<string>();
        for (int i = index + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (trimmed.StartsWith("@") || trimmed.StartsWith("#"))
                break;

            if (string.IsNullOrEmpty(trimmed))
            {
                block.Add(string.Empty);
                index = i;
                continue;
            }

            if (!char.IsWhiteSpace(line.FirstOrDefault()))
                break;

            block.Add(trimmed);
            index = i;
        }

        return string.Join("\n", block);
    }

    private static List<StoryChoice> ReadChoiceBlock(string[] lines, ref int index)
    {
        List<StoryChoice> choices = new List<StoryChoice>();
        for (int i = index + 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                index = i;
                continue;
            }

            if (trimmed.StartsWith("@") || trimmed.StartsWith("#"))
                break;

            if (!trimmed.StartsWith("-"))
                break;

            string choiceLine = trimmed.TrimStart('-').Trim();
            string[] parts = choiceLine.Split(new[] { "->" }, StringSplitOptions.None);
            choices.Add(new StoryChoice
            {
                text = parts[0].Trim(),
                label = (parts.Length > 1) ? parts[1].Trim() : string.Empty,
            });
            index = i;
        }

        return choices;
    }
}
