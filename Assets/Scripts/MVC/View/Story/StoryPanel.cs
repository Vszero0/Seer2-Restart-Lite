using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StoryPanel : Panel
{
    private const string StoryResourceRoot = "Data/Stories/";
    private const string ModStoryPrefix = "mod:";
    private const string NarrationPrompt = "\u9009\u62E9\u56DE\u5E94";
    private const string NarratorName = "\u65C1\u767D";
    private const string ChoicePrompt = "\u8BF7\u9009\u62E9\u63A5\u4E0B\u6765\u7684\u56DE\u5E94\u3002";
    private const string DefaultIconSize = "90,120";
    private const string DefaultIconPos = "0,45";
    private const float DefaultActorSpacing = 132f;
    private const float DefaultActorHeight = 250f;
    private const float DefaultActorBottom = 166f;
    private const float DefaultActorCenterGap = 112f;
    private const float DefaultActorStackOffset = 16f;

    private readonly Dictionary<string, StoryActorRuntime> actors = new Dictionary<string, StoryActorRuntime>();
    private readonly Dictionary<string, int> nextSideSlots = new Dictionary<string, int>();
    private static readonly Dictionary<Sprite, Vector2> spriteVisiblePivotCache = new Dictionary<Sprite, Vector2>();

    private StoryScript story;
    private StoryLayoutRuntime activeLayout;
    private string pendingStoryId;
    private int fallbackMapId;
    private int commandIndex;
    private int actorOrder;
    private bool isBuilt;
    private bool isClosing;
    private bool waitingForChoice;

    private Image sceneImage;
    private RectTransform actorLayer;
    private GameObject exitButton;
    private DialogInfo lastDialogInfo;

    public static StoryPanel Open(string storyId, int fallbackMapId = 0)
    {
        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null)
            return null;

        GameObject obj = new GameObject("Story Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(StoryPanel));
        obj.transform.SetParent(canvas.transform, false);
        obj.transform.SetAsLastSibling();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = obj.GetComponent<Image>();
        image.color = new Color(0, 0, 0, 0);
        image.raycastTarget = true;

        Button button = obj.GetComponent<Button>();
        button.transition = Selectable.Transition.None;

        StoryPanel panel = obj.GetComponent<StoryPanel>();
        panel.OpenStory(storyId, fallbackMapId);
        return panel;
    }

    public static bool CanOpenStory(string storyId, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrEmpty(storyId))
        {
            error = "剧情脚本为空";
            return false;
        }

        if (!IsModStory(storyId))
        {
            if (!TryLoadStoryDocument(storyId, out _, out error))
                return false;

            return true;
        }

        string modStoryId = storyId.Substring(ModStoryPrefix.Length);
        StoryDocument document = Database.instance.GetStoryInfo(modStoryId);
        if (!StoryValidator.Validate(document, out string validationError))
        {
            error = "找不到对应的Mod剧情，或剧情文件格式错误：\n" + validationError;
            return false;
        }

        return true;
    }

    public override void Init()
    {
        base.Init();
        BuildUI();
        isBuilt = true;

        if (!string.IsNullOrEmpty(pendingStoryId))
            LoadStory(pendingStoryId, fallbackMapId);
    }

    public override void ClosePanel()
    {
        ClearDialogHandlers();
        ClearActors();
        DialogManager.instance?.CloseDialog();
        if (exitButton != null)
            Destroy(exitButton);

        base.ClosePanel();
    }

    public void OpenStory(string storyId, int fallbackMapId = 0)
    {
        pendingStoryId = storyId;
        this.fallbackMapId = fallbackMapId;

        if (isBuilt)
            LoadStory(storyId, fallbackMapId);
    }

    private void BuildUI()
    {
        Button clickButton = GetComponent<Button>();
        clickButton.onClick.RemoveAllListeners();
        clickButton.onClick.AddListener(Advance);

        sceneImage = CreateImage("Scene", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero, new Color32(8, 11, 18, 255));
        sceneImage.raycastTarget = false;
        actorLayer = DialogManager.instance != null
            ? DialogManager.instance.GetStoryActorLayer()
            : CreateRect("Story Actors", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
        CreateExitButton();
    }

    private void LoadStory(string storyId, int fallbackMapId)
    {
        if (fallbackMapId != 0)
            SetScene("Maps/bg/" + fallbackMapId);

        if (!TryBuildStoryScript(storyId, out story, out string error))
        {
            SetDialogue(null, "\u7CFB\u7EDF", error);
            return;
        }

        commandIndex = 0;
        actorOrder = 0;
        isClosing = false;
        waitingForChoice = false;
        ClearActors();
        nextSideSlots.Clear();
        ClearDialogHandlers();
        lastDialogInfo = null;
        activeLayout = ResolveLayout(null);

        ShowNextCommand();
    }

    private static bool TryBuildStoryScript(string storyId, out StoryScript story, out string error)
    {
        story = null;
        error = string.Empty;
        if (IsModStory(storyId))
        {
            string modStoryId = storyId.Substring(ModStoryPrefix.Length);
            StoryDocument modDocument = Database.instance.GetStoryInfo(modStoryId);
            if (!StoryValidator.Validate(modDocument, out string validationError))
            {
                error = "Mod剧情文件格式错误：\n" + validationError;
                return false;
            }

            story = modDocument.ToScript();
            return story != null;
        }

        if (!TryLoadStoryDocument(storyId, out StoryDocument document, out error))
            return false;

        story = document.ToScript();
        return story != null;
    }

    private static bool TryLoadStoryDocument(string storyId, out StoryDocument document, out string error)
    {
        document = null;
        error = string.Empty;

        TextAsset textAsset = Resources.Load<TextAsset>(StoryResourceRoot + storyId);
        if (textAsset != null)
        {
            document = JsonUtility.FromJson<StoryDocument>(textAsset.text);
            if (!StoryValidator.Validate(document, out string validationError))
            {
                error = "剧情 JSON 格式错误：\n" + validationError;
                return false;
            }

            return true;
        }

        error = "未找到剧情 JSON：" + storyId;
        return false;
    }

    private static bool IsModStory(string storyId)
    {
        return !string.IsNullOrEmpty(storyId) && storyId.StartsWith(ModStoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private void Advance()
    {
        if (isClosing || waitingForChoice)
            return;

        ShowNextCommand();
    }

    private void ExitStory()
    {
        isClosing = true;
        ClosePanel();
    }

    private void ShowNextCommand()
    {
        if (story == null)
            return;

        ClearChoiceHandler();
        while (commandIndex < story.commands.Count)
        {
            StoryCommand command = story.commands[commandIndex++];
            switch (command.type)
            {
                case StoryCommandType.Scene:
                    ApplyScene(command);
                    continue;
                case StoryCommandType.Show:
                    RegisterActor(command);
                    continue;
                case StoryCommandType.Hide:
                    UnregisterActor(command.args);
                    continue;
                case StoryCommandType.Say:
                    SetDialogue(command.actorInfo, command.speaker, command.text);
                    return;
                case StoryCommandType.Narrate:
                    SetNarration(command.text);
                    return;
                case StoryCommandType.Choice:
                    ShowChoices(command.choices);
                    return;
                case StoryCommandType.Jump:
                    if (EvaluateCondition(command.condition))
                        JumpTo(command.args);
                    continue;
                case StoryCommandType.Mission:
                    ExecuteMission(command.args);
                    continue;
                case StoryCommandType.Teleport:
                    Teleport(command.args);
                    return;
                case StoryCommandType.End:
                    ClosePanel();
                    return;
            }
        }

        ClosePanel();
    }

    private void ApplyScene(StoryCommand command)
    {
        SetScene(GetArgValue(command.args, "bg", command.args));
        ClearActors();
        nextSideSlots.Clear();
        ApplySceneLayout(command.layout);
        PlaySceneMusic(command.bgmResourcePath);
    }

    private void PlaySceneMusic(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath) || ResourceManager.instance == null)
            return;

        string source = story?.GetResourceSource(resourcePath) ?? "auto";
        bool modOnly = source == "mod" || source == "auto";
        ResourceManager.instance.GetLocalAddressables<AudioClip>(resourcePath, modOnly,
            clip => AudioSystem.instance?.PlayMusic(clip, AudioVolumeType.BGM),
            modOnly && source == "auto"
                ? _ => ResourceManager.instance.GetLocalAddressables<AudioClip>(resourcePath, false,
                    clip => AudioSystem.instance?.PlayMusic(clip, AudioVolumeType.BGM))
                : null);
    }

    private void SetScene(string path)
    {
        Sprite sprite = LoadSprite(path);
        if (sprite == null && path.TryTrimStart("Maps/bg/", out string mapIdText) && int.TryParse(mapIdText, out int mapId))
        {
            Map map = Map.GetMap(mapId);
            if (map != null && map.resId != 0 && map.resId != mapId)
                sprite = LoadSprite("Maps/bg/" + map.resId);
        }

        sceneImage.sprite = sprite;
        sceneImage.color = sprite == null ? new Color32(8, 11, 18, 255) : Color.white;
        DialogManager.instance?.SetStoryDialogBackground(sceneImage.sprite, sceneImage.color);
    }

    private void RegisterActor(StoryCommand command)
    {
        StoryActorDocument actor = command.actorInfo;
        if (actor == null)
            actor = BuildLegacyActor(command.args);

        if (actor == null || string.IsNullOrEmpty(actor.id))
            return;

        if (!actors.TryGetValue(actor.id, out StoryActorRuntime runtime))
        {
            runtime = new StoryActorRuntime { document = actor };
            runtime.image = CreateActorImage(actor);
            runtime.canvasGroup = runtime.image.GetComponent<CanvasGroup>();
            runtime.order = actorOrder++;
            runtime.slot = actor.slot > 0 ? actor.slot : GetNextSideSlot(actor.normalizedSide);
            actors[actor.id] = runtime;
            PlayActorFade(runtime);
        }
        else
        {
            runtime.document = actor;
            runtime.image.gameObject.SetActive(true);
        }

        LayoutActors();
    }

    private void UnregisterActor(string args)
    {
        string[] tokens = SplitArgs(args);
        if (tokens.Length == 0 || tokens[0] == "all")
        {
            ClearActors();
            return;
        }

        if (actors.TryGetValue(tokens[0], out StoryActorRuntime runtime))
        {
            if (runtime.image != null)
                Destroy(runtime.image.gameObject);
            actors.Remove(tokens[0]);
            LayoutActors();
        }
    }

    private void SetDialogue(StoryActorDocument actor, string speaker, string content)
    {
        waitingForChoice = false;
        DialogManager.instance.SetStoryDialogReplyClickHandler(null);
        DialogManager.instance.ClearStoryChoices();
        DialogManager.instance.SetStoryDialogBackgroundClickHandler(Advance);
        SetActiveActor(actor?.id);
        lastDialogInfo = CreateDialogInfo(actor, speaker, content, new List<NpcButtonHandler>());
        DialogManager.instance.OpenStoryDialog(lastDialogInfo, false);
        RefreshOverlayLayering();
    }

    private void SetNarration(string content)
    {
        waitingForChoice = false;
        DialogManager.instance.SetStoryDialogReplyClickHandler(null);
        DialogManager.instance.ClearStoryChoices();
        DialogManager.instance.SetStoryDialogBackgroundClickHandler(Advance);
        SetActiveActor(null);
        lastDialogInfo = CreateDialogInfo(null, NarratorName, content, new List<NpcButtonHandler>());
        DialogManager.instance.OpenStoryDialog(lastDialogInfo, false);
        RefreshOverlayLayering();
    }

    private void ShowChoices(List<StoryChoice> choices)
    {
        if (choices == null || choices.Count == 0)
        {
            ShowNextCommand();
            return;
        }

        waitingForChoice = true;
        DialogManager.instance.SetStoryDialogBackgroundClickHandler(Advance);
        DialogManager.instance.SetStoryDialogReplyClickHandler(null);
        if (lastDialogInfo == null)
        {
            lastDialogInfo = CreateDialogInfo(null, NarrationPrompt, ChoicePrompt, new List<NpcButtonHandler>());
            DialogManager.instance.OpenStoryDialog(lastDialogInfo, false);
        }
        DialogManager.instance.ShowStoryChoices(choices.Select(x => x.text).ToList(), choiceIndex =>
        {
            if (choiceIndex < 0 || choiceIndex >= choices.Count)
                return;

            waitingForChoice = false;
            ClearChoiceHandler();
            StoryChoice choice = choices[choiceIndex];
            StoryCommand choiceCommand = GetCurrentChoiceCommand();
            story.choiceHistory.Add(new StoryChoiceHistoryEntry
            {
                commandId = choiceCommand?.commandId,
                choiceId = choiceCommand?.choiceId,
                optionId = choice.optionId,
            });
            ShowNextCommand();
        }, GetChoiceSpeakerSide());
        RefreshOverlayLayering();
    }

    private string GetChoiceSpeakerSide()
    {
        return string.Equals(lastDialogInfo?.storySpeakerSide, "right", StringComparison.OrdinalIgnoreCase)
            ? "right"
            : "left";
    }

    private DialogInfo CreateDialogInfo(StoryActorDocument actor, string speaker, string content, List<NpcButtonHandler> replyHandlers)
    {
        string iconPath = actor?.icon;
        bool hasIcon = !string.IsNullOrEmpty(iconPath);

        return new DialogInfo
        {
            id = "story",
            iconId = hasIcon ? iconPath : "none",
            iconSize = hasIcon ? DefaultIconSize : "0,0",
            iconPos = hasIcon ? DefaultIconPos : "0,0",
            name = speaker ?? string.Empty,
            storySpeakerSide = actor?.normalizedSide ?? "left",
            storyFlipIcon = actor != null && actor.flipIcon,
            storyTextStyle = story?.textStyle,
            rawContent = content ?? string.Empty,
            functionHandler = new List<NpcButtonHandler>(),
            replyHandler = replyHandlers ?? new List<NpcButtonHandler>()
        };
    }

    private void ClearDialogHandlers()
    {
        DialogManager.instance?.SetStoryDialogBackgroundClickHandler(null);
        ClearChoiceHandler();
    }

    private void ClearChoiceHandler()
    {
        waitingForChoice = false;
        DialogManager.instance?.SetStoryDialogReplyClickHandler(null);
        DialogManager.instance?.ClearStoryChoices();
    }

    private void JumpTo(string label)
    {
        int labelIndex = story.GetLabelIndex(label.Trim());
        if (labelIndex >= 0)
            commandIndex = labelIndex;
    }

    private void ExecuteMission(string args)
    {
        string[] tokens = SplitArgs(args);
        if (tokens.Length < 2 || !int.TryParse(tokens[0], out int missionId))
            return;

        string action = tokens[1].ToLower();
        Mission mission = Mission.Find(missionId);
        if (mission == null && action != "start")
            mission = Mission.Start(missionId);

        switch (action)
        {
            case "start":
                Mission.Start(missionId);
                break;
            case "complete":
                if (mission != null)
                    Mission.Complete(missionId);
                break;
            case "checkpoint":
                if (tokens.Length >= 3)
                    Mission.Checkpoint(missionId, tokens[2]);
                break;
        }

        SaveSystem.SaveData();
    }

    private void Teleport(string args)
    {
        if (!int.TryParse(args.Trim(), out int mapId))
            return;

        isClosing = true;
        ClosePanel();
        TeleportHandler.Teleport(mapId);
    }

    private Image CreateActorImage(StoryActorDocument actor)
    {
        Image image = CreateImage("Story Actor " + actor.id, actorLayer, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero, Color.white);
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.sprite = LoadSprite(actor.displaySprite);
        image.gameObject.SetActive(image.sprite != null);
        CanvasGroup canvasGroup = image.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        return image;
    }

    private void PlayActorFade(StoryActorRuntime runtime)
    {
        if (runtime == null || runtime.canvasGroup == null)
            return;

        if (runtime.fadeCoroutine != null)
            StopCoroutine(runtime.fadeCoroutine);

        runtime.fadeCoroutine = StartCoroutine(ActorFadeCoroutine(runtime));
    }

    private IEnumerator ActorFadeCoroutine(StoryActorRuntime runtime)
    {
        const float duration = 0.18f;
        float time = 0f;
        runtime.canvasGroup.alpha = 0f;

        while (time < duration)
        {
            runtime.canvasGroup.alpha = Mathf.Clamp01(time / duration);
            time += Time.unscaledDeltaTime;
            yield return null;
        }

        runtime.canvasGroup.alpha = 1f;
        runtime.fadeCoroutine = null;
    }

    private StoryActorDocument BuildLegacyActor(string args)
    {
        string[] tokens = SplitArgs(args);
        if (tokens.Length < 2)
            return null;

        return new StoryActorDocument
        {
            id = tokens[0],
            name = tokens[0],
            sprite = NormalizeSpritePath(tokens[1]),
            icon = NormalizeSpritePath(tokens[1]),
            side = "left",
            faceLeft = true,
            scale = 1f,
        };
    }

    private void LayoutActors()
    {
        if (activeLayout == null)
            activeLayout = ResolveLayout(null);

        LayoutActorsBySide("left");
        LayoutActorsBySide("right");
        ApplyInitialActorLayering();
        RefreshOverlayLayering();
    }

    private void LayoutActorsBySide(string side)
    {
        List<StoryActorRuntime> sideActors = actors.Values
            .Where(x => x?.image != null && x.document != null && x.document.normalizedSide == side)
            .OrderBy(x => x.slot)
            .ThenBy(x => x.order)
            .ToList();

        int maxSlot = sideActors.Count == 0 ? 0 : sideActors.Max(x => x.slot);
        for (int i = 0; i < sideActors.Count; i++)
        {
            StoryActorRuntime runtime = sideActors[i];
            RectTransform rect = runtime.image.rectTransform;
            bool isRight = side == "right";
            int visualIndex = Mathf.Max(0, maxSlot - runtime.slot);
            float yOffset = Mathf.Max(0, runtime.slot - 1) * activeLayout.stackOffset;
            float scale = Mathf.Max(0.1f, runtime.document.scale <= 0f ? 1f : runtime.document.scale);
            Vector2 originalSize = GetSpriteSize(runtime.image.sprite, activeLayout.actorHeight);
            float width = originalSize.x * scale;
            float height = originalSize.y * scale;
            float sideOffset = activeLayout.centerGap + visualIndex * activeLayout.actorSpacing;
            float x = isRight ? sideOffset : -sideOffset;

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = GetVisibleBottomPivot(runtime.image.sprite);
            rect.anchoredPosition = new Vector2(x, activeLayout.actorBottom + yOffset);
            rect.sizeDelta = new Vector2(width, height);

            bool shouldFaceLeft = runtime.document.faceLeft;
            float direction = shouldFaceLeft ? -1f : 1f;
            rect.localScale = new Vector3(direction, 1f, 1f);
        }
    }

    private void SetActiveActor(string actorId)
    {
        foreach (StoryActorRuntime runtime in actors.Values)
        {
            if (runtime?.image == null)
                continue;

            bool active = !string.IsNullOrEmpty(actorId) && runtime.document.id == actorId;
            runtime.image.color = active ? Color.white : new Color32(118, 118, 126, 255);
            if (active)
                runtime.image.transform.SetAsLastSibling();
        }
    }

    private void ApplySceneLayout(StoryLayoutDocument sceneLayout)
    {
        activeLayout = ResolveLayout(sceneLayout);
        LayoutActors();
    }

    private int GetNextSideSlot(string side)
    {
        if (!nextSideSlots.TryGetValue(side, out int slot))
            slot = 1;

        nextSideSlots[side] = slot + 1;
        return slot;
    }

    private static Vector2 GetSpriteSize(Sprite sprite, float fallbackHeight)
    {
        if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
        {
            float height = fallbackHeight > 0f ? fallbackHeight : DefaultActorHeight;
            return new Vector2(height * 0.72f, height);
        }

        return new Vector2(sprite.rect.width, sprite.rect.height);
    }

    private static Vector2 GetVisibleBottomPivot(Sprite sprite)
    {
        if (sprite == null)
            return new Vector2(0.5f, 0f);

        if (spriteVisiblePivotCache.TryGetValue(sprite, out Vector2 cachedPivot))
            return cachedPivot;

        Vector2 pivot = CalculateVisibleBottomPivot(sprite);
        spriteVisiblePivotCache[sprite] = pivot;
        return pivot;
    }

    private static Vector2 CalculateVisibleBottomPivot(Sprite sprite)
    {
        Rect rect = sprite.rect;
        if (rect.width <= 0f || rect.height <= 0f || sprite.texture == null)
            return new Vector2(0.5f, 0f);

        try
        {
            Texture2D texture = sprite.texture;
            Color32[] pixels = texture.GetPixels32();
            int textureWidth = texture.width;
            int xMin = Mathf.FloorToInt(rect.xMin);
            int yMin = Mathf.FloorToInt(rect.yMin);
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            int minX = width;
            int minY = height;
            int maxX = -1;

            for (int y = 0; y < height; y++)
            {
                int pixelY = yMin + y;
                if (pixelY < 0 || pixelY >= texture.height)
                    continue;

                for (int x = 0; x < width; x++)
                {
                    int pixelX = xMin + x;
                    if (pixelX < 0 || pixelX >= textureWidth)
                        continue;

                    if (pixels[pixelY * textureWidth + pixelX].a <= 8)
                        continue;

                    if (x < minX)
                        minX = x;
                    if (x > maxX)
                        maxX = x;
                    if (y < minY)
                        minY = y;
                }
            }

            if (maxX < minX || minY >= height)
                return new Vector2(0.5f, 0f);

            float pivotX = ((minX + maxX + 1f) * 0.5f) / width;
            float pivotY = minY / (float)height;
            return new Vector2(Mathf.Clamp01(pivotX), Mathf.Clamp01(pivotY));
        }
        catch (UnityException)
        {
            return new Vector2(0.5f, 0f);
        }
    }

    private void ApplyInitialActorLayering()
    {
        foreach (StoryActorRuntime runtime in actors.Values
            .Where(x => x?.image != null)
            .OrderByDescending(x => x.slot)
            .ThenBy(x => x.order))
        {
            runtime.image.transform.SetAsLastSibling();
        }
    }

    private StoryLayoutRuntime ResolveLayout(StoryLayoutDocument sceneLayout)
    {
        StoryLayoutDocument globalLayout = story?.layout;
        return new StoryLayoutRuntime
        {
            actorSpacing = FirstPositive(sceneLayout?.actorSpacing, globalLayout?.actorSpacing, DefaultActorSpacing),
            actorHeight = FirstPositive(sceneLayout?.actorHeight, globalLayout?.actorHeight, DefaultActorHeight),
            actorBottom = FirstPositive(sceneLayout?.actorBottom, globalLayout?.actorBottom, DefaultActorBottom),
            centerGap = FirstPositive(sceneLayout?.centerGap, globalLayout?.centerGap, DefaultActorCenterGap),
            stackOffset = FirstPositive(sceneLayout?.stackOffset, globalLayout?.stackOffset, DefaultActorStackOffset),
        };
    }

    private static float FirstPositive(float? primary, float? secondary, float fallback)
    {
        if (primary.HasValue && primary.Value > 0f)
            return primary.Value;

        if (secondary.HasValue && secondary.Value > 0f)
            return secondary.Value;

        return fallback;
    }

    private void ClearActors()
    {
        foreach (StoryActorRuntime runtime in actors.Values)
        {
            if (runtime?.image != null)
                Destroy(runtime.image.gameObject);
        }

        actors.Clear();
    }

    private static string[] SplitArgs(string args)
    {
        return args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string GetArgValue(string args, string key, string defaultValue = "")
    {
        string prefix = key + ":";
        string value = SplitArgs(args).FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(value) ? defaultValue : value.Substring(prefix.Length);
    }

    private static string NormalizeSpritePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        if (path.StartsWith("pet:", StringComparison.OrdinalIgnoreCase))
            return "Pets/pet/" + path.Substring("pet:".Length);

        if (path.StartsWith("npc:", StringComparison.OrdinalIgnoreCase))
            return "Npc/" + path.Substring("npc:".Length);

        return path;
    }

    private static Sprite LoadSprite(string path)
    {
        path = NormalizeSpritePath(path);
        if (string.IsNullOrEmpty(path) || path == "none")
            return null;

        bool isExplicitModPath = path.TryTrimStart("Mod/", out string modPath);
        string resourcePath = isExplicitModPath ? modPath : path;

        Sprite sprite = ResourceManager.instance.GetLocalAddressables<Sprite>(resourcePath, isExplicitModPath);
        if (sprite != null && sprite != SpriteSet.Empty)
            return sprite;

        if (!isExplicitModPath)
        {
            sprite = ResourceManager.instance.GetLocalAddressables<Sprite>(resourcePath, true);
            if (sprite != null && sprite != SpriteSet.Empty)
                return sprite;
        }

        sprite = ResourceManager.instance.Get<Sprite>(path);
        if (sprite != null && sprite != SpriteSet.Empty)
            return sprite;

        sprite = NpcInfo.GetIcon(path);
        if (sprite != null && sprite != SpriteSet.Empty)
            return sprite;

        return null;
    }

    private StoryCommand GetCurrentChoiceCommand()
    {
        int index = Mathf.Clamp(commandIndex - 1, 0, story?.commands.Count - 1 ?? 0);
        return story == null || story.commands.Count == 0 ? null : story.commands[index];
    }

    private bool EvaluateCondition(ConditionGroupDocument group)
    {
        if (group == null)
            return true;

        bool useAnd = !string.Equals(group.operatorType, "OR", StringComparison.OrdinalIgnoreCase);
        List<bool> results = new List<bool>();
        foreach (StoryConditionDocument condition in group.conditions ?? Array.Empty<StoryConditionDocument>())
            results.Add(EvaluateCondition(condition));

        if (results.Count == 0)
            return true;

        return useAnd ? results.All(x => x) : results.Any(x => x);
    }

    private bool EvaluateCondition(StoryConditionDocument condition)
    {
        if (condition == null || string.IsNullOrWhiteSpace(condition.type))
            return false;

        switch (condition.type.Trim().ToLower())
        {
            case "choiceselected":
                return story.choiceHistory.Any(x =>
                    (string.IsNullOrEmpty(condition.commandId) || x.commandId == condition.commandId) &&
                    (string.IsNullOrEmpty(condition.choiceId) || x.choiceId == condition.choiceId) &&
                    (string.IsNullOrEmpty(condition.optionId) || x.optionId == condition.optionId));
            case "choicesequencematched":
                string[] sequence = condition.optionSequence ?? Array.Empty<string>();
                if (sequence.Length == 0 || story.choiceHistory.Count < sequence.Length)
                    return false;

                int start = story.choiceHistory.Count - sequence.Length;
                return sequence.Select((optionId, index) => optionId == story.choiceHistory[start + index].optionId).All(x => x);
            case "missionstate":
                Mission mission = Mission.Find(condition.missionId);
                if (mission == null)
                    return false;

                return string.Equals(condition.missionState, "complete", StringComparison.OrdinalIgnoreCase)
                    ? mission.isDone
                    : !mission.isDone;
            case "storyflag":
                return false;
            default:
                return false;
        }
    }

    private Image CreateImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Image image = obj.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        return rect;
    }

    private void CreateExitButton()
    {
        GameObject obj = new GameObject("Exit Story Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Outline));
        exitButton = obj;

        Transform parent = transform.parent != null ? transform.parent : transform;
        obj.transform.SetParent(parent, false);
        obj.transform.SetAsLastSibling();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -20f);
        rect.sizeDelta = new Vector2(112f, 34f);

        Image image = obj.GetComponent<Image>();
        image.color = new Color32(0, 18, 24, 220);
        image.raycastTarget = true;

        Outline outline = obj.GetComponent<Outline>();
        outline.effectColor = new Color32(44, 227, 255, 210);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Button button = obj.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(18, 74, 86, 255);
        colors.pressedColor = new Color32(8, 38, 48, 255);
        button.colors = colors;
        button.onClick.AddListener(ExitStory);

        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(obj.transform, false);

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.GetComponent<TextMeshProUGUI>();
        TMP_Text textSample = FindTextSample();
        if (textSample != null && textSample.font != null)
        {
            text.font = textSample.font;
            text.fontSharedMaterial = textSample.fontSharedMaterial;
        }

        text.text = "\u9000\u51FA\u4EFB\u52A1";
        text.fontSize = 18f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color32(255, 238, 92, 255);
        text.raycastTarget = false;
    }

    private void BringExitButtonToFront()
    {
        if (exitButton != null)
            exitButton.transform.SetAsLastSibling();
    }

    private void RefreshOverlayLayering()
    {
        DialogManager.instance?.RefreshStoryOverlayLayering();
        BringExitButtonToFront();
    }

    private TMP_Text FindTextSample()
    {
        Transform canvas = transform.root;
        TMP_Text fallback = null;
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == null)
                continue;

            if (fallback == null)
                fallback = text;

            string objectName = text.gameObject.name;
            if (string.Equals(objectName, "Dialog", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "Content", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "Text", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return fallback;
    }

    private class StoryActorRuntime
    {
        public StoryActorDocument document;
        public Image image;
        public CanvasGroup canvasGroup;
        public Coroutine fadeCoroutine;
        public int order;
        public int slot;
    }

    private class StoryLayoutRuntime
    {
        public float actorSpacing;
        public float actorHeight;
        public float actorBottom;
        public float centerGap;
        public float stackOffset;
    }
}
