using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 集中处理剧情立绘与地图背景的资源覆盖顺序。
/// </summary>
public static class StorySpriteResolver
{
    private const byte VisibleAlphaThreshold = 8;
    private const int BattleSpritePadding = 3;
    private static readonly Dictionary<Sprite, Sprite> CroppedBattleSpriteCache = new Dictionary<Sprite, Sprite>();
    private static readonly Dictionary<string, Sprite> CroppedIconSpriteCache = new Dictionary<string, Sprite>();

    public static Sprite Load(string path, string source = "auto")
    {
        path = Normalize(path);
        if (string.IsNullOrEmpty(path) || path == "none" || ResourceManager.instance == null)
            return null;

        bool isExplicitModPath = path.TryTrimStart("Mod/", out string modPath);
        bool isExplicitBuiltinPath = path.TryTrimStart("Builtin/", out string builtinPath);
        string resourcePath = isExplicitModPath ? modPath : isExplicitBuiltinPath ? builtinPath : path;
        source = NormalizeSource(isExplicitModPath ? "mod" : isExplicitBuiltinPath ? "builtin" : source);

        if (source != "builtin")
        {
            Sprite modSprite = ResourceManager.instance.GetLocalAddressables<Sprite>(resourcePath, true);
            if (IsUsable(modSprite))
                return modSprite;
        }

        if (source == "mod")
            return null;

        Sprite sprite = ResourceManager.instance.GetLocalAddressables<Sprite>(resourcePath, false);
        if (IsUsable(sprite))
            return sprite;

        sprite = ResourceManager.instance.Get<Sprite>(resourcePath);
        if (IsUsable(sprite))
            return sprite;

        sprite = NpcInfo.GetIcon(path);
        return IsUsable(sprite) ? sprite : null;
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        if (path.StartsWith("pet:", StringComparison.OrdinalIgnoreCase))
            return "Pets/pet/" + path.Substring("pet:".Length);

        if (path.StartsWith("npc:", StringComparison.OrdinalIgnoreCase))
            return "Npc/" + path.Substring("npc:".Length);

        return path;
    }

    public static bool IsBattleSpritePath(string path)
    {
        string normalized = Normalize(path)?.Replace('\\', '/').TrimStart('/');
        return !string.IsNullOrWhiteSpace(normalized)
            && (normalized.StartsWith("Pets/battle/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Mod/Pets/battle/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// battle 静态图常放在大尺寸透明画布中。剧情舞台只裁切这类回退资源，
    /// 让现有尺寸与布局逻辑基于真实可见区域工作，不改变普通立绘的构图留白。
    /// </summary>
    public static Sprite PrepareBattleStageSprite(Sprite source)
    {
        if (source == null || source.texture == null)
            return source;
        if (CroppedBattleSpriteCache.TryGetValue(source, out Sprite cached))
            return cached;

        Sprite result = CropToVisibleBounds(source);
        CroppedBattleSpriteCache[source] = result;
        return result;
    }

    public static Rect NormalizeCrop(float x, float y, float width, float height)
    {
        if (width <= 0f || height <= 0f)
            return new Rect(0f, 0f, 1f, 1f);

        width = Mathf.Clamp(width, .05f, 1f);
        height = Mathf.Clamp(height, .05f, 1f);
        return new Rect(
            Mathf.Clamp(x, 0f, 1f - width),
            Mathf.Clamp(y, 0f, 1f - height),
            width,
            height);
    }

    public static Rect GetDefaultIconCrop(Sprite source)
    {
        if (source == null || source.rect.width <= 0f || source.rect.height <= 0f)
            return new Rect(0f, 0f, 1f, 1f);

        if (source.rect.width > source.rect.height)
        {
            float width = source.rect.height / source.rect.width;
            return new Rect((1f - width) * .5f, 0f, width, 1f);
        }

        float height = source.rect.width / source.rect.height;
        return new Rect(0f, 1f - height, 1f, height);
    }

    public static Sprite PrepareIconSprite(Sprite source, Rect normalizedCrop)
    {
        if (!IsUsable(source))
            return source;

        Rect crop = NormalizeCrop(normalizedCrop.x, normalizedCrop.y, normalizedCrop.width, normalizedCrop.height);
        if (crop.x <= .001f && crop.y <= .001f && crop.width >= .999f && crop.height >= .999f)
            return source;

        string key = source.GetInstanceID() + "|" + crop.x.ToString("F4") + "|" + crop.y.ToString("F4")
            + "|" + crop.width.ToString("F4") + "|" + crop.height.ToString("F4");
        if (CroppedIconSpriteCache.TryGetValue(key, out Sprite cached) && cached != null)
            return cached;

        Rect sourceRect = source.rect;
        Rect croppedRect = new Rect(
            sourceRect.x + sourceRect.width * crop.x,
            sourceRect.y + sourceRect.height * crop.y,
            sourceRect.width * crop.width,
            sourceRect.height * crop.height);
        Sprite result = Sprite.Create(source.texture, croppedRect, new Vector2(.5f, .5f),
            source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        result.name = source.name + " (Story Icon Crop)";
        result.hideFlags = HideFlags.DontSave;
        CroppedIconSpriteCache[key] = result;
        return result;
    }

    private static Sprite CropToVisibleBounds(Sprite source)
    {
        Rect rect = source.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return source;

        try
        {
            Texture2D texture = source.texture;
            Color32[] pixels = texture.GetPixels32();
            int textureWidth = texture.width;
            int xStart = Mathf.FloorToInt(rect.xMin);
            int yStart = Mathf.FloorToInt(rect.yMin);
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int pixelY = yStart + y;
                if (pixelY < 0 || pixelY >= texture.height)
                    continue;

                for (int x = 0; x < width; x++)
                {
                    int pixelX = xStart + x;
                    if (pixelX < 0 || pixelX >= textureWidth
                        || pixels[pixelY * textureWidth + pixelX].a <= VisibleAlphaThreshold)
                    {
                        continue;
                    }

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
                return source;

            minX = Mathf.Max(0, minX - BattleSpritePadding);
            minY = Mathf.Max(0, minY - BattleSpritePadding);
            maxX = Mathf.Min(width - 1, maxX + BattleSpritePadding);
            maxY = Mathf.Min(height - 1, maxY + BattleSpritePadding);
            if (minX == 0 && minY == 0 && maxX == width - 1 && maxY == height - 1)
                return source;

            Rect croppedRect = new Rect(
                xStart + minX,
                yStart + minY,
                maxX - minX + 1,
                maxY - minY + 1);
            Sprite cropped = Sprite.Create(texture, croppedRect, new Vector2(.5f, 0f),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            cropped.name = source.name + " (Story Battle Crop)";
            cropped.hideFlags = HideFlags.DontSave;
            return cropped;
        }
        catch (UnityException)
        {
            return source;
        }
    }

    private static string NormalizeSource(string source)
    {
        if (string.Equals(source, "mod", StringComparison.OrdinalIgnoreCase))
            return "mod";

        if (string.Equals(source, "builtin", StringComparison.OrdinalIgnoreCase))
            return "builtin";

        return "auto";
    }

    private static bool IsUsable(Sprite sprite)
    {
        return sprite != null && sprite != SpriteSet.Empty;
    }
}
