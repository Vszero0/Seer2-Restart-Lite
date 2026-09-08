using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 场景级环境表现。每个效果在每个深度只使用一个 UI Graphic，避免为粒子创建大量对象。
/// </summary>
public sealed class StoryEnvironmentStage
{
    private const int MaxTransientVisuals = 6;
    private readonly RectTransform[] layers;
    private readonly List<GameObject> continuousVisuals = new List<GameObject>();
    private readonly List<GameObject> effectVisuals = new List<GameObject>();
    private bool visible = true;
    private string currentSignature;
    private string previewEffectKey;
    private int effectSequence;

    public StoryEnvironmentStage(RectTransform farLayer, RectTransform atmosphereLayer, RectTransform nearLayer)
    {
        layers = new[] { farLayer, atmosphereLayer, nearLayer };
    }

    public void Apply(params StoryEnvironmentDocument[] environments)
    {
        StoryEnvironmentDocument[] active = (environments ?? Array.Empty<StoryEnvironmentDocument>())
            .Where(value => value != null && value.normalizedType != "none")
            .GroupBy(value => value.normalizedType, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(value => value.normalizedType == "rain" ? 0 : 1)
            .Take(2)
            .ToArray();
        string signature = string.Join(";", active.Select(BuildSignature));
        if (string.Equals(currentSignature, signature, StringComparison.Ordinal))
            return;

        ClearContinuous();
        currentSignature = signature;
        foreach (StoryEnvironmentDocument environment in active)
        {
            for (int layer = 0; layer < layers.Length; layer++)
                CreateVisual(environment, layer, false, continuousVisuals);
        }
    }

    public void PlayEffect(StoryEnvironmentDocument effect)
    {
        if (effect == null || effect.normalizedType != "celebration")
            return;

        effectVisuals.RemoveAll(value => value == null);
        int randomSeed = ++effectSequence;
        for (int layer = 0; layer < layers.Length; layer++)
            CreateVisual(effect, layer, true, effectVisuals, randomSeed);
        TrimTransientVisuals();
    }

    public void PreviewEffect(StoryEnvironmentDocument effect, string previewKey)
    {
        effectVisuals.RemoveAll(value => value == null);
        if (effect == null || effect.normalizedType == "none")
        {
            if (effectVisuals.Count > 0 || previewEffectKey != null)
                ClearEffects();
            return;
        }
        if (effectVisuals.Count > 0
            && string.Equals(previewEffectKey, previewKey, StringComparison.Ordinal))
            return;

        ClearEffects();
        previewEffectKey = previewKey;
        PlayEffect(effect);
    }

    private void TrimTransientVisuals()
    {
        while (effectVisuals.Count > MaxTransientVisuals)
        {
            GameObject oldest = effectVisuals[0];
            effectVisuals.RemoveAt(0);
            if (oldest != null)
            {
                oldest.SetActive(false);
                UnityEngine.Object.Destroy(oldest);
            }
        }
    }

    private void CreateVisual(StoryEnvironmentDocument environment, int layer, bool oneShot,
        ICollection<GameObject> target, int randomSeed = 0)
    {
        string type = environment?.normalizedType ?? "none";
        if (layers[layer] == null || !StoryEnvironmentGraphic.SupportsLayer(type, layer))
            return;

        GameObject value = new GameObject("Story Environment " + type + " " + layer,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(StoryEnvironmentGraphic));
        value.transform.SetParent(layers[layer], false);
        RectTransform rect = value.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        value.GetComponent<StoryEnvironmentGraphic>().Configure(type, layer, environment.normalizedIntensity,
            environment.normalizedSpeed, environment.normalizedDuration, oneShot, randomSeed);
        value.SetActive(visible);
        target.Add(value);
    }

    public void SetVisible(bool value)
    {
        visible = value;
        foreach (GameObject visual in continuousVisuals.Concat(effectVisuals))
        {
            if (visual != null)
                visual.SetActive(value);
        }
    }

    public void Clear()
    {
        ClearContinuous();
        ClearEffects();
    }

    public void ClearEffects()
    {
        previewEffectKey = null;
        DestroyVisuals(effectVisuals);
    }

    private void ClearContinuous()
    {
        currentSignature = null;
        DestroyVisuals(continuousVisuals);
    }

    private static void DestroyVisuals(ICollection<GameObject> visuals)
    {
        foreach (GameObject visual in visuals)
        {
            if (visual != null)
            {
                visual.SetActive(false);
                UnityEngine.Object.Destroy(visual);
            }
        }
        visuals.Clear();
    }

    private static string BuildSignature(StoryEnvironmentDocument environment)
    {
        return environment.normalizedType + "|" + environment.normalizedIntensity.ToString("0.000")
            + "|" + environment.normalizedSpeed.ToString("0.000");
    }
}

/// <summary>
/// 以一个动态网格绘制一层环境效果。几何边缘保持矢量清晰，不依赖地图贴图的过滤模式。
/// </summary>
public sealed class StoryEnvironmentGraphic : MaskableGraphic
{
    private const int MaxParticles = 320;
    private static readonly int[] RainCounts = { 190, 280, 32 };
    private static readonly float[] RainSpeeds = { .48f, .78f, 1.16f };
    private static readonly float[] RainLengths = { 36f, 68f, 124f };
    private static readonly float[] RainWidths = { .8f, 1.35f, 2.8f };
    private static readonly float[] RainAlphas = { .42f, .76f, .46f };
    private static readonly Color[] CelebrationPalette =
    {
        new Color(1f, .79f, .2f, 1f),
        new Color(.22f, .88f, 1f, 1f),
        new Color(1f, .32f, .48f, 1f),
        new Color(.56f, 1f, .38f, 1f),
        new Color(.78f, .46f, 1f, 1f),
        new Color(1f, .55f, .18f, 1f)
    };
    private readonly Vector2[] points = new Vector2[MaxParticles];
    private readonly float[] sizes = new float[MaxParticles];
    private readonly float[] velocities = new float[MaxParticles];
    private readonly float[] opacities = new float[MaxParticles];
    private readonly float[] winds = new float[MaxParticles];
    private string effectType = "none";
    private int depth;
    private float intensity;
    private float speed;
    private float phase;
    private float elapsed;
    private float duration;
    private bool oneShot;

    public static bool SupportsLayer(string type, int layer)
    {
        switch (type)
        {
            case "rain": return layer >= 0 && layer <= 2;
            case "crowd": return layer == 2;
            case "celebration": return layer == 1 || layer == 2;
            default: return false;
        }
    }

    public void Configure(string type, int layer, float effectIntensity, float effectSpeed,
        float effectDuration, bool isOneShot, int randomSeed = 0)
    {
        effectType = type;
        depth = layer;
        intensity = effectIntensity;
        speed = effectSpeed;
        duration = effectDuration;
        oneShot = isOneShot;
        raycastTarget = false;

        System.Random random = new System.Random(391 + layer * 97 + StableHash(type) + randomSeed * 7919);
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
            sizes[i] = .65f + (float)random.NextDouble() * .75f;
            velocities[i] = .76f + (float)random.NextDouble() * .48f;
            opacities[i] = .55f + (float)random.NextDouble() * .45f;
            winds[i] = -.022f - (float)random.NextDouble() * .025f;
        }
        SetVerticesDirty();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;
        float deltaTime = Time.unscaledDeltaTime;
        elapsed += deltaTime;
        phase += deltaTime * speed;
        if (oneShot && elapsed >= duration)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (effectType == "rain")
            DrawRain(vh, rect);
        else if (effectType == "crowd")
            DrawCrowd(vh, rect);
        else if (effectType == "celebration")
            DrawCelebration(vh, rect);
    }

    private void DrawRain(VertexHelper vh, Rect rect)
    {
        if (rect.width <= 0f || rect.height <= 0f)
            return;
        float scale = rect.height / 600f;
        float atmosphere = Mathf.InverseLerp(.32f, 1f, intensity);
        if (depth == 0)
        {
            // 二维背景需要整体色调变化，单靠细雨丝很容易被高饱和地图吃掉。
            AddRect(vh, rect, new Color(.035f, .075f, .14f, .08f + intensity * .16f));
        }
        if (depth == 1)
        {
            if (atmosphere > 0f)
                DrawRainMist(vh, rect, atmosphere * .45f);
            DrawRainSplashes(vh, rect, scale);
        }

        int count = Mathf.Clamp(Mathf.RoundToInt(RainCounts[depth] * (.35f + intensity * .65f)), 1, MaxParticles);
        for (int i = 0; i < count; i++)
        {
            float fall = phase * RainSpeeds[depth] * velocities[i];
            float cycle = Mathf.Repeat(points[i].y + fall, 1.28f);
            float lifetime = cycle / 1.28f;
            float lifetimeAlpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lifetime / .07f))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((lifetime - .9f) / .1f)));
            float y = 1.14f - cycle;
            float slant = .16f + points[i].y * .06f;
            float x = Mathf.Repeat(points[i].x - fall * slant * rect.height / rect.width, 1.16f) - .08f;
            Vector2 start = new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
            float length = RainLengths[depth] * sizes[i] * scale;
            float width = RainWidths[depth] * (.72f + sizes[i] * .28f) * scale;
            Color color = new Color(.8f, .91f, 1f,
                RainAlphas[depth] * (.55f + intensity * .45f) * opacities[i] * lifetimeAlpha);
            AddRainStreak(vh, start, start + new Vector2(-length * slant, -length), width, color);
        }
    }

    private void DrawRainMist(VertexHelper vh, Rect rect, float atmosphere)
    {
        for (int i = 0; i < 4; i++)
        {
            float centerX = Mathf.Repeat(points[84 + i].x + phase * (.012f + i * .003f), 1.42f) - .21f;
            float centerY = .1f + i * .105f + points[90 + i].y * .035f;
            float width = rect.width * (.58f + sizes[84 + i] * .13f);
            float height = rect.height * (.075f + sizes[90 + i] * .025f);
            Rect band = new Rect(rect.xMin + centerX * rect.width - width * .5f,
                rect.yMin + centerY * rect.height - height * .5f, width, height);
            AddSoftMistBand(vh, band, new Color(.65f, .75f, .8f, (.04f + i * .008f) * atmosphere));
        }
    }

    private void DrawRainSplashes(VertexHelper vh, Rect rect, float scale)
    {
        int count = Mathf.RoundToInt(24f + 44f * intensity);
        for (int i = 0; i < count; i++)
        {
            int seed = 96 + i;
            float cycle = Mathf.Repeat(points[seed].x + phase * (.85f + velocities[seed] * .28f), 1f);
            if (cycle >= .32f)
                continue;

            float progress = cycle / .32f;
            float alpha = Mathf.Sin(progress * Mathf.PI) * (1f - progress * .35f) * (.5f + intensity * .5f);
            float x = Mathf.Repeat(points[seed].y + phase * winds[seed] * .18f, 1f);
            // 分布在整个下部地面，左右侧与对白框上方也能看到水花。
            float y = .035f + points[seed].x * .32f;
            Vector2 center = new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
            float spread = (4f + progress * 11f) * sizes[seed] * scale;
            float rise = (3f + Mathf.Sin(progress * Mathf.PI) * 8f) * sizes[seed] * scale;
            Color splashColor = new Color(.8f, .92f, 1f, .72f * alpha * opacities[seed]);
            AddRainStreak(vh, center, center + new Vector2(-spread, rise), .85f * scale, splashColor);
            AddRainStreak(vh, center, center + new Vector2(spread * .82f, rise * .82f), .8f * scale, splashColor);
            AddSoftStreak(vh, center + Vector2.left * spread * .75f,
                center + Vector2.right * spread * .75f, (.7f + progress) * scale, splashColor);
        }
    }

    private void DrawCrowd(VertexHelper vh, Rect rect)
    {
        if (rect.width <= 0f || rect.height <= 0f)
            return;
        // 先画远排、再画近排，肩部互相遮挡；中间低、两侧高，留出主要角色的空间。
        for (int row = 0; row < 2; row++)
        {
            float nominalHeight = rect.height * (row == 0 ? .22f : .27f);
            float nominalWidth = nominalHeight * .64f;
            int count = Mathf.Clamp(Mathf.CeilToInt(rect.width / (nominalWidth * .7f)), 12, 48);
            float step = rect.width / (count - 1);
            for (int column = 0; column < count; column++)
            {
                int index = row * 48 + column;
                float u = column / (float)(count - 1);
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.Abs(u - .5f) * 2f);
                float x = rect.xMin + step * column + (points[index].x - .5f) * step * .3f;
                x += Mathf.Sin(phase * .48f * velocities[index] + index * 1.71f) * rect.height * .0025f;
                float personHeight = nominalHeight * (.85f + sizes[index] * .16f)
                    * (.72f + intensity * .28f);
                float personWidth = nominalWidth * (.92f + points[index].y * .2f);
                float top = rect.yMin + rect.height * ((row == 0 ? .16f : .10f)
                    + edge * (.14f + intensity * .08f) + points[index].y * .035f);
                Color silhouette = Color.Lerp(new Color(.055f, .085f, .12f, 1f),
                    new Color(.12f, .17f, .21f, 1f), points[index].x);
                if (row == 1)
                    silhouette = Color.Lerp(silhouette, new Color(.025f, .04f, .065f, 1f), .5f);
                DrawCrowdSilhouette(vh, new Vector2(x, top - personHeight), personWidth, personHeight,
                    silhouette, Mathf.Min(5, Mathf.FloorToInt(points[index].x * 6f)), rect.yMin);
            }
        }
        // 偶尔有一位从前方经过；一轮大部分时间留空，不形成持续横向传送带。
        float crossing = Mathf.Repeat(phase / 26f + .66f, 1f);
        if (crossing < .36f)
        {
            float u = Mathf.Lerp(-.14f, 1.14f, crossing / .36f);
            float height = rect.height * .24f;
            DrawCrowdSilhouette(vh, new Vector2(rect.xMin + u * rect.width, rect.yMin - height * .32f),
                height * .68f, height, new Color(.025f, .04f, .06f, 1f), 2, rect.yMin);
        }
    }

    private static void DrawCrowdSilhouette(VertexHelper vh, Vector2 feet, float width, float height,
        Color color, int variant, float bottomY)
    {
        // 只表现头肩体积，不带面孔、耳部、天线或服饰等身份特征。
        float shoulderY = feet.y + height * .5f;
        float shoulderWidth = width * (variant == 1 || variant == 4 ? .6f : .53f);
        float shoulderRise = height * (variant == 3 ? .19f : .14f);
        float bodyBottom = Mathf.Min(feet.y, bottomY);
        AddQuad(vh, new Vector2(feet.x - width * .48f, bodyBottom),
            new Vector2(feet.x - shoulderWidth, shoulderY),
            new Vector2(feet.x + shoulderWidth, shoulderY),
            new Vector2(feet.x + width * .48f, bodyBottom), color);
        AddCrowdContour(vh, new Vector2(feet.x, shoulderY),
            new Vector2(shoulderWidth, shoulderRise), 0f, 16, color);
        float lean = (variant % 3 - 1) * width * .045f;
        AddRect(vh, new Rect(feet.x + lean - width * .12f, shoulderY,
            width * .24f, height * .24f), color);
        Vector2 head = new Vector2(feet.x + lean, feet.y + height * .79f);
        float headWidth = width * (variant == 1 ? .39f : variant == 2 ? .25f : .32f);
        float headHeight = height * (variant == 2 ? .2f : variant == 4 ? .14f : .17f);
        AddCrowdContour(vh, head, new Vector2(headWidth, headHeight),
            (variant % 3 - 1) * .12f, variant >= 4 ? 8 : 16, color);
    }

    private static void AddCrowdContour(VertexHelper vh, Vector2 center, Vector2 radius,
        float tilt, int segments, Color color)
    {
        int first = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = center;
        vh.AddVert(vertex);
        float cos = Mathf.Cos(tilt);
        float sin = Mathf.Sin(tilt);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector2 point = new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);
            vertex.position = center + new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
            vh.AddVert(vertex);
        }
        for (int i = 0; i < segments; i++)
            vh.AddTriangle(first, first + i + 1, first + i + 2);
    }

    private void DrawCelebration(VertexHelper vh, Rect rect)
    {
        int baseCount = depth == 1 ? 76 : 28;
        int count = Mathf.Clamp(Mathf.RoundToInt(baseCount * (.5f + intensity * .5f)), 10, MaxParticles);
        float animationTime = phase;
        for (int i = 0; i < count; i++)
        {
            float delayWindow = Mathf.Min(duration * .42f, 1.45f);
            float delay = points[i].y * delayWindow + (i % 3) * .055f;
            float localTime = (elapsed - delay) * speed;
            float life = (depth == 1 ? 1.95f : 1.5f) * (.82f + velocities[i] * .22f);
            if (localTime < 0f || localTime > life)
                continue;

            float progress = localTime / life;
            bool fromLeft = (i & 1) == 0;
            float side = fromLeft ? 1f : -1f;
            float startX = fromLeft ? -.025f : 1.025f;
            float horizontalTravel = .29f + points[i].x * .28f;
            float x = startX + side * horizontalTravel * progress;
            x += Mathf.Sin(progress * Mathf.PI * (2.3f + points[i].y * 2.2f) + i) * .012f;
            float launch = .62f + points[i].y * .37f;
            float gravity = .7f + points[i].x * .3f;
            float y = .045f + launch * progress - gravity * progress * progress * .52f;
            y += Mathf.Sin(i * 1.37f) * .025f;

            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / .07f));
            float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - .72f) / .28f));
            float alpha = fadeIn * fadeOut * opacities[i] * (.65f + intensity * .35f);
            Vector2 center = new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
            float particleScale = depth == 1 ? 1f : 1.55f;
            float width = (3.2f + sizes[i] * 3.8f) * particleScale;
            float height = width * (.34f + points[i].x * .28f);
            float angle = (points[i].x * 360f + animationTime * (130f + velocities[i] * 90f))
                * (fromLeft ? 1f : -1f);
            Color paper = GetCelebrationColor(i, alpha * (depth == 1 ? .92f : .78f));
            AddRotatedRect(vh, center, new Vector2(width, height), angle, paper);

            // 少量长彩带让两侧喷发方向更明确，同时仍维持单 Graphic 网格。
            if (i % 7 == 0)
            {
                Vector2 tail = center - new Vector2(side * width * 2.2f, height * 1.4f);
                AddSoftStreak(vh, tail, center, Mathf.Max(.65f, height * .22f), paper);
            }
        }

        DrawCelebrationMuzzle(vh, rect, animationTime);
    }

    private void DrawCelebrationMuzzle(VertexHelper vh, Rect rect, float animationTime)
    {
        if (animationTime > .42f)
            return;
        float pulse = Mathf.Sin(Mathf.Clamp01(animationTime / .42f) * Mathf.PI);
        float alpha = pulse * intensity * (depth == 1 ? .24f : .14f);
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            float side = sideIndex == 0 ? 1f : -1f;
            Vector2 origin = new Vector2(sideIndex == 0 ? rect.xMin : rect.xMax,
                rect.yMin + rect.height * .055f);
            for (int ray = 0; ray < 4; ray++)
            {
                float rise = rect.height * (.035f + ray * .022f);
                float reach = rect.width * (.025f + ray * .012f);
                Color rayColor = GetCelebrationColor(ray + sideIndex * 3, alpha);
                AddSoftStreak(vh, origin, origin + new Vector2(side * reach, rise), 1.1f, rayColor);
            }
        }
    }

    private static Color GetCelebrationColor(int index, float alpha)
    {
        Color result = CelebrationPalette[Mathf.Abs(index) % CelebrationPalette.Length];
        result.a = alpha;
        return result;
    }

    private static void AddSoftMistBand(VertexHelper vh, Rect rect, Color color)
    {
        const int columns = 7;
        const int rows = 5;
        int first = vh.currentVertCount;
        for (int row = 0; row < rows; row++)
        {
            float v = row / (float)(rows - 1);
            float verticalFade = Mathf.Sin(v * Mathf.PI);
            for (int column = 0; column < columns; column++)
            {
                float u = column / (float)(columns - 1);
                float horizontalFade = Mathf.Sin(u * Mathf.PI);
                UIVertex vertex = UIVertex.simpleVert;
                Color vertexColor = color;
                vertexColor.a *= verticalFade * horizontalFade;
                vertex.color = vertexColor;
                vertex.position = new Vector2(rect.xMin + rect.width * u, rect.yMin + rect.height * v);
                vh.AddVert(vertex);
            }
        }

        for (int row = 0; row < rows - 1; row++)
        {
            for (int column = 0; column < columns - 1; column++)
            {
                int a = first + row * columns + column;
                int b = a + 1;
                int c = a + columns;
                int d = c + 1;
                vh.AddTriangle(a, c, d);
                vh.AddTriangle(a, d, b);
            }
        }
    }

    private static void AddSoftStreak(VertexHelper vh, Vector2 start, Vector2 end, float width, Color color)
    {
        AddStreak(vh, start, end, width, color, false);
    }

    private static void AddRainStreak(VertexHelper vh, Vector2 start, Vector2 end, float width, Color color)
    {
        AddStreak(vh, start, end, width, color, true);
    }

    private static void AddStreak(VertexHelper vh, Vector2 start, Vector2 end, float width, Color color,
        bool solidCore)
    {
        Vector2 direction = end - start;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * width;
        const int rows = 4;
        int columns = solidCore ? 4 : 3;
        int first = vh.currentVertCount;
        for (int row = 0; row < rows; row++)
        {
            float along = row / (float)(rows - 1);
            Vector2 center = Vector2.Lerp(start, end, along);
            float lengthFade = row == 0 || row == rows - 1 ? 0f : 1f;
            for (int column = 0; column < columns; column++)
            {
                float across = solidCore ? (column == 0 ? -1f : column == 1 ? -.4f : column == 2 ? .4f : 1f)
                    : column - 1f;
                UIVertex vertex = UIVertex.simpleVert;
                Color vertexColor = color;
                vertexColor.a *= lengthFade * (column > 0 && column < columns - 1 ? 1f : 0f);
                vertex.color = vertexColor;
                vertex.position = center + normal * across;
                vh.AddVert(vertex);
            }
        }

        for (int row = 0; row < rows - 1; row++)
        {
            for (int column = 0; column < columns - 1; column++)
            {
                int a = first + row * columns + column;
                int b = a + 1;
                int c = a + columns;
                int d = c + 1;
                vh.AddTriangle(a, c, d);
                vh.AddTriangle(a, d, b);
            }
        }
    }

    private static void AddRect(VertexHelper vh, Rect rect, Color color)
    {
        AddQuad(vh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax),
            new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin), color);
    }

    private static void AddRotatedRect(VertexHelper vh, Vector2 center, Vector2 size, float degrees, Color color)
    {
        float radians = degrees * Mathf.Deg2Rad;
        Vector2 axisX = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * size.x * .5f;
        Vector2 axisY = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * size.y * .5f;
        AddQuad(vh, center - axisX - axisY, center - axisX + axisY,
            center + axisX + axisY, center + axisX - axisY, color);
    }

    private static void AddCircle(VertexHelper vh, Vector2 center, float radius, Color color, int segments)
    {
        int count = Mathf.Max(3, segments);
        int first = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = center;
        vh.AddVert(vertex);
        for (int i = 0; i <= count; i++)
        {
            float angle = i / (float)count * Mathf.PI * 2f;
            vertex.position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            vh.AddVert(vertex);
        }
        for (int i = 0; i < count; i++)
            vh.AddTriangle(first, first + i + 1, first + i + 2);
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int start = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = a; vh.AddVert(vertex);
        vertex.position = b; vh.AddVert(vertex);
        vertex.position = c; vh.AddVert(vertex);
        vertex.position = d; vh.AddVert(vertex);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value ?? string.Empty)
                hash = hash * 31 + character;
            return hash;
        }
    }
}
