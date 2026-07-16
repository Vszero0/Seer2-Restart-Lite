using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class DialogManager : Manager<DialogManager>
{
    [SerializeField] private RectTransform UILayer;
    [SerializeField] private RectTransform dialogLayer;
    [SerializeField] private DialogController dialogController;
    [SerializeField] private RectTransform dialogStoryLayer;
    [SerializeField] private DialogController dialogStoryController;
    [Header("Open Animation")]
    [SerializeField] private bool useOpenAnimation = true;
    [SerializeField, Min(0f)] private float openAnimationDuration = 0.18f;
    [SerializeField] private float openAnimationRiseDistance = 12f;
    [SerializeField] private AnimationCurve openAnimationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public NpcController currentNpc { get; private set; }
    private Vector2 dialogLayerPosition;
    private Vector2 dialogStoryLayerPosition;
    private Coroutine dialogOpenAnimationCoroutine;
    private Coroutine dialogStoryOpenAnimationCoroutine;
    private Image storyDialogBackground;
    private Image storyTransitionBackground;
    private Sprite storyDialogDefaultBackgroundSprite;
    private Color storyDialogDefaultBackgroundColor;
    private RectTransform storyActorLayer;
    private RectTransform storyTextBar;
    private RectTransform storyChoiceRoot;
    private TMP_Text storyDialogTextSample;
    private TMP_FontAsset storyChoiceFont;
    private Material storyChoiceFontMaterial;

    protected override void Awake()
    {
        base.Awake();
        dialogLayerPosition = dialogLayer.anchoredPosition;
        dialogStoryLayerPosition = dialogStoryLayer.anchoredPosition;
        storyDialogBackground = FindStoryDialogBackground();
        storyTextBar = FindChildRect(dialogStoryLayer, "Text Bar");
        storyDialogTextSample = FindStoryDialogTextSample();
        storyChoiceFont = storyDialogTextSample?.font;
        storyChoiceFontMaterial = storyDialogTextSample?.fontSharedMaterial;
        EnsureStoryDialogTextBackground();
        if (storyDialogBackground != null)
        {
            storyDialogDefaultBackgroundSprite = storyDialogBackground.sprite;
            storyDialogDefaultBackgroundColor = storyDialogBackground.color;
        }
        ResetDialogLayerVisual(dialogLayer, dialogLayerPosition);
        ResetDialogLayerVisual(dialogStoryLayer, dialogStoryLayerPosition);
    }

    private void SetDialogLayerActive(bool acitve) {
        bool wasActive = dialogLayer.gameObject.activeSelf;
        UILayer.gameObject.SetActive(!acitve);
        dialogLayer.gameObject.SetActive(acitve);
        if (acitve && !wasActive)
            PlayOpenAnimation(dialogLayer, dialogLayerPosition, ref dialogOpenAnimationCoroutine);
        else if (!acitve)
            StopOpenAnimation(dialogLayer, dialogLayerPosition, ref dialogOpenAnimationCoroutine);
    }

    private void SetStoryDialogLayerActive(bool acitve, bool playOpenAnimation = true) {
        bool wasActive = dialogStoryLayer.gameObject.activeSelf;
        UILayer.gameObject.SetActive(!acitve);
        dialogStoryLayer.gameObject.SetActive(acitve);
        if (acitve && !wasActive)
            PlayOpenAnimation(dialogStoryLayer, dialogStoryLayerPosition, ref dialogStoryOpenAnimationCoroutine, playOpenAnimation);
        else if (!acitve)
            StopOpenAnimation(dialogStoryLayer, dialogStoryLayerPosition, ref dialogStoryOpenAnimationCoroutine);
    }
    public void SetCurrentNpc(NpcInfo info) {
        currentNpc = ((Dictionary<int, NpcController>)Player.GetSceneData("mapNpcList"))?.Get(info.id);
        Player.instance.currentNpcId = info.id;
    }

    public void OpenDialog(DialogInfo info) {
        Player.instance.isShootMode = false;
        dialogLayer.SetAsLastSibling();
        
        if (info == null) {
            CloseDialog();
            return;
        }

        if (dialogStoryLayer.gameObject.activeSelf)
        {
            SetStoryDialogLayerActive(false);
        }

        SetDialogLayerActive(true);
        dialogController.OpenDialog(info);
    }
    
    public void OpenStoryDialog(DialogInfo info, bool playOpenAnimation = true) {
        Player.instance.isShootMode = false;
        dialogStoryLayer.SetAsLastSibling();

        if (info == null) {
            CloseDialog();
            return;
        }
        if (dialogLayer.gameObject.activeSelf)
        {
            SetDialogLayerActive(false);
        }

        SetStoryDialogLayerActive(true, playOpenAnimation);
        SetStoryContentVisible(true);
        dialogStoryController.OpenDialog(info);
        RefreshStoryOverlayLayering();
    }

    public void SetStoryDialogBackgroundClickHandler(Action handler) {
        dialogStoryController.SetBackgroundClickHandler(handler);
    }

    public void SetStoryDialogReplyClickHandler(Action<NpcButtonHandler> handler) {
        dialogStoryController.SetReplyClickHandler(handler);
    }

    public void SetStoryDialogBackground(Sprite sprite, Color color) {
        if (storyDialogBackground == null)
            return;

        storyDialogBackground.sprite = sprite;
        storyDialogBackground.color = color;
    }

    public Image GetStoryDialogBackgroundImage()
    {
        return storyDialogBackground;
    }

    public Image GetStoryTransitionBackgroundImage()
    {
        if (storyTransitionBackground != null)
            return storyTransitionBackground;
        if (dialogStoryLayer == null || storyDialogBackground == null)
            return null;

        GameObject obj = new GameObject("Story Transition Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(dialogStoryLayer, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        storyTransitionBackground = obj.GetComponent<Image>();
        storyTransitionBackground.color = Color.clear;
        storyTransitionBackground.raycastTarget = false;
        storyTransitionBackground.gameObject.SetActive(false);
        RefreshStoryOverlayLayering();
        return storyTransitionBackground;
    }

    public void SetStoryContentVisible(bool visible)
    {
        if (storyTextBar != null)
            storyTextBar.gameObject.SetActive(visible);
        if (!visible)
            ClearStoryChoices();
    }

    public void ResetStoryDialogBackground() {
        if (storyDialogBackground == null)
            return;

        storyDialogBackground.sprite = storyDialogDefaultBackgroundSprite;
        storyDialogBackground.color = storyDialogDefaultBackgroundColor;
    }

    public RectTransform GetStoryActorLayer() {
        if (storyActorLayer == null)
            storyActorLayer = EnsureStoryActorLayer();

        PlaceStoryActorLayer();
        return storyActorLayer;
    }

    public void ShowStoryChoices(IReadOnlyList<string> choices, Action<int> onChoiceSelected, string speakerSide = "left") {
        ClearStoryChoices();
        if (choices == null || choices.Count == 0)
            return;

        storyChoiceRoot = EnsureStoryChoiceRoot();
        PlaceStoryChoiceRoot(string.Equals(speakerSide, "right", StringComparison.OrdinalIgnoreCase));
        storyChoiceRoot.gameObject.SetActive(true);

        float rootWidth = 380f;
        List<Button> buttons = new List<Button>();
        List<float> buttonHeights = new List<float>();
        float y = 0f;
        for (int i = 0; i < choices.Count; i++)
        {
            int choiceIndex = i;
            Button button = CreateStoryChoiceButton(storyChoiceRoot, choices[i], () => onChoiceSelected?.Invoke(choiceIndex));
            TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();

            text.rectTransform.sizeDelta = new Vector2(rootWidth - 38f, 1000f);
            text.ForceMeshUpdate();
            float buttonHeight = Mathf.Max(42f, text.preferredHeight + 20f);

            buttons.Add(button);
            buttonHeights.Add(buttonHeight);
            y += buttonHeight + 8f;
        }

        storyChoiceRoot.sizeDelta = new Vector2(rootWidth, y);

        y = 0f;
        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
            float buttonHeight = buttonHeights[i];

            buttonRect.anchoredPosition = new Vector2(0f, -y);
            buttonRect.sizeDelta = new Vector2(rootWidth, buttonHeight);
            text.rectTransform.anchoredPosition = new Vector2(18f, 0f);
            text.rectTransform.sizeDelta = new Vector2(rootWidth - 38f, buttonHeight - 12f);
            y += buttonHeight + 8f;
        }

        RefreshStoryOverlayLayering();
    }

    public void SetStoryChoiceFont(TMP_FontAsset font, Material material)
    {
        if (font == null)
            return;

        storyChoiceFont = font;
        storyChoiceFontMaterial = material ?? font.material;
        if (storyChoiceRoot == null)
            return;

        foreach (TextMeshProUGUI text in storyChoiceRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.font = storyChoiceFont;
            text.fontSharedMaterial = storyChoiceFontMaterial;
        }
    }

    public void ClearStoryChoices() {
        if (storyChoiceRoot == null)
            return;

        storyChoiceRoot.DestoryChildren();
        storyChoiceRoot.gameObject.SetActive(false);
        RefreshStoryOverlayLayering();
    }

    public void RefreshStoryOverlayLayering()
    {
        if (dialogStoryLayer == null)
            return;

        if (storyDialogBackground != null)
            storyDialogBackground.transform.SetAsFirstSibling();

        if (storyTransitionBackground != null)
            storyTransitionBackground.transform.SetSiblingIndex(Mathf.Min(1, dialogStoryLayer.childCount - 1));

        if (storyActorLayer != null)
            storyActorLayer.SetSiblingIndex(GetStoryOverlayInsertIndex());

        if (storyTextBar != null)
            storyTextBar.SetAsLastSibling();

        if (storyChoiceRoot != null && storyChoiceRoot.gameObject.activeSelf)
            storyChoiceRoot.SetAsLastSibling();
    }
  
    public void CloseDialog() {
        dialogController.SetBackgroundClickHandler(null);
        dialogController.SetReplyClickHandler(null);
        dialogStoryController.SetBackgroundClickHandler(null);
        dialogStoryController.SetReplyClickHandler(null);
        ClearStoryChoices();
        ResetStoryDialogBackground();
        SetDialogLayerActive(false);
        SetStoryDialogLayerActive(false);
        Player.instance.currentNpcId = 0;
    }

    private void PlayOpenAnimation(RectTransform layer, Vector2 targetPosition, ref Coroutine coroutine,
        bool playOpenAnimation = true)
    {
        StopOpenAnimation(layer, targetPosition, ref coroutine);

        if (!playOpenAnimation || !useOpenAnimation || openAnimationDuration <= 0f)
        {
            ResetDialogLayerVisual(layer, targetPosition);
            return;
        }

        coroutine = StartCoroutine(OpenAnimationCoroutine(layer, targetPosition));
    }

    private void StopOpenAnimation(RectTransform layer, Vector2 targetPosition, ref Coroutine coroutine)
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
            coroutine = null;
        }
        ResetDialogLayerVisual(layer, targetPosition);
    }

    private IEnumerator OpenAnimationCoroutine(RectTransform layer, Vector2 targetPosition)
    {
        CanvasGroup canvasGroup = GetCanvasGroup(layer);
        Vector2 startPosition = targetPosition + Vector2.down * openAnimationRiseDistance;
        float time = 0f;

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;
        layer.anchoredPosition = startPosition;

        while (time < openAnimationDuration)
        {
            float progress = Mathf.Clamp01(time / openAnimationDuration);
            float curvedProgress = openAnimationCurve.Evaluate(progress);
            canvasGroup.alpha = curvedProgress;
            layer.anchoredPosition = Vector2.LerpUnclamped(startPosition, targetPosition, curvedProgress);
            time += Time.unscaledDeltaTime;
            yield return null;
        }

        ResetDialogLayerVisual(layer, targetPosition);
    }

    private void ResetDialogLayerVisual(RectTransform layer, Vector2 targetPosition)
    {
        if (layer == null)
            return;

        CanvasGroup canvasGroup = GetCanvasGroup(layer);
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        layer.anchoredPosition = targetPosition;
    }

    private CanvasGroup GetCanvasGroup(RectTransform layer)
    {
        CanvasGroup canvasGroup = layer.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = layer.gameObject.AddComponent<CanvasGroup>();

        return canvasGroup;
    }

    private Image FindStoryDialogBackground()
    {
        if (dialogStoryLayer == null)
            return null;

        Image[] images = dialogStoryLayer.GetComponentsInChildren<Image>(true);
        foreach (var image in images)
        {
            RectTransform rect = image.rectTransform;
            if (image.gameObject.name == "Background" &&
                Vector2.Distance(rect.anchorMin, Vector2.zero) < 0.001f &&
                Vector2.Distance(rect.anchorMax, Vector2.one) < 0.001f)
            {
                return image;
            }
        }

        return null;
    }

    private Image EnsureStoryDialogTextBackground()
    {
        if (dialogStoryLayer == null)
            return null;

        RectTransform textBar = storyTextBar != null ? storyTextBar : FindChildRect(dialogStoryLayer, "Text Bar");
        if (textBar == null)
            return null;

        Image image = textBar.GetComponent<Image>();
        if (image == null)
            image = textBar.gameObject.AddComponent<Image>();

        image.color = new Color(0f, 0f, 0f, 0.62f);
        image.raycastTarget = false;
        return image;
    }

    private RectTransform FindChildRect(Transform root, string childName)
    {
        foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rect.gameObject.name == childName)
                return rect;
        }

        return null;
    }

    private TMP_Text FindStoryDialogTextSample()
    {
        if (dialogStoryLayer == null)
            return null;

        TMP_Text fallback = null;
        foreach (TMP_Text text in dialogStoryLayer.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == null)
                continue;

            if (fallback == null)
                fallback = text;
            string objectName = text.gameObject.name;
            if (string.Equals(objectName, "Dialog", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "Content", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "Text", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return fallback;
    }

    private RectTransform EnsureStoryChoiceRoot()
    {
        if (storyChoiceRoot != null)
            return storyChoiceRoot;

        GameObject obj = new GameObject("Story Choice Root", typeof(RectTransform));
        obj.transform.SetParent(dialogStoryLayer, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-90f, 160f);
        rect.sizeDelta = new Vector2(380f, 0f);
        obj.SetActive(false);
        return rect;
    }

    private void PlaceStoryChoiceRoot(bool placeLeft)
    {
        if (storyChoiceRoot == null)
            return;

        float xAnchor = placeLeft ? 0f : 1f;
        storyChoiceRoot.anchorMin = new Vector2(xAnchor, 0f);
        storyChoiceRoot.anchorMax = new Vector2(xAnchor, 0f);
        storyChoiceRoot.pivot = new Vector2(xAnchor, 0f);
        storyChoiceRoot.anchoredPosition = new Vector2(placeLeft ? 90f : -90f, GetStoryChoiceBottomOffset());
    }

    private float GetStoryChoiceBottomOffset()
    {
        const float margin = 16f;
        const float fallbackOffset = 160f;
        if (storyTextBar == null || dialogStoryLayer == null)
            return fallbackOffset;

        Vector3[] corners = new Vector3[4];
        storyTextBar.GetWorldCorners(corners);
        float textBarTop = dialogStoryLayer.InverseTransformPoint(corners[1]).y;
        return Mathf.Max(fallbackOffset, textBarTop - dialogStoryLayer.rect.yMin + margin);
    }

    private RectTransform EnsureStoryActorLayer()
    {
        if (dialogStoryLayer == null)
            return null;

        GameObject obj = new GameObject("Story Actor Layer", typeof(RectTransform));
        obj.transform.SetParent(dialogStoryLayer, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.gameObject.SetActive(true);
        return rect;
    }

    private void PlaceStoryActorLayer()
    {
        if (storyActorLayer == null)
            return;

        storyActorLayer.SetSiblingIndex(GetStoryOverlayInsertIndex());
    }

    private int GetStoryOverlayInsertIndex()
    {
        if (storyDialogBackground == null)
            return 0;

        int backgroundIndex = storyTransitionBackground != null
            ? storyTransitionBackground.transform.GetSiblingIndex()
            : storyDialogBackground.transform.GetSiblingIndex();
        return Mathf.Min(backgroundIndex + 1, dialogStoryLayer.childCount - 1);
    }

    private Button CreateStoryChoiceButton(RectTransform parent, string label, Action onClick)
    {
        GameObject obj = new GameObject("Choice", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);

        Image image = obj.GetComponent<Image>();
        Color32 normalBackground = new Color32(0, 18, 24, 176);
        Color32 hoverBackground = new Color32(5, 84, 98, 236);
        Color32 pressedBackground = new Color32(9, 50, 62, 245);
        image.color = normalBackground;
        image.raycastTarget = true;

        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color32(44, 227, 255, 96);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Button button = obj.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = normalBackground;
        colors.highlightedColor = hoverBackground;
        colors.selectedColor = normalBackground;
        colors.pressedColor = pressedBackground;
        colors.disabledColor = new Color32(0, 18, 24, 90);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(() => onClick?.Invoke());

        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(obj.transform, false);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0.5f);
        textRect.anchorMax = new Vector2(0f, 0.5f);
        textRect.pivot = new Vector2(0f, 0.5f);
        textRect.anchoredPosition = new Vector2(18f, 0f);

        TextMeshProUGUI text = textObj.GetComponent<TextMeshProUGUI>();
        if (storyChoiceFont != null)
        {
            text.font = storyChoiceFont;
            text.fontSharedMaterial = storyChoiceFontMaterial ?? storyChoiceFont.material;
        }
        else if (storyDialogTextSample != null && storyDialogTextSample.font != null)
        {
            text.font = storyDialogTextSample.font;
            text.fontSharedMaterial = storyDialogTextSample.fontSharedMaterial;
        }
        text.text = label;
        text.fontSize = 18f;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = new Color32(178, 184, 172, 235);
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        StoryChoiceHover hover = obj.AddComponent<StoryChoiceHover>();
        hover.text = text;
        hover.outline = outline;
        return button;
    }

    private class StoryChoiceHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public TextMeshProUGUI text;
        public Outline outline;

        private static readonly Color32 NormalTextColor = new Color32(178, 184, 172, 235);
        private static readonly Color32 HoverTextColor = new Color32(255, 238, 92, 255);
        private static readonly Color32 NormalOutlineColor = new Color32(44, 227, 255, 96);
        private static readonly Color32 HoverOutlineColor = new Color32(44, 227, 255, 220);

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (text != null)
                text.color = HoverTextColor;
            if (outline != null)
                outline.effectColor = HoverOutlineColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (text != null)
                text.color = NormalTextColor;
            if (outline != null)
                outline.effectColor = NormalOutlineColor;
        }
    }

}
