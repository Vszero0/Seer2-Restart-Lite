using UnityEngine;

/// <summary>
/// 鼠标驱动的剧情背景视差。
/// 该类只由 StoryPanel 在播放或预览时创建，编辑器剧情点面板不会使用它。
/// </summary>
public sealed class StoryBackgroundMotion
{
    private readonly RectTransform viewport;
    private readonly RectTransform[] primaryTargets;
    private readonly RectTransform[] transitionTargets;
    private readonly Canvas canvas;

    private Vector2 primaryBasePosition;
    private Vector3 primaryBaseScale = Vector3.one;
    private Vector2 transitionBasePosition;
    private Vector3 transitionBaseScale = Vector3.one;
    private Vector2 currentOffset;
    private Vector2 targetOffset;
    private float motionScale = 1f;

    public StoryBackgroundMotion(
        RectTransform viewport,
        RectTransform[] primaryTargets,
        RectTransform[] transitionTargets)
    {
        this.viewport = viewport;
        this.primaryTargets = primaryTargets ?? System.Array.Empty<RectTransform>();
        this.transitionTargets = transitionTargets ?? System.Array.Empty<RectTransform>();
        canvas = viewport == null ? null : viewport.GetComponentInParent<Canvas>();
        CaptureBaseTransforms();
    }

    public void SetPrimaryPosition(Vector2 position)
    {
        primaryBasePosition = position;
        ApplyTransforms();
    }

    public void SetPrimaryScale(Vector3 scale)
    {
        primaryBaseScale = scale;
        ApplyTransforms();
    }

    public void SetTransitionPosition(Vector2 position)
    {
        transitionBasePosition = position;
        ApplyTransforms();
    }

    public void SetTransitionScale(Vector3 scale)
    {
        transitionBaseScale = scale;
        ApplyTransforms();
    }

    public void Tick(StoryPresentationSettings settings, float deltaTime)
    {
        if (settings == null)
        {
            currentOffset = Vector2.zero;
            targetOffset = Vector2.zero;
            motionScale = 1f;
            ApplyTransforms();
            return;
        }

        bool enabled = settings.BackgroundMotionEnabled
            && !Application.isMobilePlatform
            && Input.touchCount == 0
            && viewport != null
            && viewport.rect.width > 0f
            && viewport.rect.height > 0f
            && Application.isFocused;

        targetOffset = enabled ? ReadPointerOffset(settings) : Vector2.zero;
        float smoothing = settings.BackgroundMotionSmoothing;
        float blend = smoothing <= 0.001f
            ? 1f
            : 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / smoothing);
        currentOffset = Vector2.Lerp(currentOffset, targetOffset, blend);
        motionScale = enabled ? settings.BackgroundMotionScale : 1f;
        ApplyTransforms();
    }

    public void Reset()
    {
        currentOffset = Vector2.zero;
        targetOffset = Vector2.zero;
        motionScale = 1f;
        ApplyTransforms();
    }

    private Vector2 ReadPointerOffset(StoryPresentationSettings settings)
    {
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            viewport, Input.mousePosition, eventCamera, out Vector2 localPoint))
        {
            return Vector2.zero;
        }

        Rect rect = viewport.rect;
        float normalizedX = Mathf.Clamp((localPoint.x - rect.xMin) / rect.width * 2f - 1f, -1f, 1f);
        float normalizedY = Mathf.Clamp((localPoint.y - rect.yMin) / rect.height * 2f - 1f, -1f, 1f);
        float horizontalCoverage = rect.width * Mathf.Max(0f, settings.BackgroundMotionScale - 1f) * .5f;
        float verticalCoverage = rect.height * Mathf.Max(0f, settings.BackgroundMotionScale - 1f) * .5f;
        float horizontalOffset = Mathf.Min(settings.BackgroundMotionMaxOffset, horizontalCoverage);
        float verticalOffset = Mathf.Min(settings.BackgroundMotionMaxOffset, verticalCoverage);

        // 鼠标向右/向上时背景向相反方向移动，模拟镜头轻微跟随。
        return new Vector2(-normalizedX * horizontalOffset, -normalizedY * verticalOffset);
    }

    private void CaptureBaseTransforms()
    {
        if (primaryTargets != null)
        {
            foreach (RectTransform target in primaryTargets)
            {
                if (target == null)
                    continue;
                primaryBasePosition = target.anchoredPosition;
                primaryBaseScale = target.localScale;
                break;
            }
        }

        if (transitionTargets != null)
        {
            foreach (RectTransform target in transitionTargets)
            {
                if (target == null)
                    continue;
                transitionBasePosition = target.anchoredPosition;
                transitionBaseScale = target.localScale;
                break;
            }
        }
    }

    private void ApplyTransforms()
    {
        ApplyTransforms(primaryTargets, primaryBasePosition, primaryBaseScale);
        ApplyTransforms(transitionTargets, transitionBasePosition, transitionBaseScale);
    }

    private void ApplyTransforms(RectTransform[] targets, Vector2 basePosition, Vector3 baseScale)
    {
        if (targets == null)
            return;

        Vector3 scaled = baseScale * motionScale;
        foreach (RectTransform target in targets)
        {
            if (target == null)
                continue;
            target.anchoredPosition = basePosition + currentOffset;
            target.localScale = scaled;
        }
    }
}
