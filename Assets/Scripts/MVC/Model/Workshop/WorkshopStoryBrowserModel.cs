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
    public bool HasUnsavedChanges { get; private set; }

    public IReadOnlyList<WorkshopStoryChoiceOption> SelectedNodeChoiceOptions => GetSelectedNodeChoiceOptions();

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
        HasUnsavedChanges = false;

        if (!string.IsNullOrEmpty(error))
            return false;

        return SelectStory(SelectedStory?.path, out error);
    }

    public bool SelectStory(string path, out string error)
    {
        if (HasUnsavedChanges
            && SelectedStory != null
            && !string.Equals(SelectedStory.path, path, StringComparison.OrdinalIgnoreCase))
        {
            error = "当前剧本尚未保存，请先保存后再切换。";
            return false;
        }

        SelectedStory = Stories.FirstOrDefault(story => story.path == path);
        SelectedDocument = null;
        SelectedNode = null;
        HasUnsavedChanges = false;
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

    public bool UpdateSelectedStoryMetadata(string title, string summary, bool replayable, out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        SelectedDocument.title = title?.Trim() ?? string.Empty;
        SelectedDocument.summary = summary?.Trim() ?? string.Empty;
        SelectedDocument.replayable = replayable;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool CreateNode(out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        string nodeId = CreateAvailableNodeId();
        StoryNodeDocument node = StoryDocumentFactory.CreateDraftPoint(nodeId);
        SelectedDocument.nodes = (SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(value => value != null)
            .Append(node)
            .ToArray();
        SelectedNode = node;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool RenameSelectedNode(string displayName, out string error)
    {
        if (SelectedNode == null)
        {
            error = "请先选择要重命名的剧情点。";
            return false;
        }

        SelectedNode.displayName = string.IsNullOrWhiteSpace(displayName)
            ? SelectedNode.id
            : displayName.Trim();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool SetSelectedNodeAsEntry(out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        if (SelectedNode == null)
        {
            error = "请先选择要设为入口的剧情点。";
            return false;
        }

        SelectedDocument.entry = SelectedNode.id;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool DeleteSelectedNode(out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        if (SelectedNode == null)
        {
            error = "请先选择要删除的剧情点。";
            return false;
        }

        StoryNodeDocument[] nodes = (SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null)
            .ToArray();
        if (nodes.Length <= 1)
        {
            error = "剧本至少要保留一个入口剧情点。";
            return false;
        }

        if (string.Equals(SelectedDocument.entry, SelectedNode.id, StringComparison.OrdinalIgnoreCase))
        {
            error = "当前剧情点是入口；请先将其他剧情点设为入口。";
            return false;
        }

        if (HasNodeReference(SelectedNode.id))
        {
            error = "已有跳转或选项引用该剧情点，不能删除。";
            return false;
        }

        SelectedDocument.nodes = nodes.Where(node => node != SelectedNode).ToArray();
        SelectedNode = SelectedDocument.nodes.FirstOrDefault(node => node != null && node.id == SelectedDocument.entry)
            ?? SelectedDocument.nodes.FirstOrDefault(node => node != null);
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool AddSelectedNodeDefaultTransition(out string transitionId, out string error)
    {
        transitionId = string.Empty;
        if (!TryGetSelectedNode(out error))
            return false;

        if ((SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>()).Any(value => value != null && value.isDefault))
        {
            error = "当前剧情点已有默认后续连接。";
            return false;
        }

        StoryNodeDocument target = GetSuggestedTransitionTarget();
        if (target == null)
        {
            error = "请先选择有效的剧情点。";
            return false;
        }

        StoryNodeTransitionDocument transition = new StoryNodeTransitionDocument
        {
            transitionId = CreateAvailableTransitionId(),
            targetNodeId = target.id,
            isDefault = true,
        };
        AppendTransition(transition);
        transitionId = transition.transitionId;
        error = string.Empty;
        return true;
    }

    public bool AddSelectedNodeChoiceTransition(out string transitionId, out string error)
    {
        transitionId = string.Empty;
        if (!TryGetSelectedNode(out error))
            return false;

        WorkshopStoryChoiceOption choice = GetSelectedNodeChoiceOptions().FirstOrDefault();
        if (choice == null)
        {
            error = "当前剧情点还没有选项；请先在可视化编辑页面添加选项。";
            return false;
        }

        StoryNodeDocument target = GetSuggestedTransitionTarget();
        if (target == null)
        {
            error = "请先选择有效的剧情点。";
            return false;
        }

        StoryNodeTransitionDocument transition = new StoryNodeTransitionDocument
        {
            transitionId = CreateAvailableTransitionId(),
            targetNodeId = target.id,
            condition = new ConditionGroupDocument
            {
                operatorType = "AND",
                conditions = new[]
                {
                    new StoryConditionDocument
                    {
                        type = "choiceSelected",
                        commandId = choice.commandId,
                        choiceId = choice.choiceId,
                        optionId = choice.optionId,
                    },
                },
            },
        };
        AppendTransition(transition);
        transitionId = transition.transitionId;
        error = string.Empty;
        return true;
    }

    public bool UpdateSelectedNodeTransitionTarget(string transitionId, string targetNodeId, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;

        if (!(SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Any(node => node != null && string.Equals(node.id, targetNodeId, StringComparison.OrdinalIgnoreCase)))
        {
            error = "请选择有效的目标剧情点。";
            return false;
        }

        transition.targetNodeId = targetNodeId;
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool UpdateSelectedNodeTransitionChoice(string transitionId, string commandId, string choiceId, string optionId, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;

        if (transition.isDefault)
        {
            error = "默认连接不需要选择触发选项。";
            return false;
        }

        WorkshopStoryChoiceOption selected = GetSelectedNodeChoiceOptions().FirstOrDefault(value => value != null
            && string.Equals(value.commandId, commandId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.choiceId, choiceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.optionId, optionId, StringComparison.OrdinalIgnoreCase));
        if (selected == null)
        {
            error = "请选择当前剧情点中的有效选项。";
            return false;
        }

        transition.condition = new ConditionGroupDocument
        {
            operatorType = transition.condition?.operatorType == "OR" ? "OR" : "AND",
            conditions = new[]
            {
                new StoryConditionDocument
                {
                    type = "choiceSelected",
                    commandId = selected.commandId,
                    choiceId = selected.choiceId,
                    optionId = selected.optionId,
                },
            },
        };
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool MoveSelectedNodeTransition(string transitionId, bool moveDown, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;

        List<StoryNodeTransitionDocument> transitions = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Where(value => value != null)
            .ToList();
        int index = transitions.IndexOf(transition);
        int targetIndex = index + (moveDown ? 1 : -1);
        if (targetIndex < 0 || targetIndex >= transitions.Count)
        {
            error = "已经位于连接列表的边界。";
            return false;
        }

        if (transition.isDefault || transitions[targetIndex].isDefault)
        {
            error = "默认连接始终位于最后，不能调整优先级。";
            return false;
        }

        StoryNodeTransitionDocument temporary = transitions[index];
        transitions[index] = transitions[targetIndex];
        transitions[targetIndex] = temporary;
        SelectedNode.transitions = transitions.ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool RemoveSelectedNodeTransition(string transitionId, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;

        SelectedNode.transitions = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Where(value => value != null && value != transition)
            .ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool CreateDraft(out string error)
    {
        if (HasUnsavedChanges)
        {
            error = "当前剧本尚未保存，请先保存后再新建。";
            return false;
        }

        if (!repository.TryCreateDraft(out WorkshopStorySummary summary, out error))
            return false;

        SelectedStory = summary;
        SelectedDocument = null;
        SelectedNode = null;
        HasUnsavedChanges = false;
        return Reload(out error);
    }

    public bool SaveSelected(out string error)
    {
        if (SelectedStory == null || SelectedDocument == null)
        {
            error = "请先选择要保存的剧本。";
            return false;
        }

        if (!repository.TrySave(SelectedStory.path, SelectedDocument, out error))
            return false;

        bool reloaded = Reload(out error);
        if (reloaded)
            HasUnsavedChanges = false;
        return reloaded;
    }

    public bool DeleteSelected(out string error)
    {
        if (SelectedStory == null)
        {
            error = "请先选择要删除的剧本。";
            return false;
        }

        if (!repository.TryDelete(SelectedStory.path, out error))
            return false;

        SelectedStory = null;
        SelectedDocument = null;
        SelectedNode = null;
        HasUnsavedChanges = false;
        return Reload(out error);
    }

    private bool TryGetSelectedDocument(out string error)
    {
        if (SelectedDocument == null)
        {
            error = "请先选择剧本。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryGetSelectedNode(out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        if (SelectedNode == null)
        {
            error = "请先选择一个剧情点。";
            return false;
        }

        return true;
    }

    private bool TryGetSelectedTransition(string transitionId, out StoryNodeTransitionDocument transition, out string error)
    {
        transition = null;
        if (!TryGetSelectedNode(out error))
            return false;

        transition = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .FirstOrDefault(value => value != null && string.Equals(value.transitionId, transitionId, StringComparison.OrdinalIgnoreCase));
        if (transition == null)
        {
            error = "未找到要编辑的后续连接。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private WorkshopStoryChoiceOption[] GetSelectedNodeChoiceOptions()
    {
        if (SelectedNode == null)
            return Array.Empty<WorkshopStoryChoiceOption>();

        return (SelectedNode.commands ?? Array.Empty<StoryCommandDocument>())
            .Where(command => command != null && string.Equals(command.type, "choice", StringComparison.OrdinalIgnoreCase))
            .SelectMany(command => (command.choices ?? Array.Empty<StoryChoiceDocument>())
                .Where(choice => choice != null && !string.IsNullOrWhiteSpace(choice.optionId))
                .Select(choice => new WorkshopStoryChoiceOption
                {
                    commandId = command.commandId,
                    choiceId = command.choiceId,
                    optionId = choice.optionId,
                    text = choice.text,
                }))
            .ToArray();
    }

    private StoryNodeDocument GetSuggestedTransitionTarget()
    {
        StoryNodeDocument[] nodes = (SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null)
            .ToArray();
        return nodes.FirstOrDefault(node => !string.Equals(node.id, SelectedNode?.id, StringComparison.OrdinalIgnoreCase))
            ?? nodes.FirstOrDefault(node => string.Equals(node.id, SelectedNode?.id, StringComparison.OrdinalIgnoreCase));
    }

    private string CreateAvailableTransitionId()
    {
        int index = 1;
        string transitionId;
        do
        {
            transitionId = "transition_" + index++;
        }
        while ((SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Any(value => value != null && string.Equals(value.transitionId, transitionId, StringComparison.OrdinalIgnoreCase)));

        return transitionId;
    }

    private void AppendTransition(StoryNodeTransitionDocument transition)
    {
        List<StoryNodeTransitionDocument> transitions = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Where(value => value != null && !value.isDefault)
            .ToList();
        StoryNodeTransitionDocument defaultTransition = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .FirstOrDefault(value => value != null && value.isDefault);
        transitions.Add(transition);
        if (defaultTransition != null)
            transitions.Add(defaultTransition);
        SelectedNode.transitions = transitions.ToArray();
        MarkUnsaved();
    }

    private void MarkUnsaved()
    {
        HasUnsavedChanges = true;
    }

    private string CreateAvailableNodeId()
    {
        int index = 1;
        string nodeId;
        do
        {
            nodeId = "point_" + index++;
        }
        while ((SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Any(node => node != null && string.Equals(node.id, nodeId, StringComparison.OrdinalIgnoreCase)));

        return nodeId;
    }

    private bool HasNodeReference(string nodeId)
    {
        foreach (StoryNodeDocument node in SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            foreach (StoryCommandDocument command in node?.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command == null)
                    continue;

                if (string.Equals(command.type, "jump", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(command.target, nodeId, StringComparison.OrdinalIgnoreCase))
                    return true;

                if ((command.choices ?? Array.Empty<StoryChoiceDocument>())
                    .Any(choice => choice != null && string.Equals(choice.target, nodeId, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            if ((node?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
                .Any(transition => transition != null && string.Equals(transition.targetNodeId, nodeId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
}

public sealed class WorkshopStoryChoiceOption
{
    public string commandId;
    public string choiceId;
    public string optionId;
    public string text;

    public string displayName => string.IsNullOrWhiteSpace(text)
        ? optionId
        : optionId + " | " + text;
}
