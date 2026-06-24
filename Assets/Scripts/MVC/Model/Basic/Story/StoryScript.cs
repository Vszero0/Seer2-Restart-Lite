using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class StoryScript
{
    public List<StoryCommand> commands = new List<StoryCommand>();
    public Dictionary<string, int> labels = new Dictionary<string, int>();

    public int GetLabelIndex(string label)
    {
        return labels.TryGetValue(label, out int index) ? index : -1;
    }
}

public class StoryCommand
{
    public StoryCommandType type;
    public string args;
    public string speaker;
    public string text;
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
    public StoryMissionDocument mission;
    public StoryActorDocument[] actors;
    public StoryNodeDocument[] nodes;

    public bool IsValid => !string.IsNullOrEmpty(id) && mission != null && mission.id < 0 && nodes != null && nodes.Length > 0;

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
        StoryScript script = new StoryScript();
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

[Serializable]
public class StoryMissionDocument
{
    public int id;
    public string title;
    public bool replayable = true;
    public int mapId;
    public string summary;
}

[Serializable]
public class StoryActorDocument
{
    public string id;
    public string name;
    public string sprite;
    public string icon;

    public string displayName => string.IsNullOrEmpty(name) ? id : name;
    public string displaySprite => string.IsNullOrEmpty(sprite) ? icon : sprite;
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
                return command;
            case "show":
                command.type = StoryCommandType.Show;
                command.args = GetActorShowArgs(document);
                return string.IsNullOrEmpty(command.args) ? null : command;
            case "hide":
                command.type = StoryCommandType.Hide;
                command.args = GetActorName(document);
                return command;
            case "say":
                command.type = StoryCommandType.Say;
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
