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
    private const int MaxParticles = 120;
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
            case "crowd": return layer == 0 || layer == 1;
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
        float atmosphere = Mathf.InverseLerp(.32f, 1f, intensity);
        if (depth == 0)
        {
            // 二维背景需要整体色调变化，单靠细雨丝很容易被高饱和地图吃掉。
            AddRect(vh, rect, new Color(.035f, .075f, .14f, .035f + intensity * .11f));
        }
        if (depth == 1)
        {
            AddRect(vh, rect, new Color(.18f, .27f, .36f, .025f + atmosphere * .05f));
            if (atmosphere > 0f)
            {
                DrawRainMist(vh, rect, atmosphere);
                DrawRainSplashes(vh, rect, atmosphere);
            }
        }

        int[] counts = { 110, 54, 18 };
        float[] layerSpeeds = { .27f, .43f, .66f };
        float[] layerLengths = { 24f, 48f, 92f };
        float[] layerWidths = { .85f, 1.45f, 3.6f };
        float[] layerAlphas = { .28f, .44f, .22f };
        int count = Mathf.Clamp(Mathf.RoundToInt(counts[depth] * (.45f + intensity * .55f)), 1, MaxParticles);
        for (int i = 0; i < count; i++)
        {
            float fall = phase * layerSpeeds[depth] * velocities[i];
            float cycle = Mathf.Repeat(points[i].y + fall, 1.28f);
            float lifetime = cycle / 1.28f;
            float lifetimeAlpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lifetime / .07f))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((lifetime - .9f) / .1f)));
            float y = 1.14f - cycle;
            float sway = Mathf.Sin((phase * .8f + points[i].y * 6.283f) * velocities[i]) * .004f;
            float x = Mathf.Repeat(points[i].x + phase * winds[i] + sway + .06f, 1.12f) - .06f;
            Vector2 start = new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
            float length = layerLengths[depth] * sizes[i];
            float width = layerWidths[depth] * (.72f + sizes[i] * .28f);
            Color color = new Color(.76f, .9f, 1f,
                layerAlphas[depth] * intensity * opacities[i] * lifetimeAlpha);
            AddSoftStreak(vh, start, start + new Vector2(-length * .2f, -length), width, color);
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

    private void DrawRainSplashes(VertexHelper vh, Rect rect, float atmosphere)
    {
        int count = Mathf.RoundToInt(15f * atmosphere);
        for (int i = 0; i < count; i++)
        {
            int seed = 96 + i;
            float cycle = Mathf.Repeat(points[seed].x + phase * (1.05f + velocities[seed] * .28f), 1f);
            if (cycle >= .2f)
                continue;

            float progress = cycle / .2f;
            float alpha = Mathf.Sin(progress * Mathf.PI) * (1f - progress * .35f) * atmosphere;
            float x = Mathf.Repeat(points[seed].y + phase * winds[seed] * .18f, 1f);
            float y = .055f + points[seed].x * .18f;
            Vector2 center = new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
            float spread = (5f + progress * 10f) * sizes[seed];
            float rise = (4f + Mathf.Sin(progress * Mathf.PI) * 10f) * sizes[seed];
            Color splashColor = new Color(.76f, .9f, 1f, .42f * alpha * opacities[seed]);
            AddSoftStreak(vh, center, center + new Vector2(-spread, rise), 1.15f, splashColor);
            AddSoftStreak(vh, center, center + new Vector2(spread * .82f, rise * .82f), 1.05f, splashColor);
            AddSoftStreak(vh, center + Vector2.left * spread * .75f,
                center + Vector2.right * spread * .75f, .7f + progress, splashColor);
        }
    }

    private void DrawCrowd(VertexHelper vh, Rect rect)
    {
        if (depth == 0)
        {
            DrawCrowdSilhouettes(vh, rect);
            return;
        }

        DrawCrowdNoise(vh, rect);
    }

    private void DrawCrowdSilhouettes(VertexHelper vh, Rect rect)
    {
        int rowCount = intensity >= .72f ? 3 : 2;
        int seed = 0;
        for (int row = 0; row < rowCount; row++)
        {
            int count = Mathf.Clamp(Mathf.RoundToInt((10f + row * 3f) * (.65f + intensity * .55f)), 7, 18);
            float rowDepth = rowCount <= 1 ? 1f : row / (float)(rowCount - 1);
            float baseY = rect.yMin + rect.height * (.025f + row * .105f);
            float rowScale = Mathf.Lerp(.58f, 1.04f, rowDepth);
            for (int column = 0; column < count; column++, seed++)
            {
                int index = seed % MaxParticles;
                float step = rect.width / count;
                float x = rect.xMin + step * (column + .5f)
                    + (points[index].x - .5f) * step * .72f;
                x += Mathf.Sin(phase * (.36f + velocities[index] * .12f) + index * 1.71f)
                    * (1.1f + rowDepth * 1.6f);
                float personHeight = rect.height * (.105f + sizes[index] * .035f) * rowScale;
                float personWidth = personHeight * (.38f + points[index].y * .12f);
                float y = baseY + points[index].y * rect.height * .018f
                    + Mathf.Sin(phase * .55f + index * .93f) * 1.2f;
                float alpha = (.13f + rowDepth * .13f) * (.48f + intensity * .52f) * opacities[index];
                Color silhouette = Color.Lerp(new Color(.025f, .055f, .09f, alpha),
                    new Color(.075f, .11f, .145f, alpha), points[index].x);
                DrawCrowdPerson(vh, new Vector2(x, y), personWidth, personHeight, silhouette,
                    index % 5 == 0);
            }
        }

        // 低位的半透明暗带把剪影连成群体，但不会像整块遮罩一样吞掉地图细节。
        Rect baseBand = new Rect(rect.xMin, rect.yMin, rect.width, rect.height * (.045f + intensity * .035f));
        AddRect(vh, baseBand, new Color(.02f, .045f, .075f, .055f + intensity * .075f));
    }

    private static void DrawCrowdPerson(VertexHelper vh, Vector2 feet, float width, float height,
        Color color, bool robotHead)
    {
        float bodyHeight = height * .62f;
        float shoulderY = feet.y + bodyHeight;
        float headRadius = width * (robotHead ? .31f : .27f);
        float neckY = shoulderY + headRadius * .15f;
        Vector2 bodyLeft = new Vector2(feet.x - width * .42f, feet.y);
        Vector2 bodyRight = new Vector2(feet.x + width * .42f, feet.y);
        Vector2 shoulderLeft = new Vector2(feet.x - width * .58f, shoulderY - height * .08f);
        Vector2 shoulderRight = new Vector2(feet.x + width * .58f, shoulderY - height * .08f);
        AddQuad(vh, bodyLeft, shoulderLeft, shoulderRight, bodyRight, color);

        Vector2 headCenter = new Vector2(feet.x, neckY + headRadius);
        if (robotHead)
        {
            float halfWidth = headRadius * 1.12f;
            float halfHeight = headRadius * .88f;
            AddRect(vh, new Rect(headCenter.x - halfWidth, headCenter.y - halfHeight,
                halfWidth * 2f, halfHeight * 2f), color);
            return;
        }
        AddCircle(vh, headCenter, headRadius, color, 8);
    }

    private void DrawCrowdNoise(VertexHelper vh, Rect rect)
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(4f + intensity * 7f), 4, 11);
        for (int i = 0; i < count; i++)
        {
            int index = 48 + i;
            float pulse = .62f + Mathf.Sin(phase * (1.2f + velocities[index] * .35f) + i * 2.17f) * .38f;
            float x = rect.xMin + rect.width * (.07f + points[index].x * .86f);
            float y = rect.yMin + rect.height * (.22f + points[index].y * .42f);
            float scale = (5.5f + sizes[index] * 4.5f) * (.72f + intensity * .35f);
            Color line = new Color(.66f, .84f, .92f, (.06f + intensity * .1f) * pulse * opacities[index]);
            bool faceRight = index % 2 == 0;
            float direction = faceRight ? 1f : -1f;
            Vector2 origin = new Vector2(x, y);
            for (int arc = 0; arc < 2; arc++)
            {
                float radius = scale * (1f + arc * .7f);
                Vector2 middle = origin + new Vector2(direction * radius * .65f, radius * .3f);
                Vector2 end = origin + new Vector2(direction * radius, radius);
                AddSoftStreak(vh, origin + Vector2.up * arc * 1.4f, middle, .65f + arc * .2f, line);
                AddSoftStreak(vh, middle, end, .65f + arc * .2f, line);
            }
        }
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
        Vector2 direction = end - start;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * width;
        const int rows = 4;
        const int columns = 3;
        int first = vh.currentVertCount;
        for (int row = 0; row < rows; row++)
        {
            float along = row / (float)(rows - 1);
            Vector2 center = Vector2.Lerp(start, end, along);
            float lengthFade = row == 0 || row == rows - 1 ? 0f : 1f;
            for (int column = 0; column < columns; column++)
            {
                float across = column - 1f;
                UIVertex vertex = UIVertex.simpleVert;
                Color vertexColor = color;
                vertexColor.a *= lengthFade * (column == 1 ? 1f : 0f);
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
