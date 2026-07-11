using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class StoryScript
{
    public List<StoryCommand> commands = new List<StoryCommand>();
    public Dictionary<string, int> labels = new Dictionary<string, int>();
    public StoryLayoutDocument layout;

    public int GetLabelIndex(string label)
    {
        return labels.TryGetValue(label, out int index) ? index : -1;
    }
}

public class StoryCommand
{
    public StoryCommandType type;
    public string args;
    public string actorId;
    public string speaker;
    public string text;
    public StoryActorDocument actorInfo;
    public StoryLayoutDocument layout;
    public List<StoryChoice> choices = new List<StoryChoice>();
}

public class StoryChoice
{
    public string text;
    public string label;
}

[Serializable]
public class StoryDocument
{
    public int schemaVersion = 1;
    public string id;
    public string title;
    public string entry = "start";
    public StoryLayoutDocument layout;
    public StoryMissionDocument mission;
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
            replayable = mission.replayable,
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
            layout = layout
        };
        if (nodes == null)
            return script;

        foreach (StoryNodeDocument node in GetOrderedNodes())
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;

            script.labels[node.id] = script.commands.Count;
            if (node.commands == null)
                continue;

            foreach (StoryCommandDocument command in node.commands)
            {
                StoryCommand parsed = command?.ToCommand(this);
                if (parsed != null)
                    script.commands.Add(parsed);
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
        Dictionary<string, StoryNodeDocument> nodeDict = ValidateNodes(document, errors);

        string entry = string.IsNullOrWhiteSpace(document.entry) ? "start" : document.entry;
        if (nodeDict.Count > 0 && !nodeDict.ContainsKey(entry))
            errors.Add("entry 指向的节点不存在：" + entry);

        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
            ValidateNodeCommands(node, actorDict, nodeDict, errors);

        error = string.Join("\n", errors);
        return errors.Count == 0;
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
                    if (string.IsNullOrWhiteSpace(command.bg) && string.IsNullOrWhiteSpace(command.args))
                        errors.Add(location + " 需要 bg 或 args");
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
                    break;
                case "jump":
                    ValidateTarget(command.target, nodeDict, errors, location);
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
    public string name;
    public string sprite;
    public string icon;
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
    public StoryCommandDocument[] commands;
}

[Serializable]
public class StoryCommandDocument
{
    public string type;
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

    public StoryCommand ToCommand(StoryDocument document)
    {
        string commandType = (type ?? string.Empty).Trim().ToLower();
        StoryCommand command = new StoryCommand();
        switch (commandType)
        {
            case "scene":
                command.type = StoryCommandType.Scene;
                command.args = string.IsNullOrEmpty(bg) ? args : bg;
                command.layout = GetSceneLayout();
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
    public string text;
    public string target;

    public StoryChoice ToChoice()
    {
        return new StoryChoice
        {
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
