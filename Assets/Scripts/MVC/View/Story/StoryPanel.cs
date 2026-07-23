using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum StoryPreviewScope
{
    Story,
    Node,
}

public class StoryPanel : Panel
{
    private sealed class PreviewNodeInfo
    {
        public string id;
        public string displayName;
        public string role;
        public int index;
        public int total;
    }

    private const string NarrationPrompt = "\u9009\u62E9\u56DE\u5E94";
    private const string NarratorName = "\u65C1\u767D";
    private const string ChoicePrompt = "\u8BF7\u9009\u62E9\u63A5\u4E0B\u6765\u7684\u56DE\u5E94\u3002";
    private const string DefaultIconSize = "90,120";
    private const string DefaultIconPos = "0,45";
    private StoryScript story;
    private StoryActorStage actorStage;
    private string pendingStoryId;
    private int boundMissionId;
    private bool boundMissionCompleted;
    private StoryScript pendingPreviewStory;
    private string pendingPreviewStartNodeId;
    private StoryPreviewScope previewScope;
    private Dictionary<string, PreviewNodeInfo> previewNodeInfos;
    private bool isEmptyNodePreview;
    private Action previewClosedCallback;
    private int fallbackMapId;
    private int commandIndex;
    private int sceneVisualRequestVersion;
    private int sceneMusicRequestVersion;
    private bool isBuilt;
    private bool isClosing;
    private bool waitingForChoice;
    private bool isTransitioning;
    private bool isPreviewMode;
    private bool suppressMusicRestore;
    private bool hasChangedMusicIdentity;
    private bool hasRestartedMusic;
    private string activeMusicIdentity;
    private AudioSystem.MusicPlaybackSnapshot musicSnapshot;
    private Coroutine sceneTransitionCoroutine;

    private Image sceneImage;
    private Image transitionSceneImage;
    private Image dialogSceneImage;
    private Image dialogTransitionSceneImage;
    private RectTransform actorLayer;
    private GameObject exitButton;
    private GameObject previewIndicator;
    private TextMeshProUGUI previewIndicatorText;
    private DialogInfo lastDialogInfo;
    private StoryBattleSession pendingBattleResume;
    private RectTransform battlePrompt;
    private RectTransform battlePromptMask;
    private StoryCommand pendingBattleCommand;

    public static StoryPanel Open(string storyId, int fallbackMapId = 0, int missionId = 0)
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
        image.color = new Color32(8, 11, 18, 255);
        image.raycastTarget = true;

        Button button = obj.GetComponent<Button>();
        button.transition = Selectable.Transition.None;

        StoryPanel panel = obj.GetComponent<StoryPanel>();
        panel.OpenStory(storyId, fallbackMapId, missionId);
        return panel;
    }

    public static bool CanOpenStory(string storyId, out string error)
    {
        return StoryDocumentLoader.CanOpen(storyId, out error);
    }

    public static StoryPanel OpenResume(StoryBattleSession session)
    {
        if (session?.story == null)
            return null;
        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null)
            return null;
        GameObject obj = new GameObject("Story Panel", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(StoryPanel));
        obj.transform.SetParent(canvas.transform, false);
        obj.transform.SetAsLastSibling();
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        obj.GetComponent<Image>().color = new Color32(8, 11, 18, 255);
        obj.GetComponent<Button>().transition = Selectable.Transition.None;
        StoryPanel panel = obj.GetComponent<StoryPanel>();
        panel.pendingBattleResume = session;
        return panel;
    }

    /// <summary>
    /// 用真实播放器预览内存中的剧情草稿；不写入文件，也不要求草稿通过发布校验。
    /// </summary>
    public static StoryPanel OpenPreview(StoryDocument document, string startNodeId,
        StoryPreviewScope scope, Action onClosed = null)
    {
        if (document == null)
            return null;

        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null)
            return null;

        GameObject obj = new GameObject("Story Preview Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(StoryPanel));
        obj.transform.SetParent(canvas.transform, false);
        obj.transform.SetAsLastSibling();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = obj.GetComponent<Image>();
        image.color = new Color32(8, 11, 18, 255);
        image.raycastTarget = true;

        Button button = obj.GetComponent<Button>();
        button.transition = Selectable.Transition.None;

        StoryPanel panel = obj.GetComponent<StoryPanel>();
        panel.pendingPreviewStory = document.ToScript();
        panel.pendingPreviewStartNodeId = startNodeId;
        panel.previewScope = scope;
        panel.previewNodeInfos = BuildPreviewNodeInfos(document);
        StoryNodeDocument previewNode = (document.nodes ?? Array.Empty<StoryNodeDocument>())
            .FirstOrDefault(node => node != null
                && string.Equals(node.id, startNodeId, StringComparison.OrdinalIgnoreCase));
        panel.isEmptyNodePreview = scope == StoryPreviewScope.Node
            && !(previewNode?.commands ?? Array.Empty<StoryCommandDocument>()).Any(command => command != null);
        panel.previewClosedCallback = onClosed;
        panel.isPreviewMode = true;
        return panel;
    }

    private static Dictionary<string, PreviewNodeInfo> BuildPreviewNodeInfos(StoryDocument document)
    {
        StoryNodeDocument[] nodes = (document?.nodes ?? Array.Empty<StoryNodeDocument>())
            .Where(node => node != null && !string.IsNullOrWhiteSpace(node.id))
            .ToArray();
        Dictionary<string, PreviewNodeInfo> infos = new Dictionary<string, PreviewNodeInfo>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < nodes.Length; index++)
        {
            StoryNodeDocument node = nodes[index];
            List<string> roles = new List<string>
            {
                node.isBranch ? "分支剧情" : "默认流程",
            };
            if (string.Equals(node.id, document.entry, StringComparison.OrdinalIgnoreCase))
                roles.Add("入口");
            if (node.isEnding)
                roles.Add("结束");
            infos[node.id] = new PreviewNodeInfo
            {
                id = node.id,
                displayName = string.IsNullOrWhiteSpace(node.displayName) ? node.id : node.displayName,
                role = string.Join(" · ", roles),
                index = index + 1,
                total = nodes.Length,
            };
        }
        return infos;
    }

    public override void Init()
    {
        base.Init();
        BuildUI();
        isBuilt = true;

        if (pendingBattleResume != null)
            ResumeAfterBattle(pendingBattleResume);
        else
        {
            CaptureMusicContext();
            if (pendingPreviewStory != null)
                LoadRuntimeStory(pendingPreviewStory, pendingPreviewStartNodeId);
            else if (!string.IsNullOrEmpty(pendingStoryId))
                LoadStory(pendingStoryId, fallbackMapId);
        }
    }

    public override void ClosePanel()
    {
        Action onPreviewClosed = previewClosedCallback;
        previewClosedCallback = null;
        sceneMusicRequestVersion++;
        sceneVisualRequestVersion++;
        if (sceneTransitionCoroutine != null)
            StopCoroutine(sceneTransitionCoroutine);
        sceneTransitionCoroutine = null;
        ResetTransitionImages();
        RestoreMusicContext();
        ClearDialogHandlers();
        actorStage?.Clear();
        DialogManager.instance?.CloseDialog();
        if (exitButton != null)
            Destroy(exitButton);
        if (previewIndicator != null)
            Destroy(previewIndicator);
        if (battlePromptMask != null)
            Destroy(battlePromptMask.gameObject);

        base.ClosePanel();
        onPreviewClosed?.Invoke();
    }

    public void OpenStory(string storyId, int fallbackMapId = 0, int missionId = 0)
    {
        isPreviewMode = false;
        pendingStoryId = storyId;
        this.fallbackMapId = fallbackMapId;
        boundMissionId = missionId;
        boundMissionCompleted = false;

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
        transitionSceneImage = CreateImage("Transition Scene", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero, Color.clear);
        transitionSceneImage.raycastTarget = false;
        transitionSceneImage.gameObject.SetActive(false);
        dialogSceneImage = DialogManager.instance?.GetStoryDialogBackgroundImage();
        dialogTransitionSceneImage = DialogManager.instance?.GetStoryTransitionBackgroundImage();
        actorLayer = DialogManager.instance != null
            ? DialogManager.instance.GetStoryActorLayer()
            : CreateRect("Story Actors", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
        actorStage = new StoryActorStage(
            actorLayer,
            this,
            RefreshOverlayLayering,
            path => story?.GetResourceSource(path) ?? "auto");
        CreateExitButton();
        if (isPreviewMode)
            CreatePreviewIndicator();
    }

    private void LoadStory(string storyId, int fallbackMapId)
    {
        if (fallbackMapId != 0)
            SetScene("Maps/bg/" + fallbackMapId);

        if (!StoryDocumentLoader.TryBuildRuntimeScript(storyId, out story, out string error))
        {
            SetDialogue(null, "\u7CFB\u7EDF", error);
            return;
        }

        LoadRuntimeStory(story, null);
    }

    private void LoadRuntimeStory(StoryScript runtimeStory, string startNodeId)
    {
        story = runtimeStory;
        if (story == null)
            return;

        int startIndex = !string.IsNullOrWhiteSpace(startNodeId)
            ? story.GetLabelIndex(startNodeId)
            : 0;
        commandIndex = Mathf.Max(0, startIndex);
        string initialPointId = commandIndex < story.commands.Count ? story.commands[commandIndex]?.pointId : startNodeId;
        story.BeginPointVisit(initialPointId);
        isClosing = false;
        waitingForChoice = false;
        isTransitioning = false;
        actorStage.Reset(story.layout);
        ClearDialogHandlers();
        lastDialogInfo = null;

        UpdatePreviewIndicator(initialPointId);
        if (isEmptyNodePreview)
        {
            SetNarration("当前剧情点没有可预览内容。");
            return;
        }

        ShowNextCommand();
    }

    private void Advance()
    {
        if (isClosing || waitingForChoice || isTransitioning)
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
        if (story == null || isTransitioning)
            return;

        ClearChoiceHandler();
        while (commandIndex < story.commands.Count)
        {
            StoryCommand command = story.commands[commandIndex++];
            if (ShouldFinishNodePreview(command))
            {
                ClosePanel();
                return;
            }
            UpdatePreviewIndicator(command.pointId);
            switch (command.type)
            {
                case StoryCommandType.Scene:
                    if (ApplyScene(command))
                        return;
                    continue;
                case StoryCommandType.Show:
                    RegisterActor(command);
                    continue;
                case StoryCommandType.Hide:
                    UnregisterActor(command.args);
                    continue;
                case StoryCommandType.Say:
                    SetDialogue(command.actorInfo, command.speaker, command.text, command.expression);
                    return;
                case StoryCommandType.Narrate:
                    SetNarration(command.text);
                    return;
                case StoryCommandType.Choice:
                    ShowChoices(command);
                    return;
                case StoryCommandType.Jump:
                    if (StoryConditionEvaluator.Evaluate(story, command.condition))
                    {
                        JumpTo(command.args);
                    }
                    continue;
                case StoryCommandType.Mission:
                    ExecuteMission(command.args);
                    continue;
                case StoryCommandType.Teleport:
                    if (isPreviewMode)
                        continue;
                    Teleport(command.args);
                    return;
                case StoryCommandType.Battle:
                    if (isPreviewMode)
                    {
                        ShowBattlePreview(command);
                        return;
                    }
                    ShowBattlePreparation(command);
                    return;
                case StoryCommandType.End:
                    if (StoryConditionEvaluator.Evaluate(story, command.condition))
                    {
                        if (!isPreviewMode && !string.IsNullOrWhiteSpace(command.args))
                            Teleport(command.args);
                        else
                            FinishStory();
                        return;
                    }
                    continue;
            }
        }

        FinishStory();
    }

    private void ResumeAfterBattle(StoryBattleSession session)
    {
        pendingBattleResume = null;
        story = session.story;
        commandIndex = Mathf.Clamp(session.commandIndex, 0, story.commands.Count);
        fallbackMapId = session.fallbackMapId;
        boundMissionId = session.boundMissionId;
        boundMissionCompleted = session.boundMissionCompleted;
        musicSnapshot = session.musicSnapshot;
        hasChangedMusicIdentity = session.hasChangedMusicIdentity;
        hasRestartedMusic = session.hasRestartedMusic;
        activeMusicIdentity = AudioSystem.instance?.CurrentMusicIdentity;
        isClosing = false;
        waitingForChoice = false;
        isTransitioning = false;
        actorStage.Reset(story.layout);
        ClearDialogHandlers();
        lastDialogInfo = null;

        StoryCommand sceneCommand = story.commands.Take(commandIndex).LastOrDefault(command => command?.type == StoryCommandType.Scene);
        if (sceneCommand != null)
        {
            StoryTransitionDocument transition = sceneCommand.transition;
            sceneCommand.transition = null;
            bool loading = ApplyScene(sceneCommand);
            sceneCommand.transition = transition;
            if (loading)
                return;
        }
        ShowNextCommand();
    }

    private void ShowBattlePreview(StoryCommand command)
    {
        pendingBattleCommand = command;
        waitingForChoice = true;
        CreateBattlePrompt("战斗预览", GetBattleDescription(command), new[]
        {
            new KeyValuePair<string, Action>("模拟胜利", () => CompletePreviewBattle("win")),
            new KeyValuePair<string, Action>("模拟失败", () => CompletePreviewBattle("lose")),
            new KeyValuePair<string, Action>("返回预览", CancelBattlePrompt),
        });
    }

    private void ShowBattlePreparation(StoryCommand command)
    {
        pendingBattleCommand = command;
        waitingForChoice = true;
        CreateBattlePrompt("战斗准备", GetBattleDescription(command), new[]
        {
            new KeyValuePair<string, Action>("打开精灵背包", OpenBattlePetBag),
            new KeyValuePair<string, Action>("开始战斗", StartStoryBattle),
            new KeyValuePair<string, Action>("返回剧情", CancelBattlePrompt),
        });
    }

    private string GetBattleDescription(StoryCommand command)
    {
        StoryBattleReferenceDocument reference = command?.battle;
        if (reference == null)
            return "战斗配置无效";
        StoryBattleOption option = StoryBattleCatalog.GetOptions(string.Empty).FirstOrDefault(value => value.reference.mapId == reference.mapId
            && value.reference.npcId == reference.npcId
            && string.Equals(value.reference.battleId, reference.battleId, StringComparison.OrdinalIgnoreCase));
        return option == null ? "地图 " + reference.mapId + " / NPC " + reference.npcId + " / 战斗 " + reference.battleId
            : option.displayName;
    }

    private void OpenBattlePetBag()
    {
        PetBagPanel panel = Panel.OpenPanel<PetBagPanel>();
        if (panel != null)
            panel.onCloseEvent += RefreshBattlePromptAfterPetBag;
    }

    private void RefreshBattlePromptAfterPetBag()
    {
        if (battlePromptMask != null)
            battlePromptMask.SetAsLastSibling();
    }

    private void StartStoryBattle()
    {
        if (!StoryBattleCatalog.TryResolve(pendingBattleCommand?.battle, out BattleInfo battleInfo, out string error)
            || !Battle.CanStartBattle(battleInfo, out error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        StoryBattleSession.current = new StoryBattleSession
        {
            story = story,
            commandIndex = commandIndex,
            fallbackMapId = fallbackMapId,
            boundMissionId = boundMissionId,
            boundMissionCompleted = boundMissionCompleted,
            pointId = pendingBattleCommand.pointId,
            commandId = pendingBattleCommand.commandId,
            musicSnapshot = musicSnapshot,
            hasChangedMusicIdentity = hasChangedMusicIdentity,
            hasRestartedMusic = hasRestartedMusic,
        };
        suppressMusicRestore = true;
        isClosing = true;
        ClosePanel();
        if (!Battle.TryStartBattle(battleInfo, out error))
        {
            StoryBattleSession.current = null;
            Hintbox.OpenHintboxWithContent(error, 16);
        }
    }

    private void CompletePreviewBattle(string result)
    {
        story.battleHistory.Add(new StoryBattleHistoryEntry
        {
            pointId = pendingBattleCommand?.pointId,
            commandId = pendingBattleCommand?.commandId,
            result = result,
        });
        CloseBattlePrompt();
        ShowNextCommand();
    }

    private void CancelBattlePrompt()
    {
        commandIndex = Mathf.Max(0, commandIndex - 1);
        CloseBattlePrompt();
    }

    private void CloseBattlePrompt()
    {
        waitingForChoice = false;
        pendingBattleCommand = null;
        if (battlePromptMask != null)
            Destroy(battlePromptMask.gameObject);
        battlePrompt = null;
        battlePromptMask = null;
    }

    private void CreateBattlePrompt(string title, string content, IEnumerable<KeyValuePair<string, Action>> actions)
    {
        if (battlePromptMask != null)
            Destroy(battlePromptMask.gameObject);
        battlePrompt = null;
        battlePromptMask = null;
        waitingForChoice = true;
        Transform overlayParent = transform.parent != null ? transform.parent : transform;
        Image mask = CreateImage("Battle Prompt Mask", overlayParent, Vector2.zero, Vector2.one, new Vector2(.5f, .5f),
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, .68f));
        mask.raycastTarget = true;
        battlePromptMask = mask.rectTransform;
        battlePrompt = CreateRect("Battle Prompt", mask.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(.5f, .5f), Vector2.zero, new Vector2(620f, 260f));
        Image background = battlePrompt.gameObject.AddComponent<Image>();
        background.color = new Color32(0, 12, 18, 250);
        Outline outline = battlePrompt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(82, 229, 249, 255);
        outline.effectDistance = new Vector2(2f, -2f);
        CreateBattlePromptText("Title", battlePrompt, title, 25f, new Vector2(0f, -28f), new Vector2(560f, 36f), new Color32(82, 229, 249, 255));
        CreateBattlePromptText("Content", battlePrompt, content, 17f, new Vector2(0f, -88f), new Vector2(540f, 76f), new Color32(210, 235, 240, 255));
        KeyValuePair<string, Action>[] buttons = actions.ToArray();
        float width = 154f;
        float gap = 20f;
        float start = -(buttons.Length * width + (buttons.Length - 1) * gap) * .5f + width * .5f;
        for (int index = 0; index < buttons.Length; index++)
            CreateBattlePromptButton(battlePrompt, buttons[index].Key, new Vector2(start + index * (width + gap), -194f), buttons[index].Value);
        mask.transform.SetAsLastSibling();
    }

    private void CreateBattlePromptText(string name, Transform parent, string value, float size, Vector2 position, Vector2 dimensions, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.font = StoryTextFontProvider.GetDefaultFont();
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
    }

    private void CreateBattlePromptButton(Transform parent, string label, Vector2 position, Action callback)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Outline));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(154f, 38f);
        obj.GetComponent<Image>().color = new Color32(0, 22, 30, 255);
        obj.GetComponent<Outline>().effectColor = new Color32(82, 229, 249, 255);
        Button button = obj.GetComponent<Button>();
        button.onClick.AddListener(() => callback?.Invoke());
        CreateBattlePromptText("Text", obj.transform, label, 18f, Vector2.zero, new Vector2(150f, 38f), new Color32(82, 229, 249, 255));
    }

    private bool ShouldFinishNodePreview(StoryCommand command)
    {
        if (!isPreviewMode || previewScope != StoryPreviewScope.Node || command == null)
            return false;
        if (!string.Equals(command.pointId, pendingPreviewStartNodeId, StringComparison.OrdinalIgnoreCase))
            return true;
        return command.type == StoryCommandType.Jump || command.type == StoryCommandType.End;
    }

    private void UpdatePreviewIndicator(string pointId)
    {
        if (!isPreviewMode || previewIndicatorText == null || string.IsNullOrWhiteSpace(pointId))
            return;

        string mode = previewScope == StoryPreviewScope.Node ? "剧情点预览" : "剧本预览";
        if (previewNodeInfos != null && previewNodeInfos.TryGetValue(pointId, out PreviewNodeInfo info))
        {
            string progress = previewScope == StoryPreviewScope.Story
                ? info.index + "/" + info.total + " · "
                : string.Empty;
            previewIndicatorText.text = mode + "  |  当前剧情点："
                + progress + info.role + " - " + info.displayName + "（" + info.id + "）";
            return;
        }

        previewIndicatorText.text = mode + "  |  当前剧情点：" + pointId;
    }

    private bool ApplyScene(StoryCommand command)
    {
        PrepareSceneBoundary();
        PlaySceneMusic(command.mapId, command.bgmResourcePath, ++sceneMusicRequestVersion);
        return BeginSceneVisualChange(command, command.transition);
    }

    private void PlaySceneMusic(int mapId, string resourcePath, int requestVersion)
    {
        if (AudioSystem.instance == null)
            return;

        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            if (mapId == 0)
                return;

            Map.GetMap(mapId, map =>
            {
                if (requestVersion != sceneMusicRequestVersion || map?.resources?.bgm == null)
                    return;

                ApplyResolvedSceneMusic(map.resources.bgm, AudioSystem.BuildMapMusicIdentity(map), requestVersion);
            });
            return;
        }

        if (ResourceManager.instance == null)
            return;

        string source = story?.GetResourceSource(resourcePath) ?? "auto";
        bool explicitMod = resourcePath.TryTrimStart("Mod/", out string modPath);
        bool explicitBuiltin = resourcePath.TryTrimStart("Builtin/", out string builtinPath);
        string loadPath = explicitMod ? modPath : explicitBuiltin ? builtinPath : resourcePath;
        if (explicitMod)
            source = "mod";
        else if (explicitBuiltin)
            source = "builtin";
        string musicIdentity = AudioSystem.BuildResourceMusicIdentity(source, loadPath);
        if (string.Equals(activeMusicIdentity, musicIdentity, StringComparison.OrdinalIgnoreCase))
            return;

        void LoadEmbeddedBuiltin()
        {
            string embeddedPath = Path.ChangeExtension(loadPath, null)?.Replace('\\', '/');
            AudioClip clip = string.IsNullOrWhiteSpace(embeddedPath)
                ? null
                : Resources.Load<AudioClip>(embeddedPath);
            ApplyResolvedSceneMusic(clip, musicIdentity, requestVersion);
        }

        bool modOnly = source == "mod" || source == "auto";
        Action<string> loadFailure = null;
        if (source == "auto")
        {
            loadFailure = _ => ResourceManager.instance.GetLocalAddressables<AudioClip>(loadPath, false,
                clip => ApplyResolvedSceneMusic(clip, musicIdentity, requestVersion),
                _ => LoadEmbeddedBuiltin());
        }
        else if (source == "builtin")
        {
            loadFailure = _ => LoadEmbeddedBuiltin();
        }
        ResourceManager.instance.GetLocalAddressables<AudioClip>(loadPath, modOnly,
            clip =>
            {
                ApplyResolvedSceneMusic(clip, musicIdentity, requestVersion);
            },
            loadFailure);
    }

    private void CaptureMusicContext()
    {
        if (AudioSystem.instance == null || musicSnapshot != null)
            return;

        musicSnapshot = AudioSystem.instance.CaptureMusic();
        activeMusicIdentity = AudioSystem.instance.CurrentMusicIdentity;
    }

    private void ApplyResolvedSceneMusic(AudioClip clip, string musicIdentity, int requestVersion)
    {
        if (requestVersion != sceneMusicRequestVersion || AudioSystem.instance == null || clip == null)
            return;
        if (string.Equals(activeMusicIdentity, musicIdentity, StringComparison.OrdinalIgnoreCase))
            return;

        hasChangedMusicIdentity = true;
        hasRestartedMusic |= AudioSystem.instance.PlayMusicTracked(clip, AudioVolumeType.BGM, musicIdentity);
        activeMusicIdentity = musicIdentity;
    }

    private void RestoreMusicContext()
    {
        if (musicSnapshot == null)
            return;

        if (!suppressMusicRestore && hasChangedMusicIdentity && AudioSystem.instance != null)
            AudioSystem.instance.TryRestoreMusic(musicSnapshot, activeMusicIdentity, hasRestartedMusic);

        musicSnapshot = null;
        hasChangedMusicIdentity = false;
        hasRestartedMusic = false;
    }

    private void SetScene(string path, int mapId = 0)
    {
        int requestVersion = ++sceneVisualRequestVersion;
        Sprite sprite = StorySpriteResolver.Load(path, story?.GetResourceSource(path));
        if (sprite != null)
        {
            ApplySceneSprite(sprite);
            return;
        }

        if (mapId == 0 && path.TryTrimStart("Maps/bg/", out string mapIdText))
            int.TryParse(mapIdText, out mapId);

        if (mapId == 0)
            return;

        Map.GetMap(mapId, map =>
        {
            if (requestVersion != sceneVisualRequestVersion)
                return;

            Sprite mapSprite = map?.resources?.bg;
            if (mapSprite == null)
            {
                int resourceId = map?.resId != 0 ? map.resId : mapId;
                string fallbackPath = "Maps/bg/" + resourceId;
                mapSprite = StorySpriteResolver.Load(fallbackPath, story?.GetResourceSource(fallbackPath));
            }
            ApplySceneSprite(mapSprite);
        }, _ => { });
    }

    private bool BeginSceneVisualChange(StoryCommand command, StoryTransitionDocument transition)
    {
        string path = StoryCommandArguments.GetValue(command.args, "bg", command.args);
        int mapId = command.mapId;
        int requestVersion = ++sceneVisualRequestVersion;
        Sprite sprite = StorySpriteResolver.Load(path, story?.GetResourceSource(path));
        if (sprite != null)
            return StartSceneTransition(command, sprite, transition);

        if (mapId == 0 && path.TryTrimStart("Maps/bg/", out string mapIdText))
            int.TryParse(mapIdText, out mapId);
        if (mapId == 0)
        {
            ApplySceneActors(command);
            return false;
        }

        isTransitioning = true;
        Map.GetMap(mapId, map =>
        {
            if (requestVersion != sceneVisualRequestVersion)
                return;

            Sprite mapSprite = map?.resources?.bg;
            if (mapSprite == null)
            {
                int resourceId = map?.resId != 0 ? map.resId : mapId;
                string fallbackPath = "Maps/bg/" + resourceId;
                mapSprite = StorySpriteResolver.Load(fallbackPath, story?.GetResourceSource(fallbackPath));
            }
            ResumeLoadedScene(command, mapSprite, transition);
        }, _ =>
        {
            if (requestVersion == sceneVisualRequestVersion)
                ResumeLoadedScene(command, null, transition);
        });
        return true;
    }

    private void ResumeLoadedScene(StoryCommand command, Sprite sprite, StoryTransitionDocument transition)
    {
        if (StartSceneTransition(command, sprite, transition))
            return;

        isTransitioning = false;
        ShowNextCommand();
    }

    private bool StartSceneTransition(StoryCommand command, Sprite sprite, StoryTransitionDocument transition)
    {
        string type = transition?.normalizedType ?? "none";
        if (type == "inherit")
            type = "none";
        if (sprite == null || sceneImage == null || type == "none")
        {
            if (sprite != null)
                ApplySceneSprite(sprite);
            ApplySceneActors(command);
            return false;
        }

        isTransitioning = true;
        sceneTransitionCoroutine = StartCoroutine(SceneTransitionCoroutine(
            command, sprite, type, transition.normalizedDuration));
        return true;
    }

    private IEnumerator SceneTransitionCoroutine(
        StoryCommand command,
        Sprite sprite,
        string type,
        float duration)
    {
        bool revealsSameBackground = type == "radial"
            && sceneImage != null
            && sceneImage.sprite == sprite;
        PrepareTransitionImage(sprite);
        if (type == "fade")
        {
            SetTransitionImagesActive(false);
            float halfDuration = duration * .5f;
            yield return Animate(halfDuration, progress => SetPrimaryImageAlpha(1f - progress));
            ApplySceneSprite(sprite);
            SetPrimaryImageAlpha(0f);
            yield return Animate(halfDuration, SetPrimaryImageAlpha);
        }
        else if (type == "crossfade")
        {
            SetTransitionImageAlpha(0f);
            yield return Animate(duration, SetTransitionImageAlpha);
        }
        else if (type == "wipeleft" || type == "wiperight"
            || type == "wipeup" || type == "wipedown" || type == "radial")
        {
            ConfigureWipe(transitionSceneImage, type);
            ConfigureWipe(dialogTransitionSceneImage, type);
            float revealDuration = duration;
            if (revealsSameBackground)
            {
                float dimDuration = Mathf.Min(.12f, duration * .2f);
                revealDuration = Mathf.Max(.01f, duration - dimDuration);
                yield return Animate(dimDuration, progress =>
                    SetPrimaryImageBrightness(Mathf.Lerp(1f, .35f, progress)));
            }
            yield return Animate(revealDuration, SetTransitionFillAmount);
        }
        else if (type == "zoomcross")
        {
            SetPrimaryImageAlpha(1f);
            SetTransitionImageAlpha(0f);
            SetPrimaryScale(Vector3.one);
            SetTransitionScale(Vector3.one * .88f);
            yield return Animate(duration, progress =>
            {
                SetPrimaryImageAlpha(1f - progress);
                SetTransitionImageAlpha(progress);
                SetPrimaryScale(Vector3.one * Mathf.Lerp(1f, 1.08f, progress));
                SetTransitionScale(Vector3.one * Mathf.Lerp(.88f, 1f, progress));
            });
        }
        else
        {
            float width = Mathf.Max(1f, sceneImage.rectTransform.rect.width);
            float height = Mathf.Max(1f, sceneImage.rectTransform.rect.height);
            Vector2 offset = type == "pushright" ? new Vector2(-width, 0f)
                : type == "pushup" ? new Vector2(0f, -height)
                : type == "pushdown" ? new Vector2(0f, height)
                : new Vector2(width, 0f);
            SetTransitionPosition(offset);
            yield return Animate(duration, progress =>
            {
                SetPrimaryPosition(-offset * progress);
                SetTransitionPosition(offset * (1f - progress));
            });
        }

        ApplySceneSprite(sprite);
        ApplySceneActors(command);
        ResetTransitionImages();
        sceneTransitionCoroutine = null;
        isTransitioning = false;
        ShowNextCommand();
    }

    private IEnumerator Animate(float duration, Action<float> update)
    {
        float elapsed = 0f;
        update?.Invoke(0f);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            update?.Invoke(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        update?.Invoke(1f);
    }

    private void PrepareTransitionImage(Sprite sprite)
    {
        PrepareTransitionImage(transitionSceneImage, sprite);
        PrepareTransitionImage(dialogTransitionSceneImage, sprite);
    }

    private void ResetTransitionImages()
    {
        SetPrimaryPosition(Vector2.zero);
        SetPrimaryImageAlpha(1f);
        SetPrimaryImageBrightness(1f);
        SetPrimaryScale(Vector3.one);
        SetTransitionScale(Vector3.one);
        ResetTransitionImage(transitionSceneImage);
        ResetTransitionImage(dialogTransitionSceneImage);
    }

    private static void PrepareTransitionImage(Image image, Sprite sprite)
    {
        if (image == null)
            return;
        image.sprite = sprite;
        image.color = Color.white;
        image.type = Image.Type.Simple;
        image.fillAmount = 1f;
        image.fillClockwise = true;
        image.rectTransform.anchoredPosition = Vector2.zero;
        image.rectTransform.localScale = Vector3.one;
        image.gameObject.SetActive(true);
    }

    private static void ResetTransitionImage(Image image)
    {
        if (image == null)
            return;
        image.rectTransform.anchoredPosition = Vector2.zero;
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.fillAmount = 1f;
        image.fillClockwise = true;
        image.rectTransform.localScale = Vector3.one;
        image.gameObject.SetActive(false);
    }

    private static void ConfigureWipe(Image image, string type)
    {
        if (image == null)
            return;
        image.type = Image.Type.Filled;
        if (type == "radial")
        {
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Bottom;
            image.fillClockwise = true;
        }
        else if (type == "wipeup" || type == "wipedown")
        {
            image.fillMethod = Image.FillMethod.Vertical;
            image.fillOrigin = type == "wipedown" ? 1 : 0;
        }
        else
        {
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = type == "wipeleft" ? 1 : 0;
        }
        image.fillAmount = 0f;
    }

    private void SetTransitionImagesActive(bool active)
    {
        if (transitionSceneImage != null)
            transitionSceneImage.gameObject.SetActive(active);
        if (dialogTransitionSceneImage != null)
            dialogTransitionSceneImage.gameObject.SetActive(active);
    }

    private void SetTransitionImageAlpha(float alpha)
    {
        SetImageAlpha(transitionSceneImage, alpha);
        SetImageAlpha(dialogTransitionSceneImage, alpha);
    }

    private void SetPrimaryImageAlpha(float alpha)
    {
        SetImageAlpha(sceneImage, alpha);
        SetImageAlpha(dialogSceneImage, alpha);
    }

    private void SetPrimaryImageBrightness(float brightness)
    {
        SetImageBrightness(sceneImage, brightness);
        SetImageBrightness(dialogSceneImage, brightness);
    }

    private void SetPrimaryScale(Vector3 scale)
    {
        if (sceneImage != null)
            sceneImage.rectTransform.localScale = scale;
        if (dialogSceneImage != null)
            dialogSceneImage.rectTransform.localScale = scale;
    }

    private void SetTransitionScale(Vector3 scale)
    {
        if (transitionSceneImage != null)
            transitionSceneImage.rectTransform.localScale = scale;
        if (dialogTransitionSceneImage != null)
            dialogTransitionSceneImage.rectTransform.localScale = scale;
    }

    private void SetTransitionFillAmount(float amount)
    {
        if (transitionSceneImage != null)
            transitionSceneImage.fillAmount = amount;
        if (dialogTransitionSceneImage != null)
            dialogTransitionSceneImage.fillAmount = amount;
    }

    private void SetPrimaryPosition(Vector2 position)
    {
        if (sceneImage != null)
            sceneImage.rectTransform.anchoredPosition = position;
        if (dialogSceneImage != null)
            dialogSceneImage.rectTransform.anchoredPosition = position;
    }

    private void SetTransitionPosition(Vector2 position)
    {
        if (transitionSceneImage != null)
            transitionSceneImage.rectTransform.anchoredPosition = position;
        if (dialogTransitionSceneImage != null)
            dialogTransitionSceneImage.rectTransform.anchoredPosition = position;
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        if (image == null)
            return;
        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }

    private static void SetImageBrightness(Image image, float brightness)
    {
        if (image == null)
            return;
        Color color = image.color;
        color.r = brightness;
        color.g = brightness;
        color.b = brightness;
        image.color = color;
    }

    private void ApplySceneActors(StoryCommand command)
    {
        actorStage.ApplyScene(command.actorLayouts, command.layout);
        foreach (StoryActorDocument actor in command.sceneActors ?? Array.Empty<StoryActorDocument>())
            actorStage.Show(actor, false);
        actorStage.PlaySceneEntrance();
    }

    private void PrepareSceneBoundary()
    {
        waitingForChoice = false;
        lastDialogInfo = null;
        DialogManager.instance?.SetStoryDialogBackgroundClickHandler(null);
        DialogManager.instance?.SetStoryDialogReplyClickHandler(null);
        DialogManager.instance?.SetStoryContentVisible(false);
        actorStage?.Clear();
    }

    private void ApplySceneSprite(Sprite sprite)
    {
        if (sceneImage == null)
            return;

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

        actorStage.Show(actor);
    }

    private void UnregisterActor(string args)
    {
        string[] tokens = StoryCommandArguments.Split(args);
        actorStage.Hide(tokens.Length == 0 ? null : tokens[0]);
    }

    private void SetDialogue(StoryActorDocument actor, string speaker, string content, string expression = null)
    {
        waitingForChoice = false;
        DialogManager.instance.SetStoryDialogReplyClickHandler(null);
        DialogManager.instance.ClearStoryChoices();
        DialogManager.instance.SetStoryDialogBackgroundClickHandler(Advance);
        actorStage?.SetActiveActor(actor?.id, StoryExpressionCatalog.GetMotion(expression));
        lastDialogInfo = CreateDialogInfo(actor, speaker, content, new List<NpcButtonHandler>(), expression);
        DialogManager.instance.OpenStoryDialog(lastDialogInfo, false);
        RefreshOverlayLayering();
    }

    private void SetNarration(string content)
    {
        waitingForChoice = false;
        DialogManager.instance.SetStoryDialogReplyClickHandler(null);
        DialogManager.instance.ClearStoryChoices();
        DialogManager.instance.SetStoryDialogBackgroundClickHandler(Advance);
        actorStage?.SetActiveActor(null);
        lastDialogInfo = CreateDialogInfo(null, NarratorName, content, new List<NpcButtonHandler>());
        DialogManager.instance.OpenStoryDialog(lastDialogInfo, false);
        RefreshOverlayLayering();
    }

    private void ShowChoices(StoryCommand command)
    {
        List<StoryChoice> choices = command?.choices;
        if (choices == null || choices.Count == 0)
        {
            ShowNextCommand();
            return;
        }

        if (!string.IsNullOrWhiteSpace(command.text))
            SetDialogue(command.actorInfo, command.speaker, command.text, command.expression);

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
                pointId = story.currentPointId,
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

    private DialogInfo CreateDialogInfo(StoryActorDocument actor, string speaker, string content,
        List<NpcButtonHandler> replyHandlers, string expression = null)
    {
        string iconPath = actor?.icon;
        bool hasIcon = !string.IsNullOrEmpty(iconPath);
        StorySceneActorLayoutDocument placement = actorStage?.GetPlacement(actor);

        return new DialogInfo
        {
            id = "story",
            iconId = hasIcon ? iconPath : "none",
            iconSize = hasIcon ? DefaultIconSize : "0,0",
            iconPos = hasIcon ? DefaultIconPos : "0,0",
            name = speaker ?? string.Empty,
            storySpeakerSide = placement?.normalizedSide ?? "left",
            storyFlipIcon = placement != null && (placement.flipIcon != (actor?.sourceFacesLeft ?? false)),
            storyUseIconCrop = actor != null && actor.usesPortraitIcon,
            storyIconCrop = actor?.normalizedIconCrop ?? new Rect(0f, 0f, 1f, 1f),
            storyExpression = expression,
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
        {
            commandIndex = labelIndex;
            story.BeginPointVisit(label);
        }
    }

    private void ExecuteMission(string args)
    {
        if (isPreviewMode)
            return;

        string[] tokens = StoryCommandArguments.Split(args);
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
                {
                    List<Item> rewards = Mission.Complete(missionId);
                    if (missionId == boundMissionId)
                        boundMissionCompleted = true;
                    ShowGrantedRewards(rewards);
                }
                break;
            case "checkpoint":
                if (tokens.Length >= 3)
                    Mission.Checkpoint(missionId, tokens[2]);
                break;
        }

        SaveSystem.SaveData();
    }

    private void FinishStory()
    {
        List<Item> rewards = CompleteBoundMission();
        ClosePanel();
        ShowGrantedRewards(rewards);
    }

    private List<Item> CompleteBoundMission()
    {
        if (isPreviewMode || boundMissionId == 0 || boundMissionCompleted)
            return new List<Item>();

        Mission mission = Mission.Find(boundMissionId);
        if (mission == null)
            return new List<Item>();

        boundMissionCompleted = true;
        List<Item> rewards = Mission.Complete(boundMissionId);
        SaveSystem.SaveData();
        return rewards;
    }

    private static void ShowGrantedRewards(IReadOnlyList<Item> rewards)
    {
        if (rewards == null || rewards.Count == 0)
            return;

        string[] lines = rewards
            .Where(reward => reward != null && reward.info != null)
            .Select(reward => reward.name + " × " + reward.num)
            .ToArray();
        if (lines.Length == 0)
            return;

        Hintbox.OpenHintboxWithContent("任务完成，奖励已发放：\n" + string.Join("\n", lines), 16)
            .SetSize(520, 300);
    }

    private void Teleport(string args)
    {
        if (!int.TryParse(args.Trim(), out int mapId))
            return;

        List<Item> rewards = CompleteBoundMission();
        isClosing = true;
        suppressMusicRestore = true;
        ClosePanel();
        TeleportHandler.Teleport(mapId);
        ShowGrantedRewards(rewards);
    }

    private StoryActorDocument BuildLegacyActor(string args)
    {
        string[] tokens = StoryCommandArguments.Split(args);
        if (tokens.Length < 2)
            return null;

        return new StoryActorDocument
        {
            id = tokens[0],
            name = tokens[0],
            sprite = StorySpriteResolver.Normalize(tokens[1]),
            icon = StorySpriteResolver.Normalize(tokens[1]),
            defaultFaceLeft = true,
            defaultScale = 1f,
        };
    }

    private StoryCommand GetCurrentChoiceCommand()
    {
        int index = Mathf.Clamp(commandIndex - 1, 0, story?.commands.Count - 1 ?? 0);
        return story == null || story.commands.Count == 0 ? null : story.commands[index];
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
        TMP_FontAsset storyFont = StoryTextFontProvider.GetDefaultFont();
        TMP_Text textSample = FindTextSample();
        if (storyFont != null)
        {
            text.font = storyFont;
            text.fontSharedMaterial = storyFont.material;
        }
        else if (textSample != null && textSample.font != null)
        {
            text.font = textSample.font;
            text.fontSharedMaterial = textSample.fontSharedMaterial;
        }

        text.text = isPreviewMode ? "退出预览" : "\u9000\u51FA\u4EFB\u52A1";
        text.fontSize = 18f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color32(255, 238, 92, 255);
        text.raycastTarget = false;
    }

    private void CreatePreviewIndicator()
    {
        GameObject obj = new GameObject("Story Preview Indicator",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        previewIndicator = obj;

        Transform parent = transform.parent != null ? transform.parent : transform;
        obj.transform.SetParent(parent, false);
        obj.transform.SetAsLastSibling();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -20f);
        rect.sizeDelta = new Vector2(650f, 38f);

        Image image = obj.GetComponent<Image>();
        image.color = new Color32(0, 12, 18, 224);
        image.raycastTarget = false;

        Outline outline = obj.GetComponent<Outline>();
        outline.effectColor = new Color32(44, 227, 255, 190);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        GameObject textObject = new GameObject("Text",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(obj.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 0f);
        textRect.offsetMax = new Vector2(-12f, 0f);

        previewIndicatorText = textObject.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset storyFont = StoryTextFontProvider.GetDefaultFont();
        TMP_Text textSample = FindTextSample();
        if (storyFont != null)
        {
            previewIndicatorText.font = storyFont;
            previewIndicatorText.fontSharedMaterial = storyFont.material;
        }
        else if (textSample != null && textSample.font != null)
        {
            previewIndicatorText.font = textSample.font;
            previewIndicatorText.fontSharedMaterial = textSample.fontSharedMaterial;
        }

        previewIndicatorText.fontSize = 16f;
        previewIndicatorText.alignment = TextAlignmentOptions.MidlineLeft;
        previewIndicatorText.color = new Color32(180, 235, 245, 255);
        previewIndicatorText.overflowMode = TextOverflowModes.Ellipsis;
        previewIndicatorText.raycastTarget = false;
        BringExitButtonToFront();
    }

    private void BringExitButtonToFront()
    {
        if (exitButton != null)
            exitButton.transform.SetAsLastSibling();
    }

    private void BringPreviewIndicatorToFront()
    {
        if (previewIndicator != null)
            previewIndicator.transform.SetAsLastSibling();
    }

    private void RefreshOverlayLayering()
    {
        DialogManager.instance?.RefreshStoryOverlayLayering();
        BringPreviewIndicatorToFront();
        BringExitButtonToFront();
        if (battlePromptMask != null)
            battlePromptMask.SetAsLastSibling();
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

}
