using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自制剧情的入口页：只负责浏览当前 Mod 的剧本和剧情点。
/// 编辑器本身将在后续独立页面实现，不在这里叠加业务控件。
/// </summary>
public class WorkshopStoryBrowserPanel : Panel
{
    private static readonly Color Cyan = new Color32(82, 229, 249, 255);
    private static readonly Color HintColor = new Color32(180, 220, 230, 255);

    private readonly WorkshopStoryBrowserController controller = new WorkshopStoryBrowserController(
        new WorkshopStoryBrowserModel(new WorkshopStoryRepository()));

    private RectTransform storyContent;
    private RectTransform nodeContent;
    private Font font;
    private GameObject listButtonPrefab;
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

        RectTransform storySection = CreateSection("剧本", new Vector2(20f, -76f));
        RectTransform nodeSection = CreateSection("剧情点", new Vector2(470f, -76f));
        storyContent = CreateScrollContent(storySection);
        nodeContent = CreateScrollContent(nodeSection);
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
                .FirstOrDefault(transform => transform.gameObject.name == "Title" &&
                    transform.GetComponentInChildren<TMP_Text>(true)?.text == "任务");
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

    private RectTransform CreateSection(string title, Vector2 position)
    {
        GameObject section = new GameObject(title + " Area", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        section.transform.SetParent(transform, false);

        RectTransform rect = section.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(430f, 402f);

        ApplyProjectFrame(section.GetComponent<Image>());

        CreateText("Heading", section.transform, title, 24, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -32f), new Vector2(220f, 32f));
        return rect;
    }

    private RectTransform CreateScrollContent(RectTransform section)
    {
        GameObject viewportObject = new GameObject("List Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.transform.SetParent(section, false);

        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(18f, 18f);
        viewport.offsetMax = new Vector2(-18f, -60f);

        Image image = viewportObject.GetComponent<Image>();
        image.color = new Color32(0, 8, 12, 255);
        Mask mask = viewportObject.GetComponent<Mask>();
        mask.showMaskGraphic = true;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(viewport, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(.5f, 1f);
        content.anchorMax = new Vector2(.5f, 1f);
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

    private void Reload()
    {
        if (!controller.Open(out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void SelectStory(string path)
    {
        if (!controller.SelectStory(path, out string error) && !string.IsNullOrEmpty(error))
            Hintbox.OpenHintboxWithContent(error, 16);
        RefreshView();
    }

    private void SelectNode(string nodeId)
    {
        controller.SelectNode(nodeId);
        RefreshNodes();
    }

    private void RefreshView()
    {
        ClearChildren(storyContent);
        int index = 0;
        foreach (WorkshopStorySummary story in controller.Stories)
        {
            string title = story.isValid ? story.title : "[格式错误] " + story.fileName;
            string path = story.path;
            CreateListButton(storyContent, title, story == controller.SelectedStory, index++, () => SelectStory(path));
        }
        RefreshNodes();
    }

    private void RefreshNodes()
    {
        ClearChildren(nodeContent);
        StoryNodeDocument[] nodes = controller.SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>();
        if (nodes.Length == 0)
        {
            string hint = controller.Stories.Count == 0 ? "当前 Mod/Stories 中暂无剧本。" : "请选择左侧剧本。";
            CreateText("Hint", nodeContent, hint, 17, TextAnchor.MiddleCenter, HintColor,
                new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -72f), new Vector2(360f, 48f));
            nodeContent.sizeDelta = new Vector2(0f, 120f);
            return;
        }

        int index = 0;
        foreach (StoryNodeDocument node in nodes)
        {
            if (node == null)
                continue;

            string label = (node.id == controller.SelectedDocument.entry ? "入口 · " : string.Empty) + (node.displayName ?? node.id);
            string nodeId = node.id;
            CreateListButton(nodeContent, label, node == controller.SelectedNode, index++, () => SelectNode(nodeId));
        }
        nodeContent.sizeDelta = new Vector2(0f, 14f + index * 42f);
    }

    private void CreateListButton(RectTransform parent, string label, bool selected, int index, Action callback)
    {
        if (listButtonPrefab == null)
            return;

        GameObject item = Instantiate(listButtonPrefab, parent);
        item.name = "Story List Button";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(.5f, 1f);
        rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -8f - index * 42f);
        rect.sizeDelta = new Vector2(382f, 36f);

        IButton button = item.GetComponent<IButton>();
        button.onPointerClickEvent.SetListener(callback.Invoke);
        if (selected && button.button.spriteState.selectedSprite != null)
            button.image.sprite = button.button.spriteState.selectedSprite;

        Text text = item.GetComponentInChildren<Text>();
        text.font = font;
        text.fontSize = 18;
        text.fontStyle = FontStyle.Normal;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = selected ? new Color32(255, 232, 71, 255) : Cyan;
        text.raycastTarget = false;
        text.text = label;
    }

    private void CreateText(string name, Transform parent, string value, int size, TextAnchor alignment, Color color,
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
    }

    private static void ClearChildren(Transform parent)
    {
        foreach (Transform child in parent.Cast<Transform>().ToArray())
            Destroy(child.gameObject);
    }

    /// <summary>
    /// 复用任务面板的黑底白框配置：同一张九宫格底图、同一组描边参数。
    /// 不自行猜测边框的厚度或偏移。
    /// </summary>
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
