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
            StoryDocument document = StoryDocumentFactory.CreateDraft(storyId);
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

    private string CreateAvailableStoryId(string directory)
    {
        string baseId = "story_" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
        string storyId = baseId;
        int suffix = 1;
        while (FileBrowserHelpers.FileExists(Path.Combine(directory, storyId + StoryFileExtension)))
            storyId = baseId + "_" + suffix++;

        return storyId;
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
