using UnityEngine;
using UnityEngine.UI;

/// <summary>剧情对白九宫格底板；资源路径、颜色和透明度由剧情表现设置统一控制。</summary>
public sealed class StoryDialogueBackground : Image
{
    private Sprite runtimeSprite;
    private Texture2D runtimeTexture;
    private Vector4 runtimeBorder;

    protected override void Awake()
    {
        base.Awake();
        ApplyPresentationSettings();
    }

    public void ApplyPresentationSettings()
    {
        StoryPresentationSettings settings = StoryPresentationSettings.Load();
        Texture2D panelTexture = Resources.Load<Texture2D>(settings.DialogueBackgroundSpriteResourcePath);
        if (panelTexture != null)
        {
            Vector4 border = ClampBorder(settings.DialogueBackgroundBorder, panelTexture);
            if (runtimeSprite == null || runtimeTexture != panelTexture || runtimeBorder != border)
            {
                if (runtimeSprite != null)
                    Destroy(runtimeSprite);

                runtimeTexture = panelTexture;
                runtimeBorder = border;
                runtimeSprite = Sprite.Create(panelTexture,
                    new Rect(0f, 0f, panelTexture.width, panelTexture.height),
                    new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, border, false);
            }

            sprite = runtimeSprite;
            type = Type.Sliced;
            fillCenter = true;
            preserveAspect = false;
        }
        else
        {
            Sprite panelSprite = Resources.Load<Sprite>(settings.DialogueBackgroundSpriteResourcePath);
            if (panelSprite != null)
            {
                sprite = panelSprite;
                type = Type.Sliced;
                fillCenter = true;
                preserveAspect = false;
            }
        }

        color = settings.DialogueBackgroundColor;
        raycastTarget = false;
        SetAllDirty();
    }

    protected override void OnDestroy()
    {
        if (runtimeSprite != null)
            Destroy(runtimeSprite);
        runtimeSprite = null;
        runtimeTexture = null;
        base.OnDestroy();
    }

    private static Vector4 ClampBorder(Vector4 border, Texture2D texture)
    {
        border.x = Mathf.Clamp(border.x, 0f, texture.width);
        border.z = Mathf.Clamp(border.z, 0f, texture.width - border.x);
        border.y = Mathf.Clamp(border.y, 0f, texture.height);
        border.w = Mathf.Clamp(border.w, 0f, texture.height - border.y);
        return border;
    }
}
