using UnityEngine;

public enum StoryTextRevealStyle
{
    FadeScale,
    Fade,
    RiseFade,
}

/// <summary>
/// Shared presentation tuning for story playback and story previews.
/// Keep this separate from StoryDocument so changing the visual style does not
/// modify a story's authored content.
/// </summary>
[CreateAssetMenu(fileName = "StoryPresentationSettings", menuName = "剧情/剧情播放器表现设置")]
public sealed class StoryPresentationSettings : ScriptableObject
{
    public const string ResourcePath = "Story/StoryPresentationSettings";

    [Header("逐字动画")]
    [InspectorName("初始延迟")]
    [SerializeField, Min(0f)] private float textInitialDelay = 0.1f;
    [InspectorName("每字间隔")]
    [SerializeField, Min(0.001f)] private float textCharacterInterval = 0.055f;
    [InspectorName("单字动画时长")]
    [SerializeField, Min(0.001f)] private float textCharacterDuration = 0.18f;
    [InspectorName("文字出现方式")]
    [SerializeField] private StoryTextRevealStyle textRevealStyle = StoryTextRevealStyle.FadeScale;
    [InspectorName("初始缩放比例")]
    [SerializeField, Range(0.9f, 1f)] private float textRevealScaleFrom = 0.98f;
    [InspectorName("上滑距离")]
    [SerializeField, Min(0f)] private float textRiseDistance = 6f;
    [InspectorName("短标点停顿")]
    [SerializeField, Min(0f)] private float shortPunctuationPause = 0.08f;
    [InspectorName("长标点停顿")]
    [SerializeField, Min(0f)] private float longPunctuationPause = 0.16f;
    [InspectorName("换行停顿")]
    [SerializeField, Min(0f)] private float newlinePause = 0.1f;

    [Header("对白文字")]
    [InspectorName("行距")]
    [SerializeField, Min(0f)] private float textLineSpacing = 10f;

    [Header("对白表情")]
    [InspectorName("缩放比例")]
    [SerializeField, Range(0.5f, 2f)] private float inlineExpressionScale = 1.25f;
    [InspectorName("垂直偏移")]
    [SerializeField, Range(-16f, 16f)] private float inlineExpressionVerticalOffset;

    [Header("对白底板")]
    [InspectorName("底板资源路径")]
    [SerializeField] private string dialogueBackgroundSpriteResourcePath = "Story/UI/StoryDialoguePanelV2";
    [InspectorName("底板颜色")]
    [SerializeField] private Color dialogueBackgroundTint = Color.white;
    [InspectorName("底板透明度")]
    [SerializeField, Range(0f, 1f)] private float dialogueBackgroundOpacity = 0.88f;
    [InspectorName("九宫格边界（左 下 右 上）")]
    [SerializeField] private Vector4 dialogueBackgroundBorder = new Vector4(180f, 180f, 180f, 180f);

    [Header("剧情动态表情")]
    [InspectorName("启用动态表情")]
    [SerializeField] private bool storyExpressionAnimationEnabled = true;
    [InspectorName("播放速度")]
    [SerializeField, Range(0.25f, 2f)] private float storyExpressionAnimationSpeed = 1f;
    [InspectorName("动画资源缓存数量")]
    [SerializeField, Range(1, 16)] private int storyExpressionSheetCacheSize = 8;

    [Header("景深焦点")]
    [InspectorName("启用景深焦点")]
    [SerializeField] private bool depthFocusEnabled = true;
    [InspectorName("背景模糊")]
    [SerializeField, Range(0f, 4f)] private float backgroundBlur = 1.2f;
    [InspectorName("背景模糊强度")]
    [SerializeField, Range(0f, 1f)] private float backgroundBlurStrength = 1f;
    [InspectorName("背景模糊渲染比例")]
    [SerializeField, Range(0.25f, 1f)] private float backgroundBlurRenderScale = 0.5f;
    [InspectorName("非当前角色模糊")]
    [SerializeField, Range(0f, 4f)] private float inactiveActorBlur = 0.6f;
    [InspectorName("非当前角色亮度")]
    [SerializeField, Range(0.3f, 1f)] private float inactiveActorBrightness = 0.75f;
    [InspectorName("焦点切换时长")]
    [SerializeField, Min(0f)] private float depthFocusTransitionDuration = 0.2f;

    [Header("角色焦点进入")]
    [InspectorName("普通切换上提距离")]
    [SerializeField, Min(0f)] private float activeActorEntryLift = 5f;
    [InspectorName("普通切换动效时长")]
    [SerializeField, Min(0.05f)] private float activeActorEntryDuration = 0.28f;

    [Header("角色焦点呼吸")]
    [InspectorName("启用当前角色呼吸")]
    [SerializeField] private bool activeActorBreathingEnabled = true;
    [InspectorName("呼吸缩放幅度")]
    [SerializeField, Range(0f, 0.04f)] private float activeActorBreathingScale = 0.006f;
    [InspectorName("呼吸周期")]
    [SerializeField, Min(0.5f)] private float activeActorBreathingPeriod = 3f;

    [Header("背景鼠标视差")]
    [InspectorName("启用背景鼠标视差")]
    [SerializeField] private bool backgroundMotionEnabled = true;
    [InspectorName("最大偏移")]
    [SerializeField, Min(0f)] private float backgroundMotionMaxOffset = 18f;
    [InspectorName("跟随平滑时间")]
    [SerializeField, Min(0.001f)] private float backgroundMotionSmoothing = 0.12f;
    [InspectorName("背景放大")]
    [SerializeField, Range(1f, 1.25f)] private float backgroundMotionScale = 1.06f;

    public float TextInitialDelay => Mathf.Max(0f, textInitialDelay);
    public float TextCharacterInterval => Mathf.Max(0.001f, textCharacterInterval);
    public float TextCharacterDuration => Mathf.Max(0.001f, textCharacterDuration);
    public StoryTextRevealStyle TextRevealStyle => textRevealStyle;
    public float TextRevealScaleFrom => Mathf.Clamp(textRevealScaleFrom, 0.9f, 1f);
    public float TextRiseDistance => Mathf.Max(0f, textRiseDistance);
    public float ShortPunctuationPause => Mathf.Max(0f, shortPunctuationPause);
    public float LongPunctuationPause => Mathf.Max(0f, longPunctuationPause);
    public float NewlinePause => Mathf.Max(0f, newlinePause);
    public float TextLineSpacing => Mathf.Max(0f, textLineSpacing);
    public float InlineExpressionScale => Mathf.Clamp(inlineExpressionScale, 0.5f, 2f);
    public float InlineExpressionVerticalOffset => Mathf.Clamp(inlineExpressionVerticalOffset, -16f, 16f);
    public string DialogueBackgroundSpriteResourcePath => string.IsNullOrWhiteSpace(dialogueBackgroundSpriteResourcePath)
        ? "Story/UI/StoryDialoguePanelV2"
        : dialogueBackgroundSpriteResourcePath.Trim();
    public Color DialogueBackgroundColor
    {
        get
        {
            Color result = dialogueBackgroundTint;
            result.a = Mathf.Clamp01(dialogueBackgroundOpacity) * dialogueBackgroundTint.a;
            return result;
        }
    }
    public Vector4 DialogueBackgroundBorder => new Vector4(
        Mathf.Max(0f, dialogueBackgroundBorder.x),
        Mathf.Max(0f, dialogueBackgroundBorder.y),
        Mathf.Max(0f, dialogueBackgroundBorder.z),
        Mathf.Max(0f, dialogueBackgroundBorder.w));
    public bool StoryExpressionAnimationEnabled => storyExpressionAnimationEnabled;
    public float StoryExpressionAnimationSpeed => Mathf.Clamp(storyExpressionAnimationSpeed, 0.25f, 2f);
    public int StoryExpressionSheetCacheSize => Mathf.Clamp(storyExpressionSheetCacheSize, 1, 16);
    public bool DepthFocusEnabled => depthFocusEnabled;
    public float BackgroundBlur => Mathf.Clamp(backgroundBlur, 0f, 4f);
    public float BackgroundBlurStrength => Mathf.Clamp01(backgroundBlurStrength);
    public float BackgroundBlurRenderScale => Mathf.Clamp(backgroundBlurRenderScale, 0.25f, 1f);
    public float InactiveActorBlur => Mathf.Clamp(inactiveActorBlur, 0f, 4f);
    public float InactiveActorBrightness => Mathf.Clamp(inactiveActorBrightness, 0.3f, 1f);
    public float DepthFocusTransitionDuration => Mathf.Max(0f, depthFocusTransitionDuration);
    public float ActiveActorEntryLift => Mathf.Max(0f, activeActorEntryLift);
    public float ActiveActorEntryDuration => Mathf.Max(0.05f, activeActorEntryDuration);
    public bool ActiveActorBreathingEnabled => activeActorBreathingEnabled;
    public float ActiveActorBreathingScale => Mathf.Clamp(activeActorBreathingScale, 0f, 0.04f);
    public float ActiveActorBreathingPeriod => Mathf.Max(0.5f, activeActorBreathingPeriod);
    public bool BackgroundMotionEnabled => backgroundMotionEnabled;
    public float BackgroundMotionMaxOffset => Mathf.Max(0f, backgroundMotionMaxOffset);
    public float BackgroundMotionSmoothing => Mathf.Max(0.001f, backgroundMotionSmoothing);
    public float BackgroundMotionScale => Mathf.Clamp(backgroundMotionScale, 1f, 1.25f);

    private static StoryPresentationSettings runtimeFallback;

    public static StoryPresentationSettings Load()
    {
        StoryPresentationSettings settings = Resources.Load<StoryPresentationSettings>(ResourcePath);
        if (settings != null)
            return settings;

        if (runtimeFallback == null)
        {
            runtimeFallback = CreateInstance<StoryPresentationSettings>();
            runtimeFallback.hideFlags = HideFlags.DontSave;
        }

        return runtimeFallback;
    }
}
