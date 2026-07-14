using System;
using System.Collections.Generic;
using System.Linq;

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
public class StoryMapReferenceDocument
{
    public int mapId;
}

[Serializable]
public class StorySceneDocument
{
    public string id;
    public int mapId;
    public string bgmResourcePath;
    public StorySceneActorLayoutDocument[] actors;
    public StoryLayoutDocument layout;

    public StorySceneActorLayoutDocument GetActorLayout(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actors == null)
            return null;

        return actors.FirstOrDefault(x => x != null && string.Equals(x.actorId, actorId, StringComparison.OrdinalIgnoreCase));
    }
}

[Serializable]
public class StorySceneActorLayoutDocument
{
    public string actorId;
    public string placementMode = "auto";
    public string side = "left";
    public int order;
    public float scale = 1f;
    public bool faceLeft = true;
    public bool flipIcon;
    public float x;
    public float y;

    public string normalizedPlacementMode => string.Equals(placementMode, "manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "auto";
    public string normalizedSide => string.Equals(side, "right", StringComparison.OrdinalIgnoreCase) ? "right" : "left";
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
    public string status = "published";
    public string id;
    public string title;
    public string summary;
    public string entry = "start";
    public StoryLayoutDocument layout;
    public StoryTextStyleDocument style;
    public StoryMissionDocument mission;
    public bool replayable = true;
    public StoryResourceDefinition[] resourceDefinitions;
    public StoryActorDocument[] actors;
    public StoryNodeDocument[] nodes;

    public bool isDraft => string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase);
    public string normalizedStatus => isDraft ? "draft" : "published";
    public bool HasSupportedStatus => string.IsNullOrWhiteSpace(status)
        || isDraft
        || string.Equals(status, "published", StringComparison.OrdinalIgnoreCase);
    public bool IsValid => StoryValidator.Validate(this, out _);

    public MissionInfo ToMissionInfo()
    {
        if (isDraft || !IsValid)
            return null;

        return new MissionInfo
        {
            id = mission.id,
            typeId = (int)MissionType.Mod,
            replayable = replayable && mission.replayable,
            title = string.IsNullOrEmpty(title) ? mission.title : title,
            checkpoints = new List<MissionCheckpoint>
            {
                new MissionCheckpoint
                {
                    id = "default",
                    mapId = mission.mapId,
                    storyId = "mod:" + id,
                    intro = string.IsNullOrEmpty(summary) ? mission.summary : summary,
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
    public float defaultScale = 1f;
    public bool defaultFaceLeft = true;
    public bool defaultFlipIcon;

    public string displayName => string.IsNullOrEmpty(name) ? id : name;
    public string displaySprite => string.IsNullOrEmpty(sprite) ? icon : sprite;
}

[Serializable]
public class StoryLayoutDocument
{
    public string autoLayoutMode = "invertedV";
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
    public StoryMapReferenceDocument[] mapReferences;
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
                command.mapId = scene != null && scene.mapId != 0 ? scene.mapId : mapId;
                command.bgmResourcePath = string.IsNullOrEmpty(scene?.bgmResourcePath)
                    ? bgmResourcePath
                    : scene.bgmResourcePath;
                command.args = string.IsNullOrEmpty(bg)
                    ? (command.mapId != 0 ? "Maps/bg/" + command.mapId : args)
                    : bg;
                command.layout = scene?.layout ?? GetSceneLayout();
                command.actorLayouts = scene?.actors;
                command.sceneActors = (scene?.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
                    .Where(layout => layout != null && !string.IsNullOrWhiteSpace(layout.actorId))
                    .Select(layout => document?.GetActor(layout.actorId))
                    .Where(actorDocument => actorDocument != null)
                    .ToArray();
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
                command.actorId = actor;
                command.actorInfo = document?.GetActor(actor);
                command.speaker = GetActorName(document);
                command.text = text;
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
