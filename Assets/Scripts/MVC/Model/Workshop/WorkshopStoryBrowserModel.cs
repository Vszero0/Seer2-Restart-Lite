using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class WorkshopStoryBrowserModel
{
    private readonly WorkshopStoryRepository repository;
    private readonly WorkshopStorySourceExporter sourceExporter = new WorkshopStorySourceExporter();

    public IReadOnlyList<WorkshopStorySummary> Stories { get; private set; } = Array.Empty<WorkshopStorySummary>();
    public WorkshopStorySummary SelectedStory { get; private set; }
    public StoryDocument SelectedDocument { get; private set; }
    public StoryNodeDocument SelectedNode { get; private set; }
    public bool HasUnsavedChanges { get; private set; }
    public bool CanExportSource => sourceExporter.CanExport;
    public bool CanCreateSource => repository.CanCreateStorage(WorkshopStoryStorageKind.Source);
    public bool CanCreateMod => repository.CanCreateStorage(WorkshopStoryStorageKind.Mod);

    public IReadOnlyList<WorkshopStoryChoiceOption> SelectedNodeChoiceOptions => GetSelectedNodeChoiceOptions();

    public WorkshopStoryBrowserModel(WorkshopStoryRepository repository)
    {
        this.repository = repository;
    }

    public bool Reload(out string error)
    {
        string selectedPath = SelectedStory?.path;
        Stories = repository.List(out error);
        SelectedStory = Stories.FirstOrDefault(story => story.path == selectedPath) ?? Stories.FirstOrDefault();
        SelectedDocument = null;
        SelectedNode = null;
        HasUnsavedChanges = false;

        if (!string.IsNullOrEmpty(error))
            return false;

        return SelectStory(SelectedStory?.path, out error);
    }

    public bool SelectStory(string path, out string error)
    {
        if (HasUnsavedChanges
            && SelectedStory != null
            && !string.Equals(SelectedStory.path, path, StringComparison.OrdinalIgnoreCase))
        {
            error = "当前剧本尚未保存，请先保存后再切换。";
            return false;
        }

        SelectedStory = Stories.FirstOrDefault(story => story.path == path);
        SelectedDocument = null;
        SelectedNode = null;
        HasUnsavedChanges = false;
        if (SelectedStory == null)
        {
            error = string.Empty;
            return true;
        }

        if (!SelectedStory.isValid)
        {
            error = SelectedStory.error;
            return false;
        }

        if (!repository.TryLoad(path, out StoryDocument document, out error))
            return false;

        SelectedDocument = document;
        SelectedNode = (document.nodes ?? Array.Empty<StoryNodeDocument>()).FirstOrDefault(node => node != null && node.id == document.entry)
            ?? (document.nodes ?? Array.Empty<StoryNodeDocument>()).FirstOrDefault(node => node != null);
        error = string.Empty;
        return true;
    }

    public void SelectNode(string nodeId)
    {
        SelectedNode = (SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
            .FirstOrDefault(node => node != null && node.id == nodeId);
    }

    public bool UpdateSelectedStoryMetadata(string title, string summary, bool replayable, out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        SelectedDocument.title = title?.Trim() ?? string.Empty;
        SelectedDocument.summary = summary?.Trim() ?? string.Empty;
        SelectedDocument.replayable = replayable;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public IReadOnlyList<StoryActorDocument> GetStoryActors()
    {
        return (SelectedDocument?.actors ?? Array.Empty<StoryActorDocument>())
            .Where(actor => actor != null)
            .OrderBy(actor => string.Equals(actor.actorType, "npc", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(actor => actor.displayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<StorySceneResourceDocument> GetStoryScenes()
    {
        return (SelectedDocument?.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
            .Where(scene => scene != null)
            .OrderBy(scene => scene.displayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool CreateStoryScene(out StorySceneResourceDocument scene, out string error)
    {
        scene = null;
        if (!TryGetSelectedDocument(out error))
            return false;

        int index = 1;
        string sceneId;
        do { sceneId = "custom_scene_" + index++; }
        while ((SelectedDocument.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
            .Any(value => value != null && string.Equals(value.id, sceneId, StringComparison.OrdinalIgnoreCase)));

        scene = new StorySceneResourceDocument { id = sceneId, name = "未命名场景" };
        SelectedDocument.sceneResources = (SelectedDocument.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
            .Where(value => value != null)
            .Append(scene)
            .ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool UpdateStoryScene(string sceneId, string name, out string error)
    {
        if (!TryGetStoryScene(sceneId, out StorySceneResourceDocument scene, out error))
            return false;
        scene.name = string.IsNullOrWhiteSpace(name) ? "未命名场景" : name.Trim();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool ImportStorySceneBackground(string sceneId, string sourcePath, out string error)
    {
        if (!TryGetStoryScene(sceneId, out StorySceneResourceDocument scene, out error))
            return false;
        if (!SaveSystem.TryLoadAllBytes(sourcePath, out byte[] bytes)
            || !SpriteSet.TryCreateSpriteFromBytes(bytes, out Sprite sprite))
        {
            error = "选择的背景无法读取，请使用 PNG 图片。";
            return false;
        }

        return WriteStoryAsset(sceneId, "Scenes", "background.png", bytes, "mapBackground",
            path =>
            {
                scene.backgroundResourcePath = path;
                CacheStorySprite(path, sprite);
            }, out error);
    }

    public bool ImportStorySceneBgm(string sceneId, string sourcePath, out string error)
    {
        if (!TryGetStoryScene(sceneId, out StorySceneResourceDocument scene, out error))
            return false;
        string previousPath = scene.defaultBgmResourcePath;
        string extension = Path.GetExtension(sourcePath)?.ToLowerInvariant();
        if (extension != ".mp3")
        {
            error = "请选择 MP3 音频。";
            return false;
        }
        if (!SaveSystem.TryLoadAllBytes(sourcePath, out byte[] bytes) || bytes == null || bytes.Length == 0)
        {
            error = "选择的 BGM 无法读取。";
            return false;
        }

        string contentHash;
        using (SHA256 sha256 = SHA256.Create())
            contentHash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        bool saved = WriteStoryAsset("Shared", "Audio", contentHash + extension, bytes, "audio",
            path => scene.defaultBgmResourcePath = path, out error);
        if (saved && !string.Equals(previousPath, scene.defaultBgmResourcePath, StringComparison.OrdinalIgnoreCase))
            RemoveStoryResource(previousPath, true, scene.id);
        return saved;
    }

    public bool ClearStorySceneBgm(string sceneId, out string error)
    {
        if (!TryGetStoryScene(sceneId, out StorySceneResourceDocument scene, out error))
            return false;
        RemoveStoryResource(scene.defaultBgmResourcePath, true, scene.id);
        scene.defaultBgmResourcePath = null;
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool DeleteStoryScene(string sceneId, out string error)
    {
        if (!TryGetStoryScene(sceneId, out StorySceneResourceDocument scene, out error))
            return false;
        bool referenced = (SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .SelectMany(node => node?.scenes ?? Array.Empty<StorySceneDocument>())
            .Any(value => value != null
                && string.Equals(value.sceneResourceId, sceneId, StringComparison.OrdinalIgnoreCase));
        if (referenced)
        {
            error = "该自制场景已被剧情点引用，请先更换相关场景。";
            return false;
        }
        RemoveStoryResource(scene.backgroundResourcePath, true, scene.id);
        RemoveStoryResource(scene.defaultBgmResourcePath, true, scene.id);
        SelectedDocument.sceneResources = (SelectedDocument.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
            .Where(value => value != null && value != scene)
            .ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool CreateNpcActor(out StoryActorDocument actor, out string error)
    {
        actor = null;
        if (!TryGetSelectedDocument(out error))
            return false;

        int index = 1;
        string actorId;
        do
        {
            actorId = "actor_" + index++;
        }
        while ((SelectedDocument.actors ?? Array.Empty<StoryActorDocument>())
            .Any(value => value != null && string.Equals(value.id, actorId, StringComparison.OrdinalIgnoreCase)));

        actor = new StoryActorDocument
        {
            id = actorId,
            actorType = "npc",
            name = "未命名角色",
            sourceFacing = "right",
            iconMode = "crop",
            defaultScale = 1f,
        };
        SelectedDocument.actors = (SelectedDocument.actors ?? Array.Empty<StoryActorDocument>())
            .Where(value => value != null)
            .Append(actor)
            .ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool UpdateNpcActor(string actorId, string name, string sourceFacing, bool usePortraitIcon, out string error)
    {
        if (!TryGetNpcActor(actorId, out StoryActorDocument actor, out error))
            return false;

        actor.name = string.IsNullOrWhiteSpace(name) ? "未命名角色" : name.Trim();
        actor.sourceFacing = string.Equals(sourceFacing, "left", StringComparison.OrdinalIgnoreCase) ? "left" : "right";
        actor.iconMode = usePortraitIcon ? "crop" : "separate";
        actor.icon = usePortraitIcon ? actor.sprite : actor.independentIcon;
        if (usePortraitIcon
            && actor.iconCropWidth <= 0f
            && actor.iconCropHeight <= 0f
            && !string.IsNullOrWhiteSpace(actor.sprite))
        {
            Sprite portrait = StorySpriteResolver.Load(actor.sprite,
                actor.sprite.StartsWith("Mod/", StringComparison.OrdinalIgnoreCase) ? "mod" : "builtin");
            if (portrait != null && portrait != SpriteSet.Empty)
                ApplyCrop(actor, StorySpriteResolver.GetDefaultIconCrop(portrait));
        }
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool SetNpcActorImage(string actorId, string resourcePath, bool isIcon, out string error)
    {
        if (!TryGetNpcActor(actorId, out StoryActorDocument actor, out error))
            return false;

        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            error = "请选择有效的图片资源。";
            return false;
        }

        Sprite sprite = StorySpriteResolver.Load(resourcePath, resourcePath.StartsWith("Mod/", StringComparison.OrdinalIgnoreCase) ? "mod" : "builtin");
        if (sprite == null || sprite == SpriteSet.Empty)
        {
            error = "无法读取图片资源：" + resourcePath;
            return false;
        }

        if (isIcon)
        {
            actor.independentIcon = resourcePath;
            actor.iconMode = "separate";
            actor.icon = resourcePath;
        }
        else
        {
            actor.sprite = resourcePath;
            if (actor.usesPortraitIcon)
            {
                actor.icon = resourcePath;
                ApplyCrop(actor, StorySpriteResolver.GetDefaultIconCrop(sprite));
            }
        }

        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool ImportNpcActorImage(string actorId, string sourcePath, bool isIcon, out string error)
    {
        if (!TryGetNpcActor(actorId, out StoryActorDocument actor, out error))
            return false;
        if (!SaveSystem.TryLoadAllBytes(sourcePath, out byte[] bytes)
            || !SpriteSet.TryCreateSpriteFromBytes(bytes, out Sprite sprite))
        {
            error = "选择的图片无法读取，请使用 PNG 图片。";
            return false;
        }

        try
        {
            string storyId = MakeSafePathSegment(SelectedDocument.id, "story");
            string safeActorId = MakeSafePathSegment(actor.id, "actor");
            string kind = isIcon ? "icon" : "sprite";
            WorkshopStoryStorageKind storageKind = SelectedStory.storageKind;
            string relativePath = Path.Combine("Characters", safeActorId, kind + ".png").Replace('\\', '/');
            if (!repository.TryGetOwnedAssetPaths(storageKind, storyId, relativePath,
                    out string absolutePath, out string resourcePath, out error))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, bytes);
            ImportSourceAssetIfNeeded(storageKind, absolutePath);
            resourcePath = Path.ChangeExtension(resourcePath, null)?.Replace('\\', '/');

            CacheStorySprite(resourcePath, sprite);
            RegisterStoryResource(resourcePath, isIcon ? "actorIcon" : "actorSprite");
            if (isIcon)
            {
                actor.independentIcon = resourcePath;
                actor.iconMode = "separate";
                actor.icon = resourcePath;
            }
            else
            {
                actor.sprite = resourcePath;
                if (actor.usesPortraitIcon)
                {
                    actor.icon = resourcePath;
                    ApplyCrop(actor, StorySpriteResolver.GetDefaultIconCrop(sprite));
                }
            }

            MarkUnsaved();
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = "导入图片失败：" + exception.Message;
            return false;
        }
    }

    public bool AdjustNpcActorCrop(string actorId, float moveX, float moveY, float zoomDelta, out string error)
    {
        if (!TryGetNpcActor(actorId, out StoryActorDocument actor, out error))
            return false;
        if (!actor.usesPortraitIcon || string.IsNullOrWhiteSpace(actor.sprite))
        {
            error = "当前角色没有使用立绘裁剪头像。";
            return false;
        }

        Rect crop = actor.normalizedIconCrop;
        Vector2 center = crop.center + new Vector2(moveX * crop.width, moveY * crop.height);
        float scale = Mathf.Clamp(1f + zoomDelta, .5f, 1.5f);
        crop.width = Mathf.Clamp(crop.width * scale, .05f, 1f);
        crop.height = Mathf.Clamp(crop.height * scale, .05f, 1f);
        crop.x = center.x - crop.width * .5f;
        crop.y = center.y - crop.height * .5f;
        ApplyCrop(actor, crop);
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool DeleteNpcActor(string actorId, out string error)
    {
        if (!TryGetNpcActor(actorId, out StoryActorDocument actor, out error))
            return false;

        bool referenced = (SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>()).Any(node => node != null
            && ((node.actorReferences ?? Array.Empty<StoryActorReferenceDocument>()).Any(reference => reference != null
                    && string.Equals(reference.actorId, actorId, StringComparison.OrdinalIgnoreCase))
                || (node.scenes ?? Array.Empty<StorySceneDocument>()).Any(scene => scene != null
                    && (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>()).Any(layout => layout != null
                        && string.Equals(layout.actorId, actorId, StringComparison.OrdinalIgnoreCase)))
                || (node.commands ?? Array.Empty<StoryCommandDocument>()).Any(command => command != null
                    && string.Equals(command.actor, actorId, StringComparison.OrdinalIgnoreCase))));
        if (referenced)
        {
            error = "该角色已被剧情点、场景或对白引用，请先移除相关引用。";
            return false;
        }

        SelectedDocument.actors = (SelectedDocument.actors ?? Array.Empty<StoryActorDocument>())
            .Where(value => value != null && value != actor)
            .ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    private bool TryGetNpcActor(string actorId, out StoryActorDocument actor, out string error)
    {
        actor = (SelectedDocument?.actors ?? Array.Empty<StoryActorDocument>())
            .FirstOrDefault(value => value != null
                && string.Equals(value.id, actorId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(value.actorType, "npc", StringComparison.OrdinalIgnoreCase));
        if (actor == null)
        {
            error = "找不到要编辑的剧本角色。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void ApplyCrop(StoryActorDocument actor, Rect crop)
    {
        crop = StorySpriteResolver.NormalizeCrop(crop.x, crop.y, crop.width, crop.height);
        actor.iconCropX = crop.x;
        actor.iconCropY = crop.y;
        actor.iconCropWidth = crop.width;
        actor.iconCropHeight = crop.height;
    }

    private static string MakeSafePathSegment(string value, string fallback)
    {
        string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            result = result.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private bool TryGetStoryScene(string sceneId, out StorySceneResourceDocument scene, out string error)
    {
        scene = (SelectedDocument?.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
            .FirstOrDefault(value => value != null
                && string.Equals(value.id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (scene == null)
        {
            error = "找不到要编辑的自制场景。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private bool WriteStoryAsset(string resourceId, string category, string fileName, byte[] bytes,
        string resourceKind, Action<string> apply, out string error)
    {
        try
        {
            string storyId = MakeSafePathSegment(SelectedDocument.id, "story");
            string safeResourceId = MakeSafePathSegment(resourceId, "resource");
            WorkshopStoryStorageKind storageKind = SelectedStory.storageKind;
            string relativePath = Path.Combine(category, safeResourceId, fileName).Replace('\\', '/');
            if (!repository.TryGetOwnedAssetPaths(storageKind, storyId, relativePath,
                    out string absolutePath, out string resourcePath, out error))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, bytes);
            if (storageKind == WorkshopStoryStorageKind.Source)
                resourcePath = Path.ChangeExtension(resourcePath, null)?.Replace('\\', '/');
            ImportSourceAssetIfNeeded(storageKind, absolutePath);
            RegisterStoryResource(resourcePath, resourceKind);
            apply?.Invoke(resourcePath);
            MarkUnsaved();
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = "导入剧本资源失败：" + exception.Message;
            return false;
        }
    }

    private void RegisterStoryResource(string path, string kind)
    {
        List<StoryResourceDefinition> resources = (SelectedDocument.resourceDefinitions
            ?? Array.Empty<StoryResourceDefinition>()).Where(value => value != null).ToList();
        StoryResourceDefinition resource = resources.FirstOrDefault(value =>
            string.Equals(value.path, path, StringComparison.OrdinalIgnoreCase));
        if (resource == null)
        {
            resource = new StoryResourceDefinition { path = path };
            resources.Add(resource);
        }
        resource.kind = kind;
        resource.source = SelectedStory.storageKind == WorkshopStoryStorageKind.Source
            ? "builtin"
            : "story";
        SelectedDocument.resourceDefinitions = resources.ToArray();
    }

    private void RemoveStoryResource(string path, bool deleteFile, string excludedSceneId = null)
    {
        if (string.IsNullOrWhiteSpace(path) || SelectedDocument == null)
            return;

        bool stillReferenced = (SelectedDocument.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
            .Where(scene => scene != null
                && !string.Equals(scene.id, excludedSceneId, StringComparison.OrdinalIgnoreCase))
            .Any(scene => string.Equals(scene.backgroundResourcePath, path, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene.defaultBgmResourcePath, path, StringComparison.OrdinalIgnoreCase));
        if (stillReferenced)
            return;

        SelectedDocument.resourceDefinitions = (SelectedDocument.resourceDefinitions
            ?? Array.Empty<StoryResourceDefinition>())
            .Where(value => value != null
                && !string.Equals(value.path, path, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (!deleteFile)
            return;

        string normalized = path.Replace('\\', '/').TrimStart('/');
        string storyId = MakeSafePathSegment(SelectedDocument.id, "story");
        WorkshopStoryStorageKind storageKind = SelectedStory.storageKind;
        string ownedPrefix = repository.GetOwnedAssetResourcePrefix(storageKind, storyId);
        if (!normalized.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string relativePath = normalized.Substring(ownedPrefix.Length);
            if (!repository.TryGetOwnedAssetPaths(storageKind, storyId, relativePath,
                    out string absolutePath, out _, out _))
            {
                return;
            }

            string existingPath = ResolveOwnedAssetFile(absolutePath);
            if (!string.IsNullOrEmpty(existingPath))
                DeleteOwnedAssetFile(storageKind, existingPath);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("清理旧剧本资源失败：" + exception.Message);
        }
    }

    private static void CacheStorySprite(string resourcePath, Sprite sprite)
    {
        if (ResourceManager.instance == null || sprite == null || string.IsNullOrWhiteSpace(resourcePath))
            return;

        string cachePath = Path.ChangeExtension(resourcePath, null)?.Replace('\\', '/');
        if (cachePath.TryTrimStart("Builtin/", out string builtinPath))
            cachePath = "Resources/" + builtinPath;
        ResourceManager.instance.Set(cachePath, sprite);
    }

    private static void ImportSourceAssetIfNeeded(
        WorkshopStoryStorageKind storageKind,
        string absolutePath)
    {
#if UNITY_EDITOR
        if (storageKind != WorkshopStoryStorageKind.Source || string.IsNullOrWhiteSpace(absolutePath))
            return;

        string normalizedAssetsPath = Path.GetFullPath(Application.dataPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string normalizedAbsolutePath = Path.GetFullPath(absolutePath);
        if (!normalizedAbsolutePath.StartsWith(normalizedAssetsPath, StringComparison.OrdinalIgnoreCase))
            return;

        string assetPath = "Assets/" + normalizedAbsolutePath.Substring(normalizedAssetsPath.Length)
            .Replace('\\', '/');
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        if (string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase)
            && AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter)
        {
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.spriteImportMode = SpriteImportMode.Single;
            textureImporter.alphaIsTransparency = true;
            textureImporter.mipmapEnabled = false;
            textureImporter.SaveAndReimport();
        }
#endif
    }

    private static string ResolveOwnedAssetFile(string path)
    {
        if (File.Exists(path))
            return path;

        foreach (string extension in new[] { ".png", ".mp3", ".wav", ".ogg" })
        {
            if (File.Exists(path + extension))
                return path + extension;
        }
        return string.Empty;
    }

    private static void DeleteOwnedAssetFile(
        WorkshopStoryStorageKind storageKind,
        string absolutePath)
    {
#if UNITY_EDITOR
        if (storageKind == WorkshopStoryStorageKind.Source)
        {
            string normalizedAssetsPath = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedAbsolutePath = Path.GetFullPath(absolutePath);
            if (normalizedAbsolutePath.StartsWith(normalizedAssetsPath, StringComparison.OrdinalIgnoreCase))
            {
                string assetPath = "Assets/" + normalizedAbsolutePath.Substring(normalizedAssetsPath.Length)
                    .Replace('\\', '/');
                AssetDatabase.DeleteAsset(assetPath);
                return;
            }
        }
#endif
        File.Delete(absolutePath);
    }

    public bool CreateNode(out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        StoryNodeDocument previousLastSequence = GetSequenceNodes().LastOrDefault();
        if (previousLastSequence != null)
        {
            previousLastSequence.transitions = (previousLastSequence.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
                .Where(transition => transition == null
                    || !transition.isDefault
                    || !transition.isEnd
                    || !transition.isAutoGenerated)
                .ToArray();
        }

        string nodeId = CreateAvailableNodeId();
        StoryNodeDocument node = StoryDocumentFactory.CreateDraftPoint(nodeId, true);
        SelectedDocument.nodes = (SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(value => value != null)
            .Append(node)
            .ToArray();
        SelectedNode = node;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool RenameSelectedNode(string displayName, out string error)
    {
        if (SelectedNode == null)
        {
            error = "请先选择要重命名的剧情点。";
            return false;
        }

        SelectedNode.displayName = string.IsNullOrWhiteSpace(displayName)
            ? SelectedNode.id
            : displayName.Trim();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool SetSelectedNodeAsEntry(out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        if (SelectedNode == null)
        {
            error = "请先选择要设为入口的剧情点。";
            return false;
        }

        if (SelectedNode.isBranch)
        {
            error = "branch node cannot be used as the entry point";
            return false;
        }

        SelectedDocument.entry = SelectedNode.id;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool CopySelectedNode(out string error)
    {
        if (!TryGetSelectedNode(out error))
            return false;

        StoryNodeDocument source = SelectedNode;
        string newNodeId = CreateAvailableNodeId();
        StoryNodeDocument copy = JsonUtility.FromJson<StoryNodeDocument>(JsonUtility.ToJson(source));
        if (copy == null)
        {
            error = "Unable to copy the selected story point";
            return false;
        }

        Dictionary<string, string> sceneIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> commandIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> choiceIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> optionIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> propIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        copy.id = newNodeId;
        copy.displayName = (string.IsNullOrWhiteSpace(source.displayName) ? source.id : source.displayName) + "（副本）";
        copy.flowRole = "branch";
        copy.fallbackNodeId = GetDefaultFlowTargetForCopy(source);
        copy.endTeleportMapId = 0;

        RemapCopiedScenes(source, copy, newNodeId, sceneIdMap, propIdMap);
        RemapCopiedCommands(source, copy, newNodeId, sceneIdMap, commandIdMap, choiceIdMap, optionIdMap, propIdMap);
        RemapCopiedTransitions(source, copy, newNodeId, commandIdMap, choiceIdMap, optionIdMap);

        StoryNodeDocument[] nodes = (SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null)
            .ToArray();
        int sourceIndex = Array.IndexOf(nodes, source);
        List<StoryNodeDocument> updatedNodes = nodes.ToList();
        if (sourceIndex < 0)
            updatedNodes.Add(copy);
        else
            updatedNodes.Insert(sourceIndex + 1, copy);

        SelectedDocument.nodes = updatedNodes.ToArray();
        SelectedNode = copy;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool DeleteSelectedNode(out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        if (SelectedNode == null)
        {
            error = "请先选择要删除的剧情点。";
            return false;
        }

        StoryNodeDocument[] nodes = (SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null)
            .ToArray();
        if (nodes.Length <= 1)
        {
            error = "剧本至少要保留一个入口剧情点。";
            return false;
        }

        if (string.Equals(SelectedDocument.entry, SelectedNode.id, StringComparison.OrdinalIgnoreCase))
        {
            error = "当前剧情点是入口；请先将其他剧情点设为入口。";
            return false;
        }

        string nodeId = SelectedNode.id;
        List<string> blockingReferences = GetBlockingNodeReferences(nodeId);
        if (blockingReferences.Count > 0)
        {
            error = "以下显式分支仍指向该剧情点，请先在对应剧情点中处理：\n"
                + string.Join("\n", blockingReferences);
            return false;
        }

        StoryNodeDocument[] remainingNodes = nodes.Where(node => node != SelectedNode).ToArray();
        StoryNodeDocument replacementEnding = null;
        if (!remainingNodes.Any(node => node != null && node.isEnding))
        {
            replacementEnding = GetSequenceNodes()
                .Where(node => node != SelectedNode && CanBecomeEndingAfterDeletion(node, nodeId))
                .LastOrDefault()
                ?? remainingNodes.LastOrDefault(node => CanBecomeEndingAfterDeletion(node, nodeId));
            if (replacementEnding == null)
            {
                error = "删除后剧本将没有带“结束”标记的剧情点，请先将其他剧情点的后续走向设置为结束。";
                return false;
            }
        }

        string successorNodeId = GetDefaultFlowTargetForRemoval(SelectedNode);
        RewireAutomaticReferences(nodeId, successorNodeId);
        SelectedDocument.nodes = remainingNodes;
        if (replacementEnding != null && !replacementEnding.isEnding)
        {
            replacementEnding.transitions = (replacementEnding.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
                .Where(transition => transition != null && !transition.isDefault)
                .Append(StoryDocumentFactory.CreateAutoEndTransition(replacementEnding.id))
                .ToArray();
        }
        SelectedNode = SelectedDocument.nodes.FirstOrDefault(node => node != null && node.id == SelectedDocument.entry)
            ?? SelectedDocument.nodes.FirstOrDefault(node => node != null);
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool AddSelectedNodeDefaultTransition(out string transitionId, out string error)
    {
        transitionId = string.Empty;
        if (!TryGetSelectedNode(out error))
            return false;

        if ((SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>()).Any(value => value != null && value.isDefault))
        {
            error = "当前剧情点已有默认后续连接。";
            return false;
        }

        string defaultTargetId = GetDefaultFlowTargetForCopy(SelectedNode);
        StoryNodeDocument target = (SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .FirstOrDefault(node => node != null
                && !string.Equals(node.id, SelectedNode.id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(node.id, defaultTargetId, StringComparison.OrdinalIgnoreCase));

        StoryNodeTransitionDocument transition = new StoryNodeTransitionDocument
        {
            transitionId = CreateAvailableTransitionId(),
            targetType = target == null ? "end" : "node",
            targetNodeId = target?.id,
            isDefault = true,
        };
        AppendTransition(transition);
        transitionId = transition.transitionId;
        error = string.Empty;
        return true;
    }

    public bool AddSelectedNodeChoiceTransition(out string transitionId, out string error)
    {
        transitionId = string.Empty;
        if (!TryGetSelectedNode(out error))
            return false;

        WorkshopStoryChoiceOption choice = GetSelectedNodeChoiceOptions().FirstOrDefault();
        if (choice == null)
        {
            error = "当前剧情点还没有选项；请先在可视化编辑页面添加选项。";
            return false;
        }

        StoryNodeDocument target = GetSuggestedTransitionTarget();
        if (target == null)
        {
            error = "请先选择有效的剧情点。";
            return false;
        }

        StoryNodeTransitionDocument transition = new StoryNodeTransitionDocument
        {
            transitionId = CreateAvailableTransitionId(),
            targetType = "node",
            targetNodeId = target.id,
            condition = new ConditionGroupDocument
            {
                clauses = new[]
                {
                    new StoryConditionClauseDocument
                    {
                        conditions = new[] { CreateChoiceCondition(choice) },
                    },
                },
            },
        };
        AppendTransition(transition);
        transitionId = transition.transitionId;
        error = string.Empty;
        return true;
    }

    public bool UpdateSelectedNodeTransitionTarget(string transitionId, string targetNodeId, out string error)
    {
        return UpdateSelectedNodeTransitionTarget(transitionId, "node", targetNodeId, out error);
    }

    public bool UpdateSelectedNodeTransitionTarget(
        string transitionId,
        string targetType,
        string targetNodeId,
        out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;

        if (string.Equals(targetType, "end", StringComparison.OrdinalIgnoreCase))
        {
            transition.targetType = "end";
            transition.targetNodeId = null;
            transition.isAutoGenerated = false;
            MarkUnsaved();
            error = string.Empty;
            return true;
        }

        if (!string.Equals(targetType, "node", StringComparison.OrdinalIgnoreCase))
        {
            error = "连接目标类型只支持剧情点或结束剧情。";
            return false;
        }

        if (!(SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Any(node => node != null && string.Equals(node.id, targetNodeId, StringComparison.OrdinalIgnoreCase)))
        {
            error = "请选择有效的目标剧情点。";
            return false;
        }

        if (transition.isDefault
            && string.Equals(SelectedNode?.id, targetNodeId, StringComparison.OrdinalIgnoreCase))
        {
            error = "默认后续不能重新进入当前剧情点；请改用条件分支或结束剧情。";
            return false;
        }

        transition.targetType = "node";
        transition.targetNodeId = targetNodeId;
        transition.isAutoGenerated = false;
        if (SelectedNode != null && !SelectedNode.isEnding)
            SelectedNode.endTeleportMapId = 0;
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool UpdateSelectedNodeEndTeleport(string transitionId, int mapId, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;
        if (!transition.isEnd || SelectedNode == null || !SelectedNode.isEnding)
        {
            error = "只有结束节点的结束连接可以配置结束后传送。";
            return false;
        }
        if (mapId != 0 && !StoryResourceValidator.TryLoadMapDefinition(mapId, out _, out string mapError))
        {
            error = "请选择有效的本体或当前 Mod 地图 XML。" + (string.IsNullOrWhiteSpace(mapError) ? string.Empty : "\n" + mapError);
            return false;
        }

        SelectedNode.endTeleportMapId = mapId;
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool UpdateSelectedNodeTransitionChoice(string transitionId, string commandId, string choiceId, string optionId, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;

        if (transition.isDefault)
        {
            error = "默认连接不需要选择触发选项。";
            return false;
        }

        WorkshopStoryChoiceOption selected = GetSelectedNodeChoiceOptions().FirstOrDefault(value => value != null
            && string.Equals(value.commandId, commandId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.choiceId, choiceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.optionId, optionId, StringComparison.OrdinalIgnoreCase));
        if (selected == null)
        {
            error = "请选择当前剧情点中的有效选项。";
            return false;
        }

        transition.condition = new ConditionGroupDocument
        {
            clauses = new[]
            {
                new StoryConditionClauseDocument
                {
                    conditions = new[] { CreateChoiceCondition(selected) },
                },
            },
        };
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool AddSelectedNodeTransitionCondition(string transitionId, string connector, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;
        if (transition.isDefault)
        {
            error = "默认后续不需要条件。";
            return false;
        }

        WorkshopStoryChoiceOption[] choices = GetSelectedNodeChoiceOptions();
        if (choices.Length == 0)
        {
            error = "当前剧情点没有可用选项。";
            return false;
        }

        List<StoryConditionClauseDocument> clauses = GetConditionClauses(transition.condition);
        bool addOrClause = clauses.Count == 0 || string.Equals(connector, "OR", StringComparison.OrdinalIgnoreCase);
        StoryConditionClauseDocument targetClause = addOrClause ? null : clauses[clauses.Count - 1];
        HashSet<string> usedChoiceIds = new HashSet<string>(
            (targetClause?.conditions ?? Array.Empty<StoryConditionDocument>())
                .Where(condition => condition != null && !condition.negated)
                .Select(condition => condition.choiceId),
            StringComparer.OrdinalIgnoreCase);
        WorkshopStoryChoiceOption choice = choices.FirstOrDefault(option => !usedChoiceIds.Contains(option.choiceId))
            ?? choices[0];
        StoryConditionDocument condition = CreateChoiceCondition(choice);
        if (addOrClause)
        {
            clauses.Add(new StoryConditionClauseDocument { conditions = new[] { condition } });
        }
        else
        {
            StoryConditionClauseDocument clause = clauses[clauses.Count - 1];
            clause.conditions = (clause.conditions ?? Array.Empty<StoryConditionDocument>()).Append(condition).ToArray();
        }

        transition.condition = new ConditionGroupDocument { clauses = clauses.ToArray() };
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool UpdateSelectedNodeTransitionCondition(
        string transitionId,
        int clauseIndex,
        int conditionIndex,
        string commandId,
        string choiceId,
        string optionId,
        out string error)
    {
        if (!TryGetSelectedTransitionCondition(transitionId, clauseIndex, conditionIndex,
                out StoryNodeTransitionDocument transition, out StoryConditionDocument condition, out error))
        {
            return false;
        }

        WorkshopStoryChoiceOption option = GetSelectedNodeChoiceOptions().FirstOrDefault(value => value != null
            && string.Equals(value.commandId, commandId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.choiceId, choiceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.optionId, optionId, StringComparison.OrdinalIgnoreCase));
        if (option == null)
        {
            error = "请选择当前剧情点中的有效选项。";
            return false;
        }

        bool negated = condition.negated;
        StoryConditionDocument replacement = CreateChoiceCondition(option);
        replacement.negated = negated;
        transition.condition.clauses[clauseIndex].conditions[conditionIndex] = replacement;
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool ToggleSelectedNodeTransitionConditionNegated(
        string transitionId,
        int clauseIndex,
        int conditionIndex,
        out string error)
    {
        if (!TryGetSelectedTransitionCondition(transitionId, clauseIndex, conditionIndex,
                out _, out StoryConditionDocument condition, out error))
        {
            return false;
        }

        condition.negated = !condition.negated;
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool RemoveSelectedNodeTransitionCondition(
        string transitionId,
        int clauseIndex,
        int conditionIndex,
        out string error)
    {
        if (!TryGetSelectedTransitionCondition(transitionId, clauseIndex, conditionIndex,
                out StoryNodeTransitionDocument transition, out _, out error))
        {
            return false;
        }

        List<StoryConditionClauseDocument> clauses = GetConditionClauses(transition.condition);
        int totalConditions = clauses.Sum(clause => clause?.conditions?.Length ?? 0);
        if (totalConditions <= 1)
        {
            error = "一条分支规则至少需要保留一个条件。";
            return false;
        }

        List<StoryConditionDocument> conditions = clauses[clauseIndex].conditions.ToList();
        conditions.RemoveAt(conditionIndex);
        if (conditions.Count == 0)
            clauses.RemoveAt(clauseIndex);
        else
            clauses[clauseIndex].conditions = conditions.ToArray();
        transition.condition.clauses = clauses.ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public string GetSelectedNodeDefaultFlowTarget()
    {
        return GetDefaultFlowTargetForCopy(SelectedNode);
    }

    public bool MoveSelectedNodeTransition(string transitionId, bool moveDown, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;

        List<StoryNodeTransitionDocument> transitions = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Where(value => value != null)
            .ToList();
        int index = transitions.IndexOf(transition);
        int targetIndex = index + (moveDown ? 1 : -1);
        if (targetIndex < 0 || targetIndex >= transitions.Count)
        {
            error = "已经位于连接列表的边界。";
            return false;
        }

        if (transition.isDefault || transitions[targetIndex].isDefault)
        {
            error = "默认连接始终位于最后，不能调整优先级。";
            return false;
        }

        StoryNodeTransitionDocument temporary = transitions[index];
        transitions[index] = transitions[targetIndex];
        transitions[targetIndex] = temporary;
        SelectedNode.transitions = transitions.ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool RemoveSelectedNodeTransition(string transitionId, out string error)
    {
        if (!TryGetSelectedTransition(transitionId, out StoryNodeTransitionDocument transition, out error))
            return false;

        SelectedNode.transitions = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Where(value => value != null && value != transition)
            .ToArray();
        MarkUnsaved();
        error = string.Empty;
        return true;
    }

    public bool CreateDraft(out string error)
    {
        return CreateDraft(repository.defaultStorageKind, out error);
    }

    public bool CreateDraft(WorkshopStoryStorageKind storageKind, out string error)
    {
        if (HasUnsavedChanges)
        {
            error = "当前剧本尚未保存，请先保存后再新建。";
            return false;
        }

        if (!repository.TryCreateDraft(storageKind, out WorkshopStorySummary summary, out error))
            return false;

        SelectedStory = summary;
        SelectedDocument = null;
        SelectedNode = null;
        HasUnsavedChanges = false;
        return Reload(out error);
    }

    public bool CopySelectedStory(out string error)
    {
        if (SelectedDocument == null)
        {
            error = "请先选择要复制的剧本。";
            return false;
        }
        if (HasUnsavedChanges)
        {
            error = "当前剧本尚未保存，请先保存后再复制。";
            return false;
        }

        if (!repository.TryCopyDraft(SelectedStory.path, SelectedDocument,
                out WorkshopStorySummary summary, out error))
            return false;

        SelectedStory = summary;
        SelectedDocument = null;
        SelectedNode = null;
        HasUnsavedChanges = false;
        return Reload(out error);
    }

    public bool SaveSelected(out string error)
    {
        return SaveSelectedDraft(out error);
    }

    public bool SaveSelectedDraft(out string error)
    {
        if (SelectedStory == null || SelectedDocument == null)
        {
            error = "请先选择要保存的剧本。";
            return false;
        }

        string selectedNodeId = SelectedNode?.id;
        bool savedModDraft = SelectedStory.storageKind == WorkshopStoryStorageKind.Mod;
        if (!repository.TrySaveDraft(SelectedStory.path, SelectedDocument, out error))
            return false;

        if (savedModDraft && Database.instance != null)
        {
            Database.instance.ReloadStoryMod();
            if (Player.instance != null)
                Mission.VersionUpdate();
        }

        bool reloaded = Reload(out error);
        if (reloaded)
        {
            if (!string.IsNullOrWhiteSpace(selectedNodeId))
                SelectNode(selectedNodeId);
            HasUnsavedChanges = false;
        }
        return reloaded;
    }

    public bool SaveSelectedForRuntime(out bool runtimeReady, out string message)
    {
        return PublishSelectedMod(out runtimeReady, out message);
    }

    public bool PublishSelectedMod(out bool runtimeReady, out string message)
    {
        runtimeReady = false;
        if (SelectedStory == null || SelectedDocument == null)
        {
            message = "请先选择要保存的剧本。";
            return false;
        }

        if (SelectedStory.storageKind != WorkshopStoryStorageKind.Mod)
        {
            message = "源码母稿只保存编辑进度，请使用“导出为”生成源码任务。";
            return false;
        }

        string selectedNodeId = SelectedNode?.id;
        if (!repository.TrySaveForRuntime(
                SelectedStory.path, SelectedDocument, out runtimeReady, out string saveMessage))
        {
            message = saveMessage;
            return false;
        }

        if (runtimeReady && Database.instance != null)
        {
            Database.instance.ReloadStoryMod();
            if (Player.instance != null)
                Mission.VersionUpdate();
        }

        if (!Reload(out message))
            return false;

        if (!string.IsNullOrWhiteSpace(selectedNodeId))
            SelectNode(selectedNodeId);
        HasUnsavedChanges = false;
        message = saveMessage;
        return true;
    }

    public IReadOnlyList<WorkshopStorySourceRewardOption> GetSourceRewardOptions(string filter)
    {
        string query = (filter ?? string.Empty).Trim();
        return ItemInfo.database
            .Where(item => item != null && item.id != 0 && item.getId != 0 && !ItemInfo.IsMod(item.id))
            .Where(item => string.IsNullOrWhiteSpace(query)
                || item.id.ToString().Contains(query)
                || (item.name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(item => item.id)
            .Select(item => new WorkshopStorySourceRewardOption
            {
                itemId = item.id,
                name = item.name,
            })
            .ToArray();
    }

    public bool ExportSelectedToSource(
        WorkshopStorySourceExportRequest request,
        out WorkshopStorySourceExportResult result,
        out string error)
    {
        result = null;
        if (!TryGetSelectedDocument(out error))
            return false;
        if (HasUnsavedChanges)
        {
            error = "当前剧本尚未保存，请先保存编辑进度后再导出。";
            return false;
        }
        if (SelectedStory.storageKind == WorkshopStoryStorageKind.Mod && SelectedDocument.isDraft)
        {
            error = "当前剧本尚未载入 Mod，请先解决运行校验问题并保存到 Mod 后再导出。";
            return false;
        }
        if (!sourceExporter.TryExport(SelectedDocument, request, out result, out error))
            return false;

        if (result?.binding != null)
        {
            string selectedNodeId = SelectedNode?.id;
            SelectedDocument.sourceExport = result.binding;
            bool saved = SelectedStory.storageKind == WorkshopStoryStorageKind.Source
                ? repository.TrySaveDraft(SelectedStory.path, SelectedDocument, out error)
                : repository.TrySave(SelectedStory.path, SelectedDocument, out error);
            if (!saved)
                return false;
            if (!Reload(out error))
                return false;
            if (!string.IsNullOrWhiteSpace(selectedNodeId))
                SelectNode(selectedNodeId);
        }
        HasUnsavedChanges = false;
        return true;
    }

    public bool DeleteSelected(out string error)
    {
        if (SelectedStory == null)
        {
            error = "请先选择要删除的剧本。";
            return false;
        }

        bool deletedModStory = SelectedStory.storageKind == WorkshopStoryStorageKind.Mod;
        int deletedMissionId = SelectedDocument?.mission?.id ?? SelectedStory.missionId;
        if (!repository.TryDelete(SelectedStory.path, out error))
            return false;

        if (deletedModStory && Database.instance != null)
            Database.instance.ReloadStoryMod();
        if (deletedModStory && deletedMissionId < 0 && Player.instance != null)
        {
            Player.instance.gameData?.missionStorage?.RemoveAll(mission => mission?.id == deletedMissionId);
            if (Player.instance.currentMissionId == deletedMissionId)
                Player.instance.currentMissionId = 0;
        }

        SelectedStory = null;
        SelectedDocument = null;
        SelectedNode = null;
        HasUnsavedChanges = false;
        return Reload(out error);
    }

    private bool TryGetSelectedDocument(out string error)
    {
        if (SelectedDocument == null)
        {
            error = "请先选择剧本。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryGetSelectedNode(out string error)
    {
        if (!TryGetSelectedDocument(out error))
            return false;

        if (SelectedNode == null)
        {
            error = "请先选择一个剧情点。";
            return false;
        }

        return true;
    }

    private bool TryGetSelectedTransition(string transitionId, out StoryNodeTransitionDocument transition, out string error)
    {
        transition = null;
        if (!TryGetSelectedNode(out error))
            return false;

        transition = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .FirstOrDefault(value => value != null && string.Equals(value.transitionId, transitionId, StringComparison.OrdinalIgnoreCase));
        if (transition == null)
        {
            error = "未找到要编辑的后续连接。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private WorkshopStoryChoiceOption[] GetSelectedNodeChoiceOptions()
    {
        if (SelectedNode == null)
            return Array.Empty<WorkshopStoryChoiceOption>();

        return (SelectedNode.commands ?? Array.Empty<StoryCommandDocument>())
            .Where(command => command != null && string.Equals(command.type, "choice", StringComparison.OrdinalIgnoreCase))
            .SelectMany(command => (command.choices ?? Array.Empty<StoryChoiceDocument>())
                .Where(choice => choice != null && !string.IsNullOrWhiteSpace(choice.optionId))
                .Select(choice => new WorkshopStoryChoiceOption
                {
                    pointId = SelectedNode.id,
                    commandId = command.commandId,
                    choiceId = command.choiceId,
                    optionId = choice.optionId,
                    question = command.text,
                    text = choice.text,
                }))
            .Concat((SelectedNode.commands ?? Array.Empty<StoryCommandDocument>())
                .Where(command => command != null && string.Equals(command.type, "battle", StringComparison.OrdinalIgnoreCase))
                .SelectMany(command => new[]
                {
                    new WorkshopStoryChoiceOption
                    {
                        conditionType = "battleResult", pointId = SelectedNode.id,
                        commandId = command.commandId, optionId = "win", text = "战斗胜利",
                    },
                    new WorkshopStoryChoiceOption
                    {
                        conditionType = "battleResult", pointId = SelectedNode.id,
                        commandId = command.commandId, optionId = "lose", text = "战斗失败",
                    },
                }))
            .ToArray();
    }

    private StoryNodeDocument GetSuggestedTransitionTarget()
    {
        StoryNodeDocument[] nodes = (SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null)
            .ToArray();
        return nodes.FirstOrDefault(node => !string.Equals(node.id, SelectedNode?.id, StringComparison.OrdinalIgnoreCase))
            ?? nodes.FirstOrDefault(node => string.Equals(node.id, SelectedNode?.id, StringComparison.OrdinalIgnoreCase));
    }

    private string CreateAvailableTransitionId()
    {
        int index = 1;
        string transitionId;
        do
        {
            transitionId = "transition_" + index++;
        }
        while ((SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Any(value => value != null && string.Equals(value.transitionId, transitionId, StringComparison.OrdinalIgnoreCase)));

        return transitionId;
    }

    private void AppendTransition(StoryNodeTransitionDocument transition)
    {
        List<StoryNodeTransitionDocument> transitions = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Where(value => value != null && !value.isDefault)
            .ToList();
        StoryNodeTransitionDocument defaultTransition = (SelectedNode.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .FirstOrDefault(value => value != null && value.isDefault);
        transitions.Add(transition);
        if (defaultTransition != null)
            transitions.Add(defaultTransition);
        SelectedNode.transitions = transitions.ToArray();
        if (!SelectedNode.isEnding)
            SelectedNode.endTeleportMapId = 0;
        MarkUnsaved();
    }

    private void MarkUnsaved()
    {
        HasUnsavedChanges = true;
    }

    private string CreateAvailableNodeId()
    {
        int index = 1;
        string nodeId;
        do
        {
            nodeId = "point_" + index++;
        }
        while ((SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .Any(node => node != null && string.Equals(node.id, nodeId, StringComparison.OrdinalIgnoreCase)));

        return nodeId;
    }

    private string GetDefaultFlowTargetForCopy(StoryNodeDocument source)
    {
        StoryNodeTransitionDocument explicitDefault = (source?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .FirstOrDefault(transition => transition != null
                && transition.isDefault
                && (transition.isEnd || !string.IsNullOrWhiteSpace(transition.targetNodeId)));
        if (explicitDefault != null)
            return explicitDefault.isEnd ? string.Empty : explicitDefault.targetNodeId;

        if (source != null && source.isBranch)
            return source.fallbackNodeId;

        List<StoryNodeDocument> sequenceNodes = GetSequenceNodes();

        int sourceIndex = sequenceNodes.IndexOf(source);
        return sourceIndex >= 0 && sourceIndex + 1 < sequenceNodes.Count
            ? sequenceNodes[sourceIndex + 1].id
            : string.Empty;
    }

    private List<StoryNodeDocument> GetSequenceNodes()
    {
        List<StoryNodeDocument> sequenceNodes = (SelectedDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null && !node.isBranch)
            .ToList();
        StoryNodeDocument entryNode = sequenceNodes.FirstOrDefault(node => string.Equals(
            node.id, SelectedDocument?.entry, StringComparison.OrdinalIgnoreCase));
        if (entryNode != null)
        {
            sequenceNodes.Remove(entryNode);
            sequenceNodes.Insert(0, entryNode);
        }
        return sequenceNodes;
    }

    private static void RemapCopiedScenes(
        StoryNodeDocument source,
        StoryNodeDocument copy,
        string newNodeId,
        Dictionary<string, string> sceneIdMap,
        Dictionary<string, string> propIdMap)
    {
        StorySceneDocument[] sourceScenes = source?.scenes ?? Array.Empty<StorySceneDocument>();
        StorySceneDocument[] copiedScenes = copy?.scenes ?? Array.Empty<StorySceneDocument>();
        for (int i = 0; i < copiedScenes.Length; i++)
        {
            StorySceneDocument copiedScene = copiedScenes[i];
            if (copiedScene == null)
                continue;

            string oldId = i < sourceScenes.Length ? sourceScenes[i]?.id : null;
            string newId = newNodeId + ":scene:" + (i + 1);
            if (!string.IsNullOrWhiteSpace(oldId))
                sceneIdMap[oldId] = newId;
            copiedScene.id = newId;

            StoryScenePropDocument[] sourceProps = i < sourceScenes.Length
                ? sourceScenes[i]?.props ?? Array.Empty<StoryScenePropDocument>()
                : Array.Empty<StoryScenePropDocument>();
            StoryScenePropDocument[] copiedProps = copiedScene.props ?? Array.Empty<StoryScenePropDocument>();
            for (int propIndex = 0; propIndex < copiedProps.Length; propIndex++)
            {
                StoryScenePropDocument copiedProp = copiedProps[propIndex];
                if (copiedProp == null)
                    continue;
                string oldPropId = propIndex < sourceProps.Length ? sourceProps[propIndex]?.id : copiedProp.id;
                string newPropId = newNodeId + ":prop:" + (i + 1) + ":" + (propIndex + 1);
                if (!string.IsNullOrWhiteSpace(oldPropId) && !string.IsNullOrWhiteSpace(oldId))
                    propIdMap[oldId + "|" + oldPropId] = newPropId;
                copiedProp.id = newPropId;
            }
        }
    }

    private static void RemapCopiedCommands(
        StoryNodeDocument source,
        StoryNodeDocument copy,
        string newNodeId,
        Dictionary<string, string> sceneIdMap,
        Dictionary<string, string> commandIdMap,
        Dictionary<string, string> choiceIdMap,
        Dictionary<string, string> optionIdMap,
        Dictionary<string, string> propIdMap)
    {
        StoryCommandDocument[] sourceCommands = source?.commands ?? Array.Empty<StoryCommandDocument>();
        StoryCommandDocument[] copiedCommands = copy?.commands ?? Array.Empty<StoryCommandDocument>();

        for (int i = 0; i < copiedCommands.Length; i++)
        {
            StoryCommandDocument copiedCommand = copiedCommands[i];
            if (copiedCommand == null)
                continue;

            StoryCommandDocument sourceCommand = i < sourceCommands.Length ? sourceCommands[i] : null;
            string newCommandId = newNodeId + ":command:" + (i + 1);
            if (!string.IsNullOrWhiteSpace(sourceCommand?.commandId))
                commandIdMap[sourceCommand.commandId] = newCommandId;

            if (copiedCommand.choices == null || copiedCommand.choices.Length == 0)
                continue;

            string newChoiceId = newCommandId + ":choice";
            if (!string.IsNullOrWhiteSpace(sourceCommand?.choiceId))
                choiceIdMap[sourceCommand.choiceId] = newChoiceId;
            for (int choiceIndex = 0; choiceIndex < copiedCommand.choices.Length; choiceIndex++)
            {
                StoryChoiceDocument sourceChoice = sourceCommand?.choices != null && choiceIndex < sourceCommand.choices.Length
                    ? sourceCommand.choices[choiceIndex]
                    : null;
                if (!string.IsNullOrWhiteSpace(sourceChoice?.choiceId))
                    choiceIdMap[sourceChoice.choiceId] = newChoiceId;
                if (!string.IsNullOrWhiteSpace(sourceChoice?.optionId))
                    optionIdMap[sourceChoice.optionId] = newChoiceId + ":" + (choiceIndex + 1);
            }
        }

        for (int i = 0; i < copiedCommands.Length; i++)
        {
            StoryCommandDocument copiedCommand = copiedCommands[i];
            if (copiedCommand == null)
                continue;

            StoryCommandDocument sourceCommand = i < sourceCommands.Length ? sourceCommands[i] : null;
            string oldCommandId = sourceCommand?.commandId;
            string newCommandId = newNodeId + ":command:" + (i + 1);
            if (!string.IsNullOrWhiteSpace(oldCommandId))
                commandIdMap[oldCommandId] = newCommandId;
            copiedCommand.commandId = newCommandId;
            copiedCommand.sceneId = RemapId(copiedCommand.sceneId, sceneIdMap);
            string propMapKey = (sourceCommand?.sceneId ?? string.Empty) + "|" + (sourceCommand?.propId ?? string.Empty);
            if (propIdMap.TryGetValue(propMapKey, out string remappedPropId))
                copiedCommand.propId = remappedPropId;
            copiedCommand.target = RemapSelfTarget(copiedCommand.target, source?.id, newNodeId);
            copiedCommand.condition = RemapConditionGroup(copiedCommand.condition, source?.id, newNodeId, commandIdMap, choiceIdMap, optionIdMap);
            copiedCommand.displayCondition = RemapConditionGroup(copiedCommand.displayCondition, source?.id, newNodeId, commandIdMap, choiceIdMap, optionIdMap);

            if (copiedCommand.choices == null || copiedCommand.choices.Length == 0)
                continue;

            string oldChoiceId = sourceCommand?.choiceId;
            string newChoiceId = newCommandId + ":choice";
            if (!string.IsNullOrWhiteSpace(oldChoiceId))
                choiceIdMap[oldChoiceId] = newChoiceId;
            copiedCommand.choiceId = newChoiceId;
            for (int choiceIndex = 0; choiceIndex < copiedCommand.choices.Length; choiceIndex++)
            {
                StoryChoiceDocument choice = copiedCommand.choices[choiceIndex];
                if (choice == null)
                    continue;

                StoryChoiceDocument sourceChoice = sourceCommand?.choices != null && choiceIndex < sourceCommand.choices.Length
                    ? sourceCommand.choices[choiceIndex]
                    : null;
                if (!string.IsNullOrWhiteSpace(sourceChoice?.choiceId))
                    choiceIdMap[sourceChoice.choiceId] = newChoiceId;
                if (!string.IsNullOrWhiteSpace(sourceChoice?.optionId))
                    optionIdMap[sourceChoice.optionId] = newChoiceId + ":" + (choiceIndex + 1);
                choice.choiceId = newChoiceId;
                choice.optionId = newChoiceId + ":" + (choiceIndex + 1);
                choice.target = RemapSelfTarget(choice.target, source?.id, newNodeId);
            }
        }
    }

    private static void RemapCopiedTransitions(
        StoryNodeDocument source,
        StoryNodeDocument copy,
        string newNodeId,
        Dictionary<string, string> commandIdMap,
        Dictionary<string, string> choiceIdMap,
        Dictionary<string, string> optionIdMap)
    {
        StoryNodeTransitionDocument[] copiedTransitions = copy?.transitions ?? Array.Empty<StoryNodeTransitionDocument>();
        List<StoryNodeTransitionDocument> transitions = new List<StoryNodeTransitionDocument>();
        int copiedIndex = 0;
        for (int i = 0; i < copiedTransitions.Length; i++)
        {
            StoryNodeTransitionDocument transition = copiedTransitions[i];
            if (transition == null || transition.isDefault)
                continue;

            transition.transitionId = newNodeId + ":transition:" + (++copiedIndex);
            if (!transition.isEnd)
                transition.targetNodeId = RemapSelfTarget(transition.targetNodeId, source?.id, newNodeId);
            transition.condition = RemapConditionGroup(transition.condition, source?.id, newNodeId, commandIdMap, choiceIdMap, optionIdMap);
            transitions.Add(transition);
        }
        copy.transitions = transitions.ToArray();
    }

    private static ConditionGroupDocument RemapConditionGroup(
        ConditionGroupDocument source,
        string oldNodeId,
        string newNodeId,
        Dictionary<string, string> commandIdMap,
        Dictionary<string, string> choiceIdMap,
        Dictionary<string, string> optionIdMap)
    {
        if (source == null)
            return null;

        return new ConditionGroupDocument
        {
            operatorType = source.operatorType,
            conditions = (source.conditions ?? Array.Empty<StoryConditionDocument>())
                .Select(condition => RemapCondition(condition, oldNodeId, newNodeId, commandIdMap, choiceIdMap, optionIdMap))
                .ToArray(),
            clauses = (source.clauses ?? Array.Empty<StoryConditionClauseDocument>())
                .Select(clause => new StoryConditionClauseDocument
                {
                    conditions = (clause?.conditions ?? Array.Empty<StoryConditionDocument>())
                        .Select(condition => RemapCondition(condition, oldNodeId, newNodeId, commandIdMap, choiceIdMap, optionIdMap))
                        .ToArray(),
                })
                .ToArray(),
        };
    }

    private static StoryConditionDocument RemapCondition(
        StoryConditionDocument source,
        string oldNodeId,
        string newNodeId,
        Dictionary<string, string> commandIdMap,
        Dictionary<string, string> choiceIdMap,
        Dictionary<string, string> optionIdMap)
    {
        if (source == null)
            return null;

        return new StoryConditionDocument
        {
            type = source.type,
            negated = source.negated,
            pointId = !string.IsNullOrWhiteSpace(oldNodeId)
                && string.Equals(source.pointId, oldNodeId, StringComparison.OrdinalIgnoreCase)
                ? newNodeId
                : source.pointId,
            commandId = RemapId(source.commandId, commandIdMap),
            choiceId = RemapId(source.choiceId, choiceIdMap),
            optionId = RemapId(source.optionId, optionIdMap),
            optionSequence = (source.optionSequence ?? Array.Empty<string>())
                .Select(value => RemapId(value, optionIdMap))
                .ToArray(),
            flag = source.flag,
            value = source.value,
            missionId = source.missionId,
            missionState = source.missionState,
        };
    }

    private static string RemapId(string value, Dictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(value) || map == null)
            return value;

        return map.TryGetValue(value, out string mapped) ? mapped : value;
    }

    private static string RemapSelfTarget(string value, string oldNodeId, string newNodeId)
    {
        return !string.IsNullOrWhiteSpace(oldNodeId)
            && string.Equals(value, oldNodeId, StringComparison.OrdinalIgnoreCase)
            ? newNodeId
            : value;
    }

    private string GetDefaultFlowTargetForRemoval(StoryNodeDocument node)
    {
        string targetNodeId = GetDefaultFlowTargetForCopy(node);
        return string.Equals(targetNodeId, node?.id, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : targetNodeId;
    }

    private static bool CanBecomeEndingAfterDeletion(StoryNodeDocument node, string deletedNodeId)
    {
        if (node == null)
            return false;

        bool hasOtherNodeExit = (node.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            .Any(transition => transition != null
                && !transition.isEnd
                && !string.Equals(transition.targetNodeId, deletedNodeId, StringComparison.OrdinalIgnoreCase));
        bool hasOtherJump = (node.commands ?? Array.Empty<StoryCommandDocument>())
            .Any(command => command != null
                && string.Equals(command.type, "jump", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command.target, deletedNodeId, StringComparison.OrdinalIgnoreCase));
        bool hasChoiceTarget = (node.commands ?? Array.Empty<StoryCommandDocument>())
            .SelectMany(command => command?.choices ?? Array.Empty<StoryChoiceDocument>())
            .Any(choice => choice != null && !string.IsNullOrWhiteSpace(choice.target));
        return !hasOtherNodeExit && !hasOtherJump && !hasChoiceTarget;
    }

    private List<string> GetBlockingNodeReferences(string nodeId)
    {
        List<string> references = new List<string>();
        foreach (StoryNodeDocument node in SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node == null || string.Equals(node.id, nodeId, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (StoryCommandDocument command in node?.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command == null)
                    continue;

                if (string.Equals(command.type, "jump", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(command.target, nodeId, StringComparison.OrdinalIgnoreCase)
                    && command.condition != null
                    && command.condition.hasConditions)
                {
                    references.Add(FormatReference(node, "条件跳转 " + command.commandId));
                }

                foreach (StoryChoiceDocument choice in command.choices ?? Array.Empty<StoryChoiceDocument>())
                {
                    if (choice != null && string.Equals(choice.target, nodeId, StringComparison.OrdinalIgnoreCase))
                        references.Add(FormatReference(node, "选项直达目标 " + choice.optionId));
                }
            }

            foreach (StoryNodeTransitionDocument transition in node.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            {
                if (transition != null
                    && !transition.isDefault
                    && string.Equals(transition.targetNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    references.Add(FormatReference(node, "条件连接 " + transition.transitionId));
                }
            }
        }

        return references;
    }

    private void RewireAutomaticReferences(string deletedNodeId, string successorNodeId)
    {
        foreach (StoryNodeDocument node in SelectedDocument.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node == null || string.Equals(node.id, deletedNodeId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(node.fallbackNodeId, deletedNodeId, StringComparison.OrdinalIgnoreCase))
                node.fallbackNodeId = successorNodeId;

            List<StoryNodeTransitionDocument> transitions = (node.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
                .Where(transition => transition != null)
                .ToList();
            for (int index = transitions.Count - 1; index >= 0; index--)
            {
                StoryNodeTransitionDocument transition = transitions[index];
                if (!transition.isDefault
                    || !string.Equals(transition.targetNodeId, deletedNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(successorNodeId))
                {
                    transition.targetType = "end";
                    transition.targetNodeId = null;
                }
                else
                {
                    transition.targetType = "node";
                    transition.targetNodeId = successorNodeId;
                    transition.isAutoGenerated = false;
                }
            }
            node.transitions = transitions.ToArray();

            foreach (StoryCommandDocument command in node.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command == null
                    || !string.Equals(command.type, "jump", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(command.target, deletedNodeId, StringComparison.OrdinalIgnoreCase)
                    || (command.condition != null && command.condition.hasConditions))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(successorNodeId))
                {
                    command.type = "end";
                    command.target = null;
                }
                else
                {
                    command.target = successorNodeId;
                }
            }
        }
    }

    private static string FormatReference(StoryNodeDocument node, string referenceType)
    {
        string displayName = string.IsNullOrWhiteSpace(node?.displayName) ? node?.id : node.displayName;
        return "• " + displayName + "（" + node?.id + "）：" + referenceType;
    }

    private static StoryConditionDocument CreateChoiceCondition(WorkshopStoryChoiceOption choice)
    {
        if (string.Equals(choice?.conditionType, "battleResult", StringComparison.OrdinalIgnoreCase))
        {
            return new StoryConditionDocument
            {
                type = "battleResult",
                pointId = choice.pointId,
                commandId = choice.commandId,
                value = choice.optionId,
            };
        }
        return new StoryConditionDocument
        {
            type = "choiceSelected",
            pointId = choice?.pointId,
            commandId = choice?.commandId,
            choiceId = choice?.choiceId,
            optionId = choice?.optionId,
        };
    }

    private static List<StoryConditionClauseDocument> GetConditionClauses(ConditionGroupDocument group)
    {
        if (group?.clauses != null && group.clauses.Length > 0)
            return group.clauses.Where(clause => clause != null).ToList();

        StoryConditionDocument[] legacyConditions = group?.conditions ?? Array.Empty<StoryConditionDocument>();
        if (legacyConditions.Length == 0)
            return new List<StoryConditionClauseDocument>();

        return string.Equals(group.operatorType, "OR", StringComparison.OrdinalIgnoreCase)
            ? legacyConditions.Select(condition => new StoryConditionClauseDocument { conditions = new[] { condition } }).ToList()
            : new List<StoryConditionClauseDocument>
            {
                new StoryConditionClauseDocument { conditions = legacyConditions },
            };
    }

    private bool TryGetSelectedTransitionCondition(
        string transitionId,
        int clauseIndex,
        int conditionIndex,
        out StoryNodeTransitionDocument transition,
        out StoryConditionDocument condition,
        out string error)
    {
        condition = null;
        if (!TryGetSelectedTransition(transitionId, out transition, out error))
            return false;

        StoryConditionClauseDocument[] clauses = transition.condition?.clauses ?? Array.Empty<StoryConditionClauseDocument>();
        if (clauseIndex < 0 || clauseIndex >= clauses.Length
            || conditionIndex < 0 || conditionIndex >= (clauses[clauseIndex]?.conditions?.Length ?? 0))
        {
            error = "找不到要编辑的分支条件。";
            return false;
        }

        condition = clauses[clauseIndex].conditions[conditionIndex];
        if (condition == null)
        {
            error = "找不到要编辑的分支条件。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class WorkshopStoryChoiceOption
{
    public string conditionType = "choiceSelected";
    public string pointId;
    public string commandId;
    public string choiceId;
    public string optionId;
    public string question;
    public string text;

    public string displayName
    {
        get
        {
            string questionText = string.IsNullOrWhiteSpace(question) ? "未填写选择问题" : question;
            string optionText = string.IsNullOrWhiteSpace(text) ? "未填写选项" : text;
            if (string.Equals(conditionType, "battleResult", StringComparison.OrdinalIgnoreCase))
                return "战斗结果  →  " + optionText;
            return questionText + "  →  " + optionText;
        }
    }
}
