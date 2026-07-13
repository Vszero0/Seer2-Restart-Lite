using System;
using UnityEngine;

/// <summary>
/// 剧情 JSON 与数据模型之间的唯一转换入口。
/// 文件读取位置由调用方决定；这里不涉及 Mod、Resources 或 UI。
/// </summary>
public static class StoryDocumentCodec
{
    public static bool TryDeserialize(string json, bool validate, out StoryDocument document, out string error)
    {
        document = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "剧情 JSON 为空。";
            return false;
        }

        try
        {
            document = JsonUtility.FromJson<StoryDocument>(json);
        }
        catch (Exception exception)
        {
            error = "剧情 JSON 解析失败：" + exception.Message;
            return false;
        }

        if (document == null)
        {
            error = "剧情 JSON 为空或格式错误。";
            return false;
        }

        if (validate && !StoryValidator.Validate(document, out error))
        {
            document = null;
            return false;
        }

        return true;
    }

    public static string Serialize(StoryDocument document, bool prettyPrint = true)
    {
        return JsonUtility.ToJson(document, prettyPrint);
    }
}
