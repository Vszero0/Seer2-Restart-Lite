using System.Collections.Generic;

/// <summary>
/// 剧情点可视化编辑器的控制器。
/// 入口页只传递剧本路径和剧情点 ID，不持有编辑器的临时草稿状态。
/// </summary>
public sealed class WorkshopStoryNodeEditorController
{
    private readonly WorkshopStoryNodeEditorModel model;

    public StoryDocument DraftDocument => model.DraftDocument;
    public StoryNodeDocument DraftNode => model.DraftNode;
    public bool HasUnsavedChanges => model.HasUnsavedChanges;

    public WorkshopStoryNodeEditorController(WorkshopStoryNodeEditorModel model)
    {
        this.model = model;
    }

    public bool Open(string storyPath, string nodeId, out string error)
    {
        return model.Open(storyPath, nodeId, out error);
    }

    public string GetResourceSource(string resourcePath)
    {
        return model.GetResourceSource(resourcePath);
    }

    public bool RenameNode(string displayName, out string error)
    {
        return model.RenameNode(displayName, out error);
    }

    public bool UpdateCommandText(string commandId, string text, out string error)
    {
        return model.UpdateCommandText(commandId, text, out error);
    }

    public bool EnsureNarrationCommand(out StoryCommandDocument command, out string error)
    {
        return model.EnsureNarrationCommand(out command, out error);
    }

    public List<WorkshopStoryPointResourceOption> GetMapOptions(string filter)
    {
        return model.GetMapOptions(filter);
    }

    public List<WorkshopStoryPointResourceOption> GetSelectedMapOptions()
    {
        return model.GetSelectedMapOptions();
    }

    public List<WorkshopStoryPointResourceOption> GetPetOptions(string filter)
    {
        return model.GetPetOptions(filter);
    }

    public bool AddScene(Map map, out StorySceneDocument scene, out string error)
    {
        return model.AddScene(map, out scene, out error);
    }

    public bool AddMapResource(Map map, out StorySceneDocument initialScene, out string error)
    {
        return model.AddMapResource(map, out initialScene, out error);
    }

    public bool RemoveMapResource(int mapId, out string error)
    {
        return model.RemoveMapResource(mapId, out error);
    }

    public bool CreateScene(int mapId, out StorySceneDocument scene, out string error)
    {
        return model.CreateScene(mapId, out scene, out error);
    }

    public bool RemoveScene(string sceneId, out string error)
    {
        return model.RemoveScene(sceneId, out error);
    }

    public bool RemoveScene(string sceneId, bool removeSectionContent, out string error)
    {
        return model.RemoveScene(sceneId, removeSectionContent, out error);
    }

    public List<WorkshopStorySceneSection> GetSceneSections()
    {
        return model.GetSceneSections();
    }

    public List<StoryCommandDocument> GetSceneCommands(string sceneId)
    {
        return model.GetSceneCommands(sceneId);
    }

    public bool RemoveSceneTextCommand(string sceneId, string commandId, out string error)
    {
        return model.RemoveSceneTextCommand(sceneId, commandId, out error);
    }

    public bool MoveSceneTextCommand(string sceneId, string commandId, bool moveDown, out string error)
    {
        return model.MoveSceneTextCommand(sceneId, commandId, moveDown, out error);
    }

    public bool ConvertSceneContentToChoice(string sceneId, string commandId, out StoryCommandDocument command, out string error)
    {
        return model.ConvertSceneContentToChoice(sceneId, commandId, out command, out error);
    }

    public bool AddChoiceOption(string sceneId, string commandId, out StoryChoiceDocument option, out string error)
    {
        return model.AddChoiceOption(sceneId, commandId, out option, out error);
    }

    public bool RestoreChoiceToSceneContent(string sceneId, string commandId, out StoryCommandDocument command, out string error)
    {
        return model.RestoreChoiceToSceneContent(sceneId, commandId, out command, out error);
    }

    public bool UpdateChoiceOptionText(string sceneId, string commandId, string optionId, string text, out string error)
    {
        return model.UpdateChoiceOptionText(sceneId, commandId, optionId, text, out error);
    }

    public bool MoveChoiceOption(string sceneId, string commandId, string optionId, bool moveDown, out string error)
    {
        return model.MoveChoiceOption(sceneId, commandId, optionId, moveDown, out error);
    }

    public bool RemoveChoiceOption(string sceneId, string commandId, string optionId, out string error)
    {
        return model.RemoveChoiceOption(sceneId, commandId, optionId, out error);
    }

    public bool SetSceneMap(string sceneId, int mapId, out string error)
    {
        return model.SetSceneMap(sceneId, mapId, out error);
    }

    public bool AddPetActor(int petId, string sceneId, out StoryActorDocument actor, out string error)
    {
        return model.AddPetActor(petId, sceneId, out actor, out error);
    }

    public bool AddPetActor(int petId, out StoryActorDocument actor, out string error)
    {
        return model.AddPetActor(petId, out actor, out error);
    }

    public bool SetActorVisible(string actorId, string sceneId, bool visible, out string error)
    {
        return model.SetActorVisible(actorId, sceneId, visible, out error);
    }

    public bool RemovePointActor(string actorId, out string error)
    {
        return model.RemovePointActor(actorId, out error);
    }

    public List<WorkshopStoryPointActorOption> GetVisibleActorOptions(string sceneId)
    {
        return model.GetVisibleActorOptions(sceneId);
    }

    public bool SetSceneActorSide(string sceneId, string actorId, string side, out string error)
    {
        return model.SetSceneActorSide(sceneId, actorId, side, out error);
    }

    public bool ResetSceneActorLayout(string sceneId, out string error)
    {
        return model.ResetSceneActorLayout(sceneId, out error);
    }

    public bool MoveSceneActorDepth(string sceneId, string actorId, bool outward, out string error)
    {
        return model.MoveSceneActorDepth(sceneId, actorId, outward, out error);
    }

    public bool ToggleSceneAutoLayoutMode(string sceneId, out string error)
    {
        return model.ToggleSceneAutoLayoutMode(sceneId, out error);
    }

    public List<WorkshopStoryPointActorOption> GetCurrentActorOptions()
    {
        return model.GetCurrentActorOptions();
    }

    public bool CreateNarrationCommand(out StoryCommandDocument command, out string error)
    {
        return model.CreateNarrationCommand(out command, out error);
    }

    public bool CreateNarrationCommand(string sceneId, out StoryCommandDocument command, out string error)
    {
        return model.CreateNarrationCommand(sceneId, out command, out error);
    }

    public bool CreateSayCommand(string actorId, out StoryCommandDocument command, out string error)
    {
        return model.CreateSayCommand(actorId, out command, out error);
    }

    public bool CreateSayCommand(string actorId, string sceneId, out StoryCommandDocument command, out string error)
    {
        return model.CreateSayCommand(actorId, sceneId, out command, out error);
    }

    public bool Save(out string error)
    {
        return model.Save(out error);
    }
}
