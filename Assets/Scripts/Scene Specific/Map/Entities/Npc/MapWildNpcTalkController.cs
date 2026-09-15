using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MapWildNpcTalkController : MonoBehaviour
{
    private const string TalkImageRoot = "Assets/StaticResources/Sprites/Map/talk bubble";
    private const string TalkImageResourceRoot = "Sprites/Map/talk bubble";

    [Serializable]
    private class TalkImageEntry
    {
        public string id;
        public Sprite sprite;
    }

    [SerializeField] private MapWildNpcBubbleHost bubbleHost;
    [SerializeField] private List<TalkImageEntry> imageLibrary = new List<TalkImageEntry>();

    private MapWildNpcSpriteBubbleController bubbleView;
    private NpcWildTalkInfo talkInfo;
    private float stationaryTimer;
    private bool useStationarySchedule;

    private void Awake()
    {
        CacheReferences();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RefreshEditorImageLibrary();
    }
#endif

    public void Init(NpcWildTalkInfo talkInfo, bool useStationarySchedule)
    {
        CacheReferences();
        bubbleView = bubbleHost?.GetOrCreate();
        this.talkInfo = talkInfo;
        this.useStationarySchedule = useStationarySchedule;
        bubbleView?.Init();

        enabled = talkInfo?.HasSelf == true;
        if (!enabled)
        {
            Stop();
            return;
        }

        if (useStationarySchedule)
            ScheduleNextStationaryTalk();
    }

    public void OnRestStarted(float restDuration)
    {
        if (useStationarySchedule || talkInfo?.HasSelf != true || restDuration < 0.2f)
            return;

        TryShowTalk(restDuration);
    }

    public void Stop()
    {
        talkInfo = null;
        stationaryTimer = 0f;
        useStationarySchedule = false;
        bubbleView?.HideImmediate();
        enabled = false;
    }

    private void Update()
    {
        if (!useStationarySchedule || talkInfo?.HasSelf != true)
            return;

        stationaryTimer -= Time.deltaTime;
        if (stationaryTimer > 0f)
            return;

        TryShowTalk(Mathf.Max(0.2f, talkInfo.duration));
        ScheduleNextStationaryTalk();
    }

    private void TryShowTalk(float availableDuration)
    {
        if (bubbleView == null || UnityEngine.Random.value > Mathf.Clamp01(talkInfo.chance))
            return;

        NpcWildTalkContent content = talkInfo.GetRandomSelf();
        if (content?.IsValid != true)
            return;

        float duration = Mathf.Min(Mathf.Max(0.2f, talkInfo.duration), availableDuration);
        if (!content.IsImage)
        {
            bubbleView.ShowText(content.text, duration);
            return;
        }

        Sprite sprite = GetTalkImage(content.image);
        if (sprite == null)
        {
            Debug.LogWarning($"Wild NPC talk image not found: {content.image}", this);
            return;
        }

        bubbleView.ShowImage(sprite, duration);
    }

    public Sprite GetTalkImage(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        id = NormalizeImageId(id);
        if (string.IsNullOrEmpty(id))
            return null;

        Sprite sprite = TryLoadLocalTalkImage(id, true);
        if (sprite != null)
            return sprite;

        sprite = TryLoadLocalTalkImage(id, false);
        if (sprite != null)
            return sprite;

        sprite = FindImageEntry(id)?.sprite;
        if (sprite != null)
            return sprite;

#if UNITY_EDITOR
        return TryCacheEditorTalkImage(id);
#else
        return null;
#endif
    }

    private static Sprite TryLoadLocalTalkImage(string id, bool isMod)
    {
        if (ResourceManager.instance == null)
            return null;

        return ResourceManager.instance.GetLocalAddressables<Sprite>(
            $"{TalkImageResourceRoot}/{id}",
            isMod);
    }

#if UNITY_EDITOR
    private Sprite TryCacheEditorTalkImage(string id)
    {
        Sprite sprite = LoadEditorTalkImage(id);
        if (sprite == null)
            return null;

        TalkImageEntry entry = FindImageEntry(id);
        if (entry == null)
        {
            entry = new TalkImageEntry { id = id };
            imageLibrary.Add(entry);
        }

        entry.sprite = sprite;
        EditorUtility.SetDirty(this);
        return sprite;
    }

    private static Sprite LoadEditorTalkImage(string id)
    {
        string normalizedId = id.Replace('\\', '/').Trim('/');

        if (normalizedId.Contains("/"))
        {
            foreach (string extension in new[] { ".png", ".jpg", ".jpeg" })
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{TalkImageRoot}/{normalizedId}{extension}");
                if (sprite != null)
                    return sprite;
            }
        }

        string fileName = System.IO.Path.GetFileNameWithoutExtension(normalizedId);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string[] guids = AssetDatabase.FindAssets($"{fileName} t:Sprite", new[] { TalkImageRoot });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        return null;
    }

    private void RefreshEditorImageLibrary()
    {
        if (!AssetDatabase.IsValidFolder(TalkImageRoot))
            return;

        bool changed = imageLibrary.RemoveAll(x => x == null) > 0;
        string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { TalkImageRoot });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string id = GetEditorTalkImageId(path);
            if (string.IsNullOrWhiteSpace(id) || FindImageEntry(id) != null)
                continue;

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                continue;

            imageLibrary.Add(new TalkImageEntry { id = id, sprite = sprite });
            changed = true;
        }

        if (changed)
            EditorUtility.SetDirty(this);
    }

    private static string GetEditorTalkImageId(string assetPath)
    {
        string normalizedRoot = TalkImageRoot.Replace('\\', '/').TrimEnd('/');
        string normalizedPath = assetPath.Replace('\\', '/');
        if (!normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
            return System.IO.Path.GetFileNameWithoutExtension(normalizedPath);

        string relativePath = normalizedPath.Substring(normalizedRoot.Length + 1);
        string directory = System.IO.Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
        string fileName = System.IO.Path.GetFileNameWithoutExtension(relativePath);
        return string.IsNullOrWhiteSpace(directory) ? fileName : $"{directory}/{fileName}";
    }
#endif

    private TalkImageEntry FindImageEntry(string id)
    {
        id = NormalizeImageId(id);
        return imageLibrary.Find(x =>
            x != null && string.Equals(NormalizeImageId(x.id), id, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeImageId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        id = id.Replace('\\', '/').Trim('/');
        foreach (string extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            if (id.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                id = id.Substring(0, id.Length - extension.Length);
                break;
            }
        }

        string[] segments = id.Split('/');
        if (segments.Length <= 0)
            return string.Empty;

        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                return string.Empty;
        }

        return id;
    }

    private void ScheduleNextStationaryTalk()
    {
        Vector2 range = talkInfo?.intervalRange ?? new Vector2(5f, 10f);
        float min = Mathf.Max(0.2f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        stationaryTimer = UnityEngine.Random.Range(min, max);
    }

    private void CacheReferences()
    {
        if (bubbleHost == null)
            bubbleHost = GetComponentInChildren<MapWildNpcBubbleHost>(true);
    }
}

/// <summary>
/// Shared catalog for map NPC bubbles and story portraits.
/// New story content uses the centralized QQ small-face catalog; old emoji/toon
/// ids are resolved through the compatibility aliases below.
/// </summary>
public enum StoryExpressionMotion
{
    Neutral,
    Bounce,
    Shake,
    Surprise,
    Tilt,
    Sink,
}

public static class StoryExpressionCatalog
{
    private const string ExternalPathPrefix = "Map/talk bubble/";
    private const string TmpSpriteNamePrefix = "story_";
    private const int AtlasColumns = 12;
    private const int AtlasCellSize = 40;
    private static MapWildNpcTalkController fallbackLibrary;
    private static TMP_SpriteAsset inlineSpriteAsset;
    private static Texture2D inlineAtlasTexture;
    private static RenderTexture inlineCellRenderTexture;
    private static Texture2D inlineCellPixels;
    private static readonly Dictionary<string, int> inlineAtlasIndices =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> inlineFrameIndices =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex InlineTagPattern = new Regex(
        "<emoji\\s+id\\s*=\\s*[\"']([^\"']+)[\"']\\s*/>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The old ids remain readable. New saves use the canonical QQ ids from the
    // centralized resource catalog and the editor only offers those new ids.
    private static readonly Dictionary<string, string> LegacyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "emoji/toon/alert", "qq_face_0" },
            { "emoji/toon/angry", "qq_face_11" },
            { "emoji/toon/annoyed", "qq_face_22" },
            { "emoji/toon/cool", "qq_face_16" },
            { "emoji/toon/cry", "qq_face_9" },
            { "emoji/toon/dizzy", "qq_face_34" },
            { "emoji/toon/grin", "qq_face_13" },
            { "emoji/toon/happy", "qq_face_14" },
            { "emoji/toon/hypno", "qq_face_34" },
            { "emoji/toon/laugh", "qq_face_182" },
            { "emoji/toon/love", "qq_face_66" },
            { "emoji/toon/mute", "qq_face_7" },
            { "emoji/toon/shocked", "qq_face_26" },
            { "emoji/toon/shy", "qq_face_6" },
            { "emoji/toon/sick", "qq_face_36" },
            { "emoji/toon/silly", "qq_face_12" },
            { "emoji/toon/skull", "qq_face_37" },
            { "emoji/toon/sleepy", "qq_face_8" },
            { "emoji/toon/smug", "qq_face_4" },
            { "emoji/toon/star_eyes", "qq_face_21" },
            { "emoji/toon/sunglasses", "qq_face_16" },
            { "emoji/toon/surprised", "qq_face_0" },
            { "emoji/toon/sweat", "qq_face_96" },
            { "emoji/toon/worried", "qq_face_15" },
        };

    public static string[] Ids => StoryQQExpressionCatalog.Ids;
    public static string[] DisplayNames => StoryQQExpressionCatalog.DisplayNames;

    public static string Normalize(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        string normalized = id.Replace('\\', '/').Trim('/');
        string qqId = StoryQQExpressionCatalog.Normalize(normalized);
        if (qqId != null)
            return qqId;

        if (LegacyAliases.TryGetValue(normalized, out string alias))
            return StoryQQExpressionCatalog.Normalize(alias) ?? alias;

        // Unknown legacy ids remain readable through the previous resource
        // path, but are never added to the new picker.
        if (normalized.StartsWith("qq_face_", StringComparison.OrdinalIgnoreCase))
            return normalized;

        return normalized.StartsWith("emoji/toon/", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    public static Sprite Load(string id)
    {
        string normalized = Normalize(id);
        if (normalized == null)
            return null;

        if (StoryQQExpressionCatalog.TryGetEntry(normalized, out _, out _))
            return StoryQQExpressionCatalog.LoadPreview(normalized);

        return LoadLegacy(normalized);
    }

    public static Sprite LoadFrame(string id, int frameIndex)
    {
        string normalized = Normalize(id);
        if (normalized == null)
            return null;

        if (StoryQQExpressionCatalog.TryGetEntry(normalized, out _, out _))
            return StoryQQExpressionCatalog.LoadFrame(normalized, frameIndex);

        return LoadLegacy(normalized);
    }

    public static bool IsAnimated(string id)
    {
        string normalized = Normalize(id);
        return normalized != null && StoryQQExpressionCatalog.IsAnimated(normalized);
    }

    public static int GetFrameIndex(string id, float elapsed)
    {
        string normalized = Normalize(id);
        return normalized == null ? 0 : StoryQQExpressionCatalog.GetFrameIndex(normalized, elapsed);
    }

    public static void SetActiveExpressionIds(IEnumerable<string> expressionIds)
    {
        StoryQQExpressionCatalog.SetActiveExpressionIds(expressionIds);
    }

    private static Sprite LoadLegacy(string normalized)
    {
        Sprite sprite = ResourceManager.instance?.GetLocalAddressables<Sprite>(ExternalPathPrefix + normalized);
        if (sprite != null)
            return sprite;

        if (fallbackLibrary == null)
        {
            GameObject prefab = Resources.Load<GameObject>("Prefabs/Map/Npc");
            fallbackLibrary = prefab?.GetComponentInChildren<MapWildNpcTalkController>(true);
        }

        return fallbackLibrary?.GetTalkImage(normalized);
    }

    public static string GetDisplayName(string id)
    {
        string normalized = Normalize(id);
        return StoryQQExpressionCatalog.GetDisplayName(normalized);
    }

    public static string GetEditorToken(string id)
    {
        string displayName = GetDisplayName(id);
        return string.IsNullOrEmpty(displayName) ? string.Empty : "[" + displayName + "]";
    }

    public static string ToEditorText(string storedText)
    {
        if (string.IsNullOrEmpty(storedText))
            return storedText ?? string.Empty;

        return InlineTagPattern.Replace(storedText, match =>
        {
            string token = GetEditorToken(match.Groups[1].Value);
            return string.IsNullOrEmpty(token) ? match.Value : token;
        });
    }

    public static string FromEditorText(string editorText)
    {
        string result = editorText ?? string.Empty;
        for (int index = 0; index < Ids.Length && index < DisplayNames.Length; index++)
            result = result.Replace("[" + DisplayNames[index] + "]", BuildInlineTag(Ids[index]));
        return result;
    }

    public static string ToTmpRichText(string storedText)
    {
        if (string.IsNullOrEmpty(storedText) || !InlineTagPattern.IsMatch(storedText)
            || GetInlineSpriteAsset() == null)
            return storedText ?? string.Empty;

        return InlineTagPattern.Replace(storedText, match =>
        {
            string normalized = Normalize(match.Groups[1].Value);
            return string.IsNullOrEmpty(GetEditorToken(normalized))
                ? match.Value
                : "<sprite name=\"" + GetTmpSpriteName(normalized) + "\">";
        });
    }

    public static bool ContainsInlineExpression(string storedText)
    {
        return !string.IsNullOrEmpty(storedText) && InlineTagPattern.IsMatch(storedText);
    }

    public static TMP_SpriteAsset GetInlineSpriteAsset()
    {
        if (inlineSpriteAsset != null)
            return inlineSpriteAsset;

        List<Sprite> sourceSprites = new List<Sprite>();
        List<string> sourceIds = new List<string>();
        for (int index = 0; index < Ids.Length; index++)
        {
            Sprite sprite = Load(Ids[index]);
            if (sprite == null)
                continue;
            sourceSprites.Add(sprite);
            sourceIds.Add(Ids[index]);
        }

        if (sourceSprites.Count == 0)
            return null;

        int rows = Mathf.CeilToInt(sourceSprites.Count / (float)AtlasColumns);
        int atlasWidth = AtlasColumns * AtlasCellSize;
        int atlasHeight = rows * AtlasCellSize;
        Texture2D atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false, false)
        {
            name = "Story Inline Expressions Atlas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        Color[] clearPixels = new Color[atlasWidth * atlasHeight];
        atlas.SetPixels(clearPixels);

        RenderTexture cellRenderTexture = RenderTexture.GetTemporary(AtlasCellSize, AtlasCellSize, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D cellPixels = new Texture2D(AtlasCellSize, AtlasCellSize, TextureFormat.RGBA32, false, false);
        RenderTexture previous = RenderTexture.active;
        Color[] sourcePixels = new Color[AtlasCellSize * AtlasCellSize];
        for (int index = 0; index < sourceSprites.Count; index++)
        {
            Sprite sprite = sourceSprites[index];
            Rect textureRect = sprite.textureRect;
            RenderTexture.active = cellRenderTexture;
            GL.Clear(true, true, Color.clear);
            Vector2 scale = new Vector2(textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height);
            Vector2 offset = new Vector2(textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height);
            Graphics.Blit(sprite.texture, cellRenderTexture, scale, offset);
            cellPixels.ReadPixels(new Rect(0f, 0f, AtlasCellSize, AtlasCellSize), 0, 0, false);
            cellPixels.Apply(false, false);
            sourcePixels = cellPixels.GetPixels();
            int column = index % AtlasColumns;
            int row = index / AtlasColumns;
            atlas.SetPixels(column * AtlasCellSize, row * AtlasCellSize,
                AtlasCellSize, AtlasCellSize, sourcePixels);
        }

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(cellRenderTexture);
        UnityEngine.Object.Destroy(cellPixels);
        atlas.Apply(false, false);

        TMP_SpriteAsset asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        asset.name = "Story Inline Expressions";
        asset.hideFlags = HideFlags.DontSave;
        // Runtime-created assets do not pass through TMP's editor importer. Mark the asset as the
        // current format before assigning a material; otherwise TMP 3.0.7 treats it as a legacy
        // asset and tries to upgrade the absent spriteInfoList.
        JsonUtility.FromJsonOverwrite(
            "{\"m_Version\":\"1.1.0\",\"m_FaceInfo\":{\"m_PointSize\":40,\"m_Scale\":1,"
            + "\"m_LineHeight\":40,\"m_AscentLine\":32,\"m_CapLine\":32,\"m_Baseline\":0,"
            + "\"m_DescentLine\":-8,\"m_TabWidth\":40}}", asset);
        asset.spriteInfoList = new List<TMP_Sprite>();
        asset.spriteGlyphTable.Clear();
        asset.spriteCharacterTable.Clear();
        asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(asset.name);
        asset.spriteSheet = atlas;
        Shader shader = Shader.Find("TextMeshPro/Sprite");
        if (shader == null)
        {
            UnityEngine.Object.Destroy(atlas);
            UnityEngine.Object.Destroy(asset);
            return null;
        }

        asset.material = new Material(shader)
        {
            name = "Story Inline Expressions Material",
            hideFlags = HideFlags.DontSave,
        };
        asset.material.SetTexture("_MainTex", atlas);

        StoryPresentationSettings settings = StoryPresentationSettings.Load();
        float expressionScale = settings.InlineExpressionScale;
        float expressionBearingY = AtlasCellSize * .82f + settings.InlineExpressionVerticalOffset;
        inlineAtlasIndices.Clear();
        inlineFrameIndices.Clear();
        for (int index = 0; index < sourceSprites.Count; index++)
        {
            int column = index % AtlasColumns;
            int row = index / AtlasColumns;
            GlyphRect glyphRect = new GlyphRect(column * AtlasCellSize, row * AtlasCellSize,
                AtlasCellSize, AtlasCellSize);
            GlyphMetrics metrics = new GlyphMetrics(AtlasCellSize, AtlasCellSize, 0f,
                expressionBearingY, AtlasCellSize);
            TMP_SpriteGlyph glyph = new TMP_SpriteGlyph((uint)index, metrics, glyphRect, 1f, 0);
            TMP_SpriteCharacter character = new TMP_SpriteCharacter(0xFFFE, asset, glyph)
            {
                name = GetTmpSpriteName(sourceIds[index]),
                scale = expressionScale,
            };
            asset.spriteGlyphTable.Add(glyph);
            asset.spriteCharacterTable.Add(character);
            inlineAtlasIndices[sourceIds[index]] = index;
            inlineFrameIndices[sourceIds[index]] = -1;
        }

        asset.UpdateLookupTables();
        inlineAtlasTexture = atlas;
        inlineSpriteAsset = asset;
        return inlineSpriteAsset;
    }

    public static List<string> GetInlineExpressionIds(string storedText)
    {
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(storedText))
            return result;

        HashSet<string> uniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MatchCollection matches = InlineTagPattern.Matches(storedText);
        foreach (Match match in matches)
        {
            string normalized = Normalize(match.Groups[1].Value);
            if (!string.IsNullOrEmpty(normalized) && uniqueIds.Add(normalized))
                result.Add(normalized);
        }

        return result;
    }

    public static void RefreshInlineExpressionFrames(IEnumerable<string> expressionIds, float elapsed)
    {
        if (inlineSpriteAsset == null || inlineAtlasTexture == null || expressionIds == null)
            return;

        bool changed = false;
        foreach (string expressionId in expressionIds)
        {
            string normalized = Normalize(expressionId);
            if (normalized == null || !StoryQQExpressionCatalog.IsAnimated(normalized)
                || !inlineAtlasIndices.TryGetValue(normalized, out int atlasIndex))
                continue;

            int frameIndex = StoryQQExpressionCatalog.GetFrameIndex(normalized, elapsed);
            if (inlineFrameIndices.TryGetValue(normalized, out int previousFrame)
                && previousFrame == frameIndex)
                continue;

            Sprite frame = StoryQQExpressionCatalog.LoadFrame(normalized, frameIndex);
            if (frame == null)
                continue;

            WriteSpriteToInlineAtlas(frame, atlasIndex);
            inlineFrameIndices[normalized] = frameIndex;
            changed = true;
        }

        if (changed)
            inlineAtlasTexture.Apply(false, false);
    }

    private static void WriteSpriteToInlineAtlas(Sprite sprite, int atlasIndex)
    {
        if (sprite == null || inlineAtlasTexture == null)
            return;

        if (inlineCellRenderTexture == null)
        {
            inlineCellRenderTexture = new RenderTexture(AtlasCellSize, AtlasCellSize, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Story Inline Expression Cell",
                hideFlags = HideFlags.DontSave,
            };
            inlineCellRenderTexture.Create();
            inlineCellPixels = new Texture2D(AtlasCellSize, AtlasCellSize, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.DontSave,
            };
        }

        Rect textureRect = sprite.textureRect;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = inlineCellRenderTexture;
        GL.Clear(true, true, Color.clear);
        Vector2 scale = new Vector2(textureRect.width / sprite.texture.width,
            textureRect.height / sprite.texture.height);
        Vector2 offset = new Vector2(textureRect.x / sprite.texture.width,
            textureRect.y / sprite.texture.height);
        Graphics.Blit(sprite.texture, inlineCellRenderTexture, scale, offset);
        inlineCellPixels.ReadPixels(new Rect(0f, 0f, AtlasCellSize, AtlasCellSize), 0, 0, false);
        inlineCellPixels.Apply(false, false);
        inlineAtlasTexture.SetPixels((atlasIndex % AtlasColumns) * AtlasCellSize,
            (atlasIndex / AtlasColumns) * AtlasCellSize, AtlasCellSize, AtlasCellSize,
            inlineCellPixels.GetPixels());
        RenderTexture.active = previous;
    }

    private static string BuildInlineTag(string id)
    {
        return "<emoji id=\"" + id + "\"/>";
    }

    private static string GetTmpSpriteName(string id)
    {
        int slash = id.LastIndexOf('/');
        return TmpSpriteNamePrefix + (slash < 0 ? id : id.Substring(slash + 1));
    }

    public static StoryExpressionMotion GetMotion(string id)
    {
        string normalized = Normalize(id);
        if (normalized == null)
            return StoryExpressionMotion.Neutral;

        string qqId = normalized.StartsWith("qq_face_", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring("qq_face_".Length)
            : normalized.Substring(normalized.LastIndexOf('/') + 1);
        switch (qqId)
        {
            case "11":
            case "18":
            case "22":
            case "31":
            case "36":
                return StoryExpressionMotion.Shake;
            case "0":
            case "26":
            case "34":
            case "37":
            case "110":
                return StoryExpressionMotion.Surprise;
            case "4":
            case "6":
            case "16":
            case "23":
            case "105":
                return StoryExpressionMotion.Tilt;
            case "7":
            case "8":
            case "9":
            case "15":
            case "25":
            case "27":
            case "96":
                return StoryExpressionMotion.Sink;
            case "12":
            case "13":
            case "14":
            case "21":
            case "28":
            case "182":
                return StoryExpressionMotion.Bounce;
            default:
                return StoryExpressionMotion.Neutral;
        }
    }
}
