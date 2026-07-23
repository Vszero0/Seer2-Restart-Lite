using System;
using System.Collections.Generic;

public sealed class WorkshopStoryBrowserController
{
    private readonly WorkshopStoryBrowserModel model;

    public IReadOnlyList<WorkshopStorySummary> Stories => model.Stories;
    public WorkshopStorySummary SelectedStory => model.SelectedStory;
    public StoryDocument SelectedDocument => model.SelectedDocument;
    public StoryNodeDocument SelectedNode => model.SelectedNode;
    public bool HasUnsavedChanges => model.HasUnsavedChanges;
    public bool CanExportSource => model.CanExportSource;

    public WorkshopStoryBrowserController(WorkshopStoryBrowserModel model)
    {
        this.model = model;
    }

    public bool Open(out string error)
    {
        return model.Reload(out error);
    }

    public bool SelectStory(string path, out string error)
    {
        return model.SelectStory(path, out error);
    }

    public void SelectNode(string nodeId)
    {
        model.SelectNode(nodeId);
    }

    public bool UpdateSelectedStoryMetadata(string title, string summary, bool replayable, out string error)
    {
        return model.UpdateSelectedStoryMetadata(title, summary, replayable, out error);
    }

    public IReadOnlyList<StoryActorDocument> GetStoryActors()
    {
        return model.GetStoryActors();
    }

    public IReadOnlyList<StorySceneResourceDocument> GetStoryScenes() => model.GetStoryScenes();

    public bool CreateStoryScene(out StorySceneResourceDocument scene, out string error)
        => model.CreateStoryScene(out scene, out error);

    public bool UpdateStoryScene(string sceneId, string name, out string error)
        => model.UpdateStoryScene(sceneId, name, out error);

    public bool ImportStorySceneBackground(string sceneId, string sourcePath, out string error)
        => model.ImportStorySceneBackground(sceneId, sourcePath, out error);

    public bool ImportStorySceneBgm(string sceneId, string sourcePath, out string error)
        => model.ImportStorySceneBgm(sceneId, sourcePath, out error);

    public bool ClearStorySceneBgm(string sceneId, out string error)
        => model.ClearStorySceneBgm(sceneId, out error);

    public bool DeleteStoryScene(string sceneId, out string error)
        => model.DeleteStoryScene(sceneId, out error);

    public bool CreateNpcActor(out StoryActorDocument actor, out string error)
    {
        return model.CreateNpcActor(out actor, out error);
    }

    public bool UpdateNpcActor(string actorId, string name, string sourceFacing, bool usePortraitIcon, out string error)
    {
        return model.UpdateNpcActor(actorId, name, sourceFacing, usePortraitIcon, out error);
    }

    public bool SetNpcActorImage(string actorId, string resourcePath, bool isIcon, out string error)
    {
        return model.SetNpcActorImage(actorId, resourcePath, isIcon, out error);
    }

    public bool ImportNpcActorImage(string actorId, string sourcePath, bool isIcon, out string error)
    {
        return model.ImportNpcActorImage(actorId, sourcePath, isIcon, out error);
    }

    public bool AdjustNpcActorCrop(string actorId, float moveX, float moveY, float zoomDelta, out string error)
    {
        return model.AdjustNpcActorCrop(actorId, moveX, moveY, zoomDelta, out error);
    }

    public bool DeleteNpcActor(string actorId, out string error)
    {
        return model.DeleteNpcActor(actorId, out error);
    }

    public bool CreateNode(out string error)
    {
        return model.CreateNode(out error);
    }

    public bool RenameSelectedNode(string displayName, out string error)
    {
        return model.RenameSelectedNode(displayName, out error);
    }

    public bool SetSelectedNodeAsEntry(out string error)
    {
        return model.SetSelectedNodeAsEntry(out error);
    }

    public bool DeleteSelectedNode(out string error)
    {
        return model.DeleteSelectedNode(out error);
    }

    public bool CopySelectedNode(out string error)
    {
        return model.CopySelectedNode(out error);
    }

    public bool AddSelectedNodeDefaultTransition(out string transitionId, out string error)
    {
        return model.AddSelectedNodeDefaultTransition(out transitionId, out error);
    }

    public bool AddSelectedNodeChoiceTransition(out string transitionId, out string error)
    {
        return model.AddSelectedNodeChoiceTransition(out transitionId, out error);
    }

    public bool UpdateSelectedNodeTransitionTarget(string transitionId, string targetNodeId, out string error)
    {
        return model.UpdateSelectedNodeTransitionTarget(transitionId, targetNodeId, out error);
    }

    public bool UpdateSelectedNodeTransitionTarget(
        string transitionId,
        string targetType,
        string targetNodeId,
        out string error)
    {
        return model.UpdateSelectedNodeTransitionTarget(transitionId, targetType, targetNodeId, out error);
    }

    public bool UpdateSelectedNodeEndTeleport(string transitionId, int mapId, out string error)
    {
        return model.UpdateSelectedNodeEndTeleport(transitionId, mapId, out error);
    }

    public bool UpdateSelectedNodeTransitionChoice(string transitionId, string commandId, string choiceId, string optionId, out string error)
    {
        return model.UpdateSelectedNodeTransitionChoice(transitionId, commandId, choiceId, optionId, out error);
    }

    public bool AddSelectedNodeTransitionCondition(string transitionId, string connector, out string error)
    {
        return model.AddSelectedNodeTransitionCondition(transitionId, connector, out error);
    }

    public bool UpdateSelectedNodeTransitionCondition(
        string transitionId,
        int clauseIndex,
        int conditionIndex,
        string commandId,
        string choiceId,
        string optionId,
        out string error)
    {
        return model.UpdateSelectedNodeTransitionCondition(
            transitionId, clauseIndex, conditionIndex, commandId, choiceId, optionId, out error);
    }

    public bool ToggleSelectedNodeTransitionConditionNegated(
        string transitionId,
        int clauseIndex,
        int conditionIndex,
        out string error)
    {
        return model.ToggleSelectedNodeTransitionConditionNegated(transitionId, clauseIndex, conditionIndex, out error);
    }

    public bool RemoveSelectedNodeTransitionCondition(
        string transitionId,
        int clauseIndex,
        int conditionIndex,
        out string error)
    {
        return model.RemoveSelectedNodeTransitionCondition(transitionId, clauseIndex, conditionIndex, out error);
    }

    public string GetSelectedNodeDefaultFlowTarget()
    {
        return model.GetSelectedNodeDefaultFlowTarget();
    }

    public bool MoveSelectedNodeTransition(string transitionId, bool moveDown, out string error)
    {
        return model.MoveSelectedNodeTransition(transitionId, moveDown, out error);
    }

    public bool RemoveSelectedNodeTransition(string transitionId, out string error)
    {
        return model.RemoveSelectedNodeTransition(transitionId, out error);
    }

    public IReadOnlyList<WorkshopStoryChoiceOption> GetSelectedNodeChoiceOptions()
    {
        return model.SelectedNodeChoiceOptions;
    }

    public bool CreateDraft(out string error)
    {
        return model.CreateDraft(out error);
    }

    public bool CopySelectedStory(out string error)
    {
        return model.CopySelectedStory(out error);
    }

    public bool SaveSelected(out string error)
    {
        return model.SaveSelected(out error);
    }

    public bool SaveSelectedForRuntime(out bool runtimeReady, out string message)
    {
        return model.SaveSelectedForRuntime(out runtimeReady, out message);
    }

    public IReadOnlyList<WorkshopStorySourceRewardOption> GetSourceRewardOptions(string filter)
    {
        return model.GetSourceRewardOptions(filter);
    }

    public bool ExportSelectedToSource(
        WorkshopStorySourceExportRequest request,
        out WorkshopStorySourceExportResult result,
        out string error)
    {
        return model.ExportSelectedToSource(request, out result, out error);
    }

    public bool DeleteSelected(out string error)
    {
        return model.DeleteSelected(out error);
    }
}
