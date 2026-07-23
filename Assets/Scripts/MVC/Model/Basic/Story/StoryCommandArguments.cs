using System;
using System.Linq;

/// <summary>
/// 兼容旧命令 args 文本的读取工具；新 JSON 字段优先在转换阶段处理。
/// </summary>
public static class StoryCommandArguments
{
    public static string[] Split(string args)
    {
        return string.IsNullOrWhiteSpace(args)
            ? Array.Empty<string>()
            : args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string GetValue(string args, string key, string defaultValue = "")
    {
        string prefix = key + ":";
        string value = Split(args).FirstOrDefault(token => token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(value) ? defaultValue : value.Substring(prefix.Length);
    }
}
