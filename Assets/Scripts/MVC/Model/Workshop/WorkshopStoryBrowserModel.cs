using System;
using System.Collections.Generic;
using System.Linq;

public sealed class WorkshopStoryBrowserModel
{
    private readonly WorkshopStoryRepository repository;

    public IReadOnlyList<WorkshopStorySummary> Stories { get; private set; } = Array.Empty<WorkshopStorySummary>();
    public WorkshopStorySummary SelectedStory { get; private set; }
    public StoryDocument SelectedDocument { get; private set; }
    public StoryNodeDocument SelectedNode { get; private set; }

    public WorkshopStoryBrowserModel(WorkshopStoryRepository repository)
    {
        this.repository = repository;
    }

    public bool Reload(out string error)
    {
        string selectedPath = SelectedStory?.path;
        Stories = repository.List(out error);
        SelectedStory = Stories.FirstOrDefault(story => story.path == selectedPath) ?? Stories.FirstOrDefault();
        SelectedDocument = null;
        SelectedNode = null;
        return SelectStory(SelectedStory?.path, out error);
    }

    public bool SelectStory(string path, out string error)
    {
        SelectedStory = Stories.FirstOrDefault(story => story.path == path);
        SelectedDocument = null;
        SelectedNode = null;
        if (SelectedStory == null)
        {
            error = string.Empty;
            return true;
        }

        if (!SelectedStory.isValid)
        {
            error = SelectedStory.error;
            return false;
        }

        if (!repository.TryLoad(path, out StoryDocument document, out error))
            return false;

        SelectedDocument = document;
        SelectedNode = (document.nodes ?? Array.Empty<StoryNodeDocument>()).FirstOrDefault(node => node != null && node.id == document.entry)
            ?? (document.nodes ?? Array.Empty<StoryNodeDocument>()).FirstOrDefault(node => node != null);
        error = string.Empty;
        return true;
    }

    public void SelectNode(string nodeId)
    {
        SelectedNode = (SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
            .FirstOrDefault(node => node != null && node.id == nodeId);
    }
}
