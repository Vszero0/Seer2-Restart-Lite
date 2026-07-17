using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TextCore.LowLevel;
using TMPro;

public class DialogView : Module
{
    private ResourceManager RM => ResourceManager.instance;
    [SerializeField] private Color32 initTextColor = new Color32(252, 237, 105, 255);
    [SerializeField] private Color32 hoverTextColor = new Color32(119, 226, 12, 255);
    [SerializeField] private GameObject functionTextPrefab;
    [SerializeField] private GameObject replyTextPrefab;

    [SerializeField] private Vector2 stdIconSize = new Vector2(150, 150);
    [SerializeField] private Image icon;
    [SerializeField] private UniGifImage gif;
    [SerializeField] private IText npcName;
    [SerializeField] private IText content;
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private RectTransform functionRect;
    [SerializeField] private RectTransform replyRect;

    private Action<NpcButtonHandler> replyClickHandler;
    private Image storySpeakerIcon;
    private Image storySpeakerExpression;
    private TextMeshProUGUI storySpeakerText;
    private TextMeshProUGUI storySpeakerHintText;
    private RectTransform storySpeakerGroupRect;
    private RectTransform storySpeakerIconRect;
    private RectTransform storySpeakerExpressionRect;
    private RectTransform storySpeakerRect;
    private RectTransform storySpeakerHintRect;
    private RectTransform contentTextRect;
    private CanvasGroup storyContentCanvasGroup;
    private CanvasGroup storyIconCanvasGroup;
    private Coroutine storyFadeCoroutine;
    private Vector2 defaultContentAnchorMin;
    private Vector2 defaultContentAnchorMax;
    private Vector2 defaultContentPivot;
    private Vector2 defaultContentAnchoredPosition;
    private Vector2 defaultContentSizeDelta;
    private Vector2 defaultTextAnchorMin;
    private Vector2 defaultTextAnchorMax;
    private Vector2 defaultTextPivot;
    private Vector2 defaultTextAnchoredPosition;
    private Vector2 defaultTextSizeDelta;
    private TMP_FontAsset defaultFont;
    private Material defaultFontSharedMaterial;
    private float defaultFontSize;
    private FontStyles defaultFontStyle;
    private Color defaultTextColor;
    private Color defaultOutlineColor;
    private float defaultOutlineWidth;
    private bool hasDefaultLayout;
    private Action storySpeakerIconClickHandler;
    private string storySpeakerHint;
    private static readonly Color32 StorySpeakerHintColor = new Color32(145, 190, 200, 210);
    private static readonly Color32 StorySpeakerHintHoverColor = new Color32(82, 229, 249, 255);

    protected override void Awake()
    {
        base.Awake();
        StoreDefaultLayout();
        StoreDefaultTextStyle();
    }

    public void SetReplyClickHandler(Action<NpcButtonHandler> handler)
    {
        replyClickHandler = handler;
    }

    public void OpenDialog(DialogInfo info)
    {
        bool useStoryLayout = info?.id == "story";
        bool hasIcon = info?.icon != null && info.icon != SpriteSet.Empty && info.size.x > 0 && info.size.y > 0;

        SetIconAndName(info.icon, info.pos, info.size, info.name);
        SetGif(info.gifInfo, info.icon);
        SetContent(info.content);
        if (useStoryLayout)
            ApplyStoryLayout(info, hasIcon);
        else
            ResetStoryLayout();

        SetFunction(info.functionHandler);
        SetReply(info.replyHandler);
    }

    private void SetIconAndName(Sprite sprite, Vector2 iconPos, Vector2 iconSize, string name)
    {
        if (icon != null)
        {
            bool hasIcon = sprite != null && sprite != SpriteSet.Empty && iconSize.x > 0 && iconSize.y > 0;
            icon.gameObject.SetActive(hasIcon);
            if (!hasIcon)
            {
                npcName?.SetText(name);
                return;
            }

            icon.SetSprite(sprite);
            icon.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, iconSize.x);
            icon.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, iconSize.y);
            icon.rectTransform.anchoredPosition = iconPos;
        }
        npcName?.SetText(name);
    }

    private void SetGif(AnimInfo gifInfo, Sprite icon = null)
    {
        if (gifInfo?.GifPath == null)
            return;
        
        gif.SetGifFromUrl(gifInfo.GifPath, loadingSprite: icon, speed: gifInfo.AnimSpeed, useGifSize: gifInfo.UseAnimSize);
    }

    private void SetContent(string text)
    {
        content?.SetText(text);
        content?.text.ForceMeshUpdate();
        content?.text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, content.size.y);
    }

    private void StoreDefaultLayout()
    {
        if (contentRect == null || content == null)
            return;

        contentTextRect = content.text.rectTransform;
        defaultContentAnchorMin = contentRect.anchorMin;
        defaultContentAnchorMax = contentRect.anchorMax;
        defaultContentPivot = contentRect.pivot;
        defaultContentAnchoredPosition = contentRect.anchoredPosition;
        defaultContentSizeDelta = contentRect.sizeDelta;

        defaultTextAnchorMin = contentTextRect.anchorMin;
        defaultTextAnchorMax = contentTextRect.anchorMax;
        defaultTextPivot = contentTextRect.pivot;
        defaultTextAnchoredPosition = contentTextRect.anchoredPosition;
        defaultTextSizeDelta = contentTextRect.sizeDelta;
        hasDefaultLayout = true;
    }

    private void StoreDefaultTextStyle()
    {
        if (content == null || content.text == null)
            return;

        defaultFont = content.text.font;
        defaultFontSharedMaterial = content.text.fontSharedMaterial;
        defaultFontSize = content.text.fontSize;
        defaultFontStyle = content.text.fontStyle;
        defaultTextColor = content.text.color;
        defaultOutlineColor = content.text.outlineColor;
        defaultOutlineWidth = content.text.outlineWidth;
    }

    public void SetStorySpeakerIconClickHandler(Action handler)
    {
        storySpeakerIconClickHandler = handler;
        EnsureStorySpeakerBlock();
        RefreshStorySpeakerIconButton();
    }

    public void SetStorySpeakerHint(string hint)
    {
        storySpeakerHint = hint ?? string.Empty;
        EnsureStorySpeakerBlock();
        storySpeakerHintText.text = storySpeakerHint;
        storySpeakerHintText.color = StorySpeakerHintColor;
        storySpeakerHintText.gameObject.SetActive(!string.IsNullOrWhiteSpace(storySpeakerHint)
            && storySpeakerIconClickHandler != null && storySpeakerIcon.gameObject.activeSelf);
    }

    private void ApplyStoryTextStyle(StoryTextStyleDocument style)
    {
        TMP_FontAsset font = null;
        if (!string.IsNullOrEmpty(style?.font))
            font = Resources.Load<TMP_FontAsset>(style.font);

        if (font == null)
            font = StoryTextFontProvider.GetDefaultFont();

        if (font != null)
        {
            content.text.font = font;
            content.text.fontSharedMaterial = font.material;
        }
        else
        {
            content.text.font = defaultFont;
            content.text.fontSharedMaterial = defaultFontSharedMaterial;
        }

        content.text.fontSize = style != null && style.fontSize > 0
            ? style.fontSize
            : defaultFontSize;
        content.text.fontStyle = style != null && style.bold
            ? FontStyles.Bold
            : FontStyles.Normal;
        content.text.color = ParseStoryColor(style?.textColor, Color.white);
        content.text.outlineColor = ParseStoryColor(style?.outlineColor, new Color32(0, 0, 0, 230));
        content.text.outlineWidth = style == null
            ? 0f
            : Mathf.Clamp(style.outlineWidth, 0f, 0.1f);

        if (storySpeakerText != null)
        {
            storySpeakerText.font = content.text.font;
            storySpeakerText.fontSharedMaterial = content.text.fontSharedMaterial;
            storySpeakerText.fontSize = content.text.fontSize;
            storySpeakerText.fontStyle = content.text.fontStyle;
        }

        if (storySpeakerHintText != null)
        {
            storySpeakerHintText.font = content.text.font;
            storySpeakerHintText.fontSharedMaterial = content.text.fontSharedMaterial;
        }

        DialogManager.instance?.SetStoryChoiceFont(content.text.font, content.text.fontSharedMaterial);
    }

    private static Color ParseStoryColor(string value, Color fallback)
    {
        return !string.IsNullOrEmpty(value) && ColorUtility.TryParseHtmlString(value, out Color color)
            ? color
            : fallback;
    }

    private void ResetTextStyle()
    {
        if (content == null || content.text == null)
            return;

        content.text.font = defaultFont;
        content.text.fontSharedMaterial = defaultFontSharedMaterial;
        content.text.fontSize = defaultFontSize;
        content.text.fontStyle = defaultFontStyle;
        content.text.color = defaultTextColor;
        content.text.outlineColor = defaultOutlineColor;
        content.text.outlineWidth = defaultOutlineWidth;
    }

    private void ApplyStoryLayout(DialogInfo info, bool isActiveSpeaker)
    {
        if (contentRect == null || content == null)
            return;

        EnsureStorySpeakerBlock();
        if (icon != null)
            icon.gameObject.SetActive(false);

        string speakerText = info.name ?? string.Empty;
        bool hasSpeaker = !string.IsNullOrEmpty(speakerText);
        Sprite speakerIcon = GetStorySpeakerIcon(info);
        bool hasSpeakerIcon = speakerIcon != null && speakerIcon != SpriteSet.Empty;
        bool speakerOnRight = string.Equals(info.storySpeakerSide, "right", StringComparison.OrdinalIgnoreCase);

        storySpeakerGroupRect.gameObject.SetActive(hasSpeaker);
        storySpeakerIcon.gameObject.SetActive(hasSpeakerIcon);
        if (hasSpeakerIcon)
            storySpeakerIcon.SetSprite(speakerIcon);
        else
            storySpeakerIcon.sprite = null;
        storySpeakerIcon.color = isActiveSpeaker ? Color.white : new Color32(150, 150, 150, 255);

        Sprite expression = hasSpeakerIcon ? StoryExpressionCatalog.Load(info.storyExpression) : null;
        storySpeakerExpression.gameObject.SetActive(expression != null);
        storySpeakerExpression.sprite = expression;
        RefreshStorySpeakerIconButton();

        bool showSpeakerHint = hasSpeakerIcon && storySpeakerIconClickHandler != null
            && !string.IsNullOrWhiteSpace(storySpeakerHint);
        storySpeakerHintText.gameObject.SetActive(showSpeakerHint);
        storySpeakerHintText.text = storySpeakerHint;
        storySpeakerHintText.color = StorySpeakerHintColor;

        storySpeakerText.gameObject.SetActive(hasSpeaker);
        storySpeakerText.text = speakerText;
        storySpeakerText.color = isActiveSpeaker ? new Color32(255, 230, 92, 255) : new Color32(185, 190, 196, 255);

        contentRect.anchorMin = defaultContentAnchorMin;
        contentRect.anchorMax = defaultContentAnchorMax;
        contentRect.pivot = defaultContentPivot;
        contentRect.anchoredPosition = defaultContentAnchoredPosition;
        contentRect.sizeDelta = defaultContentSizeDelta;

        Canvas.ForceUpdateCanvases();
        float barWidth = contentRect.rect.width > 0f ? contentRect.rect.width : 680f;
        float paddingX = 24f;
        float paddingY = 16f;
        float speakerWidth = hasSpeaker ? 116f : 0f;
        float speakerGap = hasSpeaker ? 18f : 0f;
        float textWidth = Mathf.Max(240f, barWidth - paddingX * 2f - speakerWidth - speakerGap);

        contentTextRect = content.text.rectTransform;
        contentTextRect.anchorMin = new Vector2(0f, 1f);
        contentTextRect.anchorMax = new Vector2(0f, 1f);
        contentTextRect.pivot = new Vector2(0f, 1f);
        float barHeight = contentRect.rect.height;
        float innerHeight = Mathf.Max(0f, barHeight - paddingY * 2f);
        contentTextRect.sizeDelta = new Vector2(textWidth, innerHeight);

        content.text.enableWordWrapping = true;
        content.text.alignment = TextAlignmentOptions.MidlineLeft;
        ApplyStoryTextStyle(info?.storyTextStyle);

        float speakerNameHeight = 26f;
        float speakerIconSize = 46f;
        float speakerIconGap = 6f;
        float speakerHeight = hasSpeaker ? (hasSpeakerIcon ? speakerIconSize + speakerIconGap + speakerNameHeight : speakerNameHeight) : 0f;
        float textX = speakerOnRight ? paddingX : paddingX + speakerWidth + speakerGap;
        contentTextRect.anchoredPosition = new Vector2(textX, -paddingY);

        storySpeakerGroupRect.anchorMin = new Vector2(0f, 1f);
        storySpeakerGroupRect.anchorMax = new Vector2(0f, 1f);
        storySpeakerGroupRect.pivot = new Vector2(0.5f, 0.5f);
        float speakerX = speakerOnRight ? barWidth - paddingX - speakerWidth * 0.5f : paddingX + speakerWidth * 0.5f;
        storySpeakerGroupRect.anchoredPosition = new Vector2(speakerX, -barHeight * 0.5f);
        storySpeakerGroupRect.sizeDelta = new Vector2(speakerWidth, innerHeight);

        storySpeakerHintRect.anchorMin = new Vector2(0f, 1f);
        storySpeakerHintRect.anchorMax = new Vector2(0f, 1f);
        storySpeakerHintRect.pivot = new Vector2(speakerOnRight ? 1f : 0f, 0f);
        storySpeakerHintRect.anchoredPosition = new Vector2(speakerOnRight ? barWidth - paddingX : paddingX, 4f);
        storySpeakerHintRect.sizeDelta = new Vector2(170f, 18f);
        storySpeakerHintText.alignment = speakerOnRight
            ? TextAlignmentOptions.BottomRight
            : TextAlignmentOptions.BottomLeft;

        storySpeakerIconRect.anchorMin = new Vector2(0.5f, 0.5f);
        storySpeakerIconRect.anchorMax = new Vector2(0.5f, 0.5f);
        storySpeakerIconRect.pivot = new Vector2(0.5f, 0.5f);
        storySpeakerIconRect.anchoredPosition = new Vector2(0f, hasSpeakerIcon ? (speakerNameHeight + speakerIconGap) * 0.5f : 0f);
        storySpeakerIconRect.sizeDelta = new Vector2(speakerIconSize, speakerIconSize);
        storySpeakerIconRect.localScale = new Vector3(info.storyFlipIcon ? -1f : 1f, 1f, 1f);

        storySpeakerExpressionRect.anchorMin = new Vector2(0.5f, 0.5f);
        storySpeakerExpressionRect.anchorMax = new Vector2(0.5f, 0.5f);
        storySpeakerExpressionRect.pivot = new Vector2(0.5f, 0.5f);
        storySpeakerExpressionRect.anchoredPosition = storySpeakerIconRect.anchoredPosition + new Vector2(16f, -14f);
        storySpeakerExpressionRect.sizeDelta = new Vector2(30f, 30f);
        storySpeakerExpressionRect.localScale = Vector3.one;
        storySpeakerExpressionRect.SetAsLastSibling();

        storySpeakerRect.anchorMin = new Vector2(0.5f, 0.5f);
        storySpeakerRect.anchorMax = new Vector2(0.5f, 0.5f);
        storySpeakerRect.pivot = new Vector2(0.5f, 0.5f);
        storySpeakerRect.anchoredPosition = new Vector2(0f, hasSpeakerIcon ? -(speakerIconSize + speakerIconGap) * 0.5f : 0f);
        storySpeakerRect.sizeDelta = new Vector2(speakerWidth, speakerNameHeight);
        PlayStoryFade(isActiveSpeaker);
    }

    private void ResetStoryLayout()
    {
        ResetTextStyle();

        if (storySpeakerText != null)
            storySpeakerText.gameObject.SetActive(false);

        if (storySpeakerGroupRect != null)
            storySpeakerGroupRect.gameObject.SetActive(false);

        if (storySpeakerIcon != null)
            storySpeakerIcon.gameObject.SetActive(false);

        if (storySpeakerExpression != null)
            storySpeakerExpression.gameObject.SetActive(false);

        if (storySpeakerHintText != null)
            storySpeakerHintText.gameObject.SetActive(false);

        if (storyFadeCoroutine != null)
        {
            StopCoroutine(storyFadeCoroutine);
            storyFadeCoroutine = null;
        }

        if (storyContentCanvasGroup != null)
            storyContentCanvasGroup.alpha = 1f;

        if (storyIconCanvasGroup != null)
            storyIconCanvasGroup.alpha = 1f;

        if (!hasDefaultLayout || contentRect == null || content == null)
            return;

        contentTextRect = content.text.rectTransform;
        contentRect.anchorMin = defaultContentAnchorMin;
        contentRect.anchorMax = defaultContentAnchorMax;
        contentRect.pivot = defaultContentPivot;
        contentRect.anchoredPosition = defaultContentAnchoredPosition;
        contentRect.sizeDelta = defaultContentSizeDelta;

        contentTextRect.anchorMin = defaultTextAnchorMin;
        contentTextRect.anchorMax = defaultTextAnchorMax;
        contentTextRect.pivot = defaultTextPivot;
        contentTextRect.anchoredPosition = defaultTextAnchoredPosition;
        contentTextRect.sizeDelta = defaultTextSizeDelta;
    }

    private void EnsureStorySpeakerBlock()
    {
        if (storySpeakerText != null)
            return;

        GameObject groupObj = new GameObject("Story Speaker Group", typeof(RectTransform));
        groupObj.transform.SetParent(contentRect, false);
        storySpeakerGroupRect = groupObj.GetComponent<RectTransform>();

        GameObject iconObj = new GameObject("Story Speaker Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObj.transform.SetParent(storySpeakerGroupRect, false);
        storySpeakerIconRect = iconObj.GetComponent<RectTransform>();
        storySpeakerIcon = iconObj.GetComponent<Image>();
        storySpeakerIcon.preserveAspect = true;
        storySpeakerIcon.raycastTarget = false;

        Button iconButton = iconObj.AddComponent<Button>();
        iconButton.transition = Selectable.Transition.None;
        iconButton.targetGraphic = storySpeakerIcon;
        iconButton.onClick.AddListener(() => storySpeakerIconClickHandler?.Invoke());

        EventTrigger iconTrigger = iconObj.AddComponent<EventTrigger>();
        EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener(_ => SetStorySpeakerHintHover(true));
        iconTrigger.triggers.Add(enterEntry);
        EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => SetStorySpeakerHintHover(false));
        iconTrigger.triggers.Add(exitEntry);

        GameObject expressionObj = new GameObject("Story Speaker Expression", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        expressionObj.transform.SetParent(storySpeakerGroupRect, false);
        storySpeakerExpressionRect = expressionObj.GetComponent<RectTransform>();
        storySpeakerExpression = expressionObj.GetComponent<Image>();
        storySpeakerExpression.preserveAspect = true;
        storySpeakerExpression.raycastTarget = false;
        storySpeakerExpression.gameObject.SetActive(false);

        GameObject textObj = new GameObject("Story Speaker Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(storySpeakerGroupRect, false);
        storySpeakerRect = textObj.GetComponent<RectTransform>();
        storySpeakerText = textObj.GetComponent<TextMeshProUGUI>();
        storySpeakerText.font = content.text.font;
        storySpeakerText.fontSize = content.text.fontSize;
        storySpeakerText.alignment = TextAlignmentOptions.Center;
        storySpeakerText.raycastTarget = false;
        storySpeakerText.enableWordWrapping = true;
        storySpeakerText.richText = true;

        GameObject hintObj = new GameObject("Story Speaker Editor Hint", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        hintObj.transform.SetParent(contentRect, false);
        storySpeakerHintRect = hintObj.GetComponent<RectTransform>();
        storySpeakerHintText = hintObj.GetComponent<TextMeshProUGUI>();
        storySpeakerHintText.font = content.text.font;
        storySpeakerHintText.fontSharedMaterial = content.text.fontSharedMaterial;
        storySpeakerHintText.fontSize = 11f;
        storySpeakerHintText.fontStyle = FontStyles.Normal;
        storySpeakerHintText.color = StorySpeakerHintColor;
        storySpeakerHintText.raycastTarget = false;
        storySpeakerHintText.enableWordWrapping = false;
        storySpeakerHintText.richText = false;
        storySpeakerHintText.gameObject.SetActive(false);
        RefreshStorySpeakerIconButton();
    }

    private void SetStorySpeakerHintHover(bool isHovering)
    {
        if (storySpeakerHintText != null && storySpeakerHintText.gameObject.activeSelf)
            storySpeakerHintText.color = isHovering ? StorySpeakerHintHoverColor : StorySpeakerHintColor;
    }

    private void RefreshStorySpeakerIconButton()
    {
        if (storySpeakerIcon == null)
            return;

        Button button = storySpeakerIcon.GetComponent<Button>();
        bool canClick = storySpeakerIconClickHandler != null && storySpeakerIcon.gameObject.activeSelf;
        storySpeakerIcon.raycastTarget = canClick;
        if (button != null)
            button.interactable = canClick;
    }

    private Sprite GetStorySpeakerIcon(DialogInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.iconId))
            return null;

        if (TryGetPetIdFromIconId(info.iconId, out int petId))
        {
            Sprite petIcon = PetUISystem.GetPetIcon(petId);
            if (petIcon != null && petIcon != SpriteSet.Empty)
                return petIcon;
        }

        return info.icon;
    }

    private bool TryGetPetIdFromIconId(string iconId, out int petId)
    {
        petId = 0;
        string normalized = iconId.Replace("\\", "/");
        bool isPetPath = normalized.StartsWith("pet:", StringComparison.OrdinalIgnoreCase) ||
            normalized.IndexOf("Pets/pet/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("Pets/icon/", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!isPetPath)
            return false;

        int slashIndex = normalized.LastIndexOf('/');
        string idText = slashIndex >= 0 ? normalized.Substring(slashIndex + 1) : normalized.Substring(normalized.IndexOf(':') + 1);
        idText = idText.Replace(".png", string.Empty);
        return int.TryParse(idText, out petId);
    }

    private void PlayStoryFade(bool includeIcon)
    {
        storyContentCanvasGroup = GetOrAddCanvasGroup(contentRect.gameObject);
        storyIconCanvasGroup = icon == null ? null : GetOrAddCanvasGroup(icon.gameObject);

        if (storyFadeCoroutine != null)
            StopCoroutine(storyFadeCoroutine);

        storyFadeCoroutine = StartCoroutine(StoryFadeCoroutine(storyContentCanvasGroup, storyIconCanvasGroup, includeIcon && icon != null && icon.gameObject.activeSelf));
    }

    private IEnumerator StoryFadeCoroutine(CanvasGroup textGroup, CanvasGroup iconGroup, bool includeIcon)
    {
        const float duration = 0.16f;
        float time = 0f;
        textGroup.alpha = 0f;
        if (includeIcon && iconGroup != null)
            iconGroup.alpha = 0f;

        while (time < duration)
        {
            float alpha = Mathf.Clamp01(time / duration);
            textGroup.alpha = alpha;
            if (includeIcon && iconGroup != null)
                iconGroup.alpha = alpha;

            time += Time.unscaledDeltaTime;
            yield return null;
        }

        textGroup.alpha = 1f;
        if (iconGroup != null)
            iconGroup.alpha = 1f;
        storyFadeCoroutine = null;
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject obj)
    {
        CanvasGroup canvasGroup = obj.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = obj.AddComponent<CanvasGroup>();

        return canvasGroup;
    }

    private void SetFunction(List<NpcButtonHandler> functionHandler)
    {
        functionRect.DestoryChildren();

        float functionY = Mathf.Min(-55, contentRect.anchoredPosition.y - content.size.y - 5);
        functionRect.anchoredPosition = new Vector2(functionRect.anchoredPosition.x, functionY);

        if (functionHandler == null || functionHandler.Count == 0)
            return;

        float handlerX = 0;
        IText lastText = null;

        for (int i = 0; i < functionHandler.Count; i++)
        {
            NpcButtonHandler handler = functionHandler[i];
            if (handler.typeId == "branch")
            {
                lastText?.onPointerClickEvent.AddListener(x =>
                {
                    NpcController npc = DialogManager.instance.currentNpc;
                    Dictionary<int, NpcController> npcList = (Dictionary<int, NpcController>)Player.GetSceneData("mapNpcList");
                    NpcHandler.GetNpcEntity(npc, handler, npcList)?.Invoke();
                });
                continue;
            }

            GameObject obj = Instantiate(functionTextPrefab, functionRect);
            RectTransform rect = obj.GetComponent<RectTransform>();
            IText text = obj.GetComponent<IText>();

            text.SetText("<sprite name=\"settings\"> <u>" + handler.description + "</u>");
            text.onPointerEnterEvent.AddListener(x => text.SetColor(hoverTextColor));
            text.onPointerExitEvent.AddListener(x => text.SetColor(initTextColor));
            text.onPointerClickEvent.AddListener(x =>
            {
                NpcController npc = DialogManager.instance.currentNpc;
                Dictionary<int, NpcController> npcList = (Dictionary<int, NpcController>)Player.GetSceneData("mapNpcList");
                NpcHandler.GetNpcEntity(npc, handler, npcList)?.Invoke();
            });

            rect.anchoredPosition = new Vector2(handlerX, 0);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, text.size.x);

            handlerX += (text.size.x + 10);
            lastText = text;
        }
    }

    private void SetReply(List<NpcButtonHandler> replyHandler)
    {
        replyRect.DestoryChildren();

        if (replyHandler == null || replyHandler.Count == 0)
            return;

        float handlerX = 0;
        int replyCount = replyHandler.Count(x => x.typeId != "branch");
        if (replyCount == 0)
            return;

        float handlerSize = replyRect.rect.size.x / replyCount;
        IText lastText = null;

        for (int i = 0; i < replyHandler.Count; i++)
        {
            NpcButtonHandler handler = replyHandler[i];
            if (handler.typeId == "branch")
            {
                lastText?.onPointerClickEvent.AddListener(x =>
                {
                    NpcController npc = DialogManager.instance.currentNpc;
                    Dictionary<int, NpcController> npcList = (Dictionary<int, NpcController>)Player.GetSceneData("mapNpcList");
                    NpcHandler.GetNpcEntity(npc, handler, npcList)?.Invoke();
                });
                continue;
            }

            GameObject obj = Instantiate(replyTextPrefab, replyRect);
            RectTransform rect = obj.GetComponent<RectTransform>();
            IText text = obj.GetComponent<IText>();

            text.SetText("<u>" + handler.description + "</u>");
            text.onPointerEnterEvent.AddListener(x => text.SetColor(hoverTextColor));
            text.onPointerExitEvent.AddListener(x => text.SetColor(initTextColor));
            text.onPointerClickEvent.AddListener(x =>
            {
                if (replyClickHandler != null)
                {
                    replyClickHandler.Invoke(handler);
                    return;
                }

                NpcController npc = DialogManager.instance.currentNpc;
                Dictionary<int, NpcController> npcList = (Dictionary<int, NpcController>)Player.GetSceneData("mapNpcList");
                NpcHandler.GetNpcEntity(npc, handler, npcList)?.Invoke();
            });

            rect.anchoredPosition = new Vector2(handlerX, 0);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(handlerSize, text.size.x));
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(replyRect.rect.size.y, text.size.y));

            handlerX -= (text.size.x + 10);
            lastText = text;
        }
    }

}

/// <summary>
/// Creates the story default TMP font from the same dynamic Zongyi font used by the project's UI.
/// The checked-in Zongyi SDF atlas is a fixed 17-point atlas and becomes visibly soft at story body sizes.
/// </summary>
public static class StoryTextFontProvider
{
    private const string LegacyZongyiFontPath = "Fonts/Zongyi";
    private const string FallbackFontPath = "Fonts/MSJH SDF";
    private static TMP_FontAsset defaultFont;
    private static bool hasAttemptedCreation;

    public static TMP_FontAsset GetDefaultFont()
    {
        if (defaultFont != null)
            return defaultFont;
        if (hasAttemptedCreation)
            return Resources.Load<TMP_FontAsset>(FallbackFontPath);

        hasAttemptedCreation = true;
        Font sourceFont = Resources.Load<Font>(LegacyZongyiFontPath);
        if (sourceFont != null)
        {
            defaultFont = TMP_FontAsset.CreateFontAsset(sourceFont, 90, 8, GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (defaultFont != null)
            {
                defaultFont.name = "Zongyi Story Runtime SDF";
                if (defaultFont.material != null)
                    defaultFont.material.name = "Zongyi Story Runtime Material";
            }
        }

        return defaultFont ?? Resources.Load<TMP_FontAsset>(FallbackFontPath);
    }
}
