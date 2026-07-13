using System;
using UnityEngine;

/// <summary>
/// 统一内置与 Mod 剧情的查找、反序列化、校验和运行时脚本构建。
/// 运行时 View 只依赖此入口，不再自行读取 JSON 或了解资源位置。
/// </summary>
public static class StoryDocumentLoader
{
    public const string ModStoryPrefix = "mod:";
    private const string BuiltinStoryResourceRoot = "Data/Stories/";

    public static bool CanOpen(string storyId, out string error)
    {
        if (!TryLoad(storyId, out _, out error))
            return false;

        return true;
    }

    public static bool TryBuildRuntimeScript(string storyId, out StoryScript story, out string error)
    {
        story = null;
        if (!TryLoad(storyId, out StoryDocument document, out error))
            return false;

        story = document.ToScript();
        if (story != null)
            return true;

        error = "剧情运行脚本构建失败。";
        return false;
    }

    public static bool TryLoad(string storyId, out StoryDocument document, out string error)
    {
        document = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(storyId))
        {
            error = "剧情 ID 为空。";
            return false;
        }

        if (IsModStory(storyId))
        {
            string modStoryId = storyId.Substring(ModStoryPrefix.Length);
            document = Database.instance?.GetStoryInfo(modStoryId);
            if (document == null)
            {
                error = "找不到对应的 Mod 剧情：" + modStoryId;
                return false;
            }

            if (document.isDraft)
            {
                error = "该 Mod 剧情仍是草稿，不能作为任务启动：" + modStoryId;
                document = null;
                return false;
            }

            if (!StoryValidator.Validate(document, out string validationError))
            {
                error = "Mod 剧情文件格式错误：\n" + validationError;
                document = null;
                return false;
            }

            return true;
        }

        TextAsset asset = Resources.Load<TextAsset>(BuiltinStoryResourceRoot + storyId);
        if (asset == null)
        {
            error = "未找到剧情 JSON：" + storyId;
            return false;
        }

        if (!StoryDocumentCodec.TryDeserialize(asset.text, true, out document, out string parseError))
        {
            error = "剧情 JSON 格式错误：\n" + parseError;
            return false;
        }

        return true;
    }

    public static bool IsModStory(string storyId)
    {
        return !string.IsNullOrWhiteSpace(storyId)
            && storyId.StartsWith(ModStoryPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
