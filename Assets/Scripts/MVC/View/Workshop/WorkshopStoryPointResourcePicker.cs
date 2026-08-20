using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 剧情点画布中的临时资源选择器。
/// 它只负责从既有地图 / 精灵数据库中选择资源，不持有剧情草稿，也不直接修改数据。
/// </summary>
public sealed class WorkshopStoryPointResourcePicker
{
    private readonly Transform parent;
    private readonly GameObject actionButtonPrefab;
    private readonly GameObject listButtonPrefab;
    private readonly GameObject inputPrefab;
    private readonly Font font;

    private GameObject root;
    private RectTransform listContent;
    private InputField searchInput;
    private Action<string> refresh;
    private IButton sourceButton;
    private Text sourceButtonText;
    private bool showModResources;
    private bool hasModResources;

    public bool isOpen => root != null;

    public WorkshopStoryPointResourcePicker(Transform parent, GameObject actionButtonPrefab,
        GameObject listButtonPrefab, GameObject inputPrefab, Font font)
    {
        this.parent = parent;
        this.actionButtonPrefab = actionButtonPrefab;
        this.listButtonPrefab = listButtonPrefab;
        this.inputPrefab = inputPrefab;
        this.font = font;
    }

    public void OpenMaps(Func<string, List<WorkshopStoryPointResourceOption>> getOptions,
        Action<int> onSelected,
        Func<string, List<WorkshopStoryCustomSceneOption>> getCustomScenes = null,
        Action<string> onCustomSceneSelected = null,
        string directActionLabel = null,
        Action directAction = null)
    {
        bool canUseModResources = HasModResources(getOptions);
        Open("选择本剧情点地图", "输入地图名称或 ID", query =>
        {
            List<WorkshopStoryPointResourceOption> options = getOptions?.Invoke(query)
                ?? new List<WorkshopStoryPointResourceOption>();
            BuildResourceItems(FilterResourceSource(options), option => onSelected?.Invoke(option.id));
        }, !string.IsNullOrWhiteSpace(directActionLabel) ? directActionLabel : getCustomScenes == null ? null : "自制场景",
        !string.IsNullOrWhiteSpace(directActionLabel) ? directAction : getCustomScenes == null ? null : (Action)(() => OpenCustomScenes(
            getOptions, onSelected, getCustomScenes, onCustomSceneSelected)),
        true, canUseModResources);
    }

    private void OpenCustomScenes(
        Func<string, List<WorkshopStoryPointResourceOption>> getMaps,
        Action<int> onMapSelected,
        Func<string, List<WorkshopStoryCustomSceneOption>> getOptions,
        Action<string> onSelected)
    {
        Open("选择自制场景", "输入场景名称或 ID", query =>
        {
            ClearList();
            int index = 0;
            foreach (WorkshopStoryCustomSceneOption option in getOptions?.Invoke(query)
                     ?? new List<WorkshopStoryCustomSceneOption>())
            {
                if (option == null)
                    continue;
                WorkshopStoryCustomSceneOption captured = option;
                CreateListButton(option.displayName, index++, () => onSelected?.Invoke(captured.sceneResourceId));
            }
            FinishList(index, "当前剧本还没有已配置背景的自制场景。");
        }, "返回地图", () => OpenMaps(getMaps, onMapSelected, getOptions, onSelected));
    }

    public void OpenPets(Func<string, List<WorkshopStoryPointResourceOption>> getOptions, Action<int> onSelected)
    {
        bool canUseModResources = HasModResources(getOptions);
        Open("添加本剧情点精灵", "输入精灵名称或 ID", query =>
        {
            List<WorkshopStoryPointResourceOption> options = getOptions?.Invoke(query)
                ?? new List<WorkshopStoryPointResourceOption>();
            BuildResourceItems(FilterResourceSource(options), option => onSelected?.Invoke(option.id));
        }, null, null, true, canUseModResources);
    }

    public void OpenAddActors(Func<string, List<WorkshopStoryPointAddActorOption>> getOptions,
        Action<WorkshopStoryPointAddActorOption> onSelected)
    {
        bool canUseModResources = (getOptions?.Invoke(string.Empty) ?? new List<WorkshopStoryPointAddActorOption>())
            .Any(option => option != null && option.isMod);
        Open("添加角色", "输入角色名称或 ID", query =>
        {
            IEnumerable<WorkshopStoryPointAddActorOption> options = getOptions?.Invoke(query)
                ?? Enumerable.Empty<WorkshopStoryPointAddActorOption>();
            ClearList();
            int index = 0;
            foreach (WorkshopStoryPointAddActorOption option in options.Where(value => value != null
                         && value.isMod == showModResources))
            {
                WorkshopStoryPointAddActorOption captured = option;
                CreateListButton(option.displayName, index++, () => onSelected?.Invoke(captured));
            }
            FinishList(index, showModResources
                ? "当前 Mod 中没有匹配的角色资源。"
                : "没有匹配的本体精灵或剧本角色。");
        }, null, null, true, canUseModResources);
    }

    public void OpenActors(IReadOnlyList<WorkshopStoryPointActorOption> options, Action<string> onSelected)
    {
        Open("选择说话角色", "筛选已添加的角色", query =>
        {
            string filter = (query ?? string.Empty).Trim();
            IEnumerable<WorkshopStoryPointActorOption> visible = options ?? Array.Empty<WorkshopStoryPointActorOption>();
            if (!string.IsNullOrEmpty(filter))
            {
                visible = visible.Where(option => option != null
                    && option.displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            BuildActorItems(visible, option => onSelected?.Invoke(option.actorId));
        }, null, null);
    }

    public void OpenBattles(Func<string, List<StoryBattleOption>> getOptions,
        Action<StoryBattleReferenceDocument> onSelected)
    {
        bool canUseModResources = (getOptions?.Invoke(string.Empty) ?? new List<StoryBattleOption>())
            .Any(option => option != null && option.isMod);
        Open("选择 XML 战斗", "输入地图、NPC、战斗名称或 ID", query =>
        {
            IEnumerable<StoryBattleOption> options = getOptions?.Invoke(query) ?? Enumerable.Empty<StoryBattleOption>();
            ClearList();
            int index = 0;
            foreach (StoryBattleOption option in options.Where(value => value != null && value.isMod == showModResources))
            {
                StoryBattleOption captured = option;
                CreateListButton(option.displayName, index++, () => onSelected?.Invoke(captured.reference));
            }
            FinishList(index, showModResources ? "当前 Mod 中没有匹配的 XML 战斗。" : "没有匹配的本体 XML 战斗。");
        }, null, null, true, canUseModResources);
    }

    public void OpenItems(Func<string, List<WorkshopStoryItemOption>> getOptions,
        Action<WorkshopStoryItemOption> onSelected)
    {
        bool canUseModResources = (getOptions?.Invoke(string.Empty) ?? new List<WorkshopStoryItemOption>())
            .Any(option => option != null && option.isMod);
        Open("添加场景物件", "输入物品名称或 ID", query =>
        {
            IEnumerable<WorkshopStoryItemOption> options = getOptions?.Invoke(query)
                ?? Enumerable.Empty<WorkshopStoryItemOption>();
            ClearList();
            int index = 0;
            foreach (WorkshopStoryItemOption option in options.Where(value => value != null
                && value.isMod == showModResources))
            {
                WorkshopStoryItemOption captured = option;
                CreateItemListButton(option, index++, () => onSelected?.Invoke(captured));
            }
            FinishList(index, showModResources ? "当前 Mod 中没有匹配的物品图片。" : "没有匹配的本体物品图片。");
        }, null, null, true, canUseModResources);
    }

    public void OpenMapLibrary(Func<List<WorkshopStoryPointResourceOption>> getOptions, Action onAdd,
        Action<int> onRemove)
    {
        Open("剧情点地图资源", "筛选已选地图", query =>
        {
            string filter = (query ?? string.Empty).Trim();
            IEnumerable<WorkshopStoryPointResourceOption> options = getOptions?.Invoke()
                ?? Enumerable.Empty<WorkshopStoryPointResourceOption>();
            if (!string.IsNullOrEmpty(filter))
                options = options.Where(option => option != null && option.displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            BuildManagedResourceItems(options, option => onRemove?.Invoke(option.id));
        }, "添加地图", onAdd);
    }

    public void OpenActorLibrary(Func<List<WorkshopStoryPointActorOption>> getOptions, Action onAdd,
        Action<string> onRemove)
    {
        Open("剧情点角色资源", "筛选已选角色", query =>
        {
            string filter = (query ?? string.Empty).Trim();
            IEnumerable<WorkshopStoryPointActorOption> options = getOptions?.Invoke()
                ?? Enumerable.Empty<WorkshopStoryPointActorOption>();
            if (!string.IsNullOrEmpty(filter))
                options = options.Where(option => option != null && option.displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            BuildManagedActorItems(options, option => onRemove?.Invoke(option.actorId));
        }, "添加角色", onAdd);
    }

    public void OpenSceneActors(IReadOnlyList<WorkshopStoryPointActorOption> available,
        IReadOnlyList<WorkshopStoryPointActorOption> visible, Action<string> onAdd, Action<string> onRemove)
    {
        Open("当前场景角色", "筛选剧情点角色资源", query =>
        {
            string filter = (query ?? string.Empty).Trim();
            HashSet<string> visibleIds = new HashSet<string>((visible ?? Array.Empty<WorkshopStoryPointActorOption>())
                .Where(option => option != null).Select(option => option.actorId), StringComparer.OrdinalIgnoreCase);
            IEnumerable<WorkshopStoryPointActorOption> options = available ?? Array.Empty<WorkshopStoryPointActorOption>();
            if (!string.IsNullOrWhiteSpace(filter))
                options = options.Where(option => option != null && option.displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

            ClearList();
            int index = 0;
            foreach (WorkshopStoryPointActorOption option in options)
            {
                if (option == null)
                    continue;

                WorkshopStoryPointActorOption captured = option;
                bool isVisible = visibleIds.Contains(option.actorId);
                CreateSceneActorItem(option.displayName, index++, isVisible,
                    () => onAdd?.Invoke(captured.actorId), () => onRemove?.Invoke(captured.actorId));
            }
            FinishList(index, "请先通过“角色资源”把角色加入剧情点。");
        }, null, null);
    }

    public void Close()
    {
        if (root != null)
            UnityEngine.Object.Destroy(root);

        root = null;
        listContent = null;
        searchInput = null;
        refresh = null;
        sourceButton = null;
        sourceButtonText = null;
        showModResources = false;
        hasModResources = false;
    }

    private void Open(string title, string placeholder, Action<string> refreshItems,
        string directActionLabel, Action directAction, bool showSourceButton = false,
        bool canUseModResources = false)
    {
        Close();
        if (parent == null || actionButtonPrefab == null || listButtonPrefab == null || inputPrefab == null)
            return;

        hasModResources = canUseModResources;

        root = new GameObject("Story Point Resource Picker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        root.transform.SetParent(parent, false);
        root.transform.SetAsLastSibling();
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(.5f, .5f);
        rootRect.anchorMax = new Vector2(.5f, .5f);
        rootRect.pivot = new Vector2(.5f, .5f);
        rootRect.anchoredPosition = new Vector2(0f, -8f);
        rootRect.sizeDelta = new Vector2(500f, 316f);

        Image rootImage = root.GetComponent<Image>();
        rootImage.color = new Color32(0, 8, 12, 246);
        rootImage.raycastTarget = true;
        Outline outline = root.GetComponent<Outline>();
        outline.effectColor = new Color32(82, 229, 249, 255);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        CreateText("Title", root.transform, title, 21, TextAnchor.MiddleCenter, new Color32(82, 229, 249, 255),
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -22f), new Vector2(300f, 28f));
        CreateActionButton("关闭", new Vector2(-16f, -12f), new Vector2(72f, 26f), Close, true);

        float searchWidth = showSourceButton && !string.IsNullOrWhiteSpace(directActionLabel)
            ? 218f
            : showSourceButton ? 334f : 282f;
        searchInput = CreateInput(placeholder, new Vector2(18f, -54f), new Vector2(searchWidth, 26f));
        if (searchInput != null)
            searchInput.onValueChanged.AddListener(value => refresh?.Invoke(value));

        if (showSourceButton)
        {
            float sourceX = string.IsNullOrWhiteSpace(directActionLabel) ? -16f : -140f;
            sourceButton = CreateActionButton("本体资源", new Vector2(sourceX, -54f), new Vector2(116f, 26f),
                ToggleResourceSource, true);
            sourceButtonText = sourceButton?.GetComponentInChildren<Text>(true);
            RefreshSourceButton();
        }

        if (!string.IsNullOrWhiteSpace(directActionLabel))
        {
            CreateActionButton(directActionLabel, new Vector2(-16f, -54f), new Vector2(116f, 26f), directAction, true);
        }

        listContent = CreateScrollContent(new Vector2(18f, 18f), new Vector2(-18f, -92f));
        refresh = refreshItems;
        refresh?.Invoke(string.Empty);
    }

    private static bool HasModResources(Func<string, List<WorkshopStoryPointResourceOption>> getOptions)
    {
        return (getOptions?.Invoke(string.Empty) ?? new List<WorkshopStoryPointResourceOption>())
            .Any(option => option != null && option.isMod);
    }

    private IEnumerable<WorkshopStoryPointResourceOption> FilterResourceSource(
        IEnumerable<WorkshopStoryPointResourceOption> options)
    {
        return (options ?? Enumerable.Empty<WorkshopStoryPointResourceOption>())
            .Where(option => option != null && option.isMod == showModResources);
    }

    private void ToggleResourceSource()
    {
        if (!hasModResources)
            return;

        showModResources = !showModResources;
        RefreshSourceButton();
        refresh?.Invoke(searchInput?.text ?? string.Empty);
    }

    private void RefreshSourceButton()
    {
        if (sourceButtonText != null)
            sourceButtonText.text = showModResources ? "当前 Mod" : hasModResources ? "本体资源" : "仅本体";
    }

    private void BuildResourceItems(IEnumerable<WorkshopStoryPointResourceOption> options,
        Action<WorkshopStoryPointResourceOption> onClick)
    {
        ClearList();
        int index = 0;
        foreach (WorkshopStoryPointResourceOption option in options ?? Enumerable.Empty<WorkshopStoryPointResourceOption>())
        {
            if (option == null)
                continue;

            WorkshopStoryPointResourceOption captured = option;
            CreateListButton(option.displayName, index++, () => onClick?.Invoke(captured));
        }

        string sourceLabel = showModResources ? "当前 Mod" : "本体";
        FinishList(index, "没有匹配的" + sourceLabel + "资源。");
    }

    private void BuildActorItems(IEnumerable<WorkshopStoryPointActorOption> options, Action<WorkshopStoryPointActorOption> onClick)
    {
        ClearList();
        int index = 0;
        foreach (WorkshopStoryPointActorOption option in options ?? Enumerable.Empty<WorkshopStoryPointActorOption>())
        {
            if (option == null)
                continue;

            WorkshopStoryPointActorOption captured = option;
            CreateListButton(option.displayName, index++, () => onClick?.Invoke(captured));
        }

        FinishList(index, "请先通过“添加角色”将角色加入本剧情点。");
    }

    private void BuildManagedResourceItems(IEnumerable<WorkshopStoryPointResourceOption> options,
        Action<WorkshopStoryPointResourceOption> onRemove)
    {
        ClearList();
        int index = 0;
        foreach (WorkshopStoryPointResourceOption option in options ?? Enumerable.Empty<WorkshopStoryPointResourceOption>())
        {
            if (option == null)
                continue;

            WorkshopStoryPointResourceOption captured = option;
            CreateManagedListItem(option.displayName, index++, () => onRemove?.Invoke(captured));
        }
        FinishList(index, "还没有地图资源。请点击“添加地图”。");
    }

    private void BuildManagedActorItems(IEnumerable<WorkshopStoryPointActorOption> options,
        Action<WorkshopStoryPointActorOption> onRemove)
    {
        ClearList();
        int index = 0;
        foreach (WorkshopStoryPointActorOption option in options ?? Enumerable.Empty<WorkshopStoryPointActorOption>())
        {
            if (option == null)
                continue;

            WorkshopStoryPointActorOption captured = option;
            CreateManagedListItem(option.displayName, index++, () => onRemove?.Invoke(captured));
        }
        FinishList(index, "还没有角色资源。请点击“添加角色”。");
    }

    private void FinishList(int count, string emptyHint)
    {
        if (listContent == null)
            return;

        if (count == 0)
        {
            CreateText("Empty Hint", listContent, emptyHint, 14, TextAnchor.MiddleCenter, new Color32(180, 220, 230, 255),
                new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -46f), new Vector2(410f, 44f));
            listContent.sizeDelta = new Vector2(0f, 96f);
            return;
        }

        listContent.sizeDelta = new Vector2(0f, 10f + count * 36f);
    }

    private InputField CreateInput(string placeholder, Vector2 position, Vector2 size)
    {
        GameObject item = UnityEngine.Object.Instantiate(inputPrefab, root.transform);
        item.name = "Resource Search Input";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        IInputField wrapper = item.GetComponent<IInputField>();
        InputField input = item.GetComponent<InputField>();
        if (input == null)
            return null;

        input.onValueChanged = new InputField.OnChangeEvent();
        input.onEndEdit = new InputField.EndEditEvent();
        input.text = string.Empty;
        input.interactable = true;
        wrapper?.SetPlaceHolderText(placeholder);
        return input;
    }

    private RectTransform CreateScrollContent(Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject viewportObject = new GameObject("Resource List Viewport", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.transform.SetParent(root.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = offsetMin;
        viewport.offsetMax = offsetMax;

        Image image = viewportObject.GetComponent<Image>();
        image.color = new Color32(0, 4, 8, 255);
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

    private void CreateListButton(string label, int index, Action callback)
    {
        GameObject item = UnityEngine.Object.Instantiate(listButtonPrefab, listContent);
        item.name = "Resource List Button";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -6f - index * 36f);
        rect.sizeDelta = new Vector2(0f, 30f);

        IButton button = item.GetComponent<IButton>();
        button.button.onClick = new Button.ButtonClickedEvent();
        button.onPointerClickEvent = new UnityEvent();
        button.onPointerEnterEvent = new UnityEvent();
        button.onPointerExitEvent = new UnityEvent();
        button.onPointerClickEvent.AddListener(callback.Invoke);
        button.button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(.82f, .94f, 1f, 1f);
        colors.pressedColor = new Color(.62f, .76f, .82f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = .06f;
        button.button.colors = colors;

        Text text = item.GetComponentInChildren<Text>(true);
        if (text == null)
            return;

        // 保留项目“Scroll List Button”预制体的字体、字号和材质。
        // 运行时替换为 Zongyi 会让位图字体在该弹窗缩放下发糊。
        text.raycastTarget = false;
        text.text = label;
    }

    private void CreateItemListButton(WorkshopStoryItemOption option, int index, Action callback)
    {
        CreateListButton(option?.displayName ?? string.Empty, index, callback);
        if (listContent == null || listContent.childCount == 0 || option?.icon == null)
            return;

        Transform row = listContent.GetChild(listContent.childCount - 1);
        Text text = row.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            RectTransform textRect = text.rectTransform;
            textRect.offsetMin = new Vector2(Mathf.Max(34f, textRect.offsetMin.x), textRect.offsetMin.y);
            text.alignment = TextAnchor.MiddleLeft;
        }

        GameObject iconObject = new GameObject("Item Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.transform.SetParent(row, false);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, .5f);
        iconRect.anchorMax = new Vector2(0f, .5f);
        iconRect.pivot = new Vector2(.5f, .5f);
        iconRect.anchoredPosition = new Vector2(18f, 0f);
        iconRect.sizeDelta = new Vector2(26f, 26f);
        Image image = iconObject.GetComponent<Image>();
        image.sprite = option.icon;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private void CreateManagedListItem(string label, int index, Action remove)
    {
        CreateListButton(label, index, () => { });
        Transform row = listContent.GetChild(listContent.childCount - 1);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.offsetMax = new Vector2(-74f, rowRect.offsetMax.y);

        GameObject delete = UnityEngine.Object.Instantiate(actionButtonPrefab, listContent);
        delete.name = "删除资源";
        RectTransform rect = delete.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(0f, -6f - index * 36f);
        rect.sizeDelta = new Vector2(68f, 30f);
        IButton button = delete.GetComponent<IButton>();
        button.button.onClick = new Button.ButtonClickedEvent();
        button.onPointerClickEvent = new UnityEvent();
        button.onPointerClickEvent.AddListener(remove.Invoke);
        Text text = delete.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.raycastTarget = false;
            text.text = "删除";
        }
    }

    private void CreateSceneActorItem(string label, int index, bool isVisible, Action add, Action remove)
    {
        CreateListButton(label, index, () => { });
        Transform row = listContent.GetChild(listContent.childCount - 1);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.offsetMax = new Vector2(-88f, rowRect.offsetMax.y);

        GameObject action = UnityEngine.Object.Instantiate(actionButtonPrefab, listContent);
        action.name = isVisible ? "移出当前场景" : "加入当前场景";
        RectTransform rect = action.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(0f, -6f - index * 36f);
        rect.sizeDelta = new Vector2(82f, 30f);
        IButton button = action.GetComponent<IButton>();
        button.button.onClick = new Button.ButtonClickedEvent();
        button.onPointerClickEvent = new UnityEvent();
        button.onPointerClickEvent.AddListener((isVisible ? remove : add).Invoke);
        Text text = action.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.raycastTarget = false;
            text.text = isVisible ? "移出场景" : "加入场景";
        }
    }

    private IButton CreateActionButton(string label, Vector2 position, Vector2 size, Action callback, bool anchorRight)
    {
        GameObject item = UnityEngine.Object.Instantiate(actionButtonPrefab, root.transform);
        item.name = label + " Button";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = anchorRight ? Vector2.one : new Vector2(0f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = anchorRight ? Vector2.one : new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        IButton button = item.GetComponent<IButton>();
        button.button.onClick = new Button.ButtonClickedEvent();
        button.onPointerClickEvent = new UnityEvent();
        button.onPointerClickEvent.AddListener(callback.Invoke);

        Text text = item.GetComponentInChildren<Text>(true);
        if (text == null)
            return button;

        text.raycastTarget = false;
        text.text = label;
        return button;
    }

    private void ClearList()
    {
        if (listContent == null)
            return;

        foreach (Transform child in listContent.Cast<Transform>().ToArray())
            UnityEngine.Object.Destroy(child.gameObject);
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
}
