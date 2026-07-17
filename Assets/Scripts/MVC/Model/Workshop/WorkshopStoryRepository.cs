using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleFileBrowser;
using UnityEngine;

/// <summary>
/// 仅负责 Mod/Stories 文件目录的枚举与读取。
/// 剧本的编辑状态、选中状态和 UI 行为不应放在这里。
/// </summary>
public sealed class WorkshopStoryRepository
{
    private const string StoryDirectory = "/Mod/Stories/";
    private const string StoryFileExtension = ".json";

    public IReadOnlyList<WorkshopStorySummary> List(out string error)
    {
        error = string.Empty;
        try
        {
            string directory = GetStoryDirectory();
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            return Directory.GetFiles(directory, "*.json")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(ReadSummary)
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
        if (!IsStoryPath(path) || !FileBrowserHelpers.FileExists(path))
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
        summary = null;
        error = string.Empty;
        try
        {
            string directory = GetStoryDirectory();
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string storyId = CreateAvailableStoryId(directory);
            string path = Path.Combine(directory, storyId + StoryFileExtension);
            StoryDocument document = StoryDocumentFactory.CreateDraft(storyId, CreateAvailableMissionId(null));
            if (!TrySave(path, document, out error))
                return false;

            summary = CreateSummary(path, document);
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
        summary = null;
        error = string.Empty;
        if (source == null)
        {
            error = "没有可复制的剧本。";
            return false;
        }

        try
        {
            string directory = GetStoryDirectory();
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (!StoryDocumentCodec.TryDeserialize(StoryDocumentCodec.Serialize(source), false,
                    out StoryDocument copy, out error))
            {
                return false;
            }

            string sourceId = source.id;
            string storyId = CreateAvailableStoryId(directory);
            string path = Path.Combine(directory, storyId + StoryFileExtension);
            CopyOwnedAssets(sourceId, storyId);
            RewriteOwnedAssetPaths(copy, sourceId, storyId);

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

            summary = CreateSummary(path, copy);
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
        if (!IsStoryPath(path))
        {
            error = "剧本只能保存到 Mod/Stories 目录。";
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
            string directory = GetStoryDirectory();
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            FileBrowserHelpers.WriteTextToFile(path, StoryDocumentCodec.Serialize(document));
            return true;
        }
        catch (Exception exception)
        {
            error = "保存剧本失败：" + exception.Message;
            return false;
        }
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
        if (document == null)
        {
            message = "剧本文档不能为空。";
            return false;
        }

        string originalStatus = document.normalizedStatus;
        EnsureRuntimeMetadata(path, document);
        document.status = "published";
        if (StoryValidator.Validate(document, out string validationError))
        {
            if (!TrySave(path, document, out message))
            {
                document.status = originalStatus;
                return false;
            }

            runtimeReady = true;
            message = "剧本已保存并载入 Mod。";
            return true;
        }

        document.status = originalStatus;
        if (!string.Equals(originalStatus, "draft", StringComparison.OrdinalIgnoreCase))
        {
            message = "剧本存在运行问题，本次修改尚未保存：\n" + validationError;
            return false;
        }

        if (!TrySave(path, document, out string saveError))
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
        if (!IsStoryPath(path) || !FileBrowserHelpers.FileExists(path))
        {
            error = "找不到剧本文件。";
            return false;
        }

        try
        {
            FileBrowserHelpers.DeleteFile(path);
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
        return Application.persistentDataPath + StoryDirectory;
    }

    private static WorkshopStorySummary ReadSummary(string path)
    {
        WorkshopStorySummary summary = new WorkshopStorySummary
        {
            path = path,
            fileName = Path.GetFileName(path),
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

            return CreateSummary(path, document);
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

    private static WorkshopStorySummary CreateSummary(string path, StoryDocument document)
    {
        return new WorkshopStorySummary
        {
            path = path,
            fileName = Path.GetFileName(path),
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
        string directory = GetStoryDirectory();
        if (!Directory.Exists(directory))
            return usedMissionIds;

        foreach (string storyPath in Directory.GetFiles(directory, "*.json"))
        {
            if (!string.IsNullOrWhiteSpace(excludedPath)
                && string.Equals(Path.GetFullPath(storyPath), Path.GetFullPath(excludedPath), StringComparison.OrdinalIgnoreCase))
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
        return usedMissionIds;
    }

    private string CreateAvailableStoryId(string directory)
    {
        string baseId = "story_" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
        string storyId = baseId;
        int suffix = 1;
        while (FileBrowserHelpers.FileExists(Path.Combine(directory, storyId + StoryFileExtension)))
            storyId = baseId + "_" + suffix++;

        return storyId;
    }

    private static void CopyOwnedAssets(string sourceId, string targetId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
            return;

        string sourceDirectory = Path.Combine(Application.persistentDataPath,
            "Mod", "Stories", "Assets", sourceId);
        if (!Directory.Exists(sourceDirectory))
            return;

        string targetDirectory = Path.Combine(Application.persistentDataPath,
            "Mod", "Stories", "Assets", targetId);
        foreach (string sourcePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = sourcePath.Substring(sourceDirectory.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetPath = Path.Combine(targetDirectory, relativePath);
            string targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent))
                Directory.CreateDirectory(targetParent);
            File.Copy(sourcePath, targetPath, false);
        }
    }

    private static void RewriteOwnedAssetPaths(StoryDocument document, string sourceId, string targetId)
    {
        if (document == null || string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
            return;

        foreach (StoryResourceDefinition resource in document.resourceDefinitions ?? Array.Empty<StoryResourceDefinition>())
        {
            if (resource != null)
                resource.path = RewriteOwnedAssetPath(resource.path, sourceId, targetId);
        }

        foreach (StoryActorDocument actor in document.actors ?? Array.Empty<StoryActorDocument>())
        {
            if (actor == null)
                continue;
            actor.sprite = RewriteOwnedAssetPath(actor.sprite, sourceId, targetId);
            actor.icon = RewriteOwnedAssetPath(actor.icon, sourceId, targetId);
            actor.independentIcon = RewriteOwnedAssetPath(actor.independentIcon, sourceId, targetId);
            actor.battleSprite = RewriteOwnedAssetPath(actor.battleSprite, sourceId, targetId);
        }

        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node == null)
                continue;
            foreach (StorySceneDocument scene in node.scenes ?? Array.Empty<StorySceneDocument>())
            {
                if (scene != null)
                    scene.bgmResourcePath = RewriteOwnedAssetPath(scene.bgmResourcePath, sourceId, targetId);
            }
            foreach (StoryCommandDocument command in node.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command == null)
                    continue;
                command.bgmResourcePath = RewriteOwnedAssetPath(command.bgmResourcePath, sourceId, targetId);
                command.bg = RewriteOwnedAssetPath(command.bg, sourceId, targetId);
                command.expression = RewriteOwnedAssetPath(command.expression, sourceId, targetId);
                command.args = RewriteOwnedAssetPath(command.args, sourceId, targetId);
            }
        }
    }

    private static string RewriteOwnedAssetPath(string path, string sourceId, string targetId)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string sourcePrefix = "Mod/Stories/Assets/" + sourceId + "/";
        if (!path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            return path;
        return "Mod/Stories/Assets/" + targetId + "/" + path.Substring(sourcePrefix.Length);
    }

    private bool IsStoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            string directory = Path.GetFullPath(GetStoryDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path);
            return string.Equals(Path.GetDirectoryName(fullPath), directory, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(fullPath), StoryFileExtension, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public sealed class WorkshopStorySummary
{
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
}
