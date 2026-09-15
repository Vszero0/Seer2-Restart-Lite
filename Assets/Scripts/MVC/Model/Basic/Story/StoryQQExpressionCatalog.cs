using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
internal sealed class StoryQQExpressionCatalogDocument
{
    public int version;
    public StoryQQExpressionDefinition[] expressions;
}

[Serializable]
internal sealed class StoryQQExpressionDefinition
{
    public string id;
    public string qqId;
    public string displayName;
    public string qzoneCode;
    public string previewPath;
    public string spriteSheetPath;
    public int frameCount;
    public int frameDurationMs;
    public int[] frameDurationsMs;
}

/// <summary>
/// Runtime catalog for the QQNT small yellow-face group imported into Resources.
/// The catalog is deliberately data-driven so the editor and player use the same
/// names, stable ids and resource paths.
/// </summary>
public static class StoryQQExpressionCatalog
{
    public const string ResourcePath = "Story/Expressions/QQFace/QQFaceCatalog";
    private const int FaceSize = 128;

    private static bool loaded;
    private static readonly List<StoryQQExpressionDefinition> entries =
        new List<StoryQQExpressionDefinition>();
    private static readonly Dictionary<string, StoryQQExpressionDefinition> entriesById =
        new Dictionary<string, StoryQQExpressionDefinition>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, StoryQQExpressionDefinition> entriesByQQId =
        new Dictionary<string, StoryQQExpressionDefinition>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> previewSprites =
        new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SheetRuntime> loadedSheets =
        new Dictionary<string, SheetRuntime>(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> loadedSheetOrder = new LinkedList<string>();
    private static readonly HashSet<string> protectedSheetIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static string[] ids;
    private static string[] displayNames;

    private sealed class SheetRuntime
    {
        public Texture2D texture;
        public readonly Dictionary<int, Sprite> frameSprites = new Dictionary<int, Sprite>();
    }

    public static string[] Ids
    {
        get
        {
            EnsureLoaded();
            return ids;
        }
    }

    public static string[] DisplayNames
    {
        get
        {
            EnsureLoaded();
            return displayNames;
        }
    }

    public static int Count
    {
        get
        {
            EnsureLoaded();
            return entries.Count;
        }
    }

    public static string Normalize(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        string normalized = id.Replace('\\', '/').Trim('/');
        if (entriesById.ContainsKey(normalized))
            return entriesById[normalized].id;

        if (normalized.StartsWith("qq_face_", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("qq_face_".Length);

        return entriesByQQId.TryGetValue(normalized, out StoryQQExpressionDefinition entry)
            ? entry.id
            : null;
    }

    public static string GetDisplayName(string id)
    {
        StoryQQExpressionDefinition entry = Find(id);
        return entry?.displayName;
    }

    public static bool IsAnimated(string id)
    {
        StoryQQExpressionDefinition entry = Find(id);
        return entry != null && entry.frameCount > 1 && !string.IsNullOrEmpty(entry.spriteSheetPath);
    }

    public static Sprite LoadPreview(string id)
    {
        StoryQQExpressionDefinition entry = Find(id);
        if (entry == null || string.IsNullOrEmpty(entry.previewPath))
            return null;

        if (previewSprites.TryGetValue(entry.id, out Sprite cached) && cached != null)
            return cached;

        Texture2D texture = Resources.Load<Texture2D>(entry.previewPath);
        if (texture == null)
            return null;

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(.5f, .5f), Mathf.Max(1f, texture.width), 0, SpriteMeshType.FullRect);
        sprite.name = entry.id + " Preview";
        sprite.hideFlags = HideFlags.DontSave;
        previewSprites[entry.id] = sprite;
        return sprite;
    }

    public static Sprite LoadFrame(string id, int frameIndex)
    {
        StoryQQExpressionDefinition entry = Find(id);
        if (entry == null || entry.frameCount <= 0 || string.IsNullOrEmpty(entry.spriteSheetPath))
            return LoadPreview(id);

        if (!loadedSheets.TryGetValue(entry.id, out SheetRuntime runtime) || runtime.texture == null)
        {
            runtime = new SheetRuntime
            {
                texture = Resources.Load<Texture2D>(entry.spriteSheetPath),
            };
            if (runtime.texture == null)
                return LoadPreview(id);
            loadedSheets[entry.id] = runtime;
            loadedSheetOrder.AddLast(entry.id);
        }
        else
        {
            TouchSheet(entry.id);
        }

        TrimLoadedSheets();

        frameIndex = Mathf.Clamp(frameIndex, 0, entry.frameCount - 1);
        if (runtime.frameSprites.TryGetValue(frameIndex, out Sprite cached) && cached != null)
            return cached;

        int column = frameIndex % 8;
        int topRow = frameIndex / 8;
        int rows = Mathf.CeilToInt(entry.frameCount / 8f);
        Rect rect = new Rect(column * FaceSize, (rows - 1 - topRow) * FaceSize, FaceSize, FaceSize);
        Sprite sprite = Sprite.Create(runtime.texture, rect, new Vector2(.5f, .5f), FaceSize, 0,
            SpriteMeshType.FullRect);
        sprite.name = entry.id + " Frame " + frameIndex;
        sprite.hideFlags = HideFlags.DontSave;
        runtime.frameSprites[frameIndex] = sprite;
        return sprite;
    }

    public static int GetFrameIndex(string id, float elapsed)
    {
        StoryQQExpressionDefinition entry = Find(id);
        if (entry == null || entry.frameCount <= 1)
            return 0;

        float speed = StoryPresentationSettings.Load().StoryExpressionAnimationSpeed;
        float animationTime = Mathf.Max(0f, elapsed) * speed * 1000f;
        int totalDuration = 0;
        for (int index = 0; index < entry.frameCount; index++)
            totalDuration += GetFrameDurationMs(entry, index);

        if (totalDuration <= 0)
            return Mathf.FloorToInt(animationTime / 42f) % entry.frameCount;

        int position = Mathf.FloorToInt(animationTime % totalDuration);
        for (int index = 0; index < entry.frameCount; index++)
        {
            int duration = GetFrameDurationMs(entry, index);
            if (position < duration)
                return index;
            position -= duration;
        }

        return entry.frameCount - 1;
    }

    public static bool TryGetEntry(string id, out string canonicalId, out int frameCount)
    {
        StoryQQExpressionDefinition entry = Find(id);
        canonicalId = entry?.id;
        frameCount = entry?.frameCount ?? 0;
        return entry != null;
    }

    public static void SetActiveExpressionIds(IEnumerable<string> expressionIds)
    {
        protectedSheetIds.Clear();
        foreach (string expressionId in expressionIds ?? Array.Empty<string>())
        {
            StoryQQExpressionDefinition entry = Find(expressionId);
            if (entry != null && entry.frameCount > 0 && !string.IsNullOrEmpty(entry.spriteSheetPath))
                protectedSheetIds.Add(entry.id);
        }

        TrimLoadedSheets();
    }

    private static int GetFrameDurationMs(StoryQQExpressionDefinition entry, int frameIndex)
    {
        if (entry.frameDurationsMs != null && frameIndex < entry.frameDurationsMs.Length
            && entry.frameDurationsMs[frameIndex] > 0)
            return entry.frameDurationsMs[frameIndex];
        return Mathf.Max(1, entry.frameDurationMs);
    }

    private static void TouchSheet(string id)
    {
        loadedSheetOrder.Remove(id);
        loadedSheetOrder.AddLast(id);
    }

    private static void TrimLoadedSheets()
    {
        int cacheSize = StoryPresentationSettings.Load().StoryExpressionSheetCacheSize;
        while (loadedSheets.Count > cacheSize)
        {
            LinkedListNode<string> candidate = loadedSheetOrder.First;
            while (candidate != null && protectedSheetIds.Contains(candidate.Value))
                candidate = candidate.Next;
            if (candidate == null)
                return;

            string id = candidate.Value;
            loadedSheetOrder.Remove(candidate);
            if (!loadedSheets.TryGetValue(id, out SheetRuntime runtime))
                continue;

            loadedSheets.Remove(id);
            foreach (Sprite sprite in runtime.frameSprites.Values)
            {
                if (sprite != null)
                    UnityEngine.Object.Destroy(sprite);
            }

            if (runtime.texture != null)
                Resources.UnloadAsset(runtime.texture);
        }
    }

    private static StoryQQExpressionDefinition Find(string id)
    {
        string normalized = Normalize(id);
        return normalized != null && entriesById.TryGetValue(normalized, out StoryQQExpressionDefinition entry)
            ? entry
            : null;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;
        TextAsset catalogAsset = Resources.Load<TextAsset>(ResourcePath);
        StoryQQExpressionCatalogDocument document = catalogAsset == null
            ? null
            : JsonUtility.FromJson<StoryQQExpressionCatalogDocument>(catalogAsset.text);

        foreach (StoryQQExpressionDefinition entry in document?.expressions ?? Array.Empty<StoryQQExpressionDefinition>())
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entriesById.ContainsKey(entry.id))
                continue;
            entries.Add(entry);
            entriesById.Add(entry.id, entry);
            if (!string.IsNullOrWhiteSpace(entry.qqId))
                entriesByQQId[entry.qqId] = entry;
        }

        ids = new string[entries.Count];
        displayNames = new string[entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            ids[index] = entries[index].id;
            displayNames[index] = entries[index].displayName;
        }
    }
}
