using UnityEngine;

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

    public float TextInitialDelay => Mathf.Max(0f, textInitialDelay);
    public float TextCharacterInterval => Mathf.Max(0.001f, textCharacterInterval);
    public float TextCharacterDuration => Mathf.Max(0.001f, textCharacterDuration);
    public float TextRiseDistance => Mathf.Max(0f, textRiseDistance);
    public float ShortPunctuationPause => Mathf.Max(0f, shortPunctuationPause);
    public float LongPunctuationPause => Mathf.Max(0f, longPunctuationPause);
    public float NewlinePause => Mathf.Max(0f, newlinePause);
    public float TextLineSpacing => Mathf.Max(0f, textLineSpacing);
    public float InlineExpressionScale => Mathf.Clamp(inlineExpressionScale, 0.5f, 2f);
    public float InlineExpressionVerticalOffset => Mathf.Clamp(inlineExpressionVerticalOffset, -16f, 16f);
    public bool DepthFocusEnabled => depthFocusEnabled;
    public float BackgroundBlur => Mathf.Clamp(backgroundBlur, 0f, 4f);
    public float BackgroundBlurStrength => Mathf.Clamp01(backgroundBlurStrength);
    public float BackgroundBlurRenderScale => Mathf.Clamp(backgroundBlurRenderScale, 0.25f, 1f);
    public float InactiveActorBlur => Mathf.Clamp(inactiveActorBlur, 0f, 4f);
    public float InactiveActorBrightness => Mathf.Clamp(inactiveActorBrightness, 0.3f, 1f);
    public float DepthFocusTransitionDuration => Mathf.Max(0f, depthFocusTransitionDuration);

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
