using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 剧情资源声明、角色资源和路径存在性的集中校验。
/// 资源规则与剧情命令结构分离，供运行时载入和编辑器保存前复用。
/// </summary>
public static class StoryResourceValidator
{
    private static readonly HashSet<string> ResourceKinds = new HashSet<string>
    {
        "sprite",
        "actorSprite",
        "actorIcon",
        "mapBackground",
        "audio",
        "map",
        "ui",
    };

    private static readonly HashSet<string> ResourceSources = new HashSet<string>
    {
        "auto",
        "mod",
        "builtin",
    };

    public static void ValidateResources(StoryDocument document, List<string> errors)
    {
        HashSet<string> resourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryResourceDefinition resource in document.resourceDefinitions ?? Array.Empty<StoryResourceDefinition>())
        {
            if (resource == null)
                continue;

            string path = resource.path?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                errors.Add("resourceDefinitions 中存在 path 为空的资源");
                continue;
            }

            if (!resourcePaths.Add(path))
                errors.Add("resourceDefinitions 存在重复资源路径：" + path);

            if (!string.IsNullOrEmpty(resource.kind) && !ResourceKinds.Contains(resource.kind))
                errors.Add("资源 kind 不支持：" + resource.kind + "，路径：" + path);

            string source = string.IsNullOrWhiteSpace(resource.source) ? "auto" : resource.source.Trim().ToLowerInvariant();
            if (!ResourceSources.Contains(source))
                errors.Add("资源 source 不支持：" + resource.source + "，路径：" + path);

            ValidatePath(path, resource.kind, source, errors, "resourceDefinitions[" + path + "]");
        }
    }

    public static Dictionary<string, StoryActorDocument> ValidateActors(StoryDocument document, List<string> errors)
    {
        Dictionary<string, StoryActorDocument> actorDict = new Dictionary<string, StoryActorDocument>();
        foreach (StoryActorDocument actor in document.actors ?? Array.Empty<StoryActorDocument>())
        {
            if (actor == null)
                continue;

            if (string.IsNullOrWhiteSpace(actor.id))
            {
                errors.Add("actors 中存在 id 为空的角色");
                continue;
            }

            if (actorDict.ContainsKey(actor.id))
            {
                errors.Add("actors 存在重复角色 id：" + actor.id);
                continue;
            }

            actorDict[actor.id] = actor;
            ValidatePath(actor.icon, "actorIcon", "auto", errors, "actors[" + actor.id + "].icon");
            if (string.Equals(actor.actorType, "pet", StringComparison.OrdinalIgnoreCase))
            {
                bool hasIdleSprite = ResourceExists(actor.sprite, "actorSprite", "auto");
                bool hasBattleSprite = ResourceExists(actor.battleSprite, "actorSprite", "auto");
                if (!hasIdleSprite && !hasBattleSprite)
                {
                    errors.Add("actors[" + actor.id + "] 缺少可用立绘：待机图与 battle 静态图均不存在"
                        + "（sprite=" + (actor.sprite ?? string.Empty)
                        + "，battleSprite=" + (actor.battleSprite ?? string.Empty) + "）");
                }
            }
            else
            {
                ValidatePath(actor.sprite, "actorSprite", "auto", errors, "actors[" + actor.id + "].sprite");
                ValidatePath(actor.battleSprite, "actorSprite", "auto", errors, "actors[" + actor.id + "].battleSprite");
            }
        }

        return actorDict;
    }

    /// <summary>
    /// 地图 ID 指向地图配置，而不一定直接对应背景文件。
    /// 与 ResourceManager.LoadMapResources 保持一致：优先使用 XML 的 resId，
    /// resId 为空时才使用地图自身 ID，并据此判断资源来自本体还是 Mod。
    /// </summary>
    public static void ValidateMap(int mapId, List<string> errors, string location)
    {
        if (mapId == 0)
            return;

        if (!TryLoadMapDefinition(mapId, out Map map, out string mapError))
        {
            errors.Add(location + " 引用的地图配置不存在或无法读取：" + mapId
                + (string.IsNullOrWhiteSpace(mapError) ? string.Empty : "（" + mapError + "）"));
            return;
        }

        int resourceId = map.resId == 0 ? map.id : map.resId;
        string source = Map.IsMod(resourceId) ? "mod" : "builtin";
        ValidatePath("Maps/bg/" + resourceId, "mapBackground", source, errors, location);
    }

    private static bool TryLoadMapDefinition(int mapId, out Map map, out string error)
    {
        map = null;
        error = string.Empty;
        try
        {
            string xml;
            if (Map.IsMod(mapId))
            {
                string path = Application.persistentDataPath + "/Mod/Maps/" + mapId + ".xml";
                if (!File.Exists(path))
                {
                    error = "找不到 Mod 地图 XML";
                    return false;
                }
                xml = File.ReadAllText(path);
            }
            else
            {
                TextAsset asset = Resources.Load<TextAsset>("Data/Maps/" + mapId);
                if (asset == null)
                {
                    error = "找不到本体地图 XML";
                    return false;
                }
                xml = asset.text;
            }

            map = ResourceManager.GetXML<Map>(xml);
            if (map == null)
            {
                error = "地图 XML 内容为空";
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            map = null;
            return false;
        }
    }

    public static void ValidatePath(string path, string kind, string source, List<string> errors, string location)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            return;

        if (!ResourceExists(path, kind, source))
            errors.Add(location + " 引用的资源不存在：" + path + "，source=" + source);
    }

    private static bool ResourceExists(string path, string kind, string source)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalizedPath = path.Replace('\\', '/').TrimStart('/');
        if (normalizedPath.StartsWith("Mod/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath.Substring("Mod/".Length);
            source = "mod";
        }
        else if (normalizedPath.StartsWith("Builtin/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath.Substring("Builtin/".Length);
            source = "builtin";
        }

        // 源码导出的自有资源会直接进入 Assets/Resources，而不是外部 Resources 目录。
        // 本体资源校验必须同时覆盖 Unity Resources 与可替换的外部资源目录。
        if (source != "mod" && BuiltinResourceExists(normalizedPath, kind))
            return true;

        string[] roots = source == "mod"
            ? new[] { Application.persistentDataPath + "/Mod/" }
            : source == "builtin"
                ? new[] { Application.persistentDataPath + "/Resources/" }
                : new[]
                {
                    Application.persistentDataPath + "/Mod/",
                    Application.persistentDataPath + "/Resources/",
                };

        string[] extensions = GetExtensions(kind, normalizedPath);
        return roots.Any(root => extensions.Any(extension => File.Exists(root + normalizedPath + extension)));
    }

    private static bool BuiltinResourceExists(string path, string kind)
    {
        string resourcePath = Path.ChangeExtension(path, null)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(resourcePath))
            return false;
        if (string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase))
            return Resources.Load<AudioClip>(resourcePath) != null;
        return Resources.Load<Sprite>(resourcePath) != null;
    }

    private static string[] GetExtensions(string kind, string path)
    {
        if (Path.HasExtension(path))
            return new[] { string.Empty };

        if (string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase))
            return new[] { ".mp3" };

        if (string.Equals(kind, "actorSprite", StringComparison.OrdinalIgnoreCase))
            return new[] { ".png", ".gif" };

        return new[] { ".png", ".gif" };
    }
}
