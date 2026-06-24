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
