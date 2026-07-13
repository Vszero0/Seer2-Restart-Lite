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
            ValidatePath(actor.sprite, "actorSprite", "auto", errors, "actors[" + actor.id + "].sprite");
            ValidatePath(actor.icon, "actorIcon", "auto", errors, "actors[" + actor.id + "].icon");
            ValidatePath(actor.battleSprite, "actorSprite", "auto", errors, "actors[" + actor.id + "].battleSprite");
        }

        return actorDict;
    }

    public static void ValidatePath(string path, string kind, string source, List<string> errors, string location)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            return;

        string normalizedPath = path.Replace('\\', '/').TrimStart('/');
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
        bool exists = roots.Any(root => extensions.Any(extension => File.Exists(root + normalizedPath + extension)));
        if (!exists)
            errors.Add(location + " 引用的资源不存在：" + path + "，source=" + source);
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
