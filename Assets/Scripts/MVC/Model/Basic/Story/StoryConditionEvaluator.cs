using System;
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

        switch (condition.type.Trim().ToLowerInvariant())
        {
            case "choiceselected":
                return story.choiceHistory.Any(entry =>
                    (string.IsNullOrEmpty(condition.commandId) || entry.commandId == condition.commandId) &&
                    (string.IsNullOrEmpty(condition.choiceId) || entry.choiceId == condition.choiceId) &&
                    (string.IsNullOrEmpty(condition.optionId) || entry.optionId == condition.optionId));
            case "choicesequencematched":
                string[] sequence = condition.optionSequence ?? Array.Empty<string>();
                if (sequence.Length == 0 || story.choiceHistory.Count < sequence.Length)
                    return false;

                int start = story.choiceHistory.Count - sequence.Length;
                return sequence.Select((optionId, index) => optionId == story.choiceHistory[start + index].optionId).All(value => value);
            case "missionstate":
                Mission mission = Mission.Find(condition.missionId);
                if (mission == null)
                    return false;

                return string.Equals(condition.missionState, "complete", StringComparison.OrdinalIgnoreCase)
                    ? mission.isDone
                    : !mission.isDone;
            case "storyflag":
                return false;
            default:
                return false;
        }
    }
}
