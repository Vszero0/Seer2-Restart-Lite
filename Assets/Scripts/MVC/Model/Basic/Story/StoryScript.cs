using System;
using System.Collections.Generic;

public class StoryScript
{
    public List<StoryCommand> commands = new List<StoryCommand>();
    public Dictionary<string, int> labels = new Dictionary<string, int>();
    public Dictionary<string, string> resourceSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<StoryChoiceHistoryEntry> choiceHistory = new List<StoryChoiceHistoryEntry>();
    public List<StoryBattleHistoryEntry> battleHistory = new List<StoryBattleHistoryEntry>();
    public StoryLayoutDocument layout;
    public StoryTextStyleDocument textStyle;
    public string currentPointId;
    public int currentPointVisitStartIndex;

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

    public void BeginPointVisit(string pointId)
    {
        currentPointId = pointId;
        currentPointVisitStartIndex = choiceHistory.Count;
    }
}

public class StoryCommand
{
    public string commandId;
    public string pointId;
    public StoryCommandType type;
    public string args;
    public string actorId;
    public string choiceId;
    public string speaker;
    public string expression;
    public string text;
    public StoryActorDocument actorInfo;
    public StoryActorDocument[] sceneActors;
    public StoryLayoutDocument layout;
    public StorySceneActorLayoutDocument[] actorLayouts;
    public int mapId;
    public string bgmResourcePath;
    public StoryTransitionDocument transition;
    public List<StoryChoice> choices = new List<StoryChoice>();
    public ConditionGroupDocument condition;
    public ConditionGroupDocument displayCondition;
    public StoryBattleReferenceDocument battle;
}

public class StoryChoiceHistoryEntry
{
    public string pointId;
    public string commandId;
    public string choiceId;
    public string optionId;
}

public class StoryBattleHistoryEntry
{
    public string pointId;
    public string commandId;
    public string result;
}

public class StoryChoice
{
    public string choiceId;
    public string optionId;
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
    Battle,
    End,
}
