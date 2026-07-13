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

    public bool CreateDraft(out string error)
    {
        return model.CreateDraft(out error);
    }

    public bool SaveSelected(out string error)
    {
        return model.SaveSelected(out error);
    }

    public bool DeleteSelected(out string error)
    {
        return model.DeleteSelected(out error);
    }
}
