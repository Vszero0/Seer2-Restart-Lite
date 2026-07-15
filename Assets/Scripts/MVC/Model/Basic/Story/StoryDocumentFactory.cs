using System;

/// <summary>
/// 只构造编辑器可继续完善的剧本文档，不负责文件命名或持久化。
/// </summary>
public static class StoryDocumentFactory
{
    public static StoryDocument CreateDraft(string storyId)
    {
        return new StoryDocument
        {
            id = storyId,
            status = "draft",
            title = "未命名剧本",
            summary = string.Empty,
            entry = "point_1",
            replayable = true,
            resourceDefinitions = Array.Empty<StoryResourceDefinition>(),
            actors = Array.Empty<StoryActorDocument>(),
            nodes = new[]
            {
                CreateDraftPoint("point_1"),
            },
        };
    }

    public static StoryNodeDocument CreateDraftPoint(string pointId)
    {
        return new StoryNodeDocument
        {
            id = pointId,
            flowRole = "sequence",
            displayName = "未命名剧情点",
            actorReferences = Array.Empty<StoryActorReferenceDocument>(),
            scenes = Array.Empty<StorySceneDocument>(),
            commands = Array.Empty<StoryCommandDocument>(),
            transitions = Array.Empty<StoryNodeTransitionDocument>(),
        };
    }
}
