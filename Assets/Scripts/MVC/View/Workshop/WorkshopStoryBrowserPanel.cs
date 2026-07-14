using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自制剧情入口页：负责剧本与剧情点的选择、创建、删除和保存。
/// 具体剧情点的可视化编辑将在独立页面完成，避免在入口页堆叠编辑控件。
/// </summary>
public class WorkshopStoryBrowserPanel : Panel
{
    private static readonly Color Cyan = new Color32(82, 229, 249, 255);
    private static readonly Color HintColor = new Color32(180, 220, 230, 255);
    private static readonly Color WarningColor = new Color32(255, 232, 71, 255);

    private readonly WorkshopStoryBrowserController controller = new WorkshopStoryBrowserController(
        new WorkshopStoryBrowserModel(new WorkshopStoryRepository()));

    private RectTransform storyContent;
    private RectTransform nodeContent;
    private Text storyStatusText;
    private Font font;
    private GameObject listButtonPrefab;
    private GameObject actionButtonPrefab;
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
            new Vector2(70f, -84f), new Vector2(360f, 26f), OnStorySummaryEdited);
        CreateActionButton(infoSection, "保存", new Vector2(-16f, -84f), new Vector2(96f, 26f), SaveStory, TextAnchor.UpperRight);

        CreateActionButton(nodeSection, "新建", new Vector2(16f, -50f), new Vector2(94f, 28f), CreateNode);
        CreateActionButton(nodeSection, "删除", new Vector2(120f, -50f), new Vector2(94f, 28f), DeleteNode);
        CreateActionButton(nodeSection, "设为入口", new Vector2(224f, -50f), new Vector2(116f, 28f), SetEntryNode);
        CreateActionButton(nodeSection, "编辑剧情点", new Vector2(352f, -50f), new Vector2(132f, 28f), OpenNodeEditor);
        nodeContent = CreateScrollContent(nodeSection, new Vector2(14f, 14f), new Vector2(-14f, -86f));
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

        WorkshopStoryNodeEditorPanel.Open(controller.SelectedStory.path, controller.SelectedNode.id);
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

        string status = document.isDraft ? "草稿" : "已发布";
        int nodeCount = (document.nodes ?? Array.Empty<StoryNodeDocument>()).Count(node => node != null);
        SetInputFieldValue(storyTitleInput, document.title, true);
        SetInputFieldValue(storySummaryInput, document.summary, true);
        storyStatusText.text = "状态：" + status + "\n剧情点：" + nodeCount;
        storyStatusText.color = document.isDraft ? HintColor : WarningColor;
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
            int sceneCount = (node.scenes ?? Array.Empty<StorySceneDocument>()).Count(scene => scene != null);
            int commandCount = (node.commands ?? Array.Empty<StoryCommandDocument>()).Count(command => command != null);
            string label = (node.id == controller.SelectedDocument.entry ? "入口 · " : string.Empty)
                + displayName + "    场景 " + sceneCount + " / 命令 " + commandCount;
            string nodeId = node.id;
            CreateListButton(nodeContent, label, node == controller.SelectedNode, index++, () => SelectNode(nodeId));
        }

        if (index == 0)
            CreateHint(nodeContent, controller.SelectedDocument == null ? "请选择剧本。" : "当前剧本没有剧情点。");
        else
            nodeContent.sizeDelta = new Vector2(0f, 12f + index * 42f);
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

    private GameObject FindWorkshopActionButtonPrefab()
    {
        GameObject workshopPanel = ResourceManager.instance.GetPanel("Workshop");
        if (workshopPanel == null)
            return null;

        IButton button = workshopPanel.GetComponentsInChildren<IButton>(true)
            .FirstOrDefault(value => value.GetComponentInChildren<Text>(true)?.text == "导出 mod");
        return button?.gameObject;
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
