using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// 剧情 JSON 与数据模型之间的唯一转换入口。
/// 文件读取位置由调用方决定；这里不涉及 Mod、Resources 或 UI。
/// </summary>
public static class StoryDocumentCodec
{
    public static bool TryDeserialize(string json, bool validate, out StoryDocument document, out string error)
    {
        document = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "剧情 JSON 为空。";
            return false;
        }

        try
        {
            document = JsonUtility.FromJson<StoryDocument>(json);
        }
        catch (Exception exception)
        {
            error = "剧情 JSON 解析失败：" + exception.Message;
            return false;
        }

        if (document == null)
        {
            error = "剧情 JSON 为空或格式错误。";
            return false;
        }

        Normalize(document);

        if (validate && !StoryValidator.Validate(document, out error))
        {
            document = null;
            return false;
        }

        return true;
    }

    public static string Serialize(StoryDocument document, bool prettyPrint = true)
    {
        Normalize(document);
        return JsonUtility.ToJson(document, prettyPrint);
    }

    private static void Normalize(StoryDocument document)
    {
        if (document == null)
            return;

        int sourceVersion = document.schemaVersion;
        document.schemaVersion = Math.Max(5, document.schemaVersion);
        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node != null && string.IsNullOrWhiteSpace(node.flowRole))
                node.flowRole = "sequence";

            NormalizeStableIds(node);

            foreach (StorySceneDocument scene in node?.scenes ?? Array.Empty<StorySceneDocument>())
                NormalizeTransition(scene?.transition, false);

            foreach (StoryNodeTransitionDocument transition in node?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            {
                if (transition != null)
                {
                    transition.targetType = string.IsNullOrWhiteSpace(transition.targetType)
                        ? "node"
                        : transition.targetType.Trim().ToLowerInvariant();
                    if (transition.isEnd)
                        transition.targetNodeId = null;
                }
                NormalizeTransition(transition?.transition, true);
                NormalizeConditionGroup(transition?.condition);
                if (transition != null
                    && transition.isDefault
                    && (transition.condition == null || !transition.condition.hasConditions))
                {
                    transition.condition = null;
                }
            }

            foreach (StoryCommandDocument command in node?.commands ?? Array.Empty<StoryCommandDocument>())
            {
                NormalizeConditionGroup(command?.condition);
                NormalizeConditionGroup(command?.displayCondition);
            }
        }

        if (sourceVersion < 4)
            EnsureAutoEnding(document);
    }

    private static void NormalizeTransition(StoryTransitionDocument transition, bool allowInherit)
    {
        if (transition == null)
            return;

        string normalizedType = transition.normalizedType;
        transition.type = !allowInherit && normalizedType == "inherit" ? "none" : normalizedType;
        transition.duration = transition.normalizedDuration;
    }

    private static void EnsureAutoEnding(StoryDocument document)
    {
        StoryNodeDocument[] nodes = document?.nodes ?? Array.Empty<StoryNodeDocument>();
        if (nodes.Any(node => node != null && node.isEnding))
            return;

        StoryNodeDocument candidate = nodes
            .Where(node => node != null && !node.isBranch && CanBecomeEnding(node))
            .LastOrDefault()
            ?? nodes.LastOrDefault(node => node != null && CanBecomeEnding(node));
        if (candidate == null)
            return;

        candidate.transitions = (candidate.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Where(transition => transition != null && !transition.isDefault)
            .Append(StoryDocumentFactory.CreateAutoEndTransition(candidate.id))
            .ToArray();
    }

    private static bool CanBecomeEnding(StoryNodeDocument node)
    {
        if ((node?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Any(transition => transition != null && !transition.isEnd))
        {
            return false;
        }

        return !(node?.commands ?? Array.Empty<StoryCommandDocument>())
            .Any(command => command != null
                && (string.Equals(command.type, "jump", StringComparison.OrdinalIgnoreCase)
                    || (command.choices ?? Array.Empty<StoryChoiceDocument>())
                        .Any(choice => choice != null && !string.IsNullOrWhiteSpace(choice.target))));
    }

    private static void NormalizeStableIds(StoryNodeDocument node)
    {
        if (node == null)
            return;

        StoryCommandDocument[] commands = node.commands ?? Array.Empty<StoryCommandDocument>();
        var commandIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var choiceIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var optionIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryCommandDocument existingCommand in commands)
        {
            if (!string.IsNullOrWhiteSpace(existingCommand?.commandId))
                commandIds.Add(existingCommand.commandId);
            if (!string.IsNullOrWhiteSpace(existingCommand?.choiceId))
                choiceIds.Add(existingCommand.choiceId);
            foreach (StoryChoiceDocument existingOption in existingCommand?.choices ?? Array.Empty<StoryChoiceDocument>())
            {
                if (!string.IsNullOrWhiteSpace(existingOption?.optionId))
                    optionIds.Add(existingOption.optionId);
            }
        }

        for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
        {
            StoryCommandDocument command = commands[commandIndex];
            if (command == null)
                continue;

            if (string.IsNullOrWhiteSpace(command.commandId))
            {
                string commandId = node.id + ":command:" + (commandIndex + 1);
                int suffix = 2;
                while (!commandIds.Add(commandId))
                    commandId = node.id + ":command:" + (commandIndex + 1) + ":" + suffix++;
                command.commandId = commandId;
            }
            if (!string.Equals(command.type, "choice", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(command.choiceId))
            {
                string choiceId = command.commandId + ":choice";
                int suffix = 2;
                while (!choiceIds.Add(choiceId))
                    choiceId = command.commandId + ":choice:" + suffix++;
                command.choiceId = choiceId;
            }
            StoryChoiceDocument[] options = command.choices ?? Array.Empty<StoryChoiceDocument>();
            for (int optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                StoryChoiceDocument option = options[optionIndex];
                if (option == null)
                    continue;

                option.choiceId = command.choiceId;
                if (string.IsNullOrWhiteSpace(option.optionId))
                {
                    string optionId = command.choiceId + ":" + (optionIndex + 1);
                    int suffix = 2;
                    while (!optionIds.Add(optionId))
                        optionId = command.choiceId + ":" + (optionIndex + 1) + ":" + suffix++;
                    option.optionId = optionId;
                }
            }
        }
    }

    private static void NormalizeConditionGroup(ConditionGroupDocument group)
    {
        if (group == null || (group.clauses != null && group.clauses.Length > 0)
            || group.conditions == null || group.conditions.Length == 0)
        {
            return;
        }

        StoryConditionDocument[] conditions = group.conditions;
        group.clauses = string.Equals(group.operatorType, "OR", StringComparison.OrdinalIgnoreCase)
            ? Array.ConvertAll(conditions, condition => new StoryConditionClauseDocument
            {
                conditions = new[] { condition },
            })
            : new[]
            {
                new StoryConditionClauseDocument { conditions = conditions },
            };
        group.conditions = null;
    }
}
