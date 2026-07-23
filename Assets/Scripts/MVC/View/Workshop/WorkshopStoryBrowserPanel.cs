using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleFileBrowser;
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

    private sealed class NpcImageOption
    {
        public string path;
        public string name;
        public bool isMod;
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
    private static readonly Color SavedColor = new Color32(119, 224, 113, 255);
    private static readonly Color WarningColor = new Color32(255, 232, 71, 255);

    private readonly WorkshopStoryBrowserController controller = new WorkshopStoryBrowserController(
        new WorkshopStoryBrowserModel(new WorkshopStoryRepository()));

    private RectTransform storyContent;
    private RectTransform modalLayer;
    private RectTransform nodeManagerOverlay;
    private RectTransform nodeManagerContent;
    private ScrollRect nodeManagerScroll;
    private RectTransform connectionOverlay;
    private RectTransform transitionContent;
    private RectTransform graphOverlay;
    private RectTransform graphContent;
    private RectTransform contentManagerOverlay;
    private RectTransform sceneManagerOverlay;
    private RectTransform sceneManagerContent;
    private RectTransform actorManagerOverlay;
    private RectTransform actorManagerContent;
    private RectTransform actorResourceOverlay;
    private RectTransform actorResourceContent;
    private RectTransform sourceExportOverlay;
    private RectTransform sourceRewardPickerOverlay;
    private RectTransform sourceRewardContent;
    private RectTransform actorIndependentIconControls;
    private RectTransform actorCropControls;
    private IInputField actorNameInput;
    private IInputField actorResourceSearchInput;
    private Dropdown actorFacingDropdown;
    private Dropdown actorIconModeDropdown;
    private Text actorFacingValueText;
    private Text actorIconModeValueText;
    private Text actorResourceSourceText;
    private Text actorResourcePageText;
    private Text actorPathText;
    private Text actorIconPathText;
    private Image actorPortraitPreview;
    private Image actorIconPreview;
    private IInputField sceneNameInput;
    private Image sceneBackgroundPreview;
    private Text sceneBackgroundPathText;
    private Text sceneBgmPathText;
    private Text connectionNodeTitle;
    private Text connectionDefaultText;
    private Text nodeManagerCountText;
    private Text nodeManagerDetailIdText;
    private Text nodeManagerDetailStatsText;
    private Text nodeManagerDetailDefaultText;
    private RectTransform nodeManagerDetailBadgeRoot;
    private IInputField nodeManagerNameInput;
    private Dropdown nodeManagerFlowFilterDropdown;
    private Dropdown nodeManagerMarkerFilterDropdown;
    private Text nodeManagerFlowFilterValueText;
    private Text nodeManagerMarkerFilterValueText;
    private IInputField nodeManagerSearchInput;
    private IInputField sourceExportTitleInput;
    private IInputField sourceRewardSearchInput;
    private readonly IInputField[] sourceRewardAmountInputs = new IInputField[3];
    private readonly Text[] sourceRewardLabels = new Text[3];
    private readonly int[] sourceRewardIds = new int[3];
    private Dropdown sourceMissionTypeDropdown;
    private Dropdown sourceReplayDropdown;
    private Dropdown sourceRewardModeDropdown;
    private Text sourceMissionTypeValueText;
    private Text sourceReplayValueText;
    private Text sourceRewardModeValueText;
    private Text sourceReplayLabel;
    private Text sourceRewardModeLabel;
    private Text sourceRewardPageText;
    private Text storyStatusText;
    private Text storyOverviewText;
    private Text storyStructureText;
    private Text storyResourceText;
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
    private string nodeManagerSearchQuery = string.Empty;
    private int nodeManagerFlowFilterIndex;
    private int nodeManagerMarkerFilterIndex;
    private float nodeManagerScrollPosition = 1f;
    private bool returnToNodeManagerAfterConnection;
    private string selectedCustomSceneId;
    private string selectedActorId;
    private bool actorResourceIsMod;
    private bool selectingActorIcon;
    private int actorResourcePage;
    private int activeSourceRewardSlot;
    private int sourceRewardPage;
    private string sourceRewardQuery = string.Empty;
    private bool hasBuilt;
    private WorkshopStoryPointResourcePicker connectionResourcePicker;

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
        connectionResourcePicker = new WorkshopStoryPointResourcePicker(
            transform, actionButtonPrefab, listButtonPrefab, petNameInputFieldPrefab, font);
        Reload();
    }

    public override void ClosePanel()
    {
        if (sourceRewardPickerOverlay != null && sourceRewardPickerOverlay.gameObject.activeSelf)
        {
            CloseSourceRewardPicker();
            return;
        }
        if (sourceExportOverlay != null && sourceExportOverlay.gameObject.activeSelf)
        {
            CloseSourceExport();
            return;
        }
        if (graphOverlay != null && graphOverlay.gameObject.activeSelf)
        {
            CloseGraphViewer();
            return;
        }
        if (actorResourceOverlay != null && actorResourceOverlay.gameObject.activeSelf)
        {
            CloseActorResourcePicker();
            return;
        }
        if (actorManagerOverlay != null && actorManagerOverlay.gameObject.activeSelf)
        {
            CloseActorManager();
            return;
        }
        if (sceneManagerOverlay != null && sceneManagerOverlay.gameObject.activeSelf)
        {
            CloseSceneManager();
            return;
        }
        if (contentManagerOverlay != null && contentManagerOverlay.gameObject.activeSelf)
        {
            CloseContentManager();
            return;
        }
        if (connectionOverlay != null && connectionOverlay.gameObject.activeSelf)
        {
            CloseConnectionEditor();
            return;
        }
        if (nodeManagerOverlay != null && nodeManagerOverlay.gameObject.activeSelf)
        {
            CloseNodeManager();
            return;
        }

        base.ClosePanel();
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
        RectTransform infoSection = CreateSection("剧本信息", new Vector2(270f, -76f), new Vector2(632f, 402f));

        CreateActionButton(storySection, "新建", new Vector2(32f, -50f), new Vector2(80f, 28f), CreateStory);
        CreateActionButton(storySection, "删除", new Vector2(124f, -50f), new Vector2(80f, 28f), DeleteStory);
        CreateActionButton(storySection, "复制为新剧本", new Vector2(32f, -84f), new Vector2(172f, 28f), CopyStory);
        storyContent = CreateScrollContent(storySection, new Vector2(14f, 14f), new Vector2(-14f, -120f));

        CreateText("Title Label", infoSection, "标题：", 15, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -50f), new Vector2(52f, 26f));
        storyTitleInput = CreateInputField(petNameInputFieldPrefab, infoSection, "Story Title Input", "剧本标题", "未命名剧本",
            new Vector2(70f, -50f), new Vector2(270f, 28f), OnStoryTitleEdited);
        InputField nativeTitleInput = storyTitleInput?.GetComponent<InputField>();
        if (nativeTitleInput?.textComponent != null)
            nativeTitleInput.textComponent.alignment = TextAnchor.MiddleCenter;
        CreateText("Summary Label", infoSection, "简介：", 15, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -84f), new Vector2(52f, 26f));
        storySummaryInput = CreateInputField(petDescriptionInputFieldPrefab, infoSection, "Story Summary Input", "剧本简介", "暂无简介",
            new Vector2(70f, -84f), new Vector2(270f, 116f), OnStorySummaryEdited);
        InputField nativeSummaryInput = storySummaryInput?.GetComponent<InputField>();
        if (nativeSummaryInput != null)
        {
            nativeSummaryInput.lineType = InputField.LineType.MultiLineNewline;
            if (nativeSummaryInput.textComponent != null)
            {
                nativeSummaryInput.textComponent.alignment = TextAnchor.UpperLeft;
                nativeSummaryInput.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
                nativeSummaryInput.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            }
            if (nativeSummaryInput.placeholder is Text summaryPlaceholder)
                summaryPlaceholder.alignment = TextAnchor.UpperLeft;
        }

        storyStatusText = CreateText("Story Status", infoSection, string.Empty, 13, TextAnchor.UpperLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(358f, -50f), new Vector2(170f, 40f));
        CreateActionButton(infoSection, "保存到 Mod", new Vector2(516f, -56f),
            new Vector2(98f, 28f), SaveStory);
        IButton manageButton = CreateActionButton(infoSection, "管理剧情点", new Vector2(358f, -100f),
            new Vector2(256f, 62f), OpenNodeManager);
        EmphasizePrimaryAction(manageButton);
        CreateActionButton(infoSection, "自制内容", new Vector2(358f, -170f),
            new Vector2(80f, 30f), OpenContentManager);
        CreateActionButton(infoSection, "查看结构", new Vector2(446f, -170f),
            new Vector2(80f, 30f), OpenGraphViewer);
        CreateActionButton(infoSection, "剧本预览", new Vector2(534f, -170f),
            new Vector2(80f, 30f), PreviewStory);
        if (controller.CanExportSource)
        {
            CreateText("Source Export Label", infoSection, "源码开发", 14, TextAnchor.MiddleLeft, Cyan,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(358f, -214f), new Vector2(74f, 30f));
            CreateActionButton(infoSection, "导出为……", new Vector2(438f, -214f),
                new Vector2(176f, 30f), OpenSourceExport);
            Text sourceExportHint = CreateText("Source Export Hint", infoSection,
                "仅 Unity Editor · 可导出支线 / 日常 / 活动任务", 11, TextAnchor.MiddleCenter, HintColor,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(358f, -248f), new Vector2(256f, 22f));
            sourceExportHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            sourceExportHint.verticalOverflow = VerticalWrapMode.Truncate;
        }

        GameObject overviewObject = new GameObject("Story Overview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overviewObject.transform.SetParent(infoSection, false);
        RectTransform overviewPanel = overviewObject.GetComponent<RectTransform>();
        overviewPanel.anchorMin = new Vector2(0f, 1f);
        overviewPanel.anchorMax = new Vector2(1f, 1f);
        overviewPanel.pivot = new Vector2(.5f, 1f);
        overviewPanel.anchoredPosition = new Vector2(0f, -306f);
        overviewPanel.sizeDelta = new Vector2(-28f, 80f);
        overviewObject.GetComponent<Image>().color = new Color32(0, 18, 25, 235);
        Outline overviewOutline = overviewObject.GetComponent<Outline>();
        overviewOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .38f);
        overviewOutline.effectDistance = new Vector2(1f, -1f);
        CreateText("Story Overview Heading", overviewPanel, "概览", 18, TextAnchor.MiddleCenter, Cyan,
            new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(10f, 0f), new Vector2(76f, 42f));
        RectTransform scaleCard = CreateOverviewCard(overviewPanel, "Scale Overview Card", 92f, 164f);
        RectTransform resourceCard = CreateOverviewCard(overviewPanel, "Resource Overview Card", 262f, 188f);
        RectTransform structureCard = CreateOverviewCard(overviewPanel, "Structure Overview Card", 456f, 138f);

        storyOverviewText = CreateText("Story Overview Scale", scaleCard, string.Empty, 12, TextAnchor.MiddleCenter, HintColor,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(150f, 58f));
        storyOverviewText.horizontalOverflow = HorizontalWrapMode.Overflow;
        storyOverviewText.verticalOverflow = VerticalWrapMode.Truncate;
        storyResourceText = CreateText("Story Resource Summary", resourceCard, string.Empty, 12, TextAnchor.MiddleCenter, HintColor,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(174f, 58f));
        storyResourceText.horizontalOverflow = HorizontalWrapMode.Overflow;
        storyResourceText.verticalOverflow = VerticalWrapMode.Truncate;
        storyStructureText = CreateText("Story Structure Health", structureCard, string.Empty, 12, TextAnchor.MiddleCenter, HintColor,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(124f, 58f));
        storyStructureText.horizontalOverflow = HorizontalWrapMode.Overflow;
        storyStructureText.verticalOverflow = VerticalWrapMode.Truncate;
        BuildModalLayer(root);
        BuildNodeManager(modalLayer);
        BuildConnectionEditor(modalLayer);
        BuildGraphViewer(modalLayer);
        BuildContentManager(modalLayer);
        BuildSceneManager(modalLayer);
        BuildActorManager(modalLayer);
        BuildActorResourcePicker(modalLayer);
        BuildSourceExport(modalLayer);
        BuildSourceRewardPicker(modalLayer);
        modalLayer.gameObject.SetActive(false);
    }

    private static RectTransform CreateOverviewCard(RectTransform parent, string name, float x, float width)
    {
        GameObject cardObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        cardObject.transform.SetParent(parent, false);
        RectTransform card = cardObject.GetComponent<RectTransform>();
        card.anchorMin = new Vector2(0f, .5f);
        card.anchorMax = new Vector2(0f, .5f);
        card.pivot = new Vector2(0f, .5f);
        card.anchoredPosition = new Vector2(x, 0f);
        card.sizeDelta = new Vector2(width, 64f);
        Image image = cardObject.GetComponent<Image>();
        image.color = new Color32(7, 32, 40, 218);
        image.raycastTarget = false;
        return card;
    }

    private void BuildModalLayer(RectTransform root)
    {
        GameObject layerObject = new GameObject("Story Browser Modal Layer",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        layerObject.transform.SetParent(root, false);
        layerObject.transform.SetAsLastSibling();

        modalLayer = layerObject.GetComponent<RectTransform>();
        modalLayer.anchorMin = Vector2.zero;
        modalLayer.anchorMax = Vector2.one;
        modalLayer.offsetMin = Vector2.zero;
        modalLayer.offsetMax = Vector2.zero;

        Image dimmer = layerObject.GetComponent<Image>();
        dimmer.color = new Color32(0, 0, 0, 172);
        dimmer.raycastTarget = true;
    }

    private void EmphasizePrimaryAction(IButton button)
    {
        if (button == null)
            return;

        Text text = button.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.fontSize = 20;
            text.color = WarningColor;
        }

        Outline outline = button.gameObject.GetComponent<Outline>();
        if (outline == null)
            outline = button.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(WarningColor.r, WarningColor.g, WarningColor.b, .72f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

    private void BuildNodeManager(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story Node Manager",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        nodeManagerOverlay = overlayObject.GetComponent<RectTransform>();
        nodeManagerOverlay.anchorMin = new Vector2(.5f, .5f);
        nodeManagerOverlay.anchorMax = new Vector2(.5f, .5f);
        nodeManagerOverlay.pivot = new Vector2(.5f, .5f);
        nodeManagerOverlay.anchoredPosition = Vector2.zero;
        nodeManagerOverlay.sizeDelta = new Vector2(892f, 472f);

        Image overlay = overlayObject.GetComponent<Image>();
        overlay.color = new Color32(0, 8, 12, 252);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .86f);
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("Node Manager Heading", nodeManagerOverlay, "剧情点管理", 22, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -18f), new Vector2(260f, 30f));
        CreateText("Node Search Label", nodeManagerOverlay, "搜索：", 14, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -52f), new Vector2(54f, 28f));
        nodeManagerSearchInput = CreateInputField(petNameInputFieldPrefab, nodeManagerOverlay,
            "Node Search Input", "名称或 ID", string.Empty,
            new Vector2(72f, -52f), new Vector2(210f, 28f), OnNodeManagerSearchChanged);
        InputField nativeSearchInput = nodeManagerSearchInput?.GetComponent<InputField>();
        if (nativeSearchInput != null)
        {
            nativeSearchInput.onValueChanged = new InputField.OnChangeEvent();
            nativeSearchInput.onValueChanged.AddListener(OnNodeManagerSearchChanged);
        }

        nodeManagerFlowFilterDropdown = CreateDropdown(nodeManagerOverlay, new Vector2(294f, -52f),
            new Vector2(122f, 28f), OnNodeManagerFlowFilterChanged);
        if (nodeManagerFlowFilterDropdown != null)
        {
            nodeManagerFlowFilterDropdown.ClearOptions();
            nodeManagerFlowFilterDropdown.AddOptions(new List<string> { "全部流程", "默认流程", "分支剧情" });
            nodeManagerFlowFilterValueText = CreateSelectorValueText(nodeManagerFlowFilterDropdown);
            SetSelectorValueText(nodeManagerFlowFilterValueText, nodeManagerFlowFilterDropdown, "全部流程");
        }
        nodeManagerMarkerFilterDropdown = CreateDropdown(nodeManagerOverlay, new Vector2(428f, -52f),
            new Vector2(112f, 28f), OnNodeManagerMarkerFilterChanged);
        if (nodeManagerMarkerFilterDropdown != null)
        {
            nodeManagerMarkerFilterDropdown.ClearOptions();
            nodeManagerMarkerFilterDropdown.AddOptions(new List<string> { "全部标记", "入口", "结束" });
            nodeManagerMarkerFilterValueText = CreateSelectorValueText(nodeManagerMarkerFilterDropdown);
            SetSelectorValueText(nodeManagerMarkerFilterValueText, nodeManagerMarkerFilterDropdown, "全部标记");
        }
        nodeManagerCountText = CreateText("Node Manager Count", nodeManagerOverlay, string.Empty,
            13, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(552f, -52f), new Vector2(166f, 28f));
        CreateActionButton(nodeManagerOverlay, "保存", new Vector2(-58f, -52f),
            new Vector2(88f, 28f), SaveStory, TextAnchor.UpperRight);
        CreateModalCloseButton(nodeManagerOverlay, CloseNodeManager);

        CreateActionButton(nodeManagerOverlay, "新建", new Vector2(18f, -88f), new Vector2(84f, 28f), CreateNode);
        CreateActionButton(nodeManagerOverlay, "复制并新增", new Vector2(110f, -88f), new Vector2(114f, 28f), CopyNode);
        CreateActionButton(nodeManagerOverlay, "删除", new Vector2(232f, -88f), new Vector2(84f, 28f), DeleteNode);
        CreateActionButton(nodeManagerOverlay, "设为入口", new Vector2(324f, -88f), new Vector2(104f, 28f), SetEntryNode);
        CreateActionButton(nodeManagerOverlay, "编辑剧情点", new Vector2(436f, -88f), new Vector2(122f, 28f), OpenNodeEditor);
        CreateActionButton(nodeManagerOverlay, "编辑连接", new Vector2(566f, -88f), new Vector2(108f, 28f), OpenConnectionEditor);

        nodeManagerContent = CreateScrollContent(nodeManagerOverlay, new Vector2(16f, 16f), new Vector2(-286f, -124f));
        nodeManagerScroll = nodeManagerContent.parent.GetComponent<ScrollRect>();
        BuildNodeManagerDetailPanel();
        overlayObject.SetActive(false);
    }

    private void BuildNodeManagerDetailPanel()
    {
        GameObject panelObject = new GameObject("Managed Node Details",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(nodeManagerOverlay, false);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(1f, 1f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-16f, -124f);
        panel.sizeDelta = new Vector2(254f, 214f);

        panelObject.GetComponent<Image>().color = new Color32(0, 18, 25, 220);
        CreateFocusEdge(panel, "Detail Accent", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -1f), Vector2.zero, new Color(Cyan.r, Cyan.g, Cyan.b, .72f));

        CreateText("Managed Node Heading", panel, "剧情点信息", 17,
            TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(226f, 26f));
        nodeManagerDetailIdText = CreateText("Managed Node Id", panel, string.Empty, 12,
            TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -38f), new Vector2(72f, 22f));

        GameObject badgeRoot = new GameObject("Managed Node Badges", typeof(RectTransform));
        badgeRoot.transform.SetParent(panel, false);
        nodeManagerDetailBadgeRoot = badgeRoot.GetComponent<RectTransform>();
        nodeManagerDetailBadgeRoot.anchorMin = new Vector2(0f, 1f);
        nodeManagerDetailBadgeRoot.anchorMax = new Vector2(0f, 1f);
        nodeManagerDetailBadgeRoot.pivot = new Vector2(0f, 1f);
        nodeManagerDetailBadgeRoot.anchoredPosition = new Vector2(90f, -38f);
        nodeManagerDetailBadgeRoot.sizeDelta = new Vector2(150f, 22f);

        CreateText("Managed Node Name Label", panel, "名称", 12, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -66f), new Vector2(48f, 18f));
        nodeManagerNameInput = CreateInputField(petNameInputFieldPrefab, panel,
            "Managed Node Name Input", "剧情点名称", string.Empty,
            new Vector2(14f, -84f), new Vector2(226f, 28f), OnNodeManagerNameEdited);
        nodeManagerDetailStatsText = CreateText("Managed Node Stats", panel, string.Empty, 13,
            TextAnchor.UpperLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -120f), new Vector2(226f, 42f));
        nodeManagerDetailStatsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        nodeManagerDetailDefaultText = CreateText("Managed Node Default", panel, string.Empty, 12,
            TextAnchor.UpperLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -169f), new Vector2(226f, 34f));
        nodeManagerDetailDefaultText.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    private void BuildConnectionEditor(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story Transition Editor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        connectionOverlay = overlayObject.GetComponent<RectTransform>();
        connectionOverlay.anchorMin = new Vector2(.5f, .5f);
        connectionOverlay.anchorMax = new Vector2(.5f, .5f);
        connectionOverlay.pivot = new Vector2(.5f, .5f);
        connectionOverlay.anchoredPosition = Vector2.zero;
        connectionOverlay.sizeDelta = new Vector2(892f, 472f);

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
        CreateActionButton(connectionOverlay, "保存", new Vector2(-18f, -78f), new Vector2(88f, 28f), SaveStory, TextAnchor.UpperRight);
        CreateModalCloseButton(connectionOverlay, CloseConnectionEditor);

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
        graphOverlay.anchoredPosition = Vector2.zero;
        graphOverlay.sizeDelta = new Vector2(892f, 472f);

        Image overlay = overlayObject.GetComponent<Image>();
        overlay.color = new Color32(0, 8, 12, 252);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .86f);
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("Graph Heading", graphOverlay, "剧情点结构", 22, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -18f), new Vector2(260f, 30f));
        CreateText("Graph Legend", graphOverlay, "只读视图  ·  青色：默认流程  ·  黄色：分支规则  ·  灰色：入口不可达",
            13, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -52f), new Vector2(680f, 24f));
        CreateModalCloseButton(graphOverlay, CloseGraphViewer);
        graphContent = CreateGraphScrollContent(graphOverlay);
        overlayObject.SetActive(false);
    }

    private void BuildContentManager(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story Content Manager",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        contentManagerOverlay = overlayObject.GetComponent<RectTransform>();
        contentManagerOverlay.anchorMin = new Vector2(.5f, .5f);
        contentManagerOverlay.anchorMax = new Vector2(.5f, .5f);
        contentManagerOverlay.pivot = new Vector2(.5f, .5f);
        contentManagerOverlay.anchoredPosition = Vector2.zero;
        contentManagerOverlay.sizeDelta = new Vector2(620f, 300f);
        overlayObject.GetComponent<Image>().color = new Color32(0, 8, 12, 255);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = Cyan;
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("Content Manager Heading", contentManagerOverlay, "自制内容", 24,
            TextAnchor.MiddleCenter, Cyan, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
            new Vector2(0f, -28f), new Vector2(260f, 34f));
        CreateText("Content Manager Hint", contentManagerOverlay,
            "资源随剧本保存在 Stories 目录中，不依赖地图 XML 或旧 Mod 资源结构。", 14,
            TextAnchor.MiddleCenter, HintColor, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
            new Vector2(0f, -78f), new Vector2(520f, 30f));
        IButton sceneButton = CreateActionButton(contentManagerOverlay, "自制场景",
            new Vector2(74f, -128f), new Vector2(220f, 92f), OpenSceneManager);
        EmphasizePrimaryAction(sceneButton);
        CreateActionButton(contentManagerOverlay, "自制角色",
            new Vector2(326f, -128f), new Vector2(220f, 92f), OpenActorManager);
        CreateText("Scene Content Hint", contentManagerOverlay, "背景图片与默认 BGM", 13,
            TextAnchor.MiddleCenter, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(74f, -224f), new Vector2(220f, 24f));
        CreateText("Actor Content Hint", contentManagerOverlay, "NPC 立绘、头像与朝向", 13,
            TextAnchor.MiddleCenter, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(326f, -224f), new Vector2(220f, 24f));
        CreateModalCloseButton(contentManagerOverlay, CloseContentManager);
        overlayObject.SetActive(false);
    }

    private void BuildSceneManager(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story Scene Manager",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        sceneManagerOverlay = overlayObject.GetComponent<RectTransform>();
        sceneManagerOverlay.anchorMin = new Vector2(.5f, .5f);
        sceneManagerOverlay.anchorMax = new Vector2(.5f, .5f);
        sceneManagerOverlay.pivot = new Vector2(.5f, .5f);
        sceneManagerOverlay.anchoredPosition = Vector2.zero;
        sceneManagerOverlay.sizeDelta = new Vector2(892f, 472f);
        overlayObject.GetComponent<Image>().color = new Color32(0, 8, 12, 252);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .86f);
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("Scene Manager Heading", sceneManagerOverlay, "自制场景", 22,
            TextAnchor.MiddleCenter, Cyan, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
            new Vector2(0f, -18f), new Vector2(260f, 30f));
        CreateActionButton(sceneManagerOverlay, "新建场景", new Vector2(18f, -52f),
            new Vector2(104f, 28f), CreateCustomScene);
        CreateActionButton(sceneManagerOverlay, "删除场景", new Vector2(130f, -52f),
            new Vector2(104f, 28f), DeleteCustomScene);
        CreateActionButton(sceneManagerOverlay, "保存", new Vector2(-58f, -52f),
            new Vector2(88f, 28f), SaveStory, TextAnchor.UpperRight);
        CreateModalCloseButton(sceneManagerOverlay, CloseSceneManager);

        sceneManagerContent = CreateScrollContent(sceneManagerOverlay,
            new Vector2(16f, 16f), new Vector2(-594f, -94f));
        CreateText("Scene Name Label", sceneManagerOverlay, "场景名称：", 14,
            TextAnchor.MiddleLeft, Cyan, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(316f, -56f), new Vector2(80f, 28f));
        sceneNameInput = CreateInputField(petNameInputFieldPrefab, sceneManagerOverlay,
            "Scene Name Input", "场景名称", string.Empty, new Vector2(398f, -56f),
            new Vector2(280f, 28f), OnCustomSceneNameEdited);

        sceneBackgroundPreview = CreateActorPreview("Scene Background Preview", sceneManagerOverlay,
            new Vector2(316f, -98f), new Vector2(540f, 304f));
        CreateActionButton(sceneManagerOverlay, "导入背景", new Vector2(316f, -414f),
            new Vector2(96f, 28f), ImportCustomSceneBackground);
        sceneBackgroundPathText = CreateText("Scene Background Path", sceneManagerOverlay, string.Empty, 12,
            TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(420f, -414f), new Vector2(260f, 28f));
        CreateActionButton(sceneManagerOverlay, "导入 BGM", new Vector2(688f, -414f),
            new Vector2(82f, 28f), ImportCustomSceneBgm);
        CreateActionButton(sceneManagerOverlay, "清除", new Vector2(778f, -414f),
            new Vector2(78f, 28f), ClearCustomSceneBgm);
        sceneBgmPathText = CreateText("Scene BGM Path", sceneManagerOverlay, string.Empty, 12,
            TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(688f, -446f), new Vector2(168f, 20f));
        overlayObject.SetActive(false);
    }

    private void BuildActorManager(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story Actor Manager",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        actorManagerOverlay = overlayObject.GetComponent<RectTransform>();
        actorManagerOverlay.anchorMin = new Vector2(.5f, .5f);
        actorManagerOverlay.anchorMax = new Vector2(.5f, .5f);
        actorManagerOverlay.pivot = new Vector2(.5f, .5f);
        actorManagerOverlay.anchoredPosition = Vector2.zero;
        actorManagerOverlay.sizeDelta = new Vector2(892f, 472f);
        overlayObject.GetComponent<Image>().color = new Color32(0, 8, 12, 252);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .86f);
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("Actor Manager Heading", actorManagerOverlay, "自制角色", 22, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -18f), new Vector2(260f, 30f));
        CreateActionButton(actorManagerOverlay, "新建 NPC 角色", new Vector2(18f, -52f),
            new Vector2(126f, 28f), CreateNpcActor);
        CreateActionButton(actorManagerOverlay, "删除角色", new Vector2(152f, -52f),
            new Vector2(92f, 28f), DeleteNpcActor);
        CreateActionButton(actorManagerOverlay, "保存", new Vector2(-58f, -52f),
            new Vector2(88f, 28f), SaveStory, TextAnchor.UpperRight);
        CreateModalCloseButton(actorManagerOverlay, CloseActorManager);

        actorManagerContent = CreateScrollContent(actorManagerOverlay,
            new Vector2(16f, 16f), new Vector2(-620f, -90f));

        CreateText("Actor Name Label", actorManagerOverlay, "角色名称：", 14, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(292f, -92f), new Vector2(76f, 28f));
        actorNameInput = CreateInputField(petNameInputFieldPrefab, actorManagerOverlay,
            "Actor Name Input", "角色名称", string.Empty,
            new Vector2(370f, -92f), new Vector2(210f, 28f), OnActorNameEdited);

        CreateText("Actor Facing Label", actorManagerOverlay, "原始朝向：", 14, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(592f, -92f), new Vector2(76f, 28f));
        actorFacingDropdown = CreateDropdown(actorManagerOverlay, new Vector2(670f, -92f),
            new Vector2(128f, 28f), OnActorFacingChanged);
        if (actorFacingDropdown != null)
        {
            actorFacingDropdown.ClearOptions();
            actorFacingDropdown.AddOptions(new List<string> { "原始朝右", "原始朝左" });
            actorFacingDropdown.SetValueWithoutNotify(0);
            actorFacingDropdown.RefreshShownValue();
        }
        actorFacingValueText = CreateSelectorValueText(actorFacingDropdown);

        actorPortraitPreview = CreateActorPreview("Actor Portrait Preview", actorManagerOverlay,
            new Vector2(292f, -132f), new Vector2(226f, 218f));
        actorIconPreview = CreateActorPreview("Actor Icon Preview", actorManagerOverlay,
            new Vector2(538f, -132f), new Vector2(128f, 128f));

        CreateText("Actor Portrait Label", actorManagerOverlay, "立绘", 15, TextAnchor.MiddleCenter, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(292f, -354f), new Vector2(226f, 24f));
        CreateText("Actor Icon Label", actorManagerOverlay, "头像预览", 15, TextAnchor.MiddleCenter, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(538f, -264f), new Vector2(128f, 24f));

        CreateActionButton(actorManagerOverlay, "选择 NPC 立绘", new Vector2(292f, -384f),
            new Vector2(108f, 28f), () => OpenActorResourcePicker(false));
        CreateActionButton(actorManagerOverlay, "导入立绘", new Vector2(408f, -384f),
            new Vector2(88f, 28f), () => ImportActorImage(false));

        actorPathText = CreateText("Actor Portrait Path", actorManagerOverlay, string.Empty, 12,
            TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(292f, -416f), new Vector2(226f, 22f));

        CreateText("Actor Icon Mode Label", actorManagerOverlay, "头像方式：", 14, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(684f, -132f), new Vector2(72f, 28f));
        actorIconModeDropdown = CreateDropdown(actorManagerOverlay, new Vector2(684f, -162f),
            new Vector2(180f, 28f), OnActorIconModeChanged);
        if (actorIconModeDropdown != null)
        {
            actorIconModeDropdown.ClearOptions();
            actorIconModeDropdown.AddOptions(new List<string> { "从立绘裁剪", "使用独立头像" });
            actorIconModeDropdown.SetValueWithoutNotify(0);
            actorIconModeDropdown.RefreshShownValue();
        }
        actorIconModeValueText = CreateSelectorValueText(actorIconModeDropdown);

        actorIndependentIconControls = CreateActorControlLayer("Independent Icon Controls", actorManagerOverlay);
        CreateActionButton(actorIndependentIconControls, "选择头像", new Vector2(684f, -200f),
            new Vector2(88f, 28f), () => OpenActorResourcePicker(true));
        CreateActionButton(actorIndependentIconControls, "导入头像", new Vector2(780f, -200f),
            new Vector2(88f, 28f), () => ImportActorImage(true));
        actorIconPathText = CreateText("Actor Icon Path", actorIndependentIconControls, string.Empty, 12,
            TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(684f, -232f), new Vector2(180f, 22f));

        actorCropControls = CreateActorControlLayer("Portrait Crop Controls", actorManagerOverlay);
        CreateText("Actor Crop Hint", actorCropControls, "裁剪范围微调", 13, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(684f, -200f), new Vector2(160f, 24f));
        CreateActionButton(actorCropControls, "左", new Vector2(684f, -230f), new Vector2(40f, 26f),
            () => AdjustActorCrop(-.08f, 0f, 0f));
        CreateActionButton(actorCropControls, "右", new Vector2(730f, -230f), new Vector2(40f, 26f),
            () => AdjustActorCrop(.08f, 0f, 0f));
        CreateActionButton(actorCropControls, "上", new Vector2(776f, -230f), new Vector2(40f, 26f),
            () => AdjustActorCrop(0f, .08f, 0f));
        CreateActionButton(actorCropControls, "下", new Vector2(822f, -230f), new Vector2(40f, 26f),
            () => AdjustActorCrop(0f, -.08f, 0f));
        CreateActionButton(actorCropControls, "放大", new Vector2(684f, -264f), new Vector2(82f, 26f),
            () => AdjustActorCrop(0f, 0f, -.12f));
        CreateActionButton(actorCropControls, "缩小", new Vector2(774f, -264f), new Vector2(82f, 26f),
            () => AdjustActorCrop(0f, 0f, .12f));

        overlayObject.SetActive(false);
    }

    private static RectTransform CreateActorControlLayer(string name, RectTransform parent)
    {
        GameObject layerObject = new GameObject(name, typeof(RectTransform));
        layerObject.transform.SetParent(parent, false);
        RectTransform layer = layerObject.GetComponent<RectTransform>();
        layer.anchorMin = Vector2.zero;
        layer.anchorMax = Vector2.one;
        layer.offsetMin = Vector2.zero;
        layer.offsetMax = Vector2.zero;
        return layer;
    }

    private void BuildActorResourcePicker(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story NPC Image Picker",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        actorResourceOverlay = overlayObject.GetComponent<RectTransform>();
        actorResourceOverlay.anchorMin = new Vector2(.5f, .5f);
        actorResourceOverlay.anchorMax = new Vector2(.5f, .5f);
        actorResourceOverlay.pivot = new Vector2(.5f, .5f);
        actorResourceOverlay.anchoredPosition = Vector2.zero;
        actorResourceOverlay.sizeDelta = new Vector2(700f, 430f);
        overlayObject.GetComponent<Image>().color = new Color32(0, 8, 12, 255);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = Cyan;
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("NPC Picker Heading", actorResourceOverlay, "选择 NPC 图片", 22, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -18f), new Vector2(260f, 30f));
        actorResourceSearchInput = CreateInputField(petNameInputFieldPrefab, actorResourceOverlay,
            "NPC Resource Search", "输入文件名", string.Empty,
            new Vector2(18f, -56f), new Vector2(300f, 28f), _ => { actorResourcePage = 0; RefreshActorResourcePicker(); });
        InputField search = actorResourceSearchInput?.GetComponent<InputField>();
        if (search != null)
        {
            search.onValueChanged = new InputField.OnChangeEvent();
            search.onValueChanged.AddListener(_ => { actorResourcePage = 0; RefreshActorResourcePicker(); });
        }
        IButton sourceButton = CreateActionButton(actorResourceOverlay, "本体资源", new Vector2(-106f, -56f),
            new Vector2(92f, 28f), ToggleActorResourceSource, TextAnchor.UpperRight);
        actorResourceSourceText = sourceButton?.GetComponentInChildren<Text>();
        CreateModalCloseButton(actorResourceOverlay, CloseActorResourcePicker);
        actorResourceContent = CreateScrollContent(actorResourceOverlay,
            new Vector2(16f, 48f), new Vector2(-16f, -96f));
        CreateActionButton(actorResourceOverlay, "上一页", new Vector2(250f, -394f),
            new Vector2(64f, 26f), () => ChangeActorResourcePage(-1));
        actorResourcePageText = CreateText("NPC Resource Page", actorResourceOverlay, string.Empty, 12,
            TextAnchor.MiddleCenter, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(318f, -394f), new Vector2(64f, 26f));
        CreateActionButton(actorResourceOverlay, "下一页", new Vector2(386f, -394f),
            new Vector2(64f, 26f), () => ChangeActorResourcePage(1));
        overlayObject.SetActive(false);
    }

    private void BuildSourceExport(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Story Source Export",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        sourceExportOverlay = overlayObject.GetComponent<RectTransform>();
        sourceExportOverlay.anchorMin = new Vector2(.5f, .5f);
        sourceExportOverlay.anchorMax = new Vector2(.5f, .5f);
        sourceExportOverlay.pivot = new Vector2(.5f, .5f);
        sourceExportOverlay.anchoredPosition = Vector2.zero;
        sourceExportOverlay.sizeDelta = new Vector2(760f, 450f);
        overlayObject.GetComponent<Image>().color = new Color32(0, 8, 12, 255);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = Cyan;
        outline.effectDistance = new Vector2(2f, -2f);

        CreateText("Source Export Heading", sourceExportOverlay, "导出为源码任务", 22,
            TextAnchor.MiddleCenter, Cyan, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
            new Vector2(0f, -18f), new Vector2(280f, 30f));
        CreateModalCloseButton(sourceExportOverlay, CloseSourceExport);

        CreateText("Source Type Label", sourceExportOverlay, "任务类型：", 14, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -62f), new Vector2(82f, 28f));
        sourceMissionTypeDropdown = CreateDropdown(sourceExportOverlay, new Vector2(112f, -62f),
            new Vector2(170f, 28f), OnSourceMissionTypeChanged);
        if (sourceMissionTypeDropdown != null)
        {
            sourceMissionTypeDropdown.options = new List<Dropdown.OptionData>
            {
                new Dropdown.OptionData("支线任务"),
                new Dropdown.OptionData("日常任务"),
                new Dropdown.OptionData("活动任务"),
            };
            sourceMissionTypeDropdown.RefreshShownValue();
            sourceMissionTypeValueText = CreateSelectorValueText(sourceMissionTypeDropdown);
        }

        CreateText("Source Title Label", sourceExportOverlay, "任务标题：", 14, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(306f, -62f), new Vector2(82f, 28f));
        sourceExportTitleInput = CreateInputField(petNameInputFieldPrefab, sourceExportOverlay,
            "Source Mission Title", "任务标题", string.Empty, new Vector2(390f, -62f),
            new Vector2(326f, 28f), _ => { });

        sourceReplayLabel = CreateText("Source Replay Label", sourceExportOverlay, "允许重复体验：", 14,
            TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(28f, -102f), new Vector2(112f, 28f));
        sourceReplayDropdown = CreateDropdown(sourceExportOverlay, new Vector2(140f, -102f),
            new Vector2(116f, 28f), OnSourceReplayChanged);
        if (sourceReplayDropdown != null)
        {
            sourceReplayDropdown.options = new List<Dropdown.OptionData>
            {
                new Dropdown.OptionData("不允许"),
                new Dropdown.OptionData("允许"),
            };
            sourceReplayDropdown.RefreshShownValue();
            sourceReplayValueText = CreateSelectorValueText(sourceReplayDropdown);
        }

        sourceRewardModeLabel = CreateText("Source Reward Mode Label", sourceExportOverlay, "奖励领取：", 14,
            TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(306f, -102f), new Vector2(92f, 28f));
        sourceRewardModeDropdown = CreateDropdown(sourceExportOverlay, new Vector2(398f, -102f),
            new Vector2(190f, 28f), _ => RefreshSourceExportSemantics());
        if (sourceRewardModeDropdown != null)
            sourceRewardModeValueText = CreateSelectorValueText(sourceRewardModeDropdown);
        CreateText("Source Reward Hint", sourceExportOverlay, "最多三项本体道具奖励", 12,
            TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(28f, -148f), new Vector2(300f, 28f));

        for (int index = 0; index < 3; index++)
        {
            int slot = index;
            float y = -194f - index * 50f;
            CreateText("Source Reward Index " + index, sourceExportOverlay, "奖励 " + (index + 1) + "：", 14,
                TextAnchor.MiddleLeft, Cyan, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, y), new Vector2(72f, 28f));
            sourceRewardLabels[index] = CreateText("Source Reward Value " + index, sourceExportOverlay,
                "未设置", 13, TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(100f, y), new Vector2(294f, 28f));
            CreateActionButton(sourceExportOverlay, "选择", new Vector2(402f, y), new Vector2(66f, 28f),
                () => OpenSourceRewardPicker(slot));
            CreateText("Source Reward Amount Label " + index, sourceExportOverlay, "数量", 13,
                TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(482f, y), new Vector2(42f, 28f));
            sourceRewardAmountInputs[index] = CreateInputField(petNameInputFieldPrefab, sourceExportOverlay,
                "Source Reward Amount " + index, "1", "1", new Vector2(524f, y),
                new Vector2(88f, 28f), _ => { });
            if (sourceRewardAmountInputs[index]?.inputField != null)
                sourceRewardAmountInputs[index].inputField.contentType = InputField.ContentType.IntegerNumber;
            CreateActionButton(sourceExportOverlay, "清除", new Vector2(624f, y), new Vector2(66f, 28f),
                () => ClearSourceReward(slot));
        }

        CreateText("Source Export Footnote", sourceExportOverlay,
            "导出会写入 Assets/Resources/Data，并更新对应任务计数；再次导出同一剧本与类型会覆盖更新。",
            12, TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(28f, -358f), new Vector2(590f, 44f));
        CreateActionButton(sourceExportOverlay, "确认导出", new Vector2(-28f, -390f),
            new Vector2(112f, 32f), ExportSourceStory, TextAnchor.UpperRight);
        overlayObject.SetActive(false);
    }

    private void BuildSourceRewardPicker(RectTransform root)
    {
        GameObject overlayObject = new GameObject("Source Reward Picker",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        overlayObject.transform.SetParent(root, false);
        sourceRewardPickerOverlay = overlayObject.GetComponent<RectTransform>();
        sourceRewardPickerOverlay.anchorMin = new Vector2(.5f, .5f);
        sourceRewardPickerOverlay.anchorMax = new Vector2(.5f, .5f);
        sourceRewardPickerOverlay.pivot = new Vector2(.5f, .5f);
        sourceRewardPickerOverlay.anchoredPosition = Vector2.zero;
        sourceRewardPickerOverlay.sizeDelta = new Vector2(660f, 410f);
        overlayObject.GetComponent<Image>().color = new Color32(0, 8, 12, 255);
        Outline outline = overlayObject.GetComponent<Outline>();
        outline.effectColor = Cyan;
        outline.effectDistance = new Vector2(2f, -2f);
        CreateText("Reward Picker Heading", sourceRewardPickerOverlay, "选择本体奖励道具", 22,
            TextAnchor.MiddleCenter, Cyan, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
            new Vector2(0f, -18f), new Vector2(280f, 30f));
        CreateModalCloseButton(sourceRewardPickerOverlay, CloseSourceRewardPicker);
        sourceRewardSearchInput = CreateInputField(petNameInputFieldPrefab, sourceRewardPickerOverlay,
            "Reward Search", "名称或 ID", string.Empty, new Vector2(18f, -58f),
            new Vector2(300f, 28f), OnSourceRewardSearchChanged);
        if (sourceRewardSearchInput?.inputField != null)
        {
            sourceRewardSearchInput.inputField.onValueChanged = new InputField.OnChangeEvent();
            sourceRewardSearchInput.inputField.onValueChanged.AddListener(OnSourceRewardSearchChanged);
        }
        sourceRewardContent = CreateScrollContent(sourceRewardPickerOverlay,
            new Vector2(16f, 48f), new Vector2(-16f, -98f));
        CreateActionButton(sourceRewardPickerOverlay, "上一页", new Vector2(226f, -374f),
            new Vector2(68f, 26f), () => ChangeSourceRewardPage(-1));
        sourceRewardPageText = CreateText("Reward Page", sourceRewardPickerOverlay, string.Empty, 12,
            TextAnchor.MiddleCenter, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(298f, -374f), new Vector2(64f, 26f));
        CreateActionButton(sourceRewardPickerOverlay, "下一页", new Vector2(366f, -374f),
            new Vector2(68f, 26f), () => ChangeSourceRewardPage(1));
        overlayObject.SetActive(false);
    }

    private Image CreateActorPreview(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image backgroundImage = obj.GetComponent<Image>();
        backgroundImage.color = new Color32(12, 32, 40, 255);
        backgroundImage.raycastTarget = false;
        Outline outline = obj.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .45f);
        outline.effectDistance = new Vector2(1f, -1f);
        GameObject previewObject = new GameObject("Preview Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        previewObject.transform.SetParent(obj.transform, false);
        RectTransform previewRect = previewObject.GetComponent<RectTransform>();
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = new Vector2(6f, 6f);
        previewRect.offsetMax = new Vector2(-6f, -6f);
        Image image = previewObject.GetComponent<Image>();
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
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

    private void CreateModalCloseButton(RectTransform parent, Action callback)
    {
        GameObject workshopPrefab = ResourceManager.instance.GetPanel("Workshop");
        IButton source = workshopPrefab == null
            ? null
            : workshopPrefab.GetComponentsInChildren<IButton>(true)
                .FirstOrDefault(button => button.gameObject.name == "ESC Button");
        if (source == null)
            return;

        GameObject closeObject = Instantiate(source.gameObject, parent);
        closeObject.name = "Modal ESC Button";
        RectTransform rect = closeObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-10f, -10f);
        rect.sizeDelta = new Vector2(40f, 40f);

        IButton button = closeObject.GetComponent<IButton>();
        button.onPointerClickEvent.SetListener(callback.Invoke);
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

    private void CopyStory()
    {
        if (!controller.CopySelectedStory(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void DeleteStory()
    {
        WorkshopStorySummary story = controller.SelectedStory;
        if (story == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要删除的剧本。", 16);
            return;
        }

        string title = string.IsNullOrWhiteSpace(story.title) ? story.fileName : story.title;
        OpenDeleteConfirmation(
            "确定删除剧本《" + title + "》吗？\n剧本及其中的全部剧情点将被删除，此操作无法撤销。",
            ConfirmDeleteStory);
    }

    private void ConfirmDeleteStory()
    {
        if (!controller.DeleteSelected(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void SaveStory()
    {
        bool success = controller.SaveSelectedForRuntime(out bool runtimeReady, out string message);
        if (!string.IsNullOrEmpty(message))
        {
            Hintbox hintbox = Hintbox.OpenHintboxWithContent(message, runtimeReady ? 16 : 14);
            if (!runtimeReady || !success)
                hintbox.SetSize(720, 360);
        }
        RefreshView();
        if (sceneManagerOverlay != null && sceneManagerOverlay.gameObject.activeSelf)
            RefreshSceneManager();
        if (actorManagerOverlay != null && actorManagerOverlay.gameObject.activeSelf)
            RefreshActorManager();
    }

    private void SelectStory(string path)
    {
        if (!controller.SelectStory(path, out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void CreateNode()
    {
        bool success = controller.CreateNode(out string error);
        if (!success && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        if (success)
            ResetNodeManagerFilters();
        RefreshView();
        if (success && nodeManagerOverlay != null && nodeManagerOverlay.gameObject.activeSelf)
            RefreshNodeManager(true);
    }

    private void CopyNode()
    {
        bool success = controller.CopySelectedNode(out string error);
        if (!success && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        if (success)
            ResetNodeManagerFilters();
        RefreshView();
        if (success && nodeManagerOverlay != null && nodeManagerOverlay.gameObject.activeSelf)
            RefreshNodeManager(true);
    }

    private void DeleteNode()
    {
        StoryNodeDocument node = controller.SelectedNode;
        if (node == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要删除的剧情点。", 16);
            return;
        }

        string displayName = string.IsNullOrWhiteSpace(node.displayName) ? node.id : node.displayName;
        string nodeId = node.id;
        OpenDeleteConfirmation(
            "确定删除剧情点“" + displayName + "”（" + nodeId + "）吗？\n"
            + "相关默认流程会自动衔接；显式条件引用仍需先处理。",
            ConfirmDeleteNode);
    }

    private void ConfirmDeleteNode()
    {
        bool success = controller.DeleteSelectedNode(out string error);
        if (!success && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
        if (success && nodeManagerOverlay != null && nodeManagerOverlay.gameObject.activeSelf)
            RefreshNodeManager(true);
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

        WorkshopStoryNodeEditorPanel.Open(storyPath, nodeId, () => ReloadAndSelectNode(nodeId));
    }

    private void ReloadAndSelectNode(string nodeId)
    {
        if (!controller.Open(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        controller.SelectNode(nodeId);
        RefreshView();
        if (nodeManagerOverlay != null && nodeManagerOverlay.gameObject.activeSelf)
            RefreshNodeManager(true);
    }

    private void PreviewStory()
    {
        StoryDocument document = controller.SelectedDocument;
        if (document == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要预览的剧本。", 16);
            return;
        }

        if (StoryPanel.OpenPreview(document, document.entry, StoryPreviewScope.Story) == null)
            Hintbox.OpenHintboxWithContent("无法打开剧本预览。", 16);
    }

    private void OpenContentManager()
    {
        if (controller.SelectedDocument == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要管理自制内容的剧本。", 16);
            return;
        }
        OpenModal(contentManagerOverlay);
    }

    private void CloseContentManager()
    {
        if (contentManagerOverlay != null)
            contentManagerOverlay.gameObject.SetActive(false);
        HideModalLayer();
        RefreshStoryInfo();
    }

    private void OpenSceneManager()
    {
        if (controller.SelectedDocument == null)
            return;
        OpenModal(sceneManagerOverlay);
        RefreshSceneManager();
    }

    private void CloseSceneManager()
    {
        if (sceneManagerOverlay != null)
            sceneManagerOverlay.gameObject.SetActive(false);
        OpenModal(contentManagerOverlay);
        RefreshStoryInfo();
    }

    private void CreateCustomScene()
    {
        if (!controller.CreateStoryScene(out StorySceneResourceDocument scene, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        selectedCustomSceneId = scene.id;
        RefreshSceneManager();
    }

    private void DeleteCustomScene()
    {
        StorySceneResourceDocument scene = GetSelectedCustomScene();
        if (scene == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要删除的自制场景。", 16);
            return;
        }
        string sceneId = scene.id;
        OpenDeleteConfirmation(
            "确定删除自制场景“" + scene.displayName + "”（" + sceneId + "）吗？\n"
            + "已被剧情点引用的场景需要先移除相关引用。",
            () => ConfirmDeleteCustomScene(sceneId));
    }

    private void ConfirmDeleteCustomScene(string sceneId)
    {
        if (!controller.DeleteStoryScene(sceneId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        selectedCustomSceneId = null;
        RefreshSceneManager();
    }

    private void SelectCustomScene(string sceneId)
    {
        selectedCustomSceneId = sceneId;
        RefreshSceneManager();
    }

    private StorySceneResourceDocument GetSelectedCustomScene()
    {
        return controller.GetStoryScenes().FirstOrDefault(scene => scene != null
            && string.Equals(scene.id, selectedCustomSceneId, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshSceneManager()
    {
        if (sceneManagerContent == null)
            return;

        StorySceneResourceDocument[] scenes = controller.GetStoryScenes()
            .Where(scene => scene != null).ToArray();
        if (scenes.Length > 0 && !scenes.Any(scene => string.Equals(
                scene.id, selectedCustomSceneId, StringComparison.OrdinalIgnoreCase)))
            selectedCustomSceneId = scenes[0].id;

        ClearChildren(sceneManagerContent);
        for (int index = 0; index < scenes.Length; index++)
        {
            StorySceneResourceDocument scene = scenes[index];
            bool selected = string.Equals(scene.id, selectedCustomSceneId, StringComparison.OrdinalIgnoreCase);
            CreateListButton(sceneManagerContent, "场景 · " + scene.displayName, selected, index,
                () => SelectCustomScene(scene.id));
        }
        if (scenes.Length == 0)
            CreateHint(sceneManagerContent, "尚未建立自制场景");
        else
            sceneManagerContent.sizeDelta = new Vector2(0f, 12f + scenes.Length * 42f);

        StorySceneResourceDocument selectedScene = GetSelectedCustomScene();
        bool hasScene = selectedScene != null;
        SetInputFieldValue(sceneNameInput, selectedScene?.name, hasScene);
        SetActorPreview(sceneBackgroundPreview,
            hasScene ? StorySpriteResolver.Load(selectedScene.backgroundResourcePath) : null);
        if (sceneBackgroundPathText != null)
            sceneBackgroundPathText.text = hasScene
                ? Shorten(selectedScene.backgroundResourcePath ?? "尚未导入背景", 34)
                : string.Empty;
        if (sceneBgmPathText != null)
            sceneBgmPathText.text = hasScene
                ? "BGM  " + Shorten(selectedScene.defaultBgmResourcePath ?? "未设置", 20)
                : string.Empty;
    }

    private void OnCustomSceneNameEdited(string value)
    {
        StorySceneResourceDocument scene = GetSelectedCustomScene();
        if (scene == null)
            return;
        if (!controller.UpdateStoryScene(scene.id, value, out string error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshSceneManager();
    }

    private void ImportCustomSceneBackground()
    {
        if (GetSelectedCustomScene() == null)
        {
            Hintbox.OpenHintboxWithContent("请先新建或选择一个自制场景。", 16);
            return;
        }
        FileBrowser.SetFilters(false, new FileBrowser.Filter("PNG 图片", ".png"));
        FileBrowser.ShowLoadDialog(paths =>
        {
            if (paths == null || paths.Length == 0)
                return;
            if (!controller.ImportStorySceneBackground(selectedCustomSceneId, paths[0], out string error))
                Hintbox.OpenHintboxWithContent(error, 16);
            RefreshSceneManager();
        }, () => { }, FileBrowser.PickMode.Files, title: "选择自制场景背景");
    }

    private void ImportCustomSceneBgm()
    {
        if (GetSelectedCustomScene() == null)
        {
            Hintbox.OpenHintboxWithContent("请先新建或选择一个自制场景。", 16);
            return;
        }
        FileBrowser.SetFilters(false, new FileBrowser.Filter("MP3 音频", ".mp3"));
        FileBrowser.ShowLoadDialog(paths =>
        {
            if (paths == null || paths.Length == 0)
                return;
            if (!controller.ImportStorySceneBgm(selectedCustomSceneId, paths[0], out string error))
                Hintbox.OpenHintboxWithContent(error, 16);
            RefreshSceneManager();
        }, () => { }, FileBrowser.PickMode.Files, title: "选择自制场景默认 BGM");
    }

    private void ClearCustomSceneBgm()
    {
        if (!controller.ClearStorySceneBgm(selectedCustomSceneId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshSceneManager();
    }

    private void OpenActorManager()
    {
        if (controller.SelectedDocument == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要管理角色的剧本。", 16);
            return;
        }

        OpenModal(actorManagerOverlay);
        RefreshActorManager();
    }

    private void CloseActorManager()
    {
        if (actorManagerOverlay != null)
            actorManagerOverlay.gameObject.SetActive(false);
        OpenModal(contentManagerOverlay);
        RefreshStoryInfo();
    }

    private void CreateNpcActor()
    {
        if (!controller.CreateNpcActor(out StoryActorDocument actor, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        selectedActorId = actor.id;
        RefreshActorManager();
    }

    private void DeleteNpcActor()
    {
        if (string.IsNullOrWhiteSpace(selectedActorId))
        {
            Hintbox.OpenHintboxWithContent("请先选择要删除的剧本角色。", 16);
            return;
        }

        StoryActorDocument actor = GetSelectedNpcActor();
        if (actor == null)
        {
            Hintbox.OpenHintboxWithContent("未找到要删除的剧本角色。", 16);
            return;
        }

        string actorId = actor.id;
        OpenDeleteConfirmation(
            "确定删除自制角色“" + actor.displayName + "”（" + actorId + "）吗？\n"
            + "已被剧情点引用的角色需要先移除相关引用。",
            () => ConfirmDeleteNpcActor(actorId));
    }

    private void ConfirmDeleteNpcActor(string actorId)
    {
        if (!controller.DeleteNpcActor(actorId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        selectedActorId = null;
        RefreshActorManager();
    }

    private static void OpenDeleteConfirmation(string content, Action confirmAction)
    {
        Hintbox hintbox = Hintbox.OpenHintbox();
        hintbox.SetTitle("确认删除");
        hintbox.SetContent(content, 16, FontOption.Arial);
        hintbox.SetOptionNum(2);
        hintbox.SetOptionCallback(confirmAction);
    }

    private void SelectNpcActor(string actorId)
    {
        selectedActorId = actorId;
        RefreshActorManager();
    }

    private StoryActorDocument GetSelectedNpcActor()
    {
        return controller.GetStoryActors().FirstOrDefault(actor => actor != null
            && string.Equals(actor.id, selectedActorId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actor.actorType, "npc", StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshActorManager()
    {
        if (actorManagerContent == null)
            return;

        StoryActorDocument[] actors = controller.GetStoryActors()
            .Where(actor => actor != null && string.Equals(actor.actorType, "npc", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (actors.Length > 0 && !actors.Any(actor => string.Equals(actor.id, selectedActorId, StringComparison.OrdinalIgnoreCase)))
            selectedActorId = actors[0].id;

        ClearChildren(actorManagerContent);
        for (int index = 0; index < actors.Length; index++)
        {
            StoryActorDocument actor = actors[index];
            bool selected = string.Equals(actor.id, selectedActorId, StringComparison.OrdinalIgnoreCase);
            CreateListButton(actorManagerContent, "角色 · " + actor.displayName, selected, index,
                () => SelectNpcActor(actor.id));
        }
        if (actors.Length == 0)
            CreateHint(actorManagerContent, "尚未建立 NPC 剧本角色");
        else
            actorManagerContent.sizeDelta = new Vector2(0f, 12f + actors.Length * 42f);

        StoryActorDocument selectedActor = GetSelectedNpcActor();
        bool hasActor = selectedActor != null;
        SetInputFieldValue(actorNameInput, selectedActor?.name, hasActor);
        if (actorFacingDropdown != null)
        {
            actorFacingDropdown.gameObject.SetActive(hasActor);
            actorFacingDropdown.SetValueWithoutNotify(selectedActor?.sourceFacesLeft == true ? 1 : 0);
            SetSelectorValueText(actorFacingValueText, actorFacingDropdown, "原始朝右");
        }
        if (actorIconModeDropdown != null)
        {
            actorIconModeDropdown.gameObject.SetActive(hasActor);
            actorIconModeDropdown.SetValueWithoutNotify(selectedActor?.usesPortraitIcon == true ? 0 : 1);
            SetSelectorValueText(actorIconModeValueText, actorIconModeDropdown, "从立绘裁剪");
        }
        bool usesPortraitCrop = hasActor && selectedActor.usesPortraitIcon;
        if (actorCropControls != null)
            actorCropControls.gameObject.SetActive(usesPortraitCrop);
        if (actorIndependentIconControls != null)
            actorIndependentIconControls.gameObject.SetActive(hasActor && !usesPortraitCrop);

        Sprite portrait = hasActor ? StorySpriteResolver.Load(selectedActor.sprite) : null;
        Sprite icon = hasActor ? StorySpriteResolver.Load(selectedActor.icon) : null;
        if (hasActor && selectedActor.usesPortraitIcon)
            icon = StorySpriteResolver.PrepareIconSprite(portrait, selectedActor.normalizedIconCrop);
        SetActorPreview(actorPortraitPreview, portrait);
        SetActorPreview(actorIconPreview, icon);
        if (actorPathText != null)
            actorPathText.text = hasActor ? Shorten(selectedActor.sprite ?? "尚未选择立绘", 30) : string.Empty;
        if (actorIconPathText != null)
            actorIconPathText.text = hasActor
                ? selectedActor.usesPortraitIcon ? "使用立绘裁剪" : Shorten(selectedActor.icon ?? "尚未选择头像", 22)
                : string.Empty;
    }

    private static void SetActorPreview(Image image, Sprite sprite)
    {
        if (image == null)
            return;
        image.sprite = sprite;
        image.enabled = sprite != null && sprite != SpriteSet.Empty;
    }

    private void OnActorNameEdited(string value)
    {
        UpdateSelectedNpcActor(value, null, null);
    }

    private void OnActorFacingChanged(int value)
    {
        UpdateSelectedNpcActor(null, value == 1 ? "left" : "right", null);
    }

    private void OnActorIconModeChanged(int value)
    {
        UpdateSelectedNpcActor(null, null, value == 0);
    }

    private void UpdateSelectedNpcActor(string name, string facing, bool? usePortraitIcon)
    {
        StoryActorDocument actor = GetSelectedNpcActor();
        if (actor == null)
            return;
        string resolvedName = name ?? actorNameInput?.inputString ?? actor.name;
        string resolvedFacing = facing ?? (actor.sourceFacesLeft ? "left" : "right");
        bool resolvedIconMode = usePortraitIcon ?? actor.usesPortraitIcon;
        if (!controller.UpdateNpcActor(actor.id, resolvedName, resolvedFacing, resolvedIconMode, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshActorManager();
    }

    private void AdjustActorCrop(float moveX, float moveY, float zoomDelta)
    {
        if (!controller.AdjustNpcActorCrop(selectedActorId, moveX, moveY, zoomDelta, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshActorManager();
    }

    private void ImportActorImage(bool isIcon)
    {
        if (GetSelectedNpcActor() == null)
        {
            Hintbox.OpenHintboxWithContent("请先新建或选择一个 NPC 角色。", 16);
            return;
        }

        FileBrowser.SetFilters(false, new FileBrowser.Filter("PNG 图片", ".png"));
        FileBrowser.ShowLoadDialog(paths =>
        {
            if (paths == null || paths.Length == 0)
                return;
            if (!controller.ImportNpcActorImage(selectedActorId, paths[0], isIcon, out string error))
                Hintbox.OpenHintboxWithContent(error, 16);
            RefreshActorManager();
        }, () => { }, FileBrowser.PickMode.Files, title: isIcon ? "选择要导入的头像" : "选择要导入的立绘");
    }

    private void OpenActorResourcePicker(bool isIcon)
    {
        if (GetSelectedNpcActor() == null)
        {
            Hintbox.OpenHintboxWithContent("请先新建或选择一个 NPC 角色。", 16);
            return;
        }
        selectingActorIcon = isIcon;
        actorResourceIsMod = false;
        actorResourcePage = 0;
        OpenModal(actorResourceOverlay);
        RefreshActorResourcePicker();
    }

    private void CloseActorResourcePicker()
    {
        if (actorResourceOverlay != null)
            actorResourceOverlay.gameObject.SetActive(false);
        OpenModal(actorManagerOverlay);
        RefreshActorManager();
    }

    private void ToggleActorResourceSource()
    {
        if (!GetNpcImageOptions().Any(option => option.isMod))
            return;
        actorResourceIsMod = !actorResourceIsMod;
        actorResourcePage = 0;
        RefreshActorResourcePicker();
    }

    private void ChangeActorResourcePage(int delta)
    {
        actorResourcePage = Mathf.Max(0, actorResourcePage + delta);
        RefreshActorResourcePicker();
    }

    private void RefreshActorResourcePicker()
    {
        if (actorResourceContent == null)
            return;

        NpcImageOption[] all = GetNpcImageOptions().ToArray();
        bool hasMod = all.Any(option => option.isMod);
        if (actorResourceSourceText != null)
            actorResourceSourceText.text = actorResourceIsMod ? "当前 Mod" : hasMod ? "本体资源" : "仅本体";
        string query = actorResourceSearchInput?.inputString?.Trim() ?? string.Empty;
        NpcImageOption[] filtered = all.Where(option => option.isMod == actorResourceIsMod
                && (string.IsNullOrWhiteSpace(query)
                    || option.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();
        const int pageSize = 36;
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Length / (float)pageSize));
        actorResourcePage = Mathf.Clamp(actorResourcePage, 0, pageCount - 1);
        NpcImageOption[] visible = filtered.Skip(actorResourcePage * pageSize).Take(pageSize).ToArray();
        if (actorResourcePageText != null)
            actorResourcePageText.text = (actorResourcePage + 1) + "/" + pageCount;

        ClearChildren(actorResourceContent);
        for (int index = 0; index < visible.Length; index++)
        {
            NpcImageOption option = visible[index];
            CreateNpcImageItem(option, index, () => SelectNpcImage(option));
        }
        if (visible.Length == 0)
            CreateHint(actorResourceContent, actorResourceIsMod ? "当前 Mod 没有 NPC 图片" : "本体没有 NPC 图片");
        else
        {
            int rowCount = Mathf.CeilToInt(visible.Length / 3f);
            actorResourceContent.sizeDelta = new Vector2(0f, 8f + rowCount * 74f);
        }
    }

    private IEnumerable<NpcImageOption> GetNpcImageOptions()
    {
        foreach (var source in new[]
                 {
                     new { root = Path.Combine(Application.persistentDataPath, "Resources", "Npc"), prefix = "Builtin/Npc/", isMod = false },
                     new { root = Path.Combine(Application.persistentDataPath, "Mod", "Npc"), prefix = "Mod/Npc/", isMod = true },
                 })
        {
            if (!Directory.Exists(source.root))
                continue;
            string[] files;
            try { files = Directory.GetFiles(source.root, "*.png", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (string file in files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                yield return new NpcImageOption { path = source.prefix + name, name = name, isMod = source.isMod };
            }
        }
    }

    private void SelectNpcImage(NpcImageOption option)
    {
        if (option == null)
            return;
        if (!controller.SetNpcActorImage(selectedActorId, option.path, selectingActorIcon, out string error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        CloseActorResourcePicker();
    }

    private void CreateNpcImageItem(NpcImageOption option, int index, Action callback)
    {
        const int columnCount = 3;
        const float columnGap = 8f;
        const float rowHeight = 74f;
        int column = index % columnCount;
        int row = index / columnCount;
        float cardWidth = (668f - columnGap * (columnCount - 1)) / columnCount;

        GameObject item = new GameObject("NPC Image " + option.name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(IButton), typeof(Outline));
        item.transform.SetParent(actorResourceContent, false);
        item.name = "NPC Image " + option.name;
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(column * (cardWidth + columnGap), -4f - row * rowHeight);
        rect.sizeDelta = new Vector2(cardWidth, 66f);

        Image background = item.GetComponent<Image>();
        Color normalColor = new Color32(9, 31, 38, 245);
        Color hoverColor = new Color32(13, 55, 66, 255);
        background.color = normalColor;

        Outline outline = item.GetComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .42f);
        outline.effectDistance = new Vector2(1f, -1f);

        IButton button = item.GetComponent<IButton>();
        button.button.targetGraphic = background;
        button.button.transition = Selectable.Transition.None;
        button.onPointerClickEvent = new UnityEvent();
        button.onPointerEnterEvent = new UnityEvent();
        button.onPointerExitEvent = new UnityEvent();
        button.onPointerClickEvent.AddListener(callback.Invoke);
        button.onPointerEnterEvent.AddListener(() => background.color = hoverColor);
        button.onPointerExitEvent.AddListener(() => background.color = normalColor);

        CreateText("NPC Image Name", item.transform, Shorten(option.name, 14), 15, TextAnchor.MiddleLeft,
            new Color32(205, 238, 242, 255), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(70f, -8f), new Vector2(cardWidth - 78f, 26f));
        CreateText("NPC Image Source", item.transform, option.isMod ? "当前 Mod · NPC" : "本体 · NPC", 12,
            TextAnchor.MiddleLeft, HintColor, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(70f, -35f), new Vector2(cardWidth - 78f, 22f));

        GameObject iconObject = new GameObject("Thumbnail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.transform.SetParent(item.transform, false);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, .5f);
        iconRect.anchorMax = iconRect.anchorMin;
        iconRect.pivot = new Vector2(0f, .5f);
        iconRect.anchoredPosition = new Vector2(7f, 0f);
        iconRect.sizeDelta = new Vector2(52f, 52f);
        Image icon = iconObject.GetComponent<Image>();
        icon.sprite = StorySpriteResolver.Load(option.path);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
    }

    private void OpenGraphViewer()
    {
        if (controller.SelectedDocument == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要查看的剧本。", 16);
            return;
        }

        OpenModal(graphOverlay);
        RefreshGraphViewer();
    }

    private void OpenNodeManager()
    {
        if (controller.SelectedDocument == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要管理的剧本。", 16);
            return;
        }

        OpenModal(nodeManagerOverlay);
        RefreshNodeManager(true);
    }

    private void CloseNodeManager()
    {
        CaptureNodeManagerScrollPosition();
        if (nodeManagerOverlay != null)
            nodeManagerOverlay.gameObject.SetActive(false);
        HideModalLayer();
        RefreshStoryInfo();
    }

    private void CloseGraphViewer()
    {
        if (graphOverlay != null)
            graphOverlay.gameObject.SetActive(false);
        HideModalLayer();
    }

    private void OpenConnectionEditor()
    {
        if (controller.SelectedNode == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要配置后续连接的剧情点。", 16);
            return;
        }

        returnToNodeManagerAfterConnection = nodeManagerOverlay != null
            && nodeManagerOverlay.gameObject.activeSelf;
        OpenModal(connectionOverlay);
        RefreshConnections();
    }

    private void CloseConnectionEditor()
    {
        connectionResourcePicker?.Close();
        if (connectionOverlay != null)
            connectionOverlay.gameObject.SetActive(false);
        RefreshStoryInfo();
        RefreshNodeRelatedState();
        if (returnToNodeManagerAfterConnection)
        {
            returnToNodeManagerAfterConnection = false;
            OpenModal(nodeManagerOverlay);
            RefreshNodeManager(true);
            return;
        }
        HideModalLayer();
    }

    private void OpenSourceExport()
    {
        StoryDocument document = controller.SelectedDocument;
        if (document == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择要导出的剧本。", 16);
            return;
        }
        if (!controller.CanExportSource)
        {
            Hintbox.OpenHintboxWithContent("源码任务导出只在 Unity Editor 中可用。", 16);
            return;
        }

        sourceExportTitleInput?.SetInputString(document.title ?? string.Empty);
        sourceMissionTypeDropdown?.SetValueWithoutNotify(0);
        sourceReplayDropdown?.SetValueWithoutNotify(document.replayable ? 1 : 0);
        sourceRewardModeDropdown?.SetValueWithoutNotify(0);
        for (int index = 0; index < sourceRewardIds.Length; index++)
        {
            sourceRewardIds[index] = 0;
            sourceRewardAmountInputs[index]?.SetInputString("1");
        }
        RefreshSourceRewardRows();
        RefreshSourceExportSemantics();
        OpenModal(sourceExportOverlay);
    }

    private void CloseSourceExport()
    {
        if (sourceExportOverlay != null)
            sourceExportOverlay.gameObject.SetActive(false);
        HideModalLayer();
    }

    private void OnSourceMissionTypeChanged(int _)
    {
        RefreshSourceExportSemantics();
    }

    private void OnSourceReplayChanged(int _)
    {
        RefreshSourceExportSemantics();
    }

    private void RefreshSourceExportSemantics()
    {
        bool daily = sourceMissionTypeDropdown != null && sourceMissionTypeDropdown.value == 1;
        bool replayable = sourceReplayDropdown != null && sourceReplayDropdown.value == 1;
        if (sourceReplayLabel != null)
            sourceReplayLabel.text = daily ? "允许当日重复：" : "允许重复体验：";
        if (sourceRewardModeLabel != null)
            sourceRewardModeLabel.text = daily ? "当日奖励：" : "奖励领取：";
        if (sourceRewardModeDropdown != null)
        {
            sourceRewardModeDropdown.options = new List<Dropdown.OptionData>
            {
                new Dropdown.OptionData(daily ? "每日首次完成" : "仅首次完成"),
                new Dropdown.OptionData("每次完成"),
            };
            if (!replayable)
                sourceRewardModeDropdown.SetValueWithoutNotify(0);
            sourceRewardModeDropdown.interactable = replayable;
            sourceRewardModeDropdown.RefreshShownValue();
        }
        SetSelectorValueText(sourceMissionTypeValueText, sourceMissionTypeDropdown, "支线任务");
        SetSelectorValueText(sourceReplayValueText, sourceReplayDropdown, "不允许");
        SetSelectorValueText(sourceRewardModeValueText, sourceRewardModeDropdown,
            daily ? "每日首次完成" : "仅首次完成");
        if (sourceRewardModeValueText != null)
            sourceRewardModeValueText.color = replayable ? Cyan : HintColor;
    }

    private void OpenSourceRewardPicker(int slot)
    {
        activeSourceRewardSlot = Mathf.Clamp(slot, 0, sourceRewardIds.Length - 1);
        sourceRewardPage = 0;
        sourceRewardQuery = string.Empty;
        sourceRewardSearchInput?.SetInputString(string.Empty);
        OpenModal(sourceRewardPickerOverlay);
        RefreshSourceRewardPicker();
    }

    private void CloseSourceRewardPicker()
    {
        if (sourceRewardPickerOverlay != null)
            sourceRewardPickerOverlay.gameObject.SetActive(false);
        OpenModal(sourceExportOverlay);
    }

    private void OnSourceRewardSearchChanged(string value)
    {
        sourceRewardQuery = value ?? string.Empty;
        sourceRewardPage = 0;
        RefreshSourceRewardPicker();
    }

    private void ChangeSourceRewardPage(int delta)
    {
        sourceRewardPage = Mathf.Max(0, sourceRewardPage + delta);
        RefreshSourceRewardPicker();
    }

    private void RefreshSourceRewardPicker()
    {
        if (sourceRewardContent == null)
            return;
        ClearChildren(sourceRewardContent);
        const int pageSize = 7;
        WorkshopStorySourceRewardOption[] options = controller.GetSourceRewardOptions(sourceRewardQuery).ToArray();
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(options.Length / (float)pageSize));
        sourceRewardPage = Mathf.Clamp(sourceRewardPage, 0, pageCount - 1);
        WorkshopStorySourceRewardOption[] page = options.Skip(sourceRewardPage * pageSize).Take(pageSize).ToArray();
        for (int index = 0; index < page.Length; index++)
        {
            WorkshopStorySourceRewardOption option = page[index];
            CreateListButton(sourceRewardContent, option.displayName,
                sourceRewardIds[activeSourceRewardSlot] == option.itemId, index,
                () => SelectSourceReward(option));
        }
        if (page.Length == 0)
            CreateHint(sourceRewardContent, "没有匹配的本体道具。");
        else
            sourceRewardContent.sizeDelta = new Vector2(0f, 12f + page.Length * 42f);
        if (sourceRewardPageText != null)
            sourceRewardPageText.text = (sourceRewardPage + 1) + " / " + pageCount;
    }

    private void SelectSourceReward(WorkshopStorySourceRewardOption option)
    {
        if (option == null)
            return;
        sourceRewardIds[activeSourceRewardSlot] = option.itemId;
        RefreshSourceRewardRows();
        CloseSourceRewardPicker();
    }

    private void ClearSourceReward(int slot)
    {
        if (slot < 0 || slot >= sourceRewardIds.Length)
            return;
        sourceRewardIds[slot] = 0;
        sourceRewardAmountInputs[slot]?.SetInputString("1");
        RefreshSourceRewardRows();
    }

    private void RefreshSourceRewardRows()
    {
        IReadOnlyList<WorkshopStorySourceRewardOption> all = controller.GetSourceRewardOptions(string.Empty);
        for (int index = 0; index < sourceRewardLabels.Length; index++)
        {
            WorkshopStorySourceRewardOption option = all.FirstOrDefault(value => value.itemId == sourceRewardIds[index]);
            sourceRewardLabels[index].text = option == null ? "未设置" : option.displayName;
            sourceRewardLabels[index].color = option == null ? HintColor : Cyan;
        }
    }

    private void ExportSourceStory()
    {
        StoryDocument document = controller.SelectedDocument;
        if (document == null)
            return;

        string title = sourceExportTitleInput?.inputString ?? document.title;
        string summary = storySummaryInput?.inputString ?? document.summary;
        if (!controller.UpdateSelectedStoryMetadata(title, summary, document.replayable, out string metadataError))
        {
            Hintbox.OpenHintboxWithContent(metadataError, 16);
            return;
        }
        if (controller.HasUnsavedChanges)
        {
            bool saved = controller.SaveSelectedForRuntime(out bool runtimeReady, out string saveMessage);
            if (!saved || !runtimeReady)
            {
                Hintbox hintbox = Hintbox.OpenHintboxWithContent(saveMessage, 14);
                hintbox.SetSize(720, 360);
                return;
            }
        }

        WorkshopStorySourceExportRequest request = new WorkshopStorySourceExportRequest
        {
            missionType = sourceMissionTypeDropdown?.value == 1
                ? WorkshopStorySourceMissionType.Daily
                : sourceMissionTypeDropdown?.value == 2
                    ? WorkshopStorySourceMissionType.Event
                    : WorkshopStorySourceMissionType.Side,
            title = title,
            replayable = sourceReplayDropdown != null && sourceReplayDropdown.value == 1,
            rewardMode = sourceRewardModeDropdown != null && sourceRewardModeDropdown.value == 1
                ? "always"
                : "once",
        };
        for (int index = 0; index < sourceRewardIds.Length; index++)
        {
            if (sourceRewardIds[index] == 0)
                continue;
            if (!int.TryParse(sourceRewardAmountInputs[index]?.inputString, out int amount) || amount <= 0)
            {
                Hintbox.OpenHintboxWithContent("奖励 " + (index + 1) + " 的数量必须大于 0。", 16);
                return;
            }
            request.rewards.Add(new WorkshopStorySourceReward
            {
                itemId = sourceRewardIds[index],
                amount = amount,
            });
        }

        if (!controller.ExportSelectedToSource(request, out WorkshopStorySourceExportResult result, out string error))
        {
            Hintbox hintbox = Hintbox.OpenHintboxWithContent(error, 14);
            hintbox.SetSize(720, 360);
            return;
        }
        CloseSourceExport();
        RefreshView();
        string action = result.updatedExisting ? "已更新" : "已新增";
        Hintbox.OpenHintboxWithContent(action + "源码任务 " + result.missionId
            + "。\n剧本资源：" + result.storyResourcePath
            + "\n停止并重新进入播放模式后生效。", 16).SetSize(600, 300);
    }

    private void OpenModal(RectTransform overlay)
    {
        if (modalLayer == null || overlay == null)
            return;

        if (connectionOverlay != null)
            connectionOverlay.gameObject.SetActive(connectionOverlay == overlay);
        if (graphOverlay != null)
            graphOverlay.gameObject.SetActive(graphOverlay == overlay);
        if (nodeManagerOverlay != null)
            nodeManagerOverlay.gameObject.SetActive(nodeManagerOverlay == overlay);
        if (contentManagerOverlay != null)
            contentManagerOverlay.gameObject.SetActive(contentManagerOverlay == overlay);
        if (sceneManagerOverlay != null)
            sceneManagerOverlay.gameObject.SetActive(sceneManagerOverlay == overlay);
        if (actorManagerOverlay != null)
            actorManagerOverlay.gameObject.SetActive(actorManagerOverlay == overlay);
        if (actorResourceOverlay != null)
            actorResourceOverlay.gameObject.SetActive(actorResourceOverlay == overlay);
        if (sourceExportOverlay != null)
            sourceExportOverlay.gameObject.SetActive(sourceExportOverlay == overlay);
        if (sourceRewardPickerOverlay != null)
            sourceRewardPickerOverlay.gameObject.SetActive(sourceRewardPickerOverlay == overlay);
        modalLayer.gameObject.SetActive(true);
        modalLayer.transform.SetAsLastSibling();
        overlay.transform.SetAsLastSibling();
    }

    private void HideModalLayer()
    {
        bool hasOpenModal = (connectionOverlay != null && connectionOverlay.gameObject.activeSelf)
            || (graphOverlay != null && graphOverlay.gameObject.activeSelf)
            || (nodeManagerOverlay != null && nodeManagerOverlay.gameObject.activeSelf)
            || (contentManagerOverlay != null && contentManagerOverlay.gameObject.activeSelf)
            || (sceneManagerOverlay != null && sceneManagerOverlay.gameObject.activeSelf)
            || (actorManagerOverlay != null && actorManagerOverlay.gameObject.activeSelf)
            || (actorResourceOverlay != null && actorResourceOverlay.gameObject.activeSelf)
            || (sourceExportOverlay != null && sourceExportOverlay.gameObject.activeSelf)
            || (sourceRewardPickerOverlay != null && sourceRewardPickerOverlay.gameObject.activeSelf);
        if (!hasOpenModal && modalLayer != null)
            modalLayer.gameObject.SetActive(false);
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
        RefreshNodeRelatedState();
    }

    private void OnNodeManagerSearchChanged(string value)
    {
        string query = value ?? string.Empty;
        if (string.Equals(nodeManagerSearchQuery, query, StringComparison.Ordinal))
            return;

        nodeManagerSearchQuery = query;
        nodeManagerScrollPosition = 1f;
        RefreshNodeManager();
    }

    private void OnNodeManagerFlowFilterChanged(int index)
    {
        nodeManagerFlowFilterIndex = Mathf.Clamp(index, 0, 2);
        SetSelectorValueText(nodeManagerFlowFilterValueText, nodeManagerFlowFilterDropdown, "全部流程");
        nodeManagerScrollPosition = 1f;
        RefreshNodeManager();
    }

    private void OnNodeManagerMarkerFilterChanged(int index)
    {
        nodeManagerMarkerFilterIndex = Mathf.Clamp(index, 0, 2);
        SetSelectorValueText(nodeManagerMarkerFilterValueText, nodeManagerMarkerFilterDropdown, "全部标记");
        nodeManagerScrollPosition = 1f;
        RefreshNodeManager();
    }

    private void OnNodeManagerNameEdited(string value)
    {
        if (controller.SelectedNode == null)
            return;

        if (!controller.RenameSelectedNode(value, out string error))
        {
            if (!string.IsNullOrEmpty(error))
                Hintbox.OpenHintboxWithContent(error, 16);
            RefreshNodeManagerDetails();
            return;
        }

        RefreshNodeManager();
    }

    private void ResetNodeManagerFilters()
    {
        nodeManagerSearchQuery = string.Empty;
        nodeManagerFlowFilterIndex = 0;
        nodeManagerMarkerFilterIndex = 0;
        nodeManagerScrollPosition = 1f;
        nodeManagerSearchInput?.inputField?.SetTextWithoutNotify(string.Empty);
        nodeManagerFlowFilterDropdown?.SetValueWithoutNotify(0);
        nodeManagerMarkerFilterDropdown?.SetValueWithoutNotify(0);
        SetSelectorValueText(nodeManagerFlowFilterValueText, nodeManagerFlowFilterDropdown, "全部流程");
        SetSelectorValueText(nodeManagerMarkerFilterValueText, nodeManagerMarkerFilterDropdown, "全部标记");
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
        RefreshNodeRelatedState();
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
            if (storyOverviewText != null)
                storyOverviewText.text = "<color=#52E5F9>规模</color>\n<size=15>—</size>\n请选择剧本";
            if (storyStructureText != null)
            {
                storyStructureText.text = "<color=#52E5F9>结构</color>\n<size=15>—</size>\n请选择剧本";
                storyStructureText.color = HintColor;
            }
            if (storyResourceText != null)
                storyResourceText.text = "<color=#52E5F9>资源</color>\n<size=15>—</size>\n请选择剧本";
            return;
        }

        bool hasUnsavedChanges = controller.HasUnsavedChanges;
        string saveState = hasUnsavedChanges ? "未保存" : "已保存";
        Color saveStateColor = hasUnsavedChanges ? WarningColor : SavedColor;
        SetInputFieldValue(storyTitleInput, document.title, true);
        SetInputFieldValue(storySummaryInput, document.summary, true);
        storyStatusText.text = "运行状态：" + (document.isDraft ? "暂未载入 Mod" : "已载入 Mod")
            + "\n编辑状态：<color=#" + ColorUtility.ToHtmlStringRGB(saveStateColor) + ">" + saveState + "</color>";
        storyStatusText.color = HintColor;
        RefreshStoryOverview(document);
    }

    private void RefreshStoryOverview(StoryDocument document)
    {
        if (storyOverviewText == null || storyStructureText == null || storyResourceText == null)
            return;

        StoryNodeDocument[] nodes = (document.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null)
            .ToArray();
        int sequenceCount = nodes.Count(node => !node.isBranch);
        int branchCount = nodes.Length - sequenceCount;
        int endingCount = nodes.Count(node => node.isEnding);
        int sceneCount = nodes.Sum(node => (node.scenes ?? Array.Empty<StorySceneDocument>()).Count(scene => scene != null));
        StoryCommandDocument[] contentCommands = nodes
            .SelectMany(node => node.commands ?? Array.Empty<StoryCommandDocument>())
            .Where(command => command != null && IsVisibleStoryContent(command))
            .ToArray();
        int contentCount = contentCommands.Length;
        int choiceCount = contentCommands.Sum(command =>
            (command.choices ?? Array.Empty<StoryChoiceDocument>()).Count(choice => choice != null));
        int textCharacterCount = contentCommands.Sum(command => CountVisibleTextCharacters(command.text)
            + (command.choices ?? Array.Empty<StoryChoiceDocument>())
                .Where(choice => choice != null)
                .Sum(choice => CountVisibleTextCharacters(choice.text)));
        StoryNodeDocument entryNode = nodes.FirstOrDefault(node =>
            string.Equals(node.id, document.entry, StringComparison.OrdinalIgnoreCase));
        string entryName = entryNode == null ? "未设置" : GetNodeDisplayName(entryNode);
        StoryActorDocument[] storyActors = (document.actors ?? Array.Empty<StoryActorDocument>())
            .Where(actor => actor != null)
            .ToArray();
        int customActorCount = storyActors.Count(actor =>
            !string.Equals(actor.actorType, "pet", StringComparison.OrdinalIgnoreCase));
        int petActorCount = storyActors.Length - customActorCount;

        storyOverviewText.text = "<color=#52E5F9>规模</color>\n<size=15><color=#E6F7FA>"
            + nodes.Length + " 剧情点  " + textCharacterCount.ToString("N0") + " 字</color></size>"
            + "\n默认流程 " + sequenceCount + "    分支剧情 " + branchCount;
        storyResourceText.text = "<color=#52E5F9>资源</color>\n<color=#E6F7FA>场景 " + sceneCount
            + "   角色 " + storyActors.Length + "   内容 " + contentCount + "</color>"
            + "\n精灵 " + petActorCount + "   NPC " + customActorCount + "   选项 " + choiceCount;

        int unreachableCount = 0;
        int cannotEndCount = 0;
        if (entryNode != null)
        {
            List<StoryGraphEdge> edges = BuildStoryGraphEdges(document, nodes);
            Dictionary<string, StoryGraphNodeLayout> layouts = BuildStoryGraphLayout(document, nodes, edges);
            unreachableCount = layouts.Values.Count(layout => layout.node != null && !layout.isReachable);
            HashSet<string> canReachEnd = FindNodesThatCanReachEnd(edges);
            cannotEndCount = layouts.Values.Count(layout => layout.node != null
                && layout.isReachable
                && !canReachEnd.Contains(layout.node.id));
        }

        bool structureHealthy = entryNode != null
            && endingCount > 0
            && unreachableCount == 0
            && cannotEndCount == 0;
        storyStructureText.color = HintColor;
        if (structureHealthy)
        {
            storyStructureText.text = "<color=#52E5F9>结构</color>\n<size=15><color=#77E071>结构正常</color></size>"
                + "\n<size=11>入口 " + Shorten(entryName, 3) + "  结束 " + endingCount + "</size>";
        }
        else
        {
            string issueSummary;
            if (entryNode == null && endingCount == 0)
                issueSummary = "入口缺失  结束缺失";
            else if (entryNode == null)
                issueSummary = "入口缺失  结束 " + endingCount;
            else if (endingCount == 0)
                issueSummary = "结束标记缺失";
            else
                issueSummary = "不可达 " + unreachableCount + "  无法结束 " + cannotEndCount;
            storyStructureText.text = "<color=#52E5F9>结构</color>\n<size=15><color=#FFE847>需要处理</color></size>"
                + "\n<size=11>" + issueSummary + "</size>";
        }
    }

    private static bool IsVisibleStoryContent(StoryCommandDocument command)
    {
        string type = (command?.type ?? string.Empty).Trim().ToLowerInvariant();
        return type == "say" || type == "narrate" || type == "choice";
    }

    private static int CountVisibleTextCharacters(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        int count = 0;
        bool insideTag = false;
        foreach (char character in value)
        {
            if (character == '<')
            {
                insideTag = true;
                continue;
            }
            if (insideTag)
            {
                if (character == '>')
                    insideTag = false;
                continue;
            }
            if (!char.IsWhiteSpace(character))
                count++;
        }
        return count;
    }

    private static HashSet<string> FindNodesThatCanReachEnd(IEnumerable<StoryGraphEdge> edges)
    {
        StoryGraphEdge[] graphEdges = (edges ?? Enumerable.Empty<StoryGraphEdge>()).ToArray();
        HashSet<string> reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            StoryEndGraphNodeId,
        };
        Queue<string> pending = new Queue<string>();
        pending.Enqueue(StoryEndGraphNodeId);
        while (pending.Count > 0)
        {
            string targetId = pending.Dequeue();
            foreach (StoryGraphEdge edge in graphEdges.Where(edge => edge != null
                         && string.Equals(edge.targetId, targetId, StringComparison.OrdinalIgnoreCase)))
            {
                if (reachable.Add(edge.sourceId))
                    pending.Enqueue(edge.sourceId);
            }
        }
        return reachable;
    }

    private void RefreshNodeRelatedState()
    {
        if (connectionOverlay != null && connectionOverlay.gameObject.activeSelf)
            RefreshConnections();
        if (nodeManagerOverlay != null && nodeManagerOverlay.gameObject.activeSelf)
            RefreshNodeManager();
    }

    private void RefreshNodeManager(bool focusSelected = false)
    {
        if (nodeManagerContent == null)
            return;
        if (!focusSelected)
            CaptureNodeManagerScrollPosition();

        ClearChildren(nodeManagerContent);
        StoryNodeDocument[] allNodes = (controller.SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null)
            .ToArray();
        StoryNodeDocument[] visibleNodes = allNodes.Where(IsNodeVisibleInManager).ToArray();
        for (int index = 0; index < visibleNodes.Length; index++)
        {
            StoryNodeDocument node = visibleNodes[index];
            string nodeId = node.id;
            CreateNodeManagerListItem(nodeManagerContent, node, node == controller.SelectedNode,
                index, () => SelectNode(nodeId));
        }

        if (visibleNodes.Length == 0)
            CreateHint(nodeManagerContent, allNodes.Length == 0 ? "当前剧本没有剧情点。" : "没有符合搜索条件的剧情点。");
        else
            nodeManagerContent.sizeDelta = new Vector2(0f, 12f + visibleNodes.Length * 44f);

        if (nodeManagerCountText != null)
            nodeManagerCountText.text = "显示 " + visibleNodes.Length + " / 共 " + allNodes.Length + " 个";
        RefreshNodeManagerDetails();

        Canvas.ForceUpdateCanvases();
        if (focusSelected)
            ScrollNodeManagerToSelected(visibleNodes);
        else if (nodeManagerScroll != null)
            nodeManagerScroll.verticalNormalizedPosition = nodeManagerScrollPosition;
    }

    private bool IsNodeVisibleInManager(StoryNodeDocument node)
    {
        if (node == null)
            return false;

        bool flowMatches = nodeManagerFlowFilterIndex switch
        {
            1 => !node.isBranch,
            2 => node.isBranch,
            _ => true,
        };
        if (!flowMatches)
            return false;

        bool markerMatches = nodeManagerMarkerFilterIndex switch
        {
            1 => string.Equals(node.id, controller.SelectedDocument?.entry, StringComparison.OrdinalIgnoreCase),
            2 => node.isEnding,
            _ => true,
        };
        if (!markerMatches)
            return false;

        string query = (nodeManagerSearchQuery ?? string.Empty).Trim();
        return string.IsNullOrEmpty(query)
            || (node.id ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || (node.displayName ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void CaptureNodeManagerScrollPosition()
    {
        if (nodeManagerScroll != null && nodeManagerScroll.gameObject.activeInHierarchy)
            nodeManagerScrollPosition = nodeManagerScroll.verticalNormalizedPosition;
    }

    private void ScrollNodeManagerToSelected(StoryNodeDocument[] visibleNodes)
    {
        if (nodeManagerScroll == null || visibleNodes == null || visibleNodes.Length == 0)
            return;

        int index = Array.FindIndex(visibleNodes, node => node == controller.SelectedNode);
        if (index < 0)
        {
            nodeManagerScroll.verticalNormalizedPosition = 1f;
            nodeManagerScrollPosition = 1f;
            return;
        }

        float viewportHeight = nodeManagerScroll.viewport.rect.height;
        float contentHeight = nodeManagerContent.rect.height;
        float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
        float targetScroll = Mathf.Clamp(index * 44f + 19f - viewportHeight * .5f, 0f, maxScroll);
        nodeManagerScrollPosition = maxScroll <= 0f ? 1f : 1f - targetScroll / maxScroll;
        nodeManagerScroll.verticalNormalizedPosition = nodeManagerScrollPosition;
    }

    private void RefreshNodeManagerDetails()
    {
        if (nodeManagerDetailIdText == null || nodeManagerDetailStatsText == null
            || nodeManagerDetailDefaultText == null || nodeManagerDetailBadgeRoot == null)
            return;
        ClearChildren(nodeManagerDetailBadgeRoot);

        StoryNodeDocument node = controller.SelectedNode;
        if (node == null)
        {
            nodeManagerDetailIdText.text = string.Empty;
            nodeManagerDetailStatsText.text = "请从左侧选择一个剧情点。";
            nodeManagerDetailDefaultText.text = "可按名称或 ID 搜索，筛选不会改变剧情点排列。";
            SetInputFieldValue(nodeManagerNameInput, string.Empty, false);
            return;
        }

        int sceneCount = (node.scenes ?? Array.Empty<StorySceneDocument>()).Count(scene => scene != null);
        int actorCount = (node.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
            .Where(actor => actor != null && !string.IsNullOrWhiteSpace(actor.actorId))
            .Select(actor => actor.actorId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        StoryCommandDocument[] contentCommands = (node.commands ?? Array.Empty<StoryCommandDocument>())
            .Where(command => command != null && IsVisibleStoryContent(command))
            .ToArray();
        int optionCount = contentCommands.Sum(command =>
            (command.choices ?? Array.Empty<StoryChoiceDocument>()).Count(choice => choice != null));

        nodeManagerDetailIdText.text = "ID  " + Shorten(node.id, 10);
        SetInputFieldValue(nodeManagerNameInput, GetNodeDisplayName(node), true);
        nodeManagerDetailStatsText.text = "场景 " + sceneCount + "    角色 " + actorCount
            + "\n内容 " + contentCommands.Length + "    选项 " + optionCount;
        nodeManagerDetailDefaultText.text = "默认后续  " + Shorten(GetDefaultFlowDescription(node), 15);

        float x = 0f;
        x += CreateNodeTag(nodeManagerDetailBadgeRoot, node.isBranch ? "分支剧情" : "默认流程",
            x, 62f, node.isBranch ? WarningColor : Cyan);
        bool isEntry = string.Equals(node.id, controller.SelectedDocument?.entry, StringComparison.OrdinalIgnoreCase);
        if (isEntry)
            x += CreateNodeTag(nodeManagerDetailBadgeRoot, "入口", x, 36f, WarningColor);
        if (node.isEnding)
            CreateNodeTag(nodeManagerDetailBadgeRoot, "结束", x, 36f, SavedColor);
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
                    transition.isEnd && node.endTeleportMapId != 0
                        ? "传送 " + node.endTeleportMapId
                        : transition.isDefault ? "默认" : "条件" + ruleIndex,
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
                    label = "默认流程";
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

        string roles = (layout.node.isBranch ? "分支剧情" : "默认流程")
            + (isEntry ? " · 入口" : string.Empty)
            + (layout.node.isEnding ? " · 结束" : string.Empty);
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
        float height = 104f + Mathf.Max(1, conditionCount) * 38f;

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
                && string.Equals(option.optionId,
                    string.Equals(condition?.type, "battleResult", StringComparison.OrdinalIgnoreCase)
                        ? condition?.value : condition?.optionId,
                    StringComparison.OrdinalIgnoreCase));
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
        StoryNodeTransitionDocument explicitDefault = (controller.SelectedNode?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .FirstOrDefault(transition => transition != null && transition.isDefault);
        bool canConfigureEndTeleport = explicitDefault != null
            && explicitDefault.isEnd
            && controller.SelectedNode?.isEnding == true;
        float height = canConfigureEndTeleport ? 108f : 72f;
        RectTransform card = CreateRuleCard("Default Story Flow", y, height);
        string targetNodeId = explicitDefault?.targetNodeId ?? controller.GetSelectedNodeDefaultFlowTarget();
        string targetName = explicitDefault != null && explicitDefault.isEnd
            ? "结束整个剧情"
            : string.IsNullOrWhiteSpace(targetNodeId)
            ? "剧情在此结束"
            : GetNodeDisplayName(FindNode(targetNodeId));

        CreateText("Default Heading", card, "所有规则都不满足时", 16, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(220f, 24f));
        string defaultDescription = explicitDefault == null
            ? "按默认流程继续  →  " + targetName
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
                CreateActionButton(card, "恢复默认流程", new Vector2(-14f, -28f), new Vector2(126f, 28f),
                    () => RestoreDefaultFlow(explicitDefault.transitionId), TextAnchor.UpperRight);
            }
            if (canConfigureEndTeleport)
            {
                string endLabel = controller.SelectedNode.endTeleportMapId == 0
                    ? "结束后：正常结束"
                    : "结束后：传送到地图 " + controller.SelectedNode.endTeleportMapId;
                CreateText("End Action", card, endLabel, 14, TextAnchor.MiddleLeft, HintColor,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -74f), new Vector2(500f, 26f));
                CreateActionButton(card, "设置结束行为", new Vector2(-14f, -66f), new Vector2(126f, 28f),
                    () => OpenEndTeleportPicker(explicitDefault.transitionId), TextAnchor.UpperRight);
            }
        }
        else
        {
            CreateActionButton(card, "指定默认后续", new Vector2(-14f, -28f), new Vector2(126f, 28f),
                CreateExplicitDefaultFlow, TextAnchor.UpperRight);
        }
        return height;
    }

    private void OpenEndTeleportPicker(string transitionId)
    {
        connectionResourcePicker?.OpenMaps(GetStoryMapOptions,
            mapId => SetEndTeleport(transitionId, mapId), null, null,
            "正常结束", () => SetEndTeleport(transitionId, 0));
    }

    private void SetEndTeleport(string transitionId, int mapId)
    {
        if (!controller.UpdateSelectedNodeEndTeleport(transitionId, mapId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        connectionResourcePicker?.Close();
        RefreshConnections();
    }

    private List<WorkshopStoryPointResourceOption> GetStoryMapOptions(string filter)
    {
        Dictionary<int, WorkshopStoryPointResourceOption> options = new Dictionary<int, WorkshopStoryPointResourceOption>();
        foreach (TextAsset asset in Resources.LoadAll<TextAsset>("Data/Maps"))
        {
            try { AddStoryMapOption(options, ResourceManager.GetXML<Map>(asset.text)); }
            catch { }
        }
        string modDirectory = Path.Combine(Application.persistentDataPath, "Mod", "Maps");
        try
        {
            if (Directory.Exists(modDirectory))
            {
                foreach (string path in Directory.GetFiles(modDirectory, "*.xml", SearchOption.TopDirectoryOnly))
                {
                    try { AddStoryMapOption(options, ResourceManager.GetXML<Map>(File.ReadAllText(path))); }
                    catch { }
                }
            }
        }
        catch { }

        string query = (filter ?? string.Empty).Trim();
        return options.Values
            .Where(option => string.IsNullOrEmpty(query)
                || option.id.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (option.name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(option => option.isMod).ThenBy(option => option.id).ToList();
    }

    private static void AddStoryMapOption(Dictionary<int, WorkshopStoryPointResourceOption> options, Map map)
    {
        if (map == null || map.id == 0)
            return;
        options[map.id] = new WorkshopStoryPointResourceOption
        {
            id = map.id,
            name = map.name,
            isMod = Map.IsMod(map.id),
        };
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

    private void CreateNodeManagerListItem(RectTransform parent, StoryNodeDocument node, bool selected,
        int index, Action callback)
    {
        if (listButtonPrefab == null || node == null)
            return;

        GameObject item = Instantiate(listButtonPrefab, parent);
        item.name = "Managed Node List Item";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -8f - index * 44f);
        rect.sizeDelta = new Vector2(0f, 38f);

        foreach (Text existingText in item.GetComponentsInChildren<Text>(true))
            existingText.enabled = false;

        IButton button = item.GetComponent<IButton>();
        button.onPointerClickEvent = new UnityEngine.Events.UnityEvent();
        button.onPointerEnterEvent = new UnityEngine.Events.UnityEvent();
        button.onPointerExitEvent = new UnityEngine.Events.UnityEvent();
        button.onPointerClickEvent.AddListener(callback.Invoke);
        ConfigureListButtonVisual(button, selected);

        CreateText("Node Id", item.transform, Shorten(node.id, 12), 12, TextAnchor.MiddleLeft, HintColor,
            new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(10f, 0f), new Vector2(92f, 30f));
        CreateNodeTag(rect, node.isBranch ? "分支剧情" : "默认流程", 108f, 76f,
            node.isBranch ? WarningColor : Cyan, -8f);

        Text name = CreateText("Node Name", item.transform, Shorten(GetNodeDisplayName(node), 22), 15,
            TextAnchor.MiddleLeft, selected ? Color.white : Cyan,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        RectTransform nameRect = name.rectTransform;
        nameRect.anchorMin = Vector2.zero;
        nameRect.anchorMax = Vector2.one;
        nameRect.pivot = new Vector2(.5f, .5f);
        nameRect.offsetMin = new Vector2(196f, 2f);
        nameRect.offsetMax = new Vector2(-116f, -2f);
        name.horizontalOverflow = HorizontalWrapMode.Wrap;
        name.verticalOverflow = VerticalWrapMode.Truncate;

        GameObject markerRootObject = new GameObject("Node Markers", typeof(RectTransform));
        markerRootObject.transform.SetParent(item.transform, false);
        RectTransform markerRoot = markerRootObject.GetComponent<RectTransform>();
        markerRoot.anchorMin = new Vector2(1f, .5f);
        markerRoot.anchorMax = new Vector2(1f, .5f);
        markerRoot.pivot = new Vector2(1f, .5f);
        markerRoot.anchoredPosition = new Vector2(-8f, 0f);
        markerRoot.sizeDelta = new Vector2(104f, 30f);
        bool isEntry = string.Equals(node.id, controller.SelectedDocument?.entry, StringComparison.OrdinalIgnoreCase);
        if (isEntry)
            CreateNodeTag(markerRoot, "入口", 0f, 48f, WarningColor, -4f);
        if (node.isEnding)
            CreateNodeTag(markerRoot, "结束", 54f, 48f, SavedColor, -4f);

        GameObject focusFrame = CreateListFocusFrame(item.transform);
        focusFrame.SetActive(selected);
        button.onPointerEnterEvent.AddListener(() => focusFrame.SetActive(true));
        button.onPointerExitEvent.AddListener(() => focusFrame.SetActive(selected));
    }

    private float CreateNodeTag(RectTransform parent, string label, float x, float width, Color color,
        float y = 0f)
    {
        GameObject tagObject = new GameObject("Node Tag " + label,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        tagObject.transform.SetParent(parent, false);
        RectTransform rect = tagObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, 22f);

        Image image = tagObject.GetComponent<Image>();
        image.color = new Color(color.r, color.g, color.b, .11f);
        image.raycastTarget = false;
        Outline outline = tagObject.GetComponent<Outline>();
        outline.effectColor = new Color(color.r, color.g, color.b, .58f);
        outline.effectDistance = new Vector2(1f, -1f);

        CreateText("Label", tagObject.transform, label, 11, TextAnchor.MiddleCenter, color,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(width - 4f, 20f));
        return width + 6f;
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
