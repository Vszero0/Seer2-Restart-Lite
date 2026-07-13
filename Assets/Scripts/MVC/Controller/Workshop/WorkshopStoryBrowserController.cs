using System;
using System.Collections.Generic;

public sealed class WorkshopStoryBrowserController
{
    private readonly WorkshopStoryBrowserModel model;

    public IReadOnlyList<WorkshopStorySummary> Stories => model.Stories;
    public WorkshopStorySummary SelectedStory => model.SelectedStory;
    public StoryDocument SelectedDocument => model.SelectedDocument;
    public StoryNodeDocument SelectedNode => model.SelectedNode;

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
}
