using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 剧情运行时的角色舞台：管理立绘创建、显隐、自动/手动布局和前后层级。
/// 剧情命令调度与对话 UI 保持在 StoryPanel，未来可视化编辑器可复用本类预览布局。
/// </summary>
public sealed class StoryActorStage
{
    private readonly RectTransform actorLayer;
    private readonly MonoBehaviour coroutineHost;
    private readonly Action refreshOverlay;
    private readonly Func<string, string> getResourceSource;
    private readonly Dictionary<string, StoryActorRuntime> actors = new Dictionary<string, StoryActorRuntime>();
    private readonly Dictionary<string, int> nextSideOrders = new Dictionary<string, int>();
    private readonly Dictionary<string, StorySceneActorLayoutDocument> sceneActorLayouts = new Dictionary<string, StorySceneActorLayoutDocument>(StringComparer.OrdinalIgnoreCase);

    private StoryLayoutDocument globalLayout;
    private StoryActorLayoutSettings activeLayout;
    private int actorOrder;

    public StoryActorStage(
        RectTransform actorLayer,
        MonoBehaviour coroutineHost,
        Action refreshOverlay,
        Func<string, string> getResourceSource)
    {
        this.actorLayer = actorLayer;
        this.coroutineHost = coroutineHost;
        this.refreshOverlay = refreshOverlay;
        this.getResourceSource = getResourceSource;
    }

    public void Reset(StoryLayoutDocument storyLayout)
    {
        Clear();
        globalLayout = storyLayout;
        nextSideOrders.Clear();
        sceneActorLayouts.Clear();
        actorOrder = 0;
        activeLayout = StoryActorPlacementCalculator.Resolve(null, globalLayout);
    }

    public void ApplyScene(StorySceneActorLayoutDocument[] actorLayouts, StoryLayoutDocument sceneLayout)
    {
        Clear();
        nextSideOrders.Clear();
        SetSceneActorLayouts(actorLayouts);
        activeLayout = StoryActorPlacementCalculator.Resolve(sceneLayout, globalLayout);
    }

    public void Show(StoryActorDocument actor)
    {
        if (actor == null || string.IsNullOrEmpty(actor.id))
            return;

        if (!actors.TryGetValue(actor.id, out StoryActorRuntime runtime))
        {
            runtime = new StoryActorRuntime
            {
                document = actor,
                placement = GetPlacement(actor),
                image = CreateActorImage(actor),
                order = actorOrder++,
            };
            runtime.canvasGroup = runtime.image.GetComponent<CanvasGroup>();
            actors[actor.id] = runtime;
            PlayActorFade(runtime);
        }
        else
        {
            runtime.document = actor;
            runtime.placement = GetPlacement(actor);
            runtime.image.gameObject.SetActive(true);
        }

        LayoutActors();
    }

    public void Hide(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || string.Equals(actorId, "all", StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        if (!actors.TryGetValue(actorId, out StoryActorRuntime runtime))
            return;

        if (runtime.image != null)
            UnityEngine.Object.Destroy(runtime.image.gameObject);

        actors.Remove(actorId);
        LayoutActors();
    }

    public void SetActiveActor(string actorId)
    {
        foreach (StoryActorRuntime runtime in actors.Values)
        {
            if (runtime?.image == null)
                continue;

            bool active = !string.IsNullOrEmpty(actorId) && runtime.document.id == actorId;
            runtime.image.color = active ? Color.white : new Color32(118, 118, 126, 255);
            if (active)
                runtime.image.transform.SetAsLastSibling();
        }
    }

    public StorySceneActorLayoutDocument GetPlacement(StoryActorDocument actor)
    {
        if (actor != null && sceneActorLayouts.TryGetValue(actor.id, out StorySceneActorLayoutDocument placement))
            return placement;

        if (actor != null && actors.TryGetValue(actor.id, out StoryActorRuntime runtime) && runtime.placement != null)
            return runtime.placement;

        const string fallbackSide = "left";
        if (!nextSideOrders.TryGetValue(fallbackSide, out int order))
            order = 0;

        nextSideOrders[fallbackSide] = order + 1;
        return new StorySceneActorLayoutDocument
        {
            actorId = actor?.id,
            side = fallbackSide,
            order = order,
            scale = actor?.defaultScale > 0f ? actor.defaultScale : 1f,
            faceLeft = actor == null || actor.defaultFaceLeft,
            flipIcon = actor != null && actor.defaultFlipIcon,
        };
    }

    public void Clear()
    {
        foreach (StoryActorRuntime runtime in actors.Values)
        {
            if (runtime?.fadeCoroutine != null)
                coroutineHost.StopCoroutine(runtime.fadeCoroutine);

            if (runtime?.image != null)
                UnityEngine.Object.Destroy(runtime.image.gameObject);
        }

        actors.Clear();
    }

    private void SetSceneActorLayouts(StorySceneActorLayoutDocument[] layouts)
    {
        sceneActorLayouts.Clear();
        foreach (StorySceneActorLayoutDocument layout in layouts ?? Array.Empty<StorySceneActorLayoutDocument>())
        {
            if (layout != null && !string.IsNullOrWhiteSpace(layout.actorId))
                sceneActorLayouts[layout.actorId] = layout;
        }
    }

    private Image CreateActorImage(StoryActorDocument actor)
    {
        GameObject obj = new GameObject("Story Actor " + actor.id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(actorLayer, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(.5f, 0f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(.5f, 0f);

        Image image = obj.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.sprite = ResolveActorSprite(actor);
        image.gameObject.SetActive(image.sprite != null);

        CanvasGroup canvasGroup = image.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        return image;
    }

    private Sprite ResolveActorSprite(StoryActorDocument actor)
    {
        Sprite sprite = StorySpriteResolver.Load(actor.displaySprite, getResourceSource?.Invoke(actor.displaySprite));
        if (sprite != null && sprite != SpriteSet.Empty)
            return sprite;

        // 精灵立绘不能只依赖故事文本中的路径。宠物数据本身已经封装了
        // 本体 / Mod 与默认皮肤的取图规则；路径资源尚未同步可用时，以它为准。
        if (string.Equals(actor.actorType, "pet", StringComparison.OrdinalIgnoreCase))
        {
            int petId = GetPetSkinId(actor);
            sprite = petId == 0 ? null : PetUISystem.GetPetIdleImage(petId);
            if (sprite != null && sprite != SpriteSet.Empty)
                return sprite;
        }

        // 舞台只能显示立绘；头像是对话栏专用资源，不能作为立绘回退，
        // 否则会出现“部分角色是头像、部分角色是立绘”的混杂效果。
        return null;
    }

    private static int GetPetSkinId(StoryActorDocument actor)
    {
        string path = StorySpriteResolver.Normalize(actor?.displaySprite);
        const string prefix = "Pets/pet/";
        if (!string.IsNullOrWhiteSpace(path) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(path.Substring(prefix.Length), out int skinId))
            return skinId;

        return int.TryParse(actor?.petId, out int petId) ? petId : 0;
    }

    private void PlayActorFade(StoryActorRuntime runtime)
    {
        if (runtime == null || runtime.canvasGroup == null)
            return;

        if (runtime.fadeCoroutine != null)
            coroutineHost.StopCoroutine(runtime.fadeCoroutine);

        runtime.fadeCoroutine = coroutineHost.StartCoroutine(ActorFadeCoroutine(runtime));
    }

    private IEnumerator ActorFadeCoroutine(StoryActorRuntime runtime)
    {
        const float duration = .18f;
        float time = 0f;
        runtime.canvasGroup.alpha = 0f;

        while (time < duration)
        {
            runtime.canvasGroup.alpha = Mathf.Clamp01(time / duration);
            time += Time.unscaledDeltaTime;
            yield return null;
        }

        runtime.canvasGroup.alpha = 1f;
        runtime.fadeCoroutine = null;
    }

    private void LayoutActors()
    {
        if (activeLayout == null)
            activeLayout = StoryActorPlacementCalculator.Resolve(null, globalLayout);

        LayoutActorsBySide("left");
        LayoutActorsBySide("right");
        LayoutManualActors();
        ApplyInitialActorLayering();
        refreshOverlay?.Invoke();
    }

    private void LayoutActorsBySide(string side)
    {
        List<StoryActorRuntime> sideActors = actors.Values
            .Where(x => x?.image != null && x.placement != null && x.placement.normalizedPlacementMode == "auto" && x.placement.normalizedSide == side)
            .OrderBy(x => x.placement.order)
            .ThenBy(x => x.order)
            .ToList();

        int maxOrder = sideActors.Count == 0 ? 0 : sideActors.Max(x => x.placement.order);
        foreach (StoryActorRuntime runtime in sideActors)
        {
            RectTransform rect = runtime.image.rectTransform;
            bool isRight = side == "right";
            int visualIndex = Mathf.Max(0, runtime.placement.order);
            float yOffset = activeLayout.isBottomAligned ? 0f : (maxOrder - visualIndex) * activeLayout.stackOffset;
            float scale = Mathf.Max(.1f, runtime.placement.scale);
            Vector2 originalSize = StoryActorPlacementCalculator.GetSpriteSize(runtime.image.sprite, activeLayout.actorHeight);
            float sideOffset = activeLayout.centerGap + visualIndex * activeLayout.actorSpacing;

            rect.anchorMin = new Vector2(.5f, 0f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = StoryActorPlacementCalculator.GetVisibleBottomPivot(runtime.image.sprite);
            rect.anchoredPosition = new Vector2(isRight ? sideOffset : -sideOffset, activeLayout.actorBottom + yOffset);
            rect.sizeDelta = new Vector2(originalSize.x * scale, originalSize.y * scale);
            rect.localScale = new Vector3(runtime.placement.faceLeft ? -1f : 1f, 1f, 1f);
        }
    }

    private void LayoutManualActors()
    {
        foreach (StoryActorRuntime runtime in actors.Values.Where(x => x?.image != null && x.placement?.normalizedPlacementMode == "manual"))
        {
            RectTransform rect = runtime.image.rectTransform;
            float scale = Mathf.Max(.1f, runtime.placement.scale);
            Vector2 originalSize = StoryActorPlacementCalculator.GetSpriteSize(runtime.image.sprite, activeLayout.actorHeight);
            rect.anchorMin = new Vector2(.5f, 0f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = StoryActorPlacementCalculator.GetVisibleBottomPivot(runtime.image.sprite);
            rect.anchoredPosition = new Vector2(runtime.placement.x, runtime.placement.y);
            rect.sizeDelta = new Vector2(originalSize.x * scale, originalSize.y * scale);
            rect.localScale = new Vector3(runtime.placement.faceLeft ? -1f : 1f, 1f, 1f);
        }
    }

    private void ApplyInitialActorLayering()
    {
        foreach (StoryActorRuntime runtime in actors.Values
            .Where(x => x?.image != null)
            .OrderBy(x => x.placement?.normalizedPlacementMode == "manual" ? 1 : 0)
            .ThenBy(x => x.placement?.normalizedPlacementMode == "manual" ? -x.placement.y : x.placement?.order ?? 0)
            .ThenBy(x => x.order))
        {
            runtime.image.transform.SetAsLastSibling();
        }
    }

    private sealed class StoryActorRuntime
    {
        public StoryActorDocument document;
        public Image image;
        public CanvasGroup canvasGroup;
        public Coroutine fadeCoroutine;
        public int order;
        public StorySceneActorLayoutDocument placement;
    }
}
