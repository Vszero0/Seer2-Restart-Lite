using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 根据剧本/场景布局数据计算立绘尺寸、可见底边枢轴和自动排布参数。
/// 不持有任何角色 GameObject，便于运行时舞台与未来编辑舞台共享。
/// </summary>
public static class StoryActorPlacementCalculator
{
    private const float DefaultActorSpacing = 132f;
    private const float DefaultActorHeight = 250f;
    private const float DefaultActorBottom = 166f;
    private const float DefaultActorCenterGap = 112f;
    private const float DefaultActorStackOffset = 16f;

    private static readonly Dictionary<Sprite, Vector2> VisiblePivotCache = new Dictionary<Sprite, Vector2>();

    public static StoryActorLayoutSettings Resolve(StoryLayoutDocument sceneLayout, StoryLayoutDocument globalLayout)
    {
        return new StoryActorLayoutSettings
        {
            autoLayoutMode = ResolveAutoLayoutMode(sceneLayout?.autoLayoutMode, globalLayout?.autoLayoutMode),
            actorSpacing = FirstPositive(sceneLayout?.actorSpacing, globalLayout?.actorSpacing, DefaultActorSpacing),
            actorHeight = FirstPositive(sceneLayout?.actorHeight, globalLayout?.actorHeight, DefaultActorHeight),
            actorBottom = FirstPositive(sceneLayout?.actorBottom, globalLayout?.actorBottom, DefaultActorBottom),
            centerGap = FirstPositive(sceneLayout?.centerGap, globalLayout?.centerGap, DefaultActorCenterGap),
            stackOffset = FirstPositive(sceneLayout?.stackOffset, globalLayout?.stackOffset, DefaultActorStackOffset),
        };
    }

    public static Vector2 GetSpriteSize(Sprite sprite, float fallbackHeight)
    {
        if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
        {
            float height = fallbackHeight > 0f ? fallbackHeight : DefaultActorHeight;
            return new Vector2(height * .72f, height);
        }

        return new Vector2(sprite.rect.width, sprite.rect.height);
    }

    public static Vector2 GetVisibleBottomPivot(Sprite sprite)
    {
        if (sprite == null)
            return new Vector2(.5f, 0f);

        if (VisiblePivotCache.TryGetValue(sprite, out Vector2 cachedPivot))
            return cachedPivot;

        Vector2 pivot = CalculateVisibleBottomPivot(sprite);
        VisiblePivotCache[sprite] = pivot;
        return pivot;
    }

    private static Vector2 CalculateVisibleBottomPivot(Sprite sprite)
    {
        Rect rect = sprite.rect;
        if (rect.width <= 0f || rect.height <= 0f || sprite.texture == null)
            return new Vector2(.5f, 0f);

        try
        {
            Texture2D texture = sprite.texture;
            Color32[] pixels = texture.GetPixels32();
            int textureWidth = texture.width;
            int xMin = Mathf.FloorToInt(rect.xMin);
            int yMin = Mathf.FloorToInt(rect.yMin);
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            int minX = width;
            int minY = height;
            int maxX = -1;

            for (int y = 0; y < height; y++)
            {
                int pixelY = yMin + y;
                if (pixelY < 0 || pixelY >= texture.height)
                    continue;

                for (int x = 0; x < width; x++)
                {
                    int pixelX = xMin + x;
                    if (pixelX < 0 || pixelX >= textureWidth || pixels[pixelY * textureWidth + pixelX].a <= 8)
                        continue;

                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                }
            }

            if (maxX < minX || minY >= height)
                return new Vector2(.5f, 0f);

            return new Vector2(
                Mathf.Clamp01(((minX + maxX + 1f) * .5f) / width),
                Mathf.Clamp01(minY / (float)height));
        }
        catch (UnityException)
        {
            return new Vector2(.5f, 0f);
        }
    }

    private static float FirstPositive(float? primary, float? secondary, float fallback)
    {
        if (primary.HasValue && primary.Value > 0f)
            return primary.Value;

        if (secondary.HasValue && secondary.Value > 0f)
            return secondary.Value;

        return fallback;
    }

    private static string ResolveAutoLayoutMode(string sceneMode, string globalMode)
    {
        string mode = string.IsNullOrWhiteSpace(sceneMode) ? globalMode : sceneMode;
        return string.Equals(mode, "bottomAligned", StringComparison.OrdinalIgnoreCase) ? "bottomAligned" : "invertedV";
    }
}

public sealed class StoryActorLayoutSettings
{
    public string autoLayoutMode;
    public float actorSpacing;
    public float actorHeight;
    public float actorBottom;
    public float centerGap;
    public float stackOffset;

    public bool isBottomAligned => autoLayoutMode == "bottomAligned";
}
