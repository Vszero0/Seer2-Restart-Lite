using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 读取运行时状态并判定剧情条件，不依赖任何 Panel 或对话控件。
/// </summary>
public static class StoryConditionEvaluator
{
    public static bool Evaluate(StoryScript story, ConditionGroupDocument group)
    {
        if (group == null)
            return true;

        StoryConditionClauseDocument[] clauses = group.clauses ?? Array.Empty<StoryConditionClauseDocument>();
        if (clauses.Length > 0)
        {
            return clauses.Any(clause => (clause?.conditions ?? Array.Empty<StoryConditionDocument>())
                .All(condition => Evaluate(story, condition)));
        }

        bool useAnd = !string.Equals(group.operatorType, "OR", StringComparison.OrdinalIgnoreCase);
        bool[] results = (group.conditions ?? Array.Empty<StoryConditionDocument>())
            .Select(condition => Evaluate(story, condition))
            .ToArray();

        if (results.Length == 0)
            return true;

        return useAnd ? results.All(value => value) : results.Any(value => value);
    }

    public static bool Evaluate(StoryScript story, StoryConditionDocument condition)
    {
        if (story == null || condition == null || string.IsNullOrWhiteSpace(condition.type))
            return false;

        bool result;
        switch (condition.type.Trim().ToLowerInvariant())
        {
            case "choiceselected":
                result = GetChoiceHistory(story, condition).Any(entry =>
                    (string.IsNullOrEmpty(condition.commandId) || entry.commandId == condition.commandId) &&
                    (string.IsNullOrEmpty(condition.choiceId) || entry.choiceId == condition.choiceId) &&
                    (string.IsNullOrEmpty(condition.optionId) || entry.optionId == condition.optionId));
                break;
            case "choicesequencematched":
                string[] sequence = condition.optionSequence ?? Array.Empty<string>();
                StoryChoiceHistoryEntry[] history = GetChoiceHistory(story, condition).ToArray();
                if (sequence.Length == 0 || history.Length < sequence.Length)
                {
                    result = false;
                    break;
                }

                int start = history.Length - sequence.Length;
                result = sequence.Select((optionId, index) => optionId == history[start + index].optionId).All(value => value);
                break;
            case "missionstate":
                Mission mission = Mission.Find(condition.missionId);
                if (mission == null)
                {
                    result = false;
                    break;
                }

                result = string.Equals(condition.missionState, "complete", StringComparison.OrdinalIgnoreCase)
                    ? mission.isDone
                    : !mission.isDone;
                break;
            case "storyflag":
                result = false;
                break;
            default:
                result = false;
                break;
        }

        return condition.negated ? !result : result;
    }

    private static IEnumerable<StoryChoiceHistoryEntry> GetChoiceHistory(StoryScript story, StoryConditionDocument condition)
    {
        IEnumerable<StoryChoiceHistoryEntry> history = story.choiceHistory;
        string pointId = string.IsNullOrWhiteSpace(condition.pointId) ? story.currentPointId : condition.pointId;
        if (string.Equals(pointId, story.currentPointId, StringComparison.OrdinalIgnoreCase))
            history = history.Skip(Math.Max(0, Math.Min(story.currentPointVisitStartIndex, story.choiceHistory.Count)));

        return string.IsNullOrWhiteSpace(pointId)
            ? history
            : history.Where(entry => string.Equals(entry.pointId, pointId, StringComparison.OrdinalIgnoreCase));
    }
}
