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

        Normalize(document);

        if (validate && !StoryValidator.Validate(document, out error))
        {
            document = null;
            return false;
        }

        return true;
    }

    public static string Serialize(StoryDocument document, bool prettyPrint = true)
    {
        Normalize(document);
        return JsonUtility.ToJson(document, prettyPrint);
    }

    private static void Normalize(StoryDocument document)
    {
        if (document == null)
            return;

        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node != null && string.IsNullOrWhiteSpace(node.flowRole))
                node.flowRole = "sequence";

            foreach (StoryNodeTransitionDocument transition in node?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            {
                if (transition != null
                    && transition.isDefault
                    && (transition.condition == null
                        || transition.condition.conditions == null
                        || transition.condition.conditions.Length == 0))
                {
                    transition.condition = null;
                }
            }
        }
    }
}
