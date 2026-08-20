using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders scene-local visual props. Props are presentation-only and never
/// grant, consume, or otherwise mutate inventory items.
/// </summary>
public sealed class StoryPropStage
{
    private const float DefaultHeight = 110f;
    private readonly RectTransform backLayer;
    private readonly RectTransform frontLayer;
    private readonly MonoBehaviour coroutineHost;
    private readonly Func<string, string> getResourceSource;
    private readonly bool editable;
    private readonly Action<string> onSelected;
    private readonly Action<string, Vector2> onPositionChanged;
    private readonly Dictionary<string, RuntimeProp> props = new Dictionary<string, RuntimeProp>(StringComparer.OrdinalIgnoreCase);

    public StoryPropStage(RectTransform backLayer, RectTransform frontLayer, MonoBehaviour coroutineHost,
        Func<string, string> getResourceSource, bool editable = false, Action<string> onSelected = null,
        Action<string, Vector2> onPositionChanged = null)
    {
        this.backLayer = backLayer;
        this.frontLayer = frontLayer;
        this.coroutineHost = coroutineHost;
        this.getResourceSource = getResourceSource;
        this.editable = editable;
        this.onSelected = onSelected;
        this.onPositionChanged = onPositionChanged;
    }

    public void Show(StoryScenePropDocument document, bool animate = true)
    {
        if (document == null || string.IsNullOrWhiteSpace(document.id))
            return;

        if (props.TryGetValue(document.id, out RuntimeProp existing))
        {
            StopAnimation(existing);
            existing.document = document;
            ApplyLayout(existing);
            existing.canvasGroup.alpha = 1f;
            existing.image.rectTransform.localScale = Vector3.one;
            return;
        }

        Sprite sprite = StorySpriteResolver.Load(document.sprite, getResourceSource?.Invoke(document.sprite));
        if (sprite == null)
            return;

        RectTransform layer = document.normalizedLayer == "back" ? backLayer : frontLayer;
        if (layer == null)
            return;

        GameObject obj = new GameObject("Story Prop " + document.id, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup), typeof(Outline));
        obj.transform.SetParent(layer, false);
        Image image = obj.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = editable;
        Outline outline = obj.GetComponent<Outline>();
        outline.effectColor = new Color32(255, 232, 71, 255);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.enabled = false;

        RuntimeProp runtime = new RuntimeProp
        {
            document = document,
            image = image,
            canvasGroup = obj.GetComponent<CanvasGroup>(),
            outline = outline,
        };
        props[document.id] = runtime;
        ApplyLayout(runtime);

        if (editable)
        {
            StoryPropDragHandler drag = obj.AddComponent<StoryPropDragHandler>();
            drag.Configure(layer, document.id, onSelected, position =>
            {
                document.x = position.x;
                document.y = position.y;
                ApplyLayout(runtime);
                onPositionChanged?.Invoke(document.id, position);
            });
        }

        if (animate && coroutineHost != null)
        {
            runtime.canvasGroup.alpha = 0f;
            runtime.image.rectTransform.localScale = Vector3.one * .88f;
            runtime.animation = coroutineHost.StartCoroutine(FadeIn(runtime));
        }
    }

    public void Hide(string propId, bool animate = true)
    {
        if (string.IsNullOrWhiteSpace(propId) || !props.TryGetValue(propId, out RuntimeProp runtime))
            return;

        StopAnimation(runtime);
        if (animate && coroutineHost != null)
            runtime.animation = coroutineHost.StartCoroutine(FadeOut(runtime));
        else
            DestroyRuntime(propId, runtime);
    }

    public void SetSelected(string propId)
    {
        foreach (KeyValuePair<string, RuntimeProp> entry in props)
        {
            if (entry.Value?.outline != null)
                entry.Value.outline.enabled = editable
                    && string.Equals(entry.Key, propId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Clear()
    {
        foreach (KeyValuePair<string, RuntimeProp> entry in props)
        {
            StopAnimation(entry.Value);
            if (entry.Value?.image != null)
                UnityEngine.Object.Destroy(entry.Value.image.gameObject);
        }
        props.Clear();
    }

    private void ApplyLayout(RuntimeProp runtime)
    {
        if (runtime?.image == null || runtime.document == null)
            return;

        RectTransform targetLayer = runtime.document.normalizedLayer == "back" ? backLayer : frontLayer;
        if (targetLayer != null && runtime.image.transform.parent != targetLayer)
            runtime.image.transform.SetParent(targetLayer, false);

        RectTransform rect = runtime.image.rectTransform;
        Vector2 anchor = new Vector2(runtime.document.normalizedX, runtime.document.normalizedY);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        float aspect = runtime.image.sprite == null || runtime.image.sprite.rect.height <= 0f
            ? 1f
            : runtime.image.sprite.rect.width / runtime.image.sprite.rect.height;
        float height = DefaultHeight * runtime.document.normalizedScale;
        rect.sizeDelta = new Vector2(height * aspect, height);
    }

    private IEnumerator FadeIn(RuntimeProp runtime)
    {
        const float duration = .18f;
        float elapsed = 0f;
        while (elapsed < duration && runtime?.image != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            runtime.canvasGroup.alpha = eased;
            runtime.image.rectTransform.localScale = Vector3.one * Mathf.Lerp(.88f, 1f, eased);
            yield return null;
        }
        if (runtime?.image != null)
        {
            runtime.canvasGroup.alpha = 1f;
            runtime.image.rectTransform.localScale = Vector3.one;
            runtime.animation = null;
        }
    }

    private IEnumerator FadeOut(RuntimeProp runtime)
    {
        const float duration = .15f;
        float startAlpha = runtime.canvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < duration && runtime?.image != null)
        {
            elapsed += Time.unscaledDeltaTime;
            runtime.canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        if (runtime != null)
            DestroyRuntime(runtime.document?.id, runtime);
    }

    private void StopAnimation(RuntimeProp runtime)
    {
        if (runtime?.animation == null || coroutineHost == null)
            return;
        coroutineHost.StopCoroutine(runtime.animation);
        runtime.animation = null;
    }

    private void DestroyRuntime(string propId, RuntimeProp runtime)
    {
        if (runtime?.image != null)
            UnityEngine.Object.Destroy(runtime.image.gameObject);
        if (!string.IsNullOrWhiteSpace(propId))
            props.Remove(propId);
    }

    private sealed class RuntimeProp
    {
        public StoryScenePropDocument document;
        public Image image;
        public CanvasGroup canvasGroup;
        public Outline outline;
        public Coroutine animation;
    }
}
