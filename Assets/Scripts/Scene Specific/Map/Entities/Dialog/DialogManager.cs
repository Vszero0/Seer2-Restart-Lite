using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    private Sprite storyDialogDefaultBackgroundSprite;
    private Color storyDialogDefaultBackgroundColor;
    private RectTransform storyChoiceRoot;
    private TMP_Text storyDialogTextSample;

    protected override void Awake()
    {
        base.Awake();
        dialogLayerPosition = dialogLayer.anchoredPosition;
        dialogStoryLayerPosition = dialogStoryLayer.anchoredPosition;
        storyDialogBackground = FindStoryDialogBackground();
        storyDialogTextSample = FindStoryDialogTextSample();
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

    private void SetStoryDialogLayerActive(bool acitve) {
        bool wasActive = dialogStoryLayer.gameObject.activeSelf;
        UILayer.gameObject.SetActive(!acitve);
        dialogStoryLayer.gameObject.SetActive(acitve);
        if (acitve && !wasActive)
            PlayOpenAnimation(dialogStoryLayer, dialogStoryLayerPosition, ref dialogStoryOpenAnimationCoroutine);
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
    
    public void OpenStoryDialog(DialogInfo info) {
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

        SetStoryDialogLayerActive(true);
        dialogStoryController.OpenDialog(info);
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

    public void ResetStoryDialogBackground() {
        if (storyDialogBackground == null)
            return;

        storyDialogBackground.sprite = storyDialogDefaultBackgroundSprite;
        storyDialogBackground.color = storyDialogDefaultBackgroundColor;
    }

    public void ShowStoryChoices(IReadOnlyList<string> choices, Action<int> onChoiceSelected) {
        ClearStoryChoices();
        if (choices == null || choices.Count == 0)
            return;

        storyChoiceRoot = EnsureStoryChoiceRoot();
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
    }

    public void ClearStoryChoices() {
        if (storyChoiceRoot == null)
            return;

        storyChoiceRoot.DestoryChildren();
        storyChoiceRoot.gameObject.SetActive(false);
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

    private void PlayOpenAnimation(RectTransform layer, Vector2 targetPosition, ref Coroutine coroutine)
    {
        StopOpenAnimation(layer, targetPosition, ref coroutine);

        if (!useOpenAnimation || openAnimationDuration <= 0f)
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

        RectTransform textBar = FindChildRect(dialogStoryLayer, "Text Bar");
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

    private Button CreateStoryChoiceButton(RectTransform parent, string label, Action onClick)
    {
        GameObject obj = new GameObject("Choice", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);

        Image image = obj.GetComponent<Image>();
        image.color = new Color32(0, 18, 24, 218);
        image.raycastTarget = true;

        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color32(44, 227, 255, 190);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Button button = obj.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(20, 84, 96, 255);
        colors.pressedColor = new Color32(8, 42, 52, 255);
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
        if (storyDialogTextSample != null && storyDialogTextSample.font != null)
        {
            text.font = storyDialogTextSample.font;
            text.fontSharedMaterial = storyDialogTextSample.fontSharedMaterial;
        }
        text.text = label;
        text.fontSize = 18f;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = new Color32(255, 238, 92, 255);
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return button;
    }

}
