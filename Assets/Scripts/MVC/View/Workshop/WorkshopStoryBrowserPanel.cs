using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 自制剧情入口页：负责剧本与剧情点的选择、创建、删除和保存。
/// 具体剧情点的可视化编辑将在独立页面完成，避免在入口页堆叠编辑控件。
/// </summary>
public class WorkshopStoryBrowserPanel : Panel
{
    private sealed class TransitionTargetOption
    {
        public string targetType;
        public string nodeId;
        public string displayName;
    }

    private sealed class StoryGraphEdge
    {
        public string sourceId;
        public string targetId;
        public string label;
        public bool isConditional;
    }

    private sealed class StoryGraphNodeLayout
    {
        public string id;
        public StoryNodeDocument node;
        public int level;
        public bool isReachable;
        public Vector2 position;
    }

    private const string StoryEndGraphNodeId = "__story_end__";
    private const float GraphNodeWidth = 180f;
    private const float GraphNodeHeight = 76f;
    private const float GraphColumnSpacing = 310f;
    private const float GraphRowSpacing = 122f;
    private const float GraphLeftPadding = 34f;
    private const float GraphTopPadding = 40f;

    private static readonly Color Cyan = new Color32(82, 229, 249, 255);
    private static readonly Color HintColor = new Color32(180, 220, 230, 255);
    private static readonly Color WarningColor = new Color32(255, 232, 71, 255);

    private readonly WorkshopStoryBrowserController controller = new WorkshopStoryBrowserController(
        new WorkshopStoryBrowserModel(new WorkshopStoryRepository()));

    private RectTransform storyContent;
    private RectTransform nodeContent;
    private RectTransform connectionOverlay;
    private RectTransform transitionContent;
    private RectTransform graphOverlay;
    private RectTransform graphContent;
    private Text connectionNodeTitle;
    private Text connectionDefaultText;
    private Text storyStatusText;
    private Text nodeDetailTitle;
    private Text nodeDetailBody;
    private Font font;
    private GameObject listButtonPrefab;
    private GameObject actionButtonPrefab;
    private GameObject dropdownPrefab;
    private GameObject petNameInputFieldPrefab;
    private GameObject petDescriptionInputFieldPrefab;
    private IInputField storyTitleInput;
    private IInputField storySummaryInput;
    private Image projectFrameImage;
    private Outline projectFrameOutline;
    private bool hasBuilt;

    public override void Init()
    {
        base.Init();
        if (hasBuilt)
            return;

        hasBuilt = true;
        font = ResourceManager.instance.GetFont("Zongyi");
        listButtonPrefab = Resources.Load<GameObject>("Prefabs/Scroll List Button");
        actionButtonPrefab = FindWorkshopActionButtonPrefab();
        dropdownPrefab = FindWorkshopDropdownPrefab();
        FindWorkshopPetInputFieldPrefabs();
        background = GetComponent<Image>();
        BuildLayout();
        Reload();
    }

    private void BuildLayout()
    {
        RectTransform root = GetComponent<RectTransform>();
        root.anchorMin = new Vector2(.5f, .5f);
        root.anchorMax = root.anchorMin;
        root.pivot = new Vector2(.5f, .5f);
        root.sizeDelta = new Vector2(920f, 500f);
        root.anchoredPosition = Vector2.zero;

        ApplyProjectFrame(background);
        CreateTitleDecoration();
        CreateProjectTitle();
        CreateCloseButton();

        RectTransform storySection = CreateSection("剧本", new Vector2(18f, -76f), new Vector2(236f, 402f));
        RectTransform infoSection = CreateSection("剧本信息", new Vector2(270f, -76f), new Vector2(632f, 116f));
        RectTransform nodeSection = CreateSection("剧情点", new Vector2(270f, -204f), new Vector2(632f, 274f));

        CreateActionButton(storySection, "新建", new Vector2(16f, -50f), new Vector2(96f, 28f), CreateStory);
        CreateActionButton(storySection, "删除", new Vector2(124f, -50f), new Vector2(96f, 28f), DeleteStory);
        storyContent = CreateScrollContent(storySection, new Vector2(14f, 14f), new Vector2(-14f, -86f));

        CreateText("Title Label", infoSection, "标题：", 15, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -50f), new Vector2(52f, 26f));
        storyTitleInput = CreateInputField(petNameInputFieldPrefab, infoSection, "Story Title Input", "剧本标题", "未命名剧本",
            new Vector2(70f, -50f), new Vector2(230f, 26f), OnStoryTitleEdited);
        storyStatusText = CreateText("Story Status", infoSection, string.Empty, 13, TextAnchor.MiddleCenter, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(312f, -50f), new Vector2(118f, 26f));
        CreateText("Summary Label", infoSection, "简介：", 15, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -84f), new Vector2(52f, 26f));
        storySummaryInput = CreateInputField(petDescriptionInputFieldPrefab, infoSection, "Story Summary Input", "剧本简介", "暂无简介",
            new Vector2(70f, -84f), new Vector2(244f, 26f), OnStorySummaryEdited);
        CreateActionButton(infoSection, "查看结构", new Vector2(-220f, -84f), new Vector2(92f, 26f), OpenGraphViewer, TextAnchor.UpperRight);
        CreateActionButton(infoSection, "预览剧本", new Vector2(-120f, -84f), new Vector2(96f, 26f), PreviewStory, TextAnchor.UpperRight);
        CreateActionButton(infoSection, "保存", new Vector2(-16f, -84f), new Vector2(96f, 26f), SaveStory, TextAnchor.UpperRight);

        CreateActionButton(nodeSection, "新建", new Vector2(16f, -50f), new Vector2(94f, 28f), CreateNode);
        CreateActionButton(nodeSection, "删除", new Vector2(120f, -50f), new Vector2(94f, 28f), DeleteNode);
        CreateActionButton(nodeSection, "设为入口", new Vector2(224f, -50f), new Vector2(116f, 28f), SetEntryNode);
        CreateActionButton(nodeSection, "编辑剧情点", new Vector2(352f, -50f), new Vector2(132f, 28f), OpenNodeEditor);
        CreateActionButton(nodeSection, "编辑连接", new Vector2(496f, -50f), new Vector2(120f, 28f), OpenConnectionEditor);
        CreateActionButton(nodeSection, "复制并新增", new Vector2(16f, -84f), new Vector2(112f, 28f), CopyNode);
        nodeContent = CreateScrollContent(nodeSection, new Vector2(14f, 14f), new Vector2(-246f, -118f));
        BuildNodeDetailPanel(nodeSection);
        BuildConnectionEditor(root);
        BuildGraphViewer(root);
    }

    private void BuildNodeDetailPanel(RectTransform nodeSection)
    {
        GameObject panelObject = new GameObject("Selected Node Details", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        panelObject.transform.SetParent(nodeSection, false);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(1f, 1f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-14f, -118f);
        panel.sizeDelta = new Vector2(220f, 142f);

        panelObject.GetComponent<Image>().color = new Color32(0, 18, 25, 235);
        Outline outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .38f);
        outline.effectDistance = new Vector2(1f, -1f);

        nodeDetailTitle = CreateText("Selected Node Title", panel, "剧情点详情", 16, TextAnchor.MiddleLeft, WarningColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -8f), new Vector2(196f, 24f));
        nodeDetailBody = CreateText("Selected Node Body", panel, string.Empty, 13, TextAnchor.UpperLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -36f), new Vector2(196f, 96f));
        nodeDetailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        nodeDetailBody.verticalOverflow = VerticalWrapMode.Truncate;
    }

    private void BuildConnectionEditor(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story Transition Editor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        connectionOverlay = overlayObject.GetComponent<RectTransform>();
        connectionOverlay.anchorMin = new Vector2(.5f, .5f);
        connectionOverlay.anchorMax = new Vector2(.5f, .5f);
        connectionOverlay.pivot = new Vector2(.5f, .5f);
        connectionOverlay.anchoredPosition = new Vector2(0f, -12f);
        connectionOverlay.sizeDelta = new Vector2(872f, 420f);

        Image overlay = overlayObject.GetComponent<Image>();
        overlay.color = new Color32(0, 8, 12, 252);
        Outline overlayOutline = overlayObject.GetComponent<Outline>();
        overlayOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .86f);
        overlayOutline.effectDistance = new Vector2(2f, -2f);

        CreateText("Transition Heading", connectionOverlay, "剧情点后续走向", 22, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -18f), new Vector2(260f, 30f));
        connectionNodeTitle = CreateText("Transition Node", connectionOverlay, string.Empty, 15, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -50f), new Vector2(560f, 24f));
        CreateActionButton(connectionOverlay, "添加分支规则", new Vector2(18f, -78f), new Vector2(126f, 28f), AddChoiceTransition);
        CreateActionButton(connectionOverlay, "保存", new Vector2(-112f, -78f), new Vector2(88f, 28f), SaveStory, TextAnchor.UpperRight);
        CreateActionButton(connectionOverlay, "返回列表", new Vector2(-16f, -78f), new Vector2(88f, 28f), CloseConnectionEditor, TextAnchor.UpperRight);

        transitionContent = CreateScrollContent(connectionOverlay, new Vector2(16f, 14f), new Vector2(-16f, -112f));
        overlayObject.SetActive(false);
    }

    private void BuildGraphViewer(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story Graph Viewer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        graphOverlay = overlayObject.GetComponent<RectTransform>();
        graphOverlay.anchorMin = new Vector2(.5f, .5f);
        graphOverlay.anchorMax = new Vector2(.5f, .5f);
        graphOverlay.pivot = new Vector2(.5f, .5f);
        graphOverlay.anchoredPosition = new Vector2(0f, -12f);
        graphOverlay.sizeDelta = new Vector2(872f, 420f);

        Image overlay = overlayObject.GetComponent<Image>();
        overlay.color = new Color32(0, 8, 12, 252);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .86f);
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("Graph Heading", graphOverlay, "剧情点结构", 22, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -18f), new Vector2(260f, 30f));
        CreateText("Graph Legend", graphOverlay, "只读视图  ·  青色：顺序/默认  ·  黄色：条件分支  ·  灰色：入口不可达",
            13, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -52f), new Vector2(680f, 24f));
        CreateActionButton(graphOverlay, "返回列表", new Vector2(-16f, -46f), new Vector2(88f, 28f), CloseGraphViewer, TextAnchor.UpperRight);
        graphContent = CreateGraphScrollContent(graphOverlay);
        overlayObject.SetActive(false);
    }

    private RectTransform CreateGraphScrollContent(RectTransform parent)
    {
        GameObject viewportObject = new GameObject("Graph Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.transform.SetParent(parent, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(16f, 16f);
        viewport.offsetMax = new Vector2(-16f, -82f);
        viewportObject.GetComponent<Image>().color = new Color32(0, 13, 18, 255);
        viewportObject.GetComponent<Mask>().showMaskGraphic = true;

        GameObject contentObject = new GameObject("Graph Content", typeof(RectTransform));
        contentObject.transform.SetParent(viewport, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(840f, 320f);

        ScrollRect scroll = viewportObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = true;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 70f;
        return content;
    }

    private void CreateTitleDecoration()
    {
        GameObject missionPanel = ResourceManager.instance.GetPanel("Mission");
        Image source = missionPanel == null
            ? null
            : missionPanel.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(image => image.gameObject.name == "Title line");
        if (source == null)
            return;

        GameObject decoration = Instantiate(source.gameObject, transform);
        decoration.name = "Title line";
        decoration.transform.SetSiblingIndex(0);
        RectTransform rect = decoration.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -20f);
        rect.sizeDelta = new Vector2(0f, 23f);
        decoration.GetComponent<Image>().raycastTarget = false;
    }

    private void CreateProjectTitle()
    {
        GameObject missionPanel = ResourceManager.instance.GetPanel("Mission");
        Transform source = missionPanel == null
            ? null
            : missionPanel.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value.gameObject.name == "Title"
                    && value.GetComponentInChildren<TMP_Text>(true)?.text == "任务");
        if (source == null)
            return;

        GameObject title = Instantiate(source.gameObject, transform);
        title.name = "Title";
        title.transform.SetSiblingIndex(1);
        RectTransform rect = title.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 50f);

        TMP_Text text = title.GetComponentInChildren<TMP_Text>(true);
        text.text = "自制剧情";
        text.raycastTarget = false;
    }

    private void CreateCloseButton()
    {
        GameObject workshopPrefab = ResourceManager.instance.GetPanel("Workshop");
        IButton source = workshopPrefab == null
            ? null
            : workshopPrefab.GetComponentsInChildren<IButton>(true)
                .FirstOrDefault(button => button.gameObject.name == "ESC Button");
        if (source == null)
            return;

        GameObject closeObject = Instantiate(source.gameObject, transform);
        closeObject.name = "ESC Button";
        RectTransform rect = closeObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-14f, -14f);
        rect.sizeDelta = new Vector2(40f, 40f);

        ESCButton = closeObject.GetComponent<IButton>();
        ESCButton.onPointerClickEvent.SetListener(ClosePanel);
    }

    private RectTransform CreateSection(string title, Vector2 position, Vector2 dimensions)
    {
        const float innerTopPadding = 8f;
        const float actionButtonTop = 50f;
        const float headingHeight = 30f;

        GameObject section = new GameObject(title + " Area", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        section.transform.SetParent(transform, false);

        RectTransform rect = section.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        ApplyProjectFrame(section.GetComponent<Image>());
        CreateText("Heading", section.transform, title, 22, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f),
            new Vector2(0f, -((innerTopPadding + actionButtonTop) * .5f - headingHeight * .5f)),
            new Vector2(220f, headingHeight));
        return rect;
    }

    private RectTransform CreateScrollContent(RectTransform section, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject viewportObject = new GameObject("List Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.transform.SetParent(section, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = offsetMin;
        viewport.offsetMax = offsetMax;

        Image image = viewportObject.GetComponent<Image>();
        image.color = new Color32(0, 8, 12, 255);
        Mask mask = viewportObject.GetComponent<Mask>();
        mask.showMaskGraphic = true;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(viewport, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 10f);

        ScrollRect scroll = viewportObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 90f;
        return content;
    }

    private IButton CreateActionButton(RectTransform parent, string label, Vector2 position, Vector2 dimensions, Action callback,
        TextAnchor horizontalAnchor = TextAnchor.UpperLeft)
    {
        if (actionButtonPrefab == null)
            return null;

        GameObject item = Instantiate(actionButtonPrefab, parent);
        item.name = label + " Button";
        RectTransform rect = item.GetComponent<RectTransform>();
        if (horizontalAnchor == TextAnchor.UpperRight)
        {
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
        }
        else
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        IButton button = item.GetComponent<IButton>();
        button.button.onClick = new Button.ButtonClickedEvent();
        button.onPointerClickEvent = new UnityEngine.Events.UnityEvent();
        button.onPointerClickEvent.AddListener(callback.Invoke);
        Text text = item.GetComponentInChildren<Text>();
        text.raycastTarget = false;
        text.text = label;
        return button;
    }

    private IInputField CreateInputField(GameObject sourcePrefab, RectTransform parent, string name, string placeholder, string initialValue,
        Vector2 position, Vector2 dimensions, Action<string> onEndEdit)
    {
        if (sourcePrefab == null)
            return null;

        GameObject item = Instantiate(sourcePrefab, parent);
        item.name = name;
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        IInputField input = item.GetComponent<IInputField>();
        InputField nativeInput = item.GetComponent<InputField>();
        if (nativeInput == null)
            return input;

        nativeInput.onEndEdit = new InputField.EndEditEvent();
        nativeInput.onEndEdit.AddListener(onEndEdit.Invoke);
        input?.SetPlaceHolderText(placeholder);
        input?.SetInputString(initialValue);
        return input;
    }

    private void Reload()
    {
        if (!controller.Open(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void CreateStory()
    {
        if (!controller.CreateDraft(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void DeleteStory()
    {
        if (!controller.DeleteSelected(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void SaveStory()
    {
        if (!controller.SaveSelected(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void SelectStory(string path)
    {
        if (!controller.SelectStory(path, out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void CreateNode()
    {
        if (!controller.CreateNode(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void CopyNode()
    {
        if (!controller.CopySelectedNode(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void DeleteNode()
    {
        if (!controller.DeleteSelectedNode(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void SetEntryNode()
    {
        if (!controller.SetSelectedNodeAsEntry(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void OpenNodeEditor()
    {
        if (controller.SelectedStory == null || controller.SelectedNode == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要编辑的剧情点。", 16);
            return;
        }

        string storyPath = controller.SelectedStory.path;
        string nodeId = controller.SelectedNode.id;
        if (!controller.SaveSelected(out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        WorkshopStoryNodeEditorPanel.Open(storyPath, nodeId, Reload);
    }

    private void PreviewStory()
    {
        StoryDocument document = controller.SelectedDocument;
        if (document == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要预览的剧本。", 16);
            return;
        }

        if (StoryPanel.OpenPreview(document, document.entry) == null)
            Hintbox.OpenHintboxWithContent("无法打开剧本预览。", 16);
    }

    private void OpenGraphViewer()
    {
        if (controller.SelectedDocument == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要查看的剧本。", 16);
            return;
        }

        graphOverlay.gameObject.SetActive(true);
        RefreshGraphViewer();
    }

    private void CloseGraphViewer()
    {
        if (graphOverlay != null)
            graphOverlay.gameObject.SetActive(false);
    }

    private void OpenConnectionEditor()
    {
        if (controller.SelectedNode == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要配置后续连接的剧情点。", 16);
            return;
        }

        connectionOverlay.gameObject.SetActive(true);
        RefreshConnections();
    }

    private void CloseConnectionEditor()
    {
        if (connectionOverlay != null)
            connectionOverlay.gameObject.SetActive(false);
        RefreshStoryInfo();
        RefreshNodes();
    }

    private void AddChoiceTransition()
    {
        if (!controller.AddSelectedNodeChoiceTransition(out _, out string error))
        {
            if (!string.IsNullOrEmpty(error))
                Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        RefreshConnections();
    }

    private void SelectNode(string nodeId)
    {
        controller.SelectNode(nodeId);
        RefreshNodes();
    }

    private void OnStoryTitleEdited(string title)
    {
        StoryDocument document = controller.SelectedDocument;
        if (document == null)
            return;

        if (!controller.UpdateSelectedStoryMetadata(title, document.summary, document.replayable, out string error)
            && !string.IsNullOrEmpty(error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        RefreshStoryInfo();
    }

    private void OnStorySummaryEdited(string summary)
    {
        StoryDocument document = controller.SelectedDocument;
        if (document == null)
            return;

        if (!controller.UpdateSelectedStoryMetadata(document.title, summary, document.replayable, out string error)
            && !string.IsNullOrEmpty(error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        RefreshStoryInfo();
    }

    private void RefreshView()
    {
        RefreshStories();
        RefreshStoryInfo();
        RefreshNodes();
    }

    private void RefreshStories()
    {
        ClearChildren(storyContent);
        int index = 0;
        foreach (WorkshopStorySummary story in controller.Stories)
        {
            string title = story.isValid ? story.title : "[格式错误] " + story.fileName;
            string path = story.path;
            CreateListButton(storyContent, title, story == controller.SelectedStory, index++, () => SelectStory(path));
        }

        if (index == 0)
            CreateHint(storyContent, "点击“新建”创建第一个剧本。");
        else
            storyContent.sizeDelta = new Vector2(0f, 12f + index * 42f);
    }

    private void RefreshStoryInfo()
    {
        if (storyStatusText == null)
            return;

        StoryDocument document = controller.SelectedDocument;
        if (document == null)
        {
            SetInputFieldValue(storyTitleInput, string.Empty, false);
            SetInputFieldValue(storySummaryInput, string.Empty, false);
            storyStatusText.text = string.Empty;
            return;
        }

        string status = (document.isDraft ? "草稿" : "已发布")
            + " · " + (controller.HasUnsavedChanges ? "未保存" : "已保存");
        int nodeCount = (document.nodes ?? Array.Empty<StoryNodeDocument>()).Count(node => node != null);
        SetInputFieldValue(storyTitleInput, document.title, true);
        SetInputFieldValue(storySummaryInput, document.summary, true);
        storyStatusText.text = "状态：" + status + "\n剧情点：" + nodeCount;
        storyStatusText.color = controller.HasUnsavedChanges ? WarningColor : HintColor;
    }

    private void RefreshNodes()
    {
        ClearChildren(nodeContent);
        StoryNodeDocument[] nodes = controller.SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>();
        int index = 0;
        foreach (StoryNodeDocument node in nodes)
        {
            if (node == null)
                continue;

            string displayName = string.IsNullOrWhiteSpace(node.displayName) ? node.id : node.displayName;
            string roles = (node.id == controller.SelectedDocument.entry ? "入口·" : string.Empty)
                + (node.isEnding ? "结束·" : string.Empty);
            string label = roles + (node.isBranch ? "分支" : "顺序") + " - " + displayName;
            string nodeId = node.id;
            CreateListButton(nodeContent, label, node == controller.SelectedNode, index++, () => SelectNode(nodeId));
        }

        if (index == 0)
            CreateHint(nodeContent, controller.SelectedDocument == null ? "请选择剧本。" : "当前剧本没有剧情点。");
        else
            nodeContent.sizeDelta = new Vector2(0f, 12f + index * 42f);

        RefreshNodeDetails();

        if (connectionOverlay != null && connectionOverlay.gameObject.activeSelf)
            RefreshConnections();
    }

    private void RefreshNodeDetails()
    {
        StoryNodeDocument node = controller.SelectedNode;
        if (nodeDetailTitle == null || nodeDetailBody == null)
            return;

        if (node == null)
        {
            nodeDetailTitle.text = "剧情点详情";
            nodeDetailBody.text = "选中剧情点后，\n在这里查看结构和走向。";
            return;
        }

        int sceneCount = (node.scenes ?? Array.Empty<StorySceneDocument>()).Count(scene => scene != null);
        int actorCount = (node.actorReferences ?? Array.Empty<StoryActorReferenceDocument>()).Count(actor => actor != null);
        int contentCount = (node.commands ?? Array.Empty<StoryCommandDocument>()).Count(command => command != null);
        int ruleCount = (node.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Count(transition => transition != null && !transition.isDefault);
        string flowState = node.isEnding
            ? "结束节点"
            : node.defaultEnds
                ? "默认结束，另有后续"
                : node.hasEndingPath
                    ? "含结束分支"
                    : "继续剧情";

        nodeDetailTitle.text = GetNodeDisplayName(node);
        nodeDetailBody.text = "类型：" + (node.isBranch ? "分支" : "顺序")
            + "  |  " + flowState
            + "\n场景：" + sceneCount + "    角色：" + actorCount
            + "\n内容：" + contentCount + "    分支规则：" + ruleCount
            + "\n默认后续：" + GetDefaultFlowDescription(node);
    }

    private void RefreshGraphViewer()
    {
        if (graphContent == null)
            return;

        ClearChildren(graphContent);
        graphContent.anchoredPosition = Vector2.zero;
        StoryDocument document = controller.SelectedDocument;
        StoryNodeDocument[] nodes = (document?.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null)
            .ToArray();
        if (nodes.Length == 0)
        {
            graphContent.sizeDelta = new Vector2(840f, 320f);
            CreateText("Empty Graph", graphContent, "当前剧本没有剧情点。", 16, TextAnchor.MiddleCenter, HintColor,
                new Vector2(0f, 1f), new Vector2(.5f, .5f), new Vector2(420f, -140f), new Vector2(260f, 40f));
            return;
        }

        List<StoryGraphEdge> edges = BuildStoryGraphEdges(document, nodes);
        Dictionary<string, StoryGraphNodeLayout> layouts = BuildStoryGraphLayout(document, nodes, edges);
        int maxLevel = layouts.Values.Max(layout => layout.level);
        float cardsBottom = layouts.Values.Max(layout => layout.position.y) + GraphNodeHeight;
        List<IGrouping<string, StoryGraphEdge>> renderedEdges = edges
            .GroupBy(edge => edge.sourceId + "\n" + edge.targetId)
            .ToList();
        int returnEdgeCount = renderedEdges.Count(group =>
            layouts.TryGetValue(group.First().sourceId, out StoryGraphNodeLayout source)
            && layouts.TryGetValue(group.First().targetId, out StoryGraphNodeLayout target)
            && target.level < source.level);
        float returnLaneTop = cardsBottom + 44f;
        graphContent.sizeDelta = new Vector2(
            Mathf.Max(840f, GraphLeftPadding + maxLevel * GraphColumnSpacing + GraphNodeWidth + 110f),
            Mathf.Max(320f, returnLaneTop + returnEdgeCount * 22f + 48f));

        Dictionary<string, int> targetTotals = renderedEdges
            .GroupBy(group => group.First().targetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> targetLabels = renderedEdges
            .GroupBy(group => group.First().targetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(" / ", group.SelectMany(edgeGroup => edgeGroup)
                    .Select(edge => edge.label).Distinct().ToArray()),
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> shownTargetLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> shownTargetArrows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> sourceSlots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int returnLaneIndex = 0;
        foreach (IGrouping<string, StoryGraphEdge> group in renderedEdges)
        {
            StoryGraphEdge[] groupedEdges = group.ToArray();
            StoryGraphEdge edge = groupedEdges[0];
            if (!layouts.TryGetValue(edge.sourceId, out StoryGraphNodeLayout source)
                || !layouts.TryGetValue(edge.targetId, out StoryGraphNodeLayout target))
            {
                continue;
            }

            string label = string.Join(" / ", groupedEdges.Select(value => value.label).Distinct().ToArray());
            string displayLabel = targetTotals[edge.targetId] > 1
                ? shownTargetLabels.Add(edge.targetId) ? targetLabels[edge.targetId] : null
                : label;
            bool showTargetArrow = targetTotals[edge.targetId] <= 1 || shownTargetArrows.Add(edge.targetId);
            int sourceSlot = sourceSlots.TryGetValue(edge.sourceId, out int usedSlots) ? usedSlots : 0;
            sourceSlots[edge.sourceId] = sourceSlot + 1;
            bool isReturnEdge = target.level < source.level;
            CreateGraphEdge(source.position, target.position, displayLabel,
                groupedEdges.Any(value => value.isConditional),
                sourceSlot, showTargetArrow,
                isReturnEdge ? returnLaneIndex++ : -1, returnLaneTop);
        }

        foreach (Transform child in graphContent.Cast<Transform>()
            .Where(child => child.gameObject.name == "Graph Edge Label"
                || child.gameObject.name == "Graph Edge Arrow")
            .ToArray())
        {
            child.SetAsLastSibling();
        }

        foreach (StoryGraphNodeLayout layout in layouts.Values.OrderBy(value => value.level).ThenBy(value => value.position.y))
            CreateGraphNode(layout, document.entry);
    }

    private List<StoryGraphEdge> BuildStoryGraphEdges(StoryDocument document, StoryNodeDocument[] nodes)
    {
        List<StoryGraphEdge> edges = new List<StoryGraphEdge>();
        HashSet<string> nodeIds = new HashSet<string>(nodes.Select(node => node.id), StringComparer.OrdinalIgnoreCase);
        List<StoryNodeDocument> sequenceNodes = nodes.Where(node => !node.isBranch).ToList();
        StoryNodeDocument entryNode = sequenceNodes.FirstOrDefault(node => string.Equals(node.id, document.entry, StringComparison.OrdinalIgnoreCase));
        if (entryNode != null)
        {
            sequenceNodes.Remove(entryNode);
            sequenceNodes.Insert(0, entryNode);
        }

        foreach (StoryNodeDocument node in nodes)
        {
            StoryNodeTransitionDocument[] transitions = (node.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
                .Where(transition => transition != null)
                .ToArray();
            int ruleIndex = 0;
            foreach (StoryNodeTransitionDocument transition in transitions)
            {
                bool conditional = !transition.isDefault;
                if (conditional)
                    ruleIndex++;
                AddGraphEdge(edges, nodeIds, node.id,
                    transition.isEnd ? StoryEndGraphNodeId : transition.targetNodeId,
                    transition.isDefault ? "默认" : "条件" + ruleIndex,
                    conditional);
            }

            if (!transitions.Any(transition => transition.isDefault))
            {
                string fallbackTarget;
                string label;
                if (node.isBranch)
                {
                    fallbackTarget = string.IsNullOrWhiteSpace(node.fallbackNodeId)
                        ? StoryEndGraphNodeId
                        : node.fallbackNodeId;
                    label = "默认";
                }
                else
                {
                    int sequenceIndex = sequenceNodes.IndexOf(node);
                    fallbackTarget = sequenceIndex >= 0 && sequenceIndex + 1 < sequenceNodes.Count
                        ? sequenceNodes[sequenceIndex + 1].id
                        : StoryEndGraphNodeId;
                    label = "顺序";
                }
                AddGraphEdge(edges, nodeIds, node.id, fallbackTarget, label, false);
            }

            foreach (StoryCommandDocument command in node.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command == null)
                    continue;
                bool conditional = command.condition != null && command.condition.hasConditions;
                if (string.Equals(command.type, "jump", StringComparison.OrdinalIgnoreCase))
                    AddGraphEdge(edges, nodeIds, node.id, command.target, conditional ? "条件跳转" : "跳转命令", conditional);
                else if (string.Equals(command.type, "end", StringComparison.OrdinalIgnoreCase))
                    AddGraphEdge(edges, nodeIds, node.id, StoryEndGraphNodeId, conditional ? "条件结束" : "结束命令", conditional);
            }
        }
        return edges;
    }

    private static void AddGraphEdge(List<StoryGraphEdge> edges, HashSet<string> nodeIds,
        string sourceId, string targetId, string label, bool conditional)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
            return;
        if (!string.Equals(targetId, StoryEndGraphNodeId, StringComparison.Ordinal)
            && !nodeIds.Contains(targetId))
        {
            return;
        }

        edges.Add(new StoryGraphEdge
        {
            sourceId = sourceId,
            targetId = targetId,
            label = label,
            isConditional = conditional,
        });
    }

    private Dictionary<string, StoryGraphNodeLayout> BuildStoryGraphLayout(
        StoryDocument document,
        StoryNodeDocument[] nodes,
        List<StoryGraphEdge> edges)
    {
        Dictionary<string, StoryGraphNodeLayout> layouts = nodes.ToDictionary(
            node => node.id,
            node => new StoryGraphNodeLayout { id = node.id, node = node, level = -1 },
            StringComparer.OrdinalIgnoreCase);
        if (edges.Any(edge => string.Equals(edge.targetId, StoryEndGraphNodeId, StringComparison.Ordinal)))
            layouts[StoryEndGraphNodeId] = new StoryGraphNodeLayout { id = StoryEndGraphNodeId, level = -1 };

        string entryId = !string.IsNullOrWhiteSpace(document.entry) && layouts.ContainsKey(document.entry)
            ? document.entry
            : nodes[0].id;
        Queue<string> queue = new Queue<string>();
        layouts[entryId].level = 0;
        layouts[entryId].isReachable = true;
        queue.Enqueue(entryId);
        while (queue.Count > 0)
        {
            string sourceId = queue.Dequeue();
            int nextLevel = layouts[sourceId].level + 1;
            foreach (string targetId in edges.Where(edge => string.Equals(edge.sourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                .Select(edge => edge.targetId).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!layouts.TryGetValue(targetId, out StoryGraphNodeLayout target) || target.isReachable)
                    continue;
                target.level = nextLevel;
                target.isReachable = true;
                queue.Enqueue(targetId);
            }
        }

        int unreachableLevel = layouts.Values.Where(layout => layout.isReachable).Select(layout => layout.level).DefaultIfEmpty(0).Max() + 1;
        foreach (StoryGraphNodeLayout layout in layouts.Values.Where(layout => !layout.isReachable))
            layout.level = unreachableLevel;

        foreach (IGrouping<int, StoryGraphNodeLayout> levelGroup in layouts.Values
            .OrderBy(layout => layout.level)
            .GroupBy(layout => layout.level))
        {
            List<StoryGraphNodeLayout> levelLayouts = levelGroup
                .OrderBy(layout =>
                {
                    int index = Array.FindIndex(nodes, node => node != null && node.id == layout.id);
                    return index < 0 ? int.MaxValue : index;
                })
                .ToList();
            List<KeyValuePair<StoryGraphNodeLayout, float>> desiredPositions = new List<KeyValuePair<StoryGraphNodeLayout, float>>();
            for (int index = 0; index < levelLayouts.Count; index++)
            {
                StoryGraphNodeLayout layout = levelLayouts[index];
                float[] predecessorRows = edges
                    .Where(edge => string.Equals(edge.targetId, layout.id, StringComparison.OrdinalIgnoreCase))
                    .Select(edge => layouts.TryGetValue(edge.sourceId, out StoryGraphNodeLayout source) ? source : null)
                    .Where(source => source != null && source.level < layout.level)
                    .Select(source => source.position.y)
                    .ToArray();
                float desiredY = predecessorRows.Length > 0
                    ? predecessorRows.Average()
                    : GraphTopPadding + index * GraphRowSpacing;
                desiredPositions.Add(new KeyValuePair<StoryGraphNodeLayout, float>(layout, desiredY));
            }

            float nextY = GraphTopPadding;
            foreach (KeyValuePair<StoryGraphNodeLayout, float> item in desiredPositions.OrderBy(item => item.Value))
            {
                float y = Mathf.Max(nextY, item.Value);
                item.Key.position = new Vector2(GraphLeftPadding + item.Key.level * GraphColumnSpacing, y);
                nextY = y + GraphRowSpacing;
            }
        }
        return layouts;
    }

    private void CreateGraphEdge(
        Vector2 sourcePosition,
        Vector2 targetPosition,
        string label,
        bool conditional,
        int sourceSlot,
        bool showTargetArrow,
        int returnLaneIndex,
        float returnLaneTop)
    {
        Vector2 sourceCenter = sourcePosition + new Vector2(GraphNodeWidth * .5f, GraphNodeHeight * .5f);
        Vector2 targetCenter = targetPosition + new Vector2(GraphNodeWidth * .5f, GraphNodeHeight * .5f);
        if ((targetCenter - sourceCenter).sqrMagnitude < 1f)
        {
            Vector2 first = sourceCenter + new Vector2(GraphNodeWidth * .5f, 0f);
            Vector2 second = first + new Vector2(42f + sourceSlot * 10f, 0f);
            Vector2 third = second + new Vector2(0f, -34f - sourceSlot * 8f);
            Vector2 fourth = sourceCenter + new Vector2(42f, -GraphNodeHeight * .5f);
            CreateGraphLineSegment(first, second, conditional);
            CreateGraphLineSegment(second, third, conditional);
            CreateGraphLineSegment(third, fourth, conditional);
            if (!string.IsNullOrWhiteSpace(label))
                CreateGraphEdgeLabel(new Vector2(second.x + 8f, third.y - 13f), "↺ " + label, conditional);
            if (showTargetArrow)
                CreateGraphArrow(fourth, "▼", conditional);
            return;
        }

        if (returnLaneIndex >= 0)
        {
            Vector2 start = new Vector2(sourcePosition.x, sourceCenter.y);
            Vector2 end = new Vector2(targetPosition.x + GraphNodeWidth, targetCenter.y);
            float sourceLaneX = start.x - 28f - sourceSlot * 8f;
            float targetLaneX = end.x + 28f + sourceSlot * 8f;
            float laneY = returnLaneTop + returnLaneIndex * 22f;
            CreateGraphLineSegment(start, new Vector2(sourceLaneX, start.y), conditional);
            CreateGraphLineSegment(new Vector2(sourceLaneX, start.y), new Vector2(sourceLaneX, laneY), conditional);
            CreateGraphLineSegment(new Vector2(sourceLaneX, laneY), new Vector2(targetLaneX, laneY), conditional);
            CreateGraphLineSegment(new Vector2(targetLaneX, laneY), new Vector2(targetLaneX, end.y), conditional);
            CreateGraphLineSegment(new Vector2(targetLaneX, end.y), end, conditional);
            if (!string.IsNullOrWhiteSpace(label))
                CreateGraphEdgeLabel(new Vector2((sourceLaneX + targetLaneX) * .5f, laneY - 13f), "↩ " + label, conditional);
            if (showTargetArrow)
                CreateGraphArrow(end, "◀", conditional);
            return;
        }

        if (Mathf.Abs(targetPosition.x - sourcePosition.x) < 1f)
        {
            Vector2 start = new Vector2(sourcePosition.x + GraphNodeWidth, sourceCenter.y);
            Vector2 end = new Vector2(targetPosition.x + GraphNodeWidth, targetCenter.y);
            float laneX = start.x + 44f + sourceSlot * 12f;
            CreateGraphLineSegment(start, new Vector2(laneX, start.y), conditional);
            CreateGraphLineSegment(new Vector2(laneX, start.y), new Vector2(laneX, end.y), conditional);
            CreateGraphLineSegment(new Vector2(laneX, end.y), end, conditional);
            if (!string.IsNullOrWhiteSpace(label))
                CreateGraphEdgeLabel(new Vector2(laneX + 8f, (start.y + end.y) * .5f), label, conditional);
            if (showTargetArrow)
                CreateGraphArrow(end, "◀", conditional);
            return;
        }

        Vector2 forwardStart = new Vector2(sourcePosition.x + GraphNodeWidth, sourceCenter.y);
        Vector2 forwardEnd = new Vector2(targetPosition.x, targetCenter.y);
        float laneXForward = (forwardStart.x + forwardEnd.x) * .5f;
        CreateGraphLineSegment(forwardStart, new Vector2(laneXForward, forwardStart.y), conditional);
        CreateGraphLineSegment(new Vector2(laneXForward, forwardStart.y), new Vector2(laneXForward, forwardEnd.y), conditional);
        CreateGraphLineSegment(new Vector2(laneXForward, forwardEnd.y), forwardEnd, conditional);
        if (!string.IsNullOrWhiteSpace(label))
        {
            CreateGraphEdgeLabel(
                new Vector2(laneXForward, forwardEnd.y - 17f),
                label, conditional);
        }
        if (showTargetArrow)
            CreateGraphArrow(forwardEnd, "▶", conditional);
    }

    private void CreateGraphEdgeLabel(Vector2 position, string label, bool conditional)
    {
        float width = Mathf.Clamp(46f + (label?.Length ?? 0) * 8f, 66f, 104f);
        GameObject labelObject = new GameObject("Graph Edge Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        labelObject.transform.SetParent(graphContent, false);
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(position.x, -position.y);
        rect.sizeDelta = new Vector2(width, 22f);
        Image backgroundImage = labelObject.GetComponent<Image>();
        backgroundImage.color = new Color32(0, 9, 13, 245);
        backgroundImage.raycastTarget = false;

        Text text = CreateText("Label", rect, label, 11, TextAnchor.MiddleCenter,
            conditional ? WarningColor : HintColor,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(width - 8f, 18f));
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
    }

    private void CreateGraphArrow(Vector2 position, string arrow, bool conditional)
    {
        CreateText("Graph Edge Arrow", graphContent, arrow, 13, TextAnchor.MiddleCenter,
            conditional ? WarningColor : Cyan,
            new Vector2(0f, 1f), new Vector2(.5f, .5f),
            new Vector2(position.x, -position.y), new Vector2(18f, 18f));
    }

    private void CreateGraphLineSegment(Vector2 start, Vector2 end, bool conditional)
    {
        Vector2 delta = end - start;
        GameObject lineObject = new GameObject("Graph Edge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        lineObject.transform.SetParent(graphContent, false);
        RectTransform line = lineObject.GetComponent<RectTransform>();
        line.anchorMin = new Vector2(0f, 1f);
        line.anchorMax = new Vector2(0f, 1f);
        line.pivot = new Vector2(.5f, .5f);
        line.anchoredPosition = new Vector2((start.x + end.x) * .5f, -(start.y + end.y) * .5f);
        line.sizeDelta = new Vector2(Mathf.Max(1f, delta.magnitude), conditional ? 3f : 2f);
        line.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        Image lineImage = lineObject.GetComponent<Image>();
        lineImage.color = conditional ? WarningColor : Cyan;
        lineImage.raycastTarget = false;
    }

    private void CreateGraphNode(StoryGraphNodeLayout layout, string entryId)
    {
        GameObject cardObject = new GameObject("Graph Node " + layout.id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        cardObject.transform.SetParent(graphContent, false);
        RectTransform card = cardObject.GetComponent<RectTransform>();
        card.anchorMin = new Vector2(0f, 1f);
        card.anchorMax = new Vector2(0f, 1f);
        card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = new Vector2(layout.position.x, -layout.position.y);
        card.sizeDelta = new Vector2(GraphNodeWidth, GraphNodeHeight);

        bool isEndCard = string.Equals(layout.id, StoryEndGraphNodeId, StringComparison.Ordinal);
        bool isEntry = !isEndCard && string.Equals(layout.id, entryId, StringComparison.OrdinalIgnoreCase);
        Color cardColor = !layout.isReachable
            ? new Color32(30, 36, 38, 245)
            : isEndCard
                ? new Color32(36, 28, 8, 245)
                : layout.node != null && layout.node.isBranch
                    ? new Color32(8, 32, 42, 245)
                    : new Color32(0, 24, 32, 245);
        cardObject.GetComponent<Image>().color = cardColor;
        Outline outline = cardObject.GetComponent<Outline>();
        outline.effectColor = !layout.isReachable || isEndCard || isEntry ? WarningColor : Cyan;
        outline.effectDistance = new Vector2(2f, -2f);

        if (isEndCard)
        {
            CreateText("End", card, "结束剧情", 17, TextAnchor.MiddleCenter, WarningColor,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(156f, 34f));
            return;
        }

        string roles = (isEntry ? "入口 · " : string.Empty)
            + (layout.node.isEnding ? "结束 · " : string.Empty)
            + (layout.node.isBranch ? "分支" : "顺序");
        CreateText("Node Role", card, roles, 12, TextAnchor.MiddleLeft, layout.isReachable ? WarningColor : HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -7f), new Vector2(160f, 20f));
        Text nodeName = CreateText("Node Name", card, Shorten(GetNodeDisplayName(layout.node), 13), 15, TextAnchor.MiddleLeft,
            layout.isReachable ? Cyan : HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -30f), new Vector2(160f, 24f));
        nodeName.horizontalOverflow = HorizontalWrapMode.Wrap;
        nodeName.verticalOverflow = VerticalWrapMode.Truncate;
        CreateText("Node Id", card, Shorten(layout.isReachable ? layout.id : "不可达 · " + layout.id, 20), 10, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -57f), new Vector2(160f, 14f));
    }

    private string GetDefaultFlowDescription(StoryNodeDocument node)
    {
        StoryNodeTransitionDocument explicitDefault = (node?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .FirstOrDefault(transition => transition != null && transition.isDefault);
        if (explicitDefault != null)
            return explicitDefault.isEnd ? "结束剧情" : GetNodeDisplayName(FindNode(explicitDefault.targetNodeId));
        if (node != null && node.isBranch)
            return string.IsNullOrWhiteSpace(node.fallbackNodeId)
                ? "结束剧情"
                : GetNodeDisplayName(FindNode(node.fallbackNodeId));

        string targetNodeId = node == controller.SelectedNode
            ? controller.GetSelectedNodeDefaultFlowTarget()
            : string.Empty;
        return string.IsNullOrWhiteSpace(targetNodeId)
            ? "未明确设置"
            : GetNodeDisplayName(FindNode(targetNodeId));
    }

    private void RefreshConnections()
    {
        if (transitionContent == null)
            return;

        ClearChildren(transitionContent);
        StoryNodeDocument selectedNode = controller.SelectedNode;
        if (selectedNode == null)
            return;

        if (connectionNodeTitle != null)
            connectionNodeTitle.text = "当前剧情点：" + GetNodeDisplayName(selectedNode)
                + "    ·    规则从上到下判定，第一条满足的规则生效";

        StoryNodeTransitionDocument[] transitions = controller.SelectedNode?.transitions ?? Array.Empty<StoryNodeTransitionDocument>();
        StoryNodeTransitionDocument[] rules = transitions
            .Where(transition => transition != null && !transition.isDefault)
            .ToArray();
        float y = -8f;
        for (int index = 0; index < rules.Length; index++)
        {
            float height = CreateTransitionRuleCard(rules[index], index, y);
            y -= height + 10f;
        }

        if (rules.Length == 0)
        {
            CreateText("No Transition Rules", transitionContent,
                "尚未设置条件分支。点击“添加分支规则”，从当前剧情点的选择中建立后续走向。",
                15, TextAnchor.MiddleCenter, HintColor,
                new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, y - 32f), new Vector2(800f, 54f));
            y -= 70f;
        }

        y -= CreateDefaultFlowCard(y) + 12f;
        transitionContent.sizeDelta = new Vector2(0f, Mathf.Max(12f, -y));
    }

    private float CreateTransitionRuleCard(StoryNodeTransitionDocument transition, int ruleIndex, float y)
    {
        StoryConditionClauseDocument[] clauses = transition?.condition?.clauses ?? Array.Empty<StoryConditionClauseDocument>();
        int conditionCount = clauses.Sum(clause => clause?.conditions?.Length ?? 0);
        float height = 106f + Mathf.Max(1, conditionCount) * 38f;

        RectTransform card = CreateRuleCard("Branch Rule " + (ruleIndex + 1), y, height);
        CreateText("Rule Heading", card, "分支规则 " + (ruleIndex + 1), 17, TextAnchor.MiddleLeft, WarningColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(180f, 26f));
        CreateActionButton(card, "上移", new Vector2(-188f, -8f), new Vector2(52f, 26f),
            () => MoveTransition(transition.transitionId, false), TextAnchor.UpperRight);
        CreateActionButton(card, "下移", new Vector2(-130f, -8f), new Vector2(52f, 26f),
            () => MoveTransition(transition.transitionId, true), TextAnchor.UpperRight);
        CreateActionButton(card, "删除规则", new Vector2(-14f, -8f), new Vector2(104f, 26f),
            () => DeleteTransition(transition.transitionId), TextAnchor.UpperRight);

        float rowY = -42f;
        for (int clauseIndex = 0; clauseIndex < clauses.Length; clauseIndex++)
        {
            StoryConditionDocument[] conditions = clauses[clauseIndex]?.conditions ?? Array.Empty<StoryConditionDocument>();
            for (int conditionIndex = 0; conditionIndex < conditions.Length; conditionIndex++)
            {
                string connector = clauseIndex > 0 && conditionIndex == 0 ? "或者" : conditionIndex > 0 ? "并且" : "如果";
                CreateConditionRow(card, transition, conditions[conditionIndex], clauseIndex, conditionIndex, connector, rowY);
                rowY -= 38f;
            }
        }

        CreateActionButton(card, "＋ 且条件", new Vector2(14f, rowY - 2f), new Vector2(92f, 26f),
            () => AddRuleCondition(transition.transitionId, "AND"));
        CreateActionButton(card, "＋ 或条件组", new Vector2(114f, rowY - 2f), new Vector2(104f, 26f),
            () => AddRuleCondition(transition.transitionId, "OR"));

        CreateText("Then Label", card, "则前往", 14, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(246f, rowY - 2f), new Vector2(62f, 26f));
        CreateTargetDropdown(card, transition, new Vector2(308f, rowY - 2f), new Vector2(490f, 26f));
        return height;
    }

    private RectTransform CreateRuleCard(string name, float y, float height)
    {
        GameObject cardObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        cardObject.transform.SetParent(transitionContent, false);
        RectTransform card = cardObject.GetComponent<RectTransform>();
        card.anchorMin = new Vector2(0f, 1f);
        card.anchorMax = new Vector2(1f, 1f);
        card.pivot = new Vector2(.5f, 1f);
        card.anchoredPosition = new Vector2(0f, y);
        card.sizeDelta = new Vector2(-12f, height);
        cardObject.GetComponent<Image>().color = new Color32(0, 20, 28, 230);
        Outline outline = cardObject.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .38f);
        outline.effectDistance = new Vector2(1f, -1f);
        return card;
    }

    private void CreateConditionRow(
        RectTransform card,
        StoryNodeTransitionDocument transition,
        StoryConditionDocument condition,
        int clauseIndex,
        int conditionIndex,
        string connector,
        float y)
    {
        CreateText("Condition Connector", card, connector, 14, TextAnchor.MiddleCenter,
            connector == "或者" ? WarningColor : Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, y), new Vector2(54f, 28f));

        WorkshopStoryChoiceOption[] options = controller.GetSelectedNodeChoiceOptions()?.ToArray()
            ?? Array.Empty<WorkshopStoryChoiceOption>();
        Dropdown dropdown = CreateDropdown(card, new Vector2(72f, y), new Vector2(570f, 28f), optionIndex =>
        {
            if (optionIndex < 0 || optionIndex >= options.Length)
                return;
            WorkshopStoryChoiceOption option = options[optionIndex];
            if (!controller.UpdateSelectedNodeTransitionCondition(transition.transitionId, clauseIndex, conditionIndex,
                    option.commandId, option.choiceId, option.optionId, out string error))
            {
                Hintbox.OpenHintboxWithContent(error, 16);
                return;
            }
            RefreshConnections();
        });
        if (dropdown != null)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(options.Select(option => Shorten(option.displayName, 42)).ToList());
            int selectedIndex = Array.FindIndex(options, option => option != null
                && string.Equals(option.commandId, condition?.commandId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(option.choiceId, condition?.choiceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(option.optionId, condition?.optionId, StringComparison.OrdinalIgnoreCase));
            dropdown.SetValueWithoutNotify(Mathf.Max(0, selectedIndex));
            dropdown.interactable = options.Length > 0;
            SetSelectorValueText(CreateSelectorValueText(dropdown), dropdown, "当前剧情点没有可用选项");
        }

        CreateActionButton(card, condition != null && condition.negated ? "不是" : "是", new Vector2(650f, y), new Vector2(58f, 28f),
            () => ToggleRuleCondition(transition.transitionId, clauseIndex, conditionIndex));
        CreateActionButton(card, "删除", new Vector2(716f, y), new Vector2(70f, 28f),
            () => RemoveRuleCondition(transition.transitionId, clauseIndex, conditionIndex));
    }

    private void CreateTargetDropdown(RectTransform card, StoryNodeTransitionDocument transition, Vector2 position, Vector2 size)
    {
        string currentNodeId = controller.SelectedNode?.id;
        List<TransitionTargetOption> targets = new List<TransitionTargetOption>
        {
            new TransitionTargetOption
            {
                targetType = "end",
                displayName = "结束整个剧情",
            },
        };
        targets.AddRange((controller.SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null
                && (!transition.isDefault
                    || !string.Equals(node.id, currentNodeId, StringComparison.OrdinalIgnoreCase)))
            .Select(node => new TransitionTargetOption
            {
                targetType = "node",
                nodeId = node.id,
                displayName = string.Equals(node.id, currentNodeId, StringComparison.OrdinalIgnoreCase)
                    ? "重新进入当前剧情点 · " + GetNodeDisplayName(node)
                    : GetNodeDisplayName(node),
            }));
        Dropdown dropdown = CreateDropdown(card, position, size, optionIndex =>
        {
            if (optionIndex < 0 || optionIndex >= targets.Count)
                return;
            TransitionTargetOption target = targets[optionIndex];
            if (!controller.UpdateSelectedNodeTransitionTarget(
                    transition.transitionId, target.targetType, target.nodeId, out string error))
            {
                Hintbox.OpenHintboxWithContent(error, 16);
                return;
            }
            if (target.targetType == "node"
                && string.Equals(target.nodeId, currentNodeId, StringComparison.OrdinalIgnoreCase))
            {
                Hintbox.OpenHintboxWithContent("该分支会重新播放当前剧情点，并重新记录本次选择。", 16);
            }
            RefreshConnections();
        });
        if (dropdown == null)
            return;

        dropdown.ClearOptions();
        dropdown.AddOptions(targets.Select(target => target.displayName).ToList());
        int selectedIndex = targets.FindIndex(target => transition.isEnd
            ? target.targetType == "end"
            : target.targetType == "node"
                && string.Equals(target.nodeId, transition.targetNodeId, StringComparison.OrdinalIgnoreCase));
        dropdown.SetValueWithoutNotify(Mathf.Max(0, selectedIndex));
        dropdown.interactable = targets.Count > 0;
        SetSelectorValueText(CreateSelectorValueText(dropdown), dropdown, "暂无可选剧情点");
    }

    private float CreateDefaultFlowCard(float y)
    {
        const float height = 74f;
        RectTransform card = CreateRuleCard("Default Story Flow", y, height);
        StoryNodeTransitionDocument explicitDefault = (controller.SelectedNode?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .FirstOrDefault(transition => transition != null && transition.isDefault);
        string targetNodeId = explicitDefault?.targetNodeId ?? controller.GetSelectedNodeDefaultFlowTarget();
        string targetName = explicitDefault != null && explicitDefault.isEnd
            ? "结束整个剧情"
            : string.IsNullOrWhiteSpace(targetNodeId)
            ? "剧情在此结束"
            : GetNodeDisplayName(FindNode(targetNodeId));

        CreateText("Default Heading", card, "所有规则都不满足时", 16, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(220f, 24f));
        string defaultDescription = explicitDefault == null
            ? "按默认顺序继续  →  " + targetName
            : explicitDefault.isEnd
                ? "结束整个剧情"
                : "前往指定剧情点  →  " + targetName;
        connectionDefaultText = CreateText("Default Target", card, defaultDescription,
            15, TextAnchor.MiddleLeft, explicitDefault == null ? HintColor : WarningColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -38f),
            new Vector2(explicitDefault == null ? 610f : 270f, 26f));
        if (explicitDefault != null)
        {
            CreateTargetDropdown(card, explicitDefault, new Vector2(286f, -38f), new Vector2(350f, 26f));
            if (!explicitDefault.isAutoGenerated)
            {
                CreateActionButton(card, "恢复默认顺序", new Vector2(-14f, -28f), new Vector2(126f, 28f),
                    () => RestoreDefaultFlow(explicitDefault.transitionId), TextAnchor.UpperRight);
            }
        }
        else
        {
            CreateActionButton(card, "指定默认后续", new Vector2(-14f, -28f), new Vector2(126f, 28f),
                CreateExplicitDefaultFlow, TextAnchor.UpperRight);
        }
        return height;
    }

    private void CreateExplicitDefaultFlow()
    {
        if (!controller.AddSelectedNodeDefaultTransition(out _, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshConnections();
    }

    private void MoveTransition(string transitionId, bool moveDown)
    {
        if (!controller.MoveSelectedNodeTransition(transitionId, moveDown, out string error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshConnections();
    }

    private void DeleteTransition(string transitionId)
    {
        if (!controller.RemoveSelectedNodeTransition(transitionId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshConnections();
    }

    private void AddRuleCondition(string transitionId, string connector)
    {
        if (!controller.AddSelectedNodeTransitionCondition(transitionId, connector, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshConnections();
    }

    private void ToggleRuleCondition(string transitionId, int clauseIndex, int conditionIndex)
    {
        if (!controller.ToggleSelectedNodeTransitionConditionNegated(transitionId, clauseIndex, conditionIndex, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshConnections();
    }

    private void RemoveRuleCondition(string transitionId, int clauseIndex, int conditionIndex)
    {
        if (!controller.RemoveSelectedNodeTransitionCondition(transitionId, clauseIndex, conditionIndex, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshConnections();
    }

    private void RestoreDefaultFlow(string transitionId)
    {
        if (!controller.RemoveSelectedNodeTransition(transitionId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshConnections();
    }

    private StoryNodeDocument FindNode(string nodeId)
    {
        return (controller.SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
            .FirstOrDefault(node => node != null && string.Equals(node.id, nodeId, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetNodeDisplayName(StoryNodeDocument node)
    {
        if (node == null)
            return "目标不存在";
        return string.IsNullOrWhiteSpace(node.displayName) ? node.id : node.displayName;
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value ?? string.Empty;
        return value.Substring(0, maxLength) + "…";
    }

    private void SetInputFieldValue(IInputField input, string value, bool interactable)
    {
        if (input == null)
            return;

        input.gameObject.SetActive(interactable);
        InputField nativeInput = input.GetComponent<InputField>();
        if (nativeInput != null)
        {
            nativeInput.interactable = interactable;
            input.SetInputString(value ?? string.Empty);
        }
    }

    private void CreateListButton(RectTransform parent, string label, bool selected, int index, Action callback)
    {
        if (listButtonPrefab == null)
            return;

        GameObject item = Instantiate(listButtonPrefab, parent);
        item.name = "Story List Button";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -8f - index * 42f);
        rect.sizeDelta = new Vector2(0f, 36f);

        IButton button = item.GetComponent<IButton>();
        button.onPointerClickEvent = new UnityEngine.Events.UnityEvent();
        button.onPointerEnterEvent = new UnityEngine.Events.UnityEvent();
        button.onPointerExitEvent = new UnityEngine.Events.UnityEvent();
        button.onPointerClickEvent.AddListener(callback.Invoke);
        ConfigureListButtonVisual(button, selected);

        GameObject focusFrame = CreateListFocusFrame(item.transform);
        focusFrame.SetActive(selected);
        button.onPointerEnterEvent.AddListener(() => focusFrame.SetActive(true));
        button.onPointerExitEvent.AddListener(() => focusFrame.SetActive(selected));

        Text text = item.GetComponentInChildren<Text>();
        text.font = font;
        text.fontSize = 17;
        text.fontStyle = FontStyle.Normal;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = selected ? WarningColor : Cyan;
        text.raycastTarget = false;
        text.text = label;
    }

    private static void ConfigureListButtonVisual(IButton button, bool selected)
    {
        button.image.sprite = button.initSprite;
        button.button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(.72f, .82f, .88f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = .08f;
        button.button.colors = colors;
    }

    private static GameObject CreateListFocusFrame(Transform parent)
    {
        GameObject frame = new GameObject("Focus Frame", typeof(RectTransform));
        frame.transform.SetParent(parent, false);
        RectTransform rect = frame.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(2f, 2f);
        rect.offsetMax = new Vector2(-2f, -2f);

        Color glowColor = new Color(Cyan.r, Cyan.g, Cyan.b, .28f);
        CreateFocusEdge(frame.transform, "Glow Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), Vector2.zero, glowColor);
        CreateFocusEdge(frame.transform, "Glow Bottom", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f), glowColor);
        CreateFocusEdge(frame.transform, "Glow Left", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), glowColor);
        CreateFocusEdge(frame.transform, "Glow Right", new Vector2(1f, 0f), Vector2.one, new Vector2(-3f, 0f), Vector2.zero, glowColor);

        CreateFocusEdge(frame.transform, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), Vector2.zero, Cyan);
        CreateFocusEdge(frame.transform, "Bottom", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f), Cyan);
        CreateFocusEdge(frame.transform, "Left", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(1f, 0f), Cyan);
        CreateFocusEdge(frame.transform, "Right", new Vector2(1f, 0f), Vector2.one, new Vector2(-1f, 0f), Vector2.zero, Cyan);
        return frame;
    }

    private static void CreateFocusEdge(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject edge = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        edge.transform.SetParent(parent, false);
        RectTransform rect = edge.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = edge.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private void CreateHint(RectTransform parent, string value)
    {
        CreateText("Hint", parent, value, 15, TextAnchor.MiddleCenter, HintColor,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -64f), new Vector2(200f, 42f));
        parent.sizeDelta = new Vector2(0f, 96f);
    }

    private Text CreateText(string name, Transform parent, string value, int size, TextAnchor alignment, Color color,
        Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 dimensions)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text text = obj.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private static void ClearChildren(Transform parent)
    {
        foreach (Transform child in parent.Cast<Transform>().ToArray())
            Destroy(child.gameObject);
    }

    private Dropdown CreateDropdown(Transform parent, Vector2 position, Vector2 dimensions, UnityAction<int> onChanged)
    {
        if (dropdownPrefab == null)
            return null;

        GameObject item = Instantiate(dropdownPrefab, parent);
        item.name = "Story Transition Selector";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Dropdown dropdown = item.GetComponent<Dropdown>();
        if (dropdown == null)
            return null;

        dropdown.onValueChanged = new Dropdown.DropdownEvent();
        dropdown.onValueChanged.AddListener(onChanged);
        if (dropdown.captionText != null)
        {
            dropdown.captionText.font = font;
            dropdown.captionText.fontSize = 13;
            dropdown.captionText.alignment = TextAnchor.MiddleLeft;
            dropdown.captionText.color = Cyan;
        }
        if (dropdown.itemText != null)
        {
            dropdown.itemText.font = font;
            dropdown.itemText.fontSize = 13;
            dropdown.itemText.color = Cyan;
        }
        return dropdown;
    }

    private Text CreateSelectorValueText(Dropdown dropdown)
    {
        if (dropdown == null)
            return null;

        // The Workshop dropdown prefab already renders captionText. This editor adds a wider,
        // non-clipped value layer, so the original caption must be hidden to avoid double text.
        if (dropdown.captionText != null)
            dropdown.captionText.enabled = false;

        Text text = CreateText("Selector Value", dropdown.transform, string.Empty, 13, TextAnchor.MiddleLeft, Cyan,
            Vector2.zero, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8f, 1f);
        rect.offsetMax = new Vector2(-26f, -1f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.transform.SetAsLastSibling();
        return text;
    }

    private static void SetSelectorValueText(Text text, Dropdown dropdown, string emptyText)
    {
        if (text == null)
            return;

        if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
        {
            text.text = emptyText ?? string.Empty;
            return;
        }

        int value = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
        text.text = dropdown.options[value]?.text ?? (emptyText ?? string.Empty);
    }

    private GameObject FindWorkshopActionButtonPrefab()
    {
        GameObject workshopPanel = ResourceManager.instance.GetPanel("Workshop");
        if (workshopPanel == null)
            return null;

        IButton button = workshopPanel.GetComponentsInChildren<IButton>(true)
            .FirstOrDefault(value => value.GetComponentInChildren<Text>(true)?.text == "导出 mod");
        return button?.gameObject;
    }

    private GameObject FindWorkshopDropdownPrefab()
    {
        GameObject workshopPanel = ResourceManager.instance.GetPanel("Workshop");
        return workshopPanel?.GetComponentsInChildren<IDropdown>(true)
            .FirstOrDefault()?.gameObject;
    }

    private void FindWorkshopPetInputFieldPrefabs()
    {
        GameObject workshopPanel = ResourceManager.instance.GetPanel("Workshop");
        if (workshopPanel == null)
            return;

        IInputField[] inputs = workshopPanel.GetComponentsInChildren<IInputField>(true);
        petNameInputFieldPrefab = inputs
            .FirstOrDefault(value => value.placeHolderText != null && value.placeHolderText.text == "输入名字")
            ?.gameObject;
        petDescriptionInputFieldPrefab = inputs
            .FirstOrDefault(value => value.placeHolderText != null && value.placeHolderText.text == "输入叙述")
            ?.gameObject;
    }

    private void ApplyProjectFrame(Image target)
    {
        CacheProjectFrameTemplate();
        if (projectFrameImage != null)
        {
            target.sprite = projectFrameImage.sprite;
            target.type = projectFrameImage.type;
            target.preserveAspect = projectFrameImage.preserveAspect;
            target.fillCenter = projectFrameImage.fillCenter;
            target.pixelsPerUnitMultiplier = projectFrameImage.pixelsPerUnitMultiplier;
        }
        target.color = Color.black;

        Outline outline = target.GetComponent<Outline>() ?? target.gameObject.AddComponent<Outline>();
        if (projectFrameOutline == null)
        {
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = true;
            return;
        }

        outline.effectColor = projectFrameOutline.effectColor;
        outline.effectDistance = projectFrameOutline.effectDistance;
        outline.useGraphicAlpha = projectFrameOutline.useGraphicAlpha;
    }

    private void CacheProjectFrameTemplate()
    {
        if (projectFrameImage != null)
            return;

        GameObject missionPanel = ResourceManager.instance.GetPanel("Mission");
        if (missionPanel == null)
            return;

        projectFrameImage = missionPanel.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(image => image.gameObject.name == "Background" && image.GetComponent<Outline>() != null);
        projectFrameOutline = projectFrameImage?.GetComponent<Outline>();
    }
}
