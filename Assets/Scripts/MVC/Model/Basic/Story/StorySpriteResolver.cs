using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 集中处理剧情立绘与地图背景的资源覆盖顺序。
/// </summary>
public static class StorySpriteResolver
{
    public static Sprite Load(string path, string source = "auto")
    {
        path = Normalize(path);
        if (string.IsNullOrEmpty(path) || path == "none" || ResourceManager.instance == null)
            return null;

        bool isExplicitModPath = path.TryTrimStart("Mod/", out string modPath);
        string resourcePath = isExplicitModPath ? modPath : path;
        source = NormalizeSource(isExplicitModPath ? "mod" : source);

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

        sprite = ResourceManager.instance.Get<Sprite>(path);
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
