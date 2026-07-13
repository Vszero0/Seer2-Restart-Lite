using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleFileBrowser;
using UnityEngine;

public sealed class WorkshopStoryRepository
{
    private const string StoryDirectory = "/Mod/Stories/";

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
        if (string.IsNullOrWhiteSpace(path) || !FileBrowserHelpers.FileExists(path))
        {
            error = "找不到剧本文件。";
            return false;
        }

        try
        {
            document = JsonUtility.FromJson<StoryDocument>(FileBrowserHelpers.ReadTextFromFile(path));
            if (!StoryValidator.Validate(document, out error))
            {
                document = null;
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = "读取剧本失败：" + exception.Message;
            return false;
        }
    }

    private static WorkshopStorySummary ReadSummary(string path)
    {
        WorkshopStorySummary summary = new WorkshopStorySummary { path = path, fileName = Path.GetFileName(path) };
        if (!TryRead(path, out StoryDocument document, out string error))
        {
            summary.error = error;
            return summary;
        }

        summary.id = document.id;
        summary.title = string.IsNullOrWhiteSpace(document.title) ? Path.GetFileNameWithoutExtension(path) : document.title;
        summary.summary = document.mission?.summary ?? string.Empty;
        summary.nodeCount = (document.nodes ?? Array.Empty<StoryNodeDocument>()).Count(node => node != null);
        summary.missionId = document.mission?.id ?? 0;
        return summary;
    }

    private static bool TryRead(string path, out StoryDocument document, out string error)
    {
        document = null;
        error = string.Empty;
        try
        {
            document = JsonUtility.FromJson<StoryDocument>(FileBrowserHelpers.ReadTextFromFile(path));
            if (document == null)
            {
                error = "JSON 为空或格式错误。";
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string GetStoryDirectory()
    {
        return Application.persistentDataPath + StoryDirectory;
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
    public string error;

    public bool isValid => string.IsNullOrEmpty(error);
}
