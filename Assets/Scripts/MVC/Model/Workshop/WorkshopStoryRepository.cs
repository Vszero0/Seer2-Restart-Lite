using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleFileBrowser;
using UnityEngine;

public enum WorkshopStoryStorageKind
{
    Source,
    Mod,
}

/// <summary>
/// 负责源码母稿与 Mod 剧本目录的枚举、读取和写入。
/// 剧本的编辑状态、选中状态和 UI 行为不应放在这里。
/// </summary>
public sealed class WorkshopStoryRepository
{
    private const string StoryFileExtension = ".json";
    private const string PackageStoryFileName = "story.json";
    private readonly Dictionary<StoryDocument, string> loadedDocumentPaths =
        new Dictionary<StoryDocument, string>();

    public WorkshopStoryStorageKind defaultStorageKind
    {
        get
        {
#if UNITY_EDITOR
            return WorkshopStoryStorageKind.Source;
#else
            return WorkshopStoryStorageKind.Mod;
#endif
        }
    }

    public IReadOnlyList<WorkshopStorySummary> List(out string error)
    {
        error = string.Empty;
        try
        {
            List<(string path, WorkshopStoryStorageKind storageKind)> paths =
                new List<(string path, WorkshopStoryStorageKind storageKind)>();
            foreach (WorkshopStoryStorageKind storageKind in GetAvailableStorageKinds())
            {
                string directory = GetStoryDirectory(storageKind);
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                if (!Directory.Exists(directory))
                {
                    if (storageKind == defaultStorageKind)
                        Directory.CreateDirectory(directory);
                    else
                        continue;
                }

                paths.AddRange(EnumerateStoryPaths(directory).Select(path => (path, storageKind)));
            }

            return paths
                .OrderBy(value => value.storageKind == WorkshopStoryStorageKind.Source ? 0 : 1)
                .ThenBy(value => value.path, StringComparer.OrdinalIgnoreCase)
                .Select(value => ReadSummary(value.path, value.storageKind))
                .ToList();
        }
        catch (Exception exception)
        {
            error = "读取自制剧情列表失败：" + exception.Message;
            return Array.Empty<WorkshopStorySummary>();
        }
    }

    public bool TryLoad(string path, out StoryDocument document, out string error)
    {
        document = null;
        error = string.Empty;
        if (!TryGetStorageKind(path, out _) || !FileBrowserHelpers.FileExists(path))
        {
            error = "找不到剧本文件。";
            return false;
        }

        try
        {
            if (!TryReadDocument(path, out document, out error))
                return false;

            if (document.isDraft && !StoryValidator.ValidateDraft(document, out error))
            {
                document = null;
                return false;
            }

            if (!document.isDraft && !StoryValidator.Validate(document, out error))
            {
                document = null;
                return false;
            }

            error = string.Empty;
            loadedDocumentPaths[document] = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception)
        {
            error = "读取剧本失败：" + exception.Message;
            return false;
        }
    }

    public bool TryCreateDraft(out WorkshopStorySummary summary, out string error)
    {
        return TryCreateDraft(defaultStorageKind, out summary, out error);
    }

    public bool TryCreateDraft(
        WorkshopStoryStorageKind storageKind,
        out WorkshopStorySummary summary,
        out string error)
    {
        summary = null;
        error = string.Empty;
        try
        {
            string directory = GetStoryDirectory(storageKind);
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = storageKind == WorkshopStoryStorageKind.Source
                    ? "源码母稿只能在 Unity Editor 中创建。"
                    : "当前环境无法创建 Mod 剧本。";
                return false;
            }
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string storyId = CreateAvailableStoryId();
            string path = GetPackageStoryPath(directory, storyId);
            StoryDocument document = StoryDocumentFactory.CreateDraft(storyId, CreateAvailableMissionId(null));
            if (!TrySave(path, document, out error))
                return false;

            summary = CreateSummary(path, document, storageKind);
            return true;
        }
        catch (Exception exception)
        {
            error = "新建剧本失败：" + exception.Message;
            return false;
        }
    }

    public bool TryCopyDraft(StoryDocument source, out WorkshopStorySummary summary, out string error)
    {
        if (source == null)
        {
            summary = null;
            error = "没有可复制的剧本。";
            return false;
        }

        if (!loadedDocumentPaths.TryGetValue(source, out string sourcePath)
            && !TryFindStoryPath(source.id, out sourcePath, out error))
        {
            summary = null;
            return false;
        }

        return TryCopyDraft(sourcePath, source, out summary, out error);
    }

    public bool TryCopyDraft(
        string sourcePath,
        StoryDocument source,
        out WorkshopStorySummary summary,
        out string error)
    {
        summary = null;
        error = string.Empty;
        if (source == null)
        {
            error = "没有可复制的剧本。";
            return false;
        }

        try
        {
            if (!TryGetStorageKind(sourcePath, out WorkshopStoryStorageKind storageKind))
            {
                error = "复制来源不在有效的剧本目录中。";
                return false;
            }

            string directory = GetStoryDirectory(storageKind);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (!StoryDocumentCodec.TryDeserialize(StoryDocumentCodec.Serialize(source), false,
                    out StoryDocument copy, out error))
            {
                return false;
            }

            string sourceId = source.id;
            string storyId = CreateAvailableStoryId();
            string path = GetPackageStoryPath(directory, storyId);
            CopyOwnedAssets(storageKind, sourceId, storyId);
            RewriteOwnedAssetPaths(copy, storageKind, sourceId, storyId);

            copy.id = storyId;
            copy.status = "draft";
            copy.title = (string.IsNullOrWhiteSpace(source.title) ? "未命名剧本" : source.title.Trim()) + "（副本）";
            if (copy.mission == null)
                copy.mission = new StoryMissionDocument();
            copy.mission.id = CreateAvailableMissionId(null);
            copy.mission.title = copy.title;
            copy.mission.summary = copy.summary ?? string.Empty;
            copy.mission.replayable = copy.replayable;

            if (!TrySave(path, copy, out error))
                return false;

            RefreshSourceAssetDatabase(storageKind);
            summary = CreateSummary(path, copy, storageKind);
            return true;
        }
        catch (Exception exception)
        {
            error = "复制剧本失败：" + exception.Message;
            return false;
        }
    }

    public bool TrySave(string path, StoryDocument document, out string error)
    {
        error = string.Empty;
        if (!TryGetStorageKind(path, out WorkshopStoryStorageKind storageKind))
        {
            error = "剧本只能保存到源码母稿或 Mod/Stories 目录。";
            return false;
        }

        if (document == null)
        {
            error = "剧本文档不能为空。";
            return false;
        }

        if (!document.HasSupportedStatus)
        {
            error = "story.status 只支持 draft 或 published";
            return false;
        }

        document.status = document.normalizedStatus;
        if (string.IsNullOrWhiteSpace(document.id))
        {
            error = "剧本 ID 不能为空。";
            return false;
        }

        if (document.isDraft && !StoryValidator.ValidateDraft(document, out error))
            return false;

        if (!document.isDraft && !StoryValidator.Validate(document, out error))
            return false;

        try
        {
            string directory = GetStoryDirectory(storageKind);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string parentDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDirectory) && !Directory.Exists(parentDirectory))
                Directory.CreateDirectory(parentDirectory);

            FileBrowserHelpers.WriteTextToFile(path, StoryDocumentCodec.Serialize(document));
            return true;
        }
        catch (Exception exception)
        {
            error = "保存剧本失败：" + exception.Message;
            return false;
        }
    }

    public bool TrySaveDraft(string path, StoryDocument document, out string error)
    {
        error = string.Empty;
        if (document == null)
        {
            error = "剧本文档不能为空。";
            return false;
        }

        if (!StoryDocumentCodec.TryDeserialize(StoryDocumentCodec.Serialize(document), false,
                out StoryDocument draft, out error))
        {
            return false;
        }

        draft.status = "draft";
        return TrySave(path, draft, out error);
    }

    /// <summary>
    /// 保存入口页确认的剧本。完整校验通过时直接作为 Mod 剧情生效；
    /// 尚未完成的草稿仍会保存编辑进度，但不会进入运行时数据库。
    /// </summary>
    public bool TrySaveForRuntime(
        string path,
        StoryDocument document,
        out bool runtimeReady,
        out string message)
    {
        runtimeReady = false;
        message = string.Empty;
        if (!TryGetStorageKind(path, out WorkshopStoryStorageKind storageKind))
        {
            message = "剧本路径不在有效的源码母稿或 Mod/Stories 目录中。";
            return false;
        }

        if (storageKind != WorkshopStoryStorageKind.Mod)
        {
            message = "源码母稿不能直接载入 Mod，请使用源码任务导出功能。";
            return false;
        }

        if (document == null)
        {
            message = "剧本文档不能为空。";
            return false;
        }

        string originalStatus = document.normalizedStatus;
        if (!StoryDocumentCodec.TryDeserialize(StoryDocumentCodec.Serialize(document), false,
                out StoryDocument runtimeDocument, out string cloneError))
        {
            message = cloneError;
            return false;
        }

        EnsureRuntimeMetadata(path, runtimeDocument);
        runtimeDocument.status = "published";
        if (StoryValidator.Validate(runtimeDocument, out string validationError))
        {
            if (!TrySave(path, runtimeDocument, out message))
                return false;

            runtimeReady = true;
            message = "剧本已保存并载入 Mod。";
            return true;
        }

        if (!string.Equals(originalStatus, "draft", StringComparison.OrdinalIgnoreCase))
        {
            message = "剧本存在运行问题，本次修改尚未保存：\n" + validationError;
            return false;
        }

        if (!TrySaveDraft(path, document, out string saveError))
        {
            message = saveError;
            return false;
        }

        message = "编辑进度已保存，但剧本暂未载入 Mod：\n" + validationError;
        return true;
    }

    public bool TryDelete(string path, out string error)
    {
        error = string.Empty;
        if (!TryGetStorageKind(path, out WorkshopStoryStorageKind storageKind)
            || !FileBrowserHelpers.FileExists(path))
        {
            error = "找不到剧本文件。";
            return false;
        }

        try
        {
            TryReadDocument(path, out StoryDocument document, out _);
            string storyId = string.IsNullOrWhiteSpace(document?.id)
                ? GetStoryIdFromPath(path)
                : document.id;
            if (TryGetPackageDirectory(path, storageKind, out string packageDirectory))
                Directory.Delete(packageDirectory, true);
            else
                FileBrowserHelpers.DeleteFile(path);

            DeleteSeparatedOwnedAssets(storageKind, storyId);
            RefreshSourceAssetDatabase(storageKind);
            return true;
        }
        catch (Exception exception)
        {
            error = "删除剧本失败：" + exception.Message;
            return false;
        }
    }

    public string GetStoryDirectory()
    {
        return GetStoryDirectory(defaultStorageKind);
    }

    public string GetStoryDirectory(WorkshopStoryStorageKind storageKind)
    {
        if (storageKind == WorkshopStoryStorageKind.Mod)
            return Path.Combine(Application.persistentDataPath, "Mod", "Stories");

#if UNITY_EDITOR
        string projectDirectory = Path.GetDirectoryName(Application.dataPath);
        return string.IsNullOrWhiteSpace(projectDirectory)
            ? string.Empty
            : Path.Combine(projectDirectory, "UserSettings", "StoryWorkshop", "Stories");
#else
        return string.Empty;
#endif
    }

    public bool CanCreateStorage(WorkshopStoryStorageKind storageKind)
    {
        return !string.IsNullOrWhiteSpace(GetStoryDirectory(storageKind));
    }

    private static WorkshopStorySummary ReadSummary(string path, WorkshopStoryStorageKind storageKind)
    {
        WorkshopStorySummary summary = new WorkshopStorySummary
        {
            path = path,
            fileName = Path.GetFileName(path),
            storageKind = storageKind,
        };

        try
        {
            if (!StoryDocumentCodec.TryDeserialize(FileBrowserHelpers.ReadTextFromFile(path), false, out StoryDocument document, out string error))
            {
                summary.error = error;
                return summary;
            }

            if (!document.HasSupportedStatus)
            {
                summary.error = "story.status 只支持 draft 或 published";
                return summary;
            }

            if (document.isDraft && !StoryValidator.ValidateDraft(document, out error))
            {
                summary.error = error;
                return summary;
            }

            if (!document.isDraft && !StoryValidator.Validate(document, out error))
            {
                summary.error = error;
                return summary;
            }

            return CreateSummary(path, document, storageKind);
        }
        catch (Exception exception)
        {
            summary.error = exception.Message;
            return summary;
        }
    }

    private static bool TryReadDocument(string path, out StoryDocument document, out string error)
    {
        if (!StoryDocumentCodec.TryDeserialize(FileBrowserHelpers.ReadTextFromFile(path), false, out document, out error))
            return false;

        if (!document.HasSupportedStatus)
        {
            error = "story.status 只支持 draft 或 published";
            document = null;
            return false;
        }

        return true;
    }

    private static WorkshopStorySummary CreateSummary(
        string path,
        StoryDocument document,
        WorkshopStoryStorageKind storageKind)
    {
        return new WorkshopStorySummary
        {
            path = path,
            fileName = Path.GetFileName(path),
            storageKind = storageKind,
            id = document.id,
            title = string.IsNullOrWhiteSpace(document.title) ? Path.GetFileNameWithoutExtension(path) : document.title,
            summary = string.IsNullOrEmpty(document.summary) ? document.mission?.summary ?? string.Empty : document.summary,
            nodeCount = (document.nodes ?? Array.Empty<StoryNodeDocument>()).Count(node => node != null),
            missionId = document.mission?.id ?? 0,
            status = document.normalizedStatus,
        };
    }

    private void EnsureRuntimeMetadata(string path, StoryDocument document)
    {
        HashSet<int> usedMissionIds = GetUsedMissionIds(path);
        if (document.mission == null)
            document.mission = new StoryMissionDocument();
        if (document.mission.id >= 0 || usedMissionIds.Contains(document.mission.id))
            document.mission.id = CreateAvailableMissionId(path);

        document.mission.title = document.title ?? string.Empty;
        document.mission.summary = document.summary ?? string.Empty;
        document.mission.replayable = document.replayable;
        document.mission.mapId = 0;
    }

    private int CreateAvailableMissionId(string excludedPath)
    {
        HashSet<int> usedMissionIds = GetUsedMissionIds(excludedPath);
        int missionId = -1;
        while (usedMissionIds.Contains(missionId))
            missionId--;
        return missionId;
    }

    private HashSet<int> GetUsedMissionIds(string excludedPath)
    {
        HashSet<int> usedMissionIds = new HashSet<int>();
        foreach (WorkshopStoryStorageKind storageKind in GetAvailableStorageKinds())
        {
            string directory = GetStoryDirectory(storageKind);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                continue;

            foreach (string storyPath in EnumerateStoryPaths(directory))
            {
                if (!string.IsNullOrWhiteSpace(excludedPath)
                    && string.Equals(Path.GetFullPath(storyPath), Path.GetFullPath(excludedPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (TryReadDocument(storyPath, out StoryDocument document, out _)
                        && document?.mission != null
                        && document.mission.id < 0)
                    {
                        usedMissionIds.Add(document.mission.id);
                    }
                }
                catch (Exception)
                {
                    // 损坏文件会由列表读取和正式校验报告，不阻塞其他剧本分配内部任务 ID。
                }
            }
        }
        return usedMissionIds;
    }

    private string CreateAvailableStoryId()
    {
        string baseId = "story_" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
        string storyId = baseId;
        int suffix = 1;
        while (IsStoryIdUsed(storyId))
            storyId = baseId + "_" + suffix++;

        return storyId;
    }

    private bool IsStoryIdUsed(string storyId)
    {
        foreach (WorkshopStoryStorageKind storageKind in GetAvailableStorageKinds())
        {
            string directory = GetStoryDirectory(storageKind);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                continue;
            if (File.Exists(Path.Combine(directory, storyId + StoryFileExtension))
                || Directory.Exists(Path.Combine(directory, storyId))
                || Directory.Exists(GetOwnedAssetDirectory(storageKind, storyId)))
            {
                return true;
            }

            foreach (string storyPath in EnumerateStoryPaths(directory))
            {
                try
                {
                    if (TryReadDocument(storyPath, out StoryDocument document, out _)
                        && string.Equals(document?.id, storyId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // 文件名和目录检查仍可规避正常剧本冲突；损坏文件由列表读取报告。
                }
            }
        }

        return false;
    }

    private void CopyOwnedAssets(
        WorkshopStoryStorageKind storageKind,
        string sourceId,
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
            return;

        string sourceDirectory = GetReadableOwnedAssetDirectory(storageKind, sourceId);
        if (!Directory.Exists(sourceDirectory))
            return;

        string targetDirectory = GetOwnedAssetDirectory(storageKind, targetId);
        if (string.Equals(Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string sourcePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetExtension(sourcePath), ".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            string relativePath = sourcePath.Substring(sourceDirectory.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetPath = Path.Combine(targetDirectory, relativePath);
            string targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent))
                Directory.CreateDirectory(targetParent);
            File.Copy(sourcePath, targetPath, false);
        }
    }

    private void RewriteOwnedAssetPaths(
        StoryDocument document,
        WorkshopStoryStorageKind storageKind,
        string sourceId,
        string targetId)
    {
        if (document == null || string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
            return;

        foreach (StoryResourceDefinition resource in document.resourceDefinitions ?? Array.Empty<StoryResourceDefinition>())
        {
            if (resource != null)
                resource.path = RewriteOwnedAssetPath(resource.path, storageKind, sourceId, targetId);
        }

        foreach (StorySceneResourceDocument sceneResource in document.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
        {
            if (sceneResource == null)
                continue;
            sceneResource.backgroundResourcePath = RewriteOwnedAssetPath(
                sceneResource.backgroundResourcePath, storageKind, sourceId, targetId);
            sceneResource.defaultBgmResourcePath = RewriteOwnedAssetPath(
                sceneResource.defaultBgmResourcePath, storageKind, sourceId, targetId);
        }

        foreach (StoryActorDocument actor in document.actors ?? Array.Empty<StoryActorDocument>())
        {
            if (actor == null)
                continue;
            actor.sprite = RewriteOwnedAssetPath(actor.sprite, storageKind, sourceId, targetId);
            actor.icon = RewriteOwnedAssetPath(actor.icon, storageKind, sourceId, targetId);
            actor.independentIcon = RewriteOwnedAssetPath(actor.independentIcon, storageKind, sourceId, targetId);
            actor.battleSprite = RewriteOwnedAssetPath(actor.battleSprite, storageKind, sourceId, targetId);
        }

        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node == null)
                continue;
            foreach (StorySceneDocument scene in node.scenes ?? Array.Empty<StorySceneDocument>())
            {
                if (scene != null)
                    scene.bgmResourcePath = RewriteOwnedAssetPath(
                        scene.bgmResourcePath, storageKind, sourceId, targetId);
            }
            foreach (StoryCommandDocument command in node.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command == null)
                    continue;
                command.bgmResourcePath = RewriteOwnedAssetPath(
                    command.bgmResourcePath, storageKind, sourceId, targetId);
                command.bg = RewriteOwnedAssetPath(command.bg, storageKind, sourceId, targetId);
                command.expression = RewriteOwnedAssetPath(command.expression, storageKind, sourceId, targetId);
                command.args = RewriteOwnedAssetPath(command.args, storageKind, sourceId, targetId);
            }
        }
    }

    private string RewriteOwnedAssetPath(
        string path,
        WorkshopStoryStorageKind storageKind,
        string sourceId,
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string suffix = null;
        string ownedPrefix = GetOwnedAssetResourcePrefix(storageKind, sourceId);
        if (path.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
            suffix = path.Substring(ownedPrefix.Length);

        if (suffix == null && storageKind == WorkshopStoryStorageKind.Mod)
        {
            string legacyModPrefix = "Mod/Stories/Assets/" + sourceId + "/";
            if (path.StartsWith(legacyModPrefix, StringComparison.OrdinalIgnoreCase))
                suffix = path.Substring(legacyModPrefix.Length);
        }

        return suffix == null
            ? path
            : GetOwnedAssetResourcePrefix(storageKind, targetId) + suffix;
    }

    public bool TryGetStorageKind(string path, out WorkshopStoryStorageKind storageKind)
    {
        foreach (WorkshopStoryStorageKind candidate in GetAvailableStorageKinds())
        {
            if (IsStoryPathInDirectory(path, GetStoryDirectory(candidate)))
            {
                storageKind = candidate;
                return true;
            }
        }

        storageKind = defaultStorageKind;
        return false;
    }

    public bool IsModStoragePath(string path)
    {
        return TryGetStorageKind(path, out WorkshopStoryStorageKind storageKind)
            && storageKind == WorkshopStoryStorageKind.Mod;
    }

    public string GetOwnedAssetDirectory(WorkshopStoryStorageKind storageKind, string storyId)
    {
        if (storageKind == WorkshopStoryStorageKind.Source)
            return Path.Combine(Application.dataPath, "Resources", "StoryAssets", storyId ?? string.Empty);

        return Path.Combine(Application.persistentDataPath, "Mod", "Stories",
            storyId ?? string.Empty, "Assets");
    }

    public string GetOwnedAssetResourcePrefix(WorkshopStoryStorageKind storageKind, string storyId)
    {
        string normalizedStoryId = (storyId ?? string.Empty).Replace('\\', '/').Trim('/');
        return storageKind == WorkshopStoryStorageKind.Source
            ? "Builtin/StoryAssets/" + normalizedStoryId + "/"
            : "Mod/Stories/" + normalizedStoryId + "/Assets/";
    }

    public bool TryGetOwnedAssetPaths(
        WorkshopStoryStorageKind storageKind,
        string storyId,
        string relativePath,
        out string absolutePath,
        out string resourcePath,
        out string error)
    {
        absolutePath = null;
        resourcePath = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(storyId) || string.IsNullOrWhiteSpace(relativePath))
        {
            error = "剧本 ID 和资源相对路径不能为空。";
            return false;
        }

        try
        {
            string root = Path.GetFullPath(GetOwnedAssetDirectory(storageKind, storyId))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRelativePath = relativePath.Replace('\\', '/')
                .TrimStart('/');
            string fullPath = Path.GetFullPath(Path.Combine(root,
                normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            string rootPrefix = root + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "资源路径超出了当前剧本的自有资源目录。";
                return false;
            }

            absolutePath = fullPath;
            resourcePath = GetOwnedAssetResourcePrefix(storageKind, storyId) + normalizedRelativePath;
            return true;
        }
        catch (Exception exception)
        {
            error = "资源路径无效：" + exception.Message;
            return false;
        }
    }

    public bool TryResolveOwnedAssetPath(
        WorkshopStoryStorageKind storageKind,
        string storyId,
        string resourcePath,
        out string absolutePath)
    {
        absolutePath = null;
        if (string.IsNullOrWhiteSpace(storyId) || string.IsNullOrWhiteSpace(resourcePath))
            return false;

        string normalizedPath = resourcePath.Replace('\\', '/').TrimStart('/');
        string prefix = GetOwnedAssetResourcePrefix(storageKind, storyId);
        string suffix;
        if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            suffix = normalizedPath.Substring(prefix.Length);
        }
        else if (storageKind == WorkshopStoryStorageKind.Mod)
        {
            string legacyPrefix = "Mod/Stories/Assets/" + storyId + "/";
            if (!normalizedPath.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            suffix = normalizedPath.Substring(legacyPrefix.Length);
        }
        else
        {
            return false;
        }

        return TryGetOwnedAssetPaths(storageKind, storyId, suffix,
            out absolutePath, out _, out _);
    }

    private static bool IsStoryPathInDirectory(string path, string storyDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(storyDirectory))
            return false;

        try
        {
            string directory = Path.GetFullPath(storyDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(fullPath), StoryFileExtension, StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(Path.GetDirectoryName(fullPath), directory, StringComparison.OrdinalIgnoreCase))
                return true;
            string parent = Path.GetDirectoryName(fullPath);
            return string.Equals(Path.GetFileName(fullPath), PackageStoryFileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(parent), directory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateStoryPaths(string directory)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<string>();

        IEnumerable<string> legacy = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        IEnumerable<string> packages = Directory.GetDirectories(directory)
            .Select(value => Path.Combine(value, PackageStoryFileName))
            .Where(File.Exists);
        return legacy.Concat(packages);
    }

    private static string GetPackageStoryPath(string directory, string storyId)
    {
        string packageDirectory = Path.Combine(directory, storyId);
        Directory.CreateDirectory(packageDirectory);
        return Path.Combine(packageDirectory, PackageStoryFileName);
    }

    private string GetReadableOwnedAssetDirectory(WorkshopStoryStorageKind storageKind, string storyId)
    {
        string directory = GetOwnedAssetDirectory(storageKind, storyId);
        if (Directory.Exists(directory) || storageKind == WorkshopStoryStorageKind.Source)
            return directory;
        return Path.Combine(Application.persistentDataPath, "Mod", "Stories", "Assets", storyId);
    }

    private bool TryGetPackageDirectory(
        string storyPath,
        WorkshopStoryStorageKind storageKind,
        out string packageDirectory)
    {
        packageDirectory = null;
        if (!string.Equals(Path.GetFileName(storyPath), PackageStoryFileName, StringComparison.OrdinalIgnoreCase))
            return false;

        string root = Path.GetFullPath(GetStoryDirectory(storageKind))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetFullPath(Path.GetDirectoryName(storyPath) ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(parent), root, StringComparison.OrdinalIgnoreCase))
            return false;

        packageDirectory = parent;
        return true;
    }

    private static string GetStoryIdFromPath(string storyPath)
    {
        if (string.Equals(Path.GetFileName(storyPath), PackageStoryFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(Path.GetDirectoryName(storyPath));
        }

        return Path.GetFileNameWithoutExtension(storyPath);
    }

    private bool TryFindStoryPath(string storyId, out string storyPath, out string error)
    {
        storyPath = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(storyId))
        {
            error = "没有可复制的剧本。";
            return false;
        }

        List<string> matches = new List<string>();
        foreach (WorkshopStoryStorageKind storageKind in GetAvailableStorageKinds())
        {
            string directory = GetStoryDirectory(storageKind);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                continue;
            foreach (string path in EnumerateStoryPaths(directory))
            {
                try
                {
                    if (TryReadDocument(path, out StoryDocument document, out _)
                        && string.Equals(document?.id, storyId, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(path);
                    }
                }
                catch (Exception)
                {
                    // 损坏剧本不会成为可复制来源。
                }
            }
        }

        if (matches.Count == 0)
        {
            error = "找不到要复制的剧本文件。";
            return false;
        }
        if (matches.Count > 1)
        {
            error = "源码母稿与 Mod 中存在重复剧本 ID，请按明确路径复制。";
            return false;
        }

        storyPath = matches[0];
        return true;
    }

    private void DeleteSeparatedOwnedAssets(WorkshopStoryStorageKind storageKind, string storyId)
    {
        if (string.IsNullOrWhiteSpace(storyId))
            return;

        DeleteDirectoryAndMeta(GetOwnedAssetDirectory(storageKind, storyId));
        if (storageKind == WorkshopStoryStorageKind.Source)
            return;

        string legacyDirectory = Path.Combine(Application.persistentDataPath,
            "Mod", "Stories", "Assets", storyId);
        DeleteDirectoryAndMeta(legacyDirectory);
    }

    private static void DeleteDirectoryAndMeta(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
        string metaPath = directory + ".meta";
        if (File.Exists(metaPath))
            File.Delete(metaPath);
    }

    private static void RefreshSourceAssetDatabase(WorkshopStoryStorageKind storageKind)
    {
#if UNITY_EDITOR
        if (storageKind == WorkshopStoryStorageKind.Source)
            UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
#endif
    }

    private static IEnumerable<WorkshopStoryStorageKind> GetAvailableStorageKinds()
    {
#if UNITY_EDITOR
        yield return WorkshopStoryStorageKind.Source;
#endif
        yield return WorkshopStoryStorageKind.Mod;
    }
}

public sealed class WorkshopStorySummary
{
    public WorkshopStoryStorageKind storageKind;
    public string path;
    public string fileName;
    public string id;
    public string title;
    public string summary;
    public int missionId;
    public int nodeCount;
    public string status;
    public string error;

    public bool isValid => string.IsNullOrEmpty(error);
    public bool isDraft => string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase);
    public bool isSource => storageKind == WorkshopStoryStorageKind.Source;
    public bool isMod => storageKind == WorkshopStoryStorageKind.Mod;
}
