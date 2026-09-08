using UnityEngine;
using UnityEngine.UI;

/// <summary>对白底板的小圆角；直接绘制网格，避免背景贴图拉伸。</summary>
public sealed class StoryDialogueBackground : Image
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        float radius = Mathf.Min(12f, Mathf.Min(rect.width, rect.height) * .5f);
        float feather = Mathf.Min(1f, radius * .25f);
        const int steps = 10;
        const int count = 4 * (steps + 1);
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = rect.center;
        vh.AddVert(vertex);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                corner < 2 ? rect.yMax - radius : rect.yMin + radius);
            for (int step = 0; step <= steps; step++)
            {
                float angle = (corner * 90f + step * 90f / steps) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertex.position = center + direction * (radius - feather);
                vertex.color = color;
                vh.AddVert(vertex);
                vertex.position = center + direction * radius;
                Color edgeColor = color;
                edgeColor.a = 0f;
                vertex.color = edgeColor;
                vh.AddVert(vertex);
            }
        }
        for (int i = 0; i < count; i++)
        {
            int inner = 1 + i * 2;
            int next = 1 + ((i + 1) % count) * 2;
            vh.AddTriangle(0, inner, next);
            vh.AddTriangle(inner, inner + 1, next + 1);
            vh.AddTriangle(inner, next + 1, next);
        }
    }
}
