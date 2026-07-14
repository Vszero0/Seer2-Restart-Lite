using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 剧情点的可视化编辑画布。
/// 舞台和对白直接复用运行时 Dialog Story Layer；资源选择、命令追加与草稿保存保持在编辑器侧。
/// </summary>
public class WorkshopStoryNodeEditorPanel : Panel
{
    private static readonly Color Cyan = new Color32(82, 229, 249, 255);
    private static readonly Color StageFallbackColor = new Color32(8, 11, 18, 255);
    private const string DefaultIconSize = "90,120";
    private const string DefaultIconPosition = "0,45";

    private readonly WorkshopStoryNodeEditorController controller = new WorkshopStoryNodeEditorController(
        new WorkshopStoryNodeEditorModel(new WorkshopStoryRepository()));

    private string pendingStoryPath;
    private string pendingNodeId;
    private string activeSceneId;
    private bool hasBuilt;
    private bool isUpdatingDialogueInput;
    private int sceneVisualRequestVersion;
    private int sceneMusicRequestVersion;
    private Font font;
    private GameObject actionButtonPrefab;
    private GameObject listButtonPrefab;
    private GameObject dialogueInputPrefab;
    private GameObject dropdownPrefab;
    private RectTransform storyLayer;
    private Image sceneImage;
    private DialogController dialogController;
    private StoryActorStage actorStage;
    private RectTransform actorLayer;
    private RectTransform toolbar;
    private RectTransform editorActions;
    private Text nodeTitleText;
    private Text dirtyStateText;
    private Text sceneStateText;
    private Dropdown sceneDropdown;
    private Dropdown sceneActorDropdown;
    private Dropdown sceneContentDropdown;
    private Text sceneDropdownValueText;
    private Text sceneActorDropdownValueText;
    private Text sceneContentDropdownValueText;
    private Text layoutModeButtonText;
    private string activeSceneActorId;
    private bool isUpdatingSceneSelectors;
    private TextMeshProUGUI sourceDialogueText;
    private IInputField dialogueInput;
    private InputField nativeDialogueInput;
    private StoryCommandDocument activeDialogueCommand;
    private Color sourceDialogueVisibleColor;
    private bool sourceDialogueIsHidden;
    private WorkshopStoryPointResourcePicker resourcePicker;
    private int renderedMapId = int.MinValue;
    private string renderedActorSignature;
    private string renderedMusicSignature;

    public static WorkshopStoryNodeEditorPanel Open(string storyPath, string nodeId)
    {
        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null)
            return null;

        GameObject obj = new GameObject("Workshop Story Node Editor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(WorkshopStoryNodeEditorPanel));
        obj.transform.SetParent(canvas.transform, false);
        obj.transform.SetAsLastSibling();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image rootImage = obj.GetComponent<Image>();
        rootImage.color = Color.clear;
        rootImage.raycastTarget = false;

        WorkshopStoryNodeEditorPanel panel = obj.GetComponent<WorkshopStoryNodeEditorPanel>();
        panel.OpenTarget(storyPath, nodeId);
        return panel;
    }

    public override void Init()
    {
        base.Init();
        if (hasBuilt)
            return;

        hasBuilt = true;
        font = ResourceManager.instance.GetFont("Zongyi");
        actionButtonPrefab = FindWorkshopActionButtonPrefab();
        listButtonPrefab = Resources.Load<GameObject>("Prefabs/Scroll List Button");
        dialogueInputPrefab = FindWorkshopDescriptionInputFieldPrefab();
        dropdownPrefab = FindWorkshopDropdownPrefab();
        BuildRuntimeCanvas();
        BuildToolbar();
        resourcePicker = new WorkshopStoryPointResourcePicker(transform, actionButtonPrefab, listButtonPrefab, dialogueInputPrefab, font);

        if (!string.IsNullOrWhiteSpace(pendingStoryPath))
            LoadTarget(pendingStoryPath, pendingNodeId);
    }

    public override void ClosePanel()
    {
        resourcePicker?.Close();
        actorStage?.Clear();
        base.ClosePanel();
    }

    public void OpenTarget(string storyPath, string nodeId)
    {
        pendingStoryPath = storyPath;
        pendingNodeId = nodeId;
        if (hasBuilt)
            LoadTarget(storyPath, nodeId);
    }

    private void BuildRuntimeCanvas()
    {
        GameObject storyLayerPrefab = Resources.Load<GameObject>("Prefabs/Dialog Story Layer");
        if (storyLayerPrefab == null)
            return;

        GameObject layer = Instantiate(storyLayerPrefab, transform);
        layer.name = "Story Editor Runtime Canvas";
        storyLayer = layer.GetComponent<RectTransform>();
        storyLayer.anchorMin = Vector2.zero;
        storyLayer.anchorMax = Vector2.one;
        storyLayer.offsetMin = Vector2.zero;
        storyLayer.offsetMax = Vector2.zero;
        layer.SetActive(true);

        dialogController = layer.GetComponentInChildren<DialogController>(true);
        sceneImage = layer.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(image => image.gameObject.name == "Background"
                && Vector2.Distance(image.rectTransform.anchorMin, Vector2.zero) < .001f
                && Vector2.Distance(image.rectTransform.anchorMax, Vector2.one) < .001f);
        if (sceneImage != null)
        {
            // 背景是舞台，要留给立绘的拖拽和选取，而不是资源选择按钮。
            sceneImage.raycastTarget = false;
            IButton backgroundButton = sceneImage.GetComponent<IButton>();
            if (backgroundButton != null)
                backgroundButton.enabled = false;
        }

        RectTransform textBar = layer.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.gameObject.name == "Text Bar");
        if (textBar != null)
        {
            Image textBarBackground = textBar.GetComponent<Image>() ?? textBar.gameObject.AddComponent<Image>();
            textBarBackground.color = new Color(0f, 0f, 0f, .62f);
            textBarBackground.raycastTarget = false;
        }

        actorLayer = CreateRect("Story Editor Actor Layer", storyLayer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        actorLayer.SetSiblingIndex(sceneImage == null ? 0 : sceneImage.transform.GetSiblingIndex() + 1);
        actorStage = new StoryActorStage(actorLayer, this, RefreshOverlayLayering, controller.GetResourceSource);
        sourceDialogueText = layer.GetComponentsInChildren<TextMeshProUGUI>(true)
            .FirstOrDefault(text => text.gameObject.name == "Dialog");
        if (sourceDialogueText != null)
            sourceDialogueVisibleColor = sourceDialogueText.color;
    }

    private void BuildToolbar()
    {
        toolbar = CreateRect("Story Editor Toolbar", transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -24f), new Vector2(0f, 48f));
        Image toolbarBackground = toolbar.gameObject.AddComponent<Image>();
        toolbarBackground.color = new Color(0f, 0f, 0f, .78f);
        toolbarBackground.raycastTarget = true;

        CreateToolbarButton(toolbar, "返回", new Vector2(18f, -8f), new Vector2(96f, 28f), ClosePanel, false);
        CreateToolbarButton(toolbar, "保存", new Vector2(-18f, -8f), new Vector2(96f, 28f), SaveDraft, true);
        CreateToolbarButton(toolbar, "预览", new Vector2(-122f, -8f), new Vector2(96f, 28f), PreviewDraft, true);
        nodeTitleText = CreateText("Node Title", toolbar, "编辑剧情点", 22, TextAnchor.MiddleCenter, Cyan,
            new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -8f), new Vector2(420f, 28f));

        editorActions = CreateRect("Story Editor Console", transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(14f, -58f), new Vector2(604f, 100f));
        editorActions.pivot = new Vector2(0f, 1f);
        Image consoleBackground = editorActions.gameObject.AddComponent<Image>();
        consoleBackground.color = new Color(0f, 0f, 0f, .76f);
        consoleBackground.raycastTarget = true;

        CreateText("Scene Group", editorActions, "场景", 13, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -5f), new Vector2(34f, 20f));
        sceneDropdown = CreateDropdown(editorActions, new Vector2(46f, -3f), new Vector2(154f, 25f), OnSceneDropdownChanged);
        sceneDropdownValueText = CreateSelectorValueText(sceneDropdown);
        CreateToolbarButton(editorActions, "新建", new Vector2(208f, -3f), new Vector2(58f, 25f), OpenCreateScenePicker, false);
        CreateToolbarButton(editorActions, "删除", new Vector2(274f, -3f), new Vector2(58f, 25f), RemoveActiveScene, false);
        CreateToolbarButton(editorActions, "更换背景", new Vector2(340f, -3f), new Vector2(82f, 25f), OpenChangeSceneMapPicker, false);
        sceneStateText = CreateText("Scene State", editorActions, string.Empty, 12, TextAnchor.MiddleLeft, Color.white,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(430f, -5f), new Vector2(164f, 20f));

        CreateText("Actor Group", editorActions, "角色", 13, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -33f), new Vector2(34f, 20f));
        sceneActorDropdown = CreateDropdown(editorActions, new Vector2(46f, -31f), new Vector2(90f, 25f), OnSceneActorDropdownChanged);
        sceneActorDropdownValueText = CreateSelectorValueText(sceneActorDropdown);
        CreateToolbarButton(editorActions, "添加精灵", new Vector2(144f, -31f), new Vector2(65f, 25f), OpenPetPicker, false);
        CreateToolbarButton(editorActions, "移除", new Vector2(217f, -31f), new Vector2(50f, 25f), RemoveActiveSceneActor, false);
        CreateToolbarButton(editorActions, "放左", new Vector2(275f, -31f), new Vector2(48f, 25f), () => SetActiveActorSide("left"), false);
        CreateToolbarButton(editorActions, "放右", new Vector2(331f, -31f), new Vector2(48f, 25f), () => SetActiveActorSide("right"), false);
        CreateToolbarButton(editorActions, "靠内", new Vector2(387f, -31f), new Vector2(48f, 25f), () => MoveActiveActorDepth(false), false);
        CreateToolbarButton(editorActions, "靠外", new Vector2(443f, -31f), new Vector2(48f, 25f), () => MoveActiveActorDepth(true), false);
        layoutModeButtonText = CreateToolbarButton(editorActions, "布局", new Vector2(499f, -31f), new Vector2(95f, 25f), ToggleAutoLayoutMode, false);

        CreateText("Content Group", editorActions, "内容", 13, TextAnchor.MiddleLeft, Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -61f), new Vector2(34f, 20f));
        sceneContentDropdown = CreateDropdown(editorActions, new Vector2(46f, -59f), new Vector2(190f, 25f), OnSceneContentDropdownChanged);
        sceneContentDropdownValueText = CreateSelectorValueText(sceneContentDropdown);
        CreateToolbarButton(editorActions, "+旁白", new Vector2(244f, -59f), new Vector2(58f, 25f), CreateNarration, false);
        CreateToolbarButton(editorActions, "+对白", new Vector2(310f, -59f), new Vector2(58f, 25f), OpenSayActorPicker, false);
        CreateToolbarButton(editorActions, "删除内容", new Vector2(376f, -59f), new Vector2(70f, 25f), RemoveActiveSceneContent, false);
        dirtyStateText = CreateText("Dirty State", editorActions, string.Empty, 12, TextAnchor.MiddleRight,
            new Color32(255, 230, 92, 255), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(454f, -61f), new Vector2(138f, 20f));
        toolbar.SetAsLastSibling();
        editorActions.SetAsLastSibling();
    }

    private void LoadTarget(string storyPath, string nodeId)
    {
        if (!controller.Open(storyPath, nodeId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            ClosePanel();
            return;
        }

        activeSceneId = null;
        activeDialogueCommand = null;
        renderedMapId = int.MinValue;
        renderedActorSignature = null;
        renderedMusicSignature = null;
        RefreshCanvas();
    }

    private void RefreshCanvas()
    {
        StoryDocument document = controller.DraftDocument;
        StoryNodeDocument node = controller.DraftNode;
        if (document == null || node == null)
            return;

        string displayName = string.IsNullOrWhiteSpace(node.displayName) ? node.id : node.displayName;
        nodeTitleText.text = "编辑剧情点 · " + displayName;
        dirtyStateText.text = controller.HasUnsavedChanges ? "未保存" : string.Empty;

        List<WorkshopStorySceneSection> sections = controller.GetSceneSections();
        StorySceneDocument activeScene = node.GetScene(activeSceneId)
            ?? sections.FirstOrDefault(section => section?.scene != null)?.scene;
        activeSceneId = activeScene?.id;
        if (sceneStateText != null)
            sceneStateText.text = activeScene == null ? "请先通过“地图资源”添加地图" : "当前地图：" + activeScene.mapId;
        RefreshSceneSelectors(node, activeScene);
        RefreshSceneActorSelector(activeScene);
        RefreshLayoutModeLabel(activeScene);

        StoryCommandDocument[] textCommands = controller.GetSceneCommands(activeSceneId)
            .Where(command => command != null
                && (string.Equals(command.type, "say", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(command.type, "narrate", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (activeDialogueCommand == null || !textCommands.Any(command => string.Equals(command.commandId,
                activeDialogueCommand.commandId, StringComparison.OrdinalIgnoreCase)))
        {
            activeDialogueCommand = textCommands.LastOrDefault();
        }
        RefreshSceneContentSelector(textCommands);

        RefreshActorStage(document, activeScene);

        SetSceneBackground(activeScene);
        PlaySceneMusic(activeScene);
        SetRuntimeDialogue(document, node, activeScene, activeDialogueCommand);
        RefreshOverlayLayering();
    }

    private void SetSceneBackground(StorySceneDocument scene)
    {
        if (sceneImage == null)
            return;

        int mapId = scene?.mapId ?? 0;
        if (renderedMapId == mapId)
            return;

        renderedMapId = mapId;

        int requestVersion = ++sceneVisualRequestVersion;
        sceneImage.sprite = null;
        sceneImage.color = StageFallbackColor;
        if (scene == null || scene.mapId == 0)
            return;

        Map.GetMap(scene.mapId, map =>
        {
            if (requestVersion != sceneVisualRequestVersion || sceneImage == null)
                return;

            Sprite sprite = map?.resources?.bg;
            if (sprite == null)
            {
                int resourceId = map?.resId > 0 ? map.resId : scene.mapId;
                string path = "Maps/bg/" + resourceId;
                sprite = StorySpriteResolver.Load(path, controller.GetResourceSource(path));
            }

            sceneImage.sprite = sprite;
            sceneImage.color = sprite == null ? StageFallbackColor : Color.white;
        }, _ => { });
    }

    private void PlaySceneMusic(StorySceneDocument scene)
    {
        if (AudioSystem.instance == null || scene == null)
            return;

        string musicSignature = scene.mapId + "|" + (scene.bgmResourcePath ?? string.Empty);
        if (string.Equals(renderedMusicSignature, musicSignature, StringComparison.Ordinal))
            return;

        renderedMusicSignature = musicSignature;

        int requestVersion = ++sceneMusicRequestVersion;
        if (string.IsNullOrWhiteSpace(scene.bgmResourcePath))
        {
            Map.GetMap(scene.mapId, map =>
            {
                if (requestVersion == sceneMusicRequestVersion && map?.resources?.bgm != null)
                    AudioSystem.instance?.PlayMusic(map.resources.bgm, AudioVolumeType.BGM);
            }, _ => { });
            return;
        }

        string source = controller.GetResourceSource(scene.bgmResourcePath);
        bool modOnly = source == "mod" || source == "auto";
        ResourceManager.instance.GetLocalAddressables<AudioClip>(scene.bgmResourcePath, modOnly,
            clip =>
            {
                if (requestVersion == sceneMusicRequestVersion)
                    AudioSystem.instance?.PlayMusic(clip, AudioVolumeType.BGM);
            },
            modOnly && source == "auto"
                ? _ => ResourceManager.instance.GetLocalAddressables<AudioClip>(scene.bgmResourcePath, false,
                    clip =>
                    {
                        if (requestVersion == sceneMusicRequestVersion)
                            AudioSystem.instance?.PlayMusic(clip, AudioVolumeType.BGM);
                    })
                : null);
    }

    private void RefreshActorStage(StoryDocument document, StorySceneDocument scene)
    {
        string signature = BuildSceneActorSignature(scene);
        if (string.Equals(renderedActorSignature, signature, StringComparison.Ordinal))
            return;

        renderedActorSignature = signature;
        actorStage.Reset(document.layout);
        if (scene == null)
            return;

        actorStage.ApplyScene(scene.actors, scene.layout);
        foreach (StorySceneActorLayoutDocument layout in scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
            actorStage.Show(document.GetActor(layout?.actorId));
    }

    private static string BuildSceneActorSignature(StorySceneDocument scene)
    {
        if (scene == null)
            return string.Empty;

        StoryLayoutDocument layout = scene.layout;
        string layoutSignature = string.Join(",", layout?.autoLayoutMode ?? string.Empty,
            layout?.actorSpacing ?? 0f, layout?.actorHeight ?? 0f, layout?.actorBottom ?? 0f,
            layout?.centerGap ?? 0f, layout?.stackOffset ?? 0f);
        return scene.id + "|" + layoutSignature + "|" + string.Join(";", (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
            .Where(layout => layout != null)
            .Select(layout => string.Join(",", layout.actorId, layout.placementMode, layout.side,
                layout.order, layout.x, layout.y, layout.scale, layout.faceLeft, layout.flipIcon)));
    }

    private void SetRuntimeDialogue(StoryDocument document, StoryNodeDocument node, StorySceneDocument scene,
        StoryCommandDocument command)
    {
        if (dialogController == null)
            return;

        RestoreSourceDialogueText();
        string commandType = (command?.type ?? string.Empty).Trim().ToLowerInvariant();
        StoryActorDocument actor = commandType == "narrate" ? null : document.GetActor(command?.actor);
        StorySceneActorLayoutDocument placement = scene?.GetActorLayout(actor?.id);
        bool isNarration = command == null || commandType == "narrate" || actor == null;
        bool canEditDialogue = scene != null;
        string content = command == null
            ? (canEditDialogue ? "点击此处输入旁白" : "请先点击背景选择本剧情点的地图")
            : command.text ?? string.Empty;

        dialogController.OpenDialog(new DialogInfo
        {
            id = "story",
            iconId = isNarration ? "none" : actor.icon,
            iconSize = isNarration ? "0,0" : DefaultIconSize,
            iconPos = isNarration ? "0,0" : DefaultIconPosition,
            name = isNarration ? "旁白" : actor.displayName,
            storySpeakerSide = placement?.normalizedSide ?? "left",
            storyFlipIcon = placement != null && placement.flipIcon,
            storyTextStyle = node.style ?? document.style,
            rawContent = content,
            functionHandler = new List<NpcButtonHandler>(),
            replyHandler = new List<NpcButtonHandler>(),
        });
        if (sourceDialogueText != null)
            sourceDialogueVisibleColor = sourceDialogueText.color;

        actorStage.SetActiveActor(isNarration ? null : actor.id);
        RefreshDialogueInput(command, canEditDialogue);
    }

    private void RefreshDialogueInput(StoryCommandDocument command, bool interactable)
    {
        if (sourceDialogueText == null)
            return;

        if (!interactable)
        {
            if (dialogueInput != null)
                dialogueInput.gameObject.SetActive(false);
            return;
        }

        EnsureDialogueInput();
        if (dialogueInput == null || nativeDialogueInput == null)
            return;

        dialogueInput.gameObject.SetActive(true);
        CopyRectTransform(sourceDialogueText.rectTransform, dialogueInput.GetComponent<RectTransform>());
        dialogueInput.transform.SetAsLastSibling();
        nativeDialogueInput.interactable = true;
        nativeDialogueInput.lineType = InputField.LineType.MultiLineNewline;
        nativeDialogueInput.contentType = InputField.ContentType.Standard;

        Text inputText = nativeDialogueInput.textComponent;
        if (inputText != null)
        {
            inputText.font = font;
            inputText.fontSize = Mathf.RoundToInt(sourceDialogueText.fontSize);
            inputText.alignment = TextAnchor.UpperLeft;
            inputText.color = sourceDialogueText.color;
            inputText.raycastTarget = false;
        }

        Text placeholder = nativeDialogueInput.placeholder as Text;
        if (placeholder != null)
        {
            placeholder.font = font;
            placeholder.fontSize = Mathf.RoundToInt(sourceDialogueText.fontSize);
            placeholder.alignment = TextAnchor.UpperLeft;
            placeholder.color = new Color(sourceDialogueText.color.r, sourceDialogueText.color.g, sourceDialogueText.color.b, .62f);
            placeholder.text = command == null ? "点击此处输入旁白" : "";
            placeholder.raycastTarget = false;
        }

        Image inputImage = dialogueInput.GetComponent<Image>();
        if (inputImage != null)
        {
            inputImage.color = Color.clear;
            inputImage.raycastTarget = true;
        }

        Color hiddenColor = sourceDialogueVisibleColor;
        hiddenColor.a = 0f;
        sourceDialogueText.color = hiddenColor;
        sourceDialogueIsHidden = true;

        isUpdatingDialogueInput = true;
        nativeDialogueInput.SetTextWithoutNotify(command?.text ?? string.Empty);
        isUpdatingDialogueInput = false;
    }

    private void EnsureDialogueInput()
    {
        if (dialogueInput != null || sourceDialogueText == null || dialogueInputPrefab == null)
            return;

        GameObject inputObject = Instantiate(dialogueInputPrefab, sourceDialogueText.transform.parent);
        inputObject.name = "Story Dialogue Input";
        dialogueInput = inputObject.GetComponent<IInputField>();
        nativeDialogueInput = inputObject.GetComponent<InputField>();
        if (dialogueInput == null || nativeDialogueInput == null)
        {
            Destroy(inputObject);
            dialogueInput = null;
            nativeDialogueInput = null;
            return;
        }

        nativeDialogueInput.onValueChanged = new InputField.OnChangeEvent();
        nativeDialogueInput.onEndEdit = new InputField.EndEditEvent();
        nativeDialogueInput.onValueChanged.AddListener(OnDialogueTextChanged);
        dialogueInput.SetPlaceHolderText("点击此处输入旁白");
    }

    private void RestoreSourceDialogueText()
    {
        if (sourceDialogueText == null || !sourceDialogueIsHidden)
            return;

        sourceDialogueText.color = sourceDialogueVisibleColor;
        sourceDialogueIsHidden = false;
    }

    private void OnDialogueTextChanged(string value)
    {
        if (isUpdatingDialogueInput)
            return;

        if (activeDialogueCommand == null)
        {
            if (!controller.CreateNarrationCommand(activeSceneId, out activeDialogueCommand, out string createError))
            {
                Hintbox.OpenHintboxWithContent(createError, 16);
                return;
            }
        }

        if (!controller.UpdateCommandText(activeDialogueCommand.commandId, value, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        dirtyStateText.text = "未保存";
    }

    private void OpenCreateScenePicker()
    {
        resourcePicker?.OpenMaps(controller.GetMapOptions, CreateSceneFromMap, CreateSceneFromMap);
    }

    private void CreateSceneFromMap(int mapId)
    {
        if (!controller.CreateScene(mapId, out StorySceneDocument scene, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        resourcePicker?.Close();
        activeSceneId = scene.id;
        activeDialogueCommand = null;
        RefreshCanvas();
    }

    private void OpenChangeSceneMapPicker()
    {
        if (string.IsNullOrWhiteSpace(activeSceneId))
        {
            Hintbox.OpenHintboxWithContent("请先新建并选择一个场景。", 16);
            return;
        }

        resourcePicker?.OpenMaps(controller.GetMapOptions, ChangeSceneMap, ChangeSceneMap);
    }

    private void ChangeSceneMap(int mapId)
    {
        if (!controller.SetSceneMap(activeSceneId, mapId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        resourcePicker?.Close();
        RefreshCanvas();
    }

    private void OpenPetPicker()
    {
        resourcePicker?.OpenPets(controller.GetPetOptions, AddPet);
    }

    private void AddPet(int petId)
    {
        if (string.IsNullOrWhiteSpace(activeSceneId))
        {
            Hintbox.OpenHintboxWithContent("请先新建并选择一个场景。", 16);
            return;
        }

        if (!controller.AddPetActor(petId, activeSceneId, out StoryActorDocument actor, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        resourcePicker?.Close();
        activeSceneActorId = actor.id;
        RefreshCanvas();
    }

    private void RemoveActiveSceneActor()
    {
        if (string.IsNullOrWhiteSpace(activeSceneActorId))
        {
            Hintbox.OpenHintboxWithContent("请先选择一个场景角色。", 16);
            return;
        }

        if (!controller.SetActorVisible(activeSceneActorId, activeSceneId, false, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        activeSceneActorId = null;
        RefreshCanvas();
    }

    private void CreateNarration()
    {
        if (controller.DraftNode?.GetScene(activeSceneId) == null)
        {
            Hintbox.OpenHintboxWithContent("请先选择本剧情点的地图。", 16);
            return;
        }

        if (!controller.CreateNarrationCommand(activeSceneId, out activeDialogueCommand, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        RefreshCanvas();
        FocusDialogueInput();
    }

    private void RemoveActiveSceneContent()
    {
        if (activeDialogueCommand == null || string.IsNullOrWhiteSpace(activeDialogueCommand.commandId))
        {
            Hintbox.OpenHintboxWithContent("请先在“内容”中选择要删除的旁白或对白。", 16);
            return;
        }

        List<StoryCommandDocument> commands = GetTextCommandsForActiveScene();
        int removedIndex = commands.FindIndex(command => string.Equals(command.commandId,
            activeDialogueCommand.commandId, StringComparison.OrdinalIgnoreCase));
        string commandId = activeDialogueCommand.commandId;
        if (!controller.RemoveSceneTextCommand(activeSceneId, commandId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        List<StoryCommandDocument> remaining = GetTextCommandsForActiveScene();
        activeDialogueCommand = remaining.Count == 0
            ? null
            : remaining[Mathf.Clamp(removedIndex - 1, 0, remaining.Count - 1)];
        RefreshCanvas();
    }

    private List<StoryCommandDocument> GetTextCommandsForActiveScene()
    {
        return controller.GetSceneCommands(activeSceneId)
            .Where(command => command != null && (string.Equals(command.type, "say", StringComparison.OrdinalIgnoreCase)
                || string.Equals(command.type, "narrate", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void OpenSayActorPicker()
    {
        List<WorkshopStoryPointActorOption> actors = controller.GetVisibleActorOptions(activeSceneId);
        if (actors.Count == 0)
        {
            Hintbox.OpenHintboxWithContent("请先在“精灵资源”中添加精灵，并让它显示在当前场景。", 16);
            return;
        }

        resourcePicker?.OpenActors(actors, CreateSay);
    }

    private void CreateSay(string actorId)
    {
        if (!controller.CreateSayCommand(actorId, activeSceneId, out activeDialogueCommand, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        resourcePicker?.Close();
        RefreshCanvas();
        FocusDialogueInput();
    }

    private void FocusDialogueInput()
    {
        if (nativeDialogueInput == null || !nativeDialogueInput.gameObject.activeInHierarchy)
            return;

        nativeDialogueInput.Select();
        nativeDialogueInput.ActivateInputField();
    }

    private void RemoveActiveScene()
    {
        if (string.IsNullOrWhiteSpace(activeSceneId))
            return;

        WorkshopStorySceneSection section = controller.GetSceneSections()
            .FirstOrDefault(value => value?.scene != null && value.scene.id == activeSceneId);
        if (section != null && section.contentCount > 0)
        {
            string sceneId = activeSceneId;
            Hintbox hintbox = Hintbox.OpenHintbox();
            hintbox.SetContent("删除该场景会同时删除其中 " + section.contentCount + " 条剧情内容，是否继续？", 16, FontOption.Arial);
            hintbox.SetOptionNum(2);
            hintbox.SetOptionCallback(() => ConfirmRemoveScene(sceneId));
            return;
        }

        ConfirmRemoveScene(activeSceneId);
    }

    private void ConfirmRemoveScene(string sceneId)
    {
        if (!controller.RemoveScene(sceneId, true, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        activeSceneId = null;
        activeDialogueCommand = null;
        RefreshCanvas();
    }

    private void RefreshSceneSelectors(StoryNodeDocument node, StorySceneDocument activeScene)
    {
        isUpdatingSceneSelectors = true;
        try
        {
            List<WorkshopStorySceneSection> sections = controller.GetSceneSections();
            List<StorySceneDocument> scenes = sections.Where(section => section?.scene != null).Select(section => section.scene).ToList();
            if (sceneDropdown != null)
            {
                sceneDropdown.ClearOptions();
                sceneDropdown.AddOptions(sections.Select((section, index) => "场景 " + (index + 1) + " · 地图 "
                    + section.scene.mapId + " · " + section.contentCount + " 条内容").ToList());
                sceneDropdown.value = Mathf.Max(0, scenes.FindIndex(scene => scene.id == activeScene?.id));
                sceneDropdown.RefreshShownValue();
                sceneDropdown.interactable = scenes.Count > 0;
                SetSelectorValueText(sceneDropdownValueText, sceneDropdown, "未选择场景");
            }

        }
        finally
        {
            isUpdatingSceneSelectors = false;
        }
    }

    private void OnSceneDropdownChanged(int index)
    {
        if (isUpdatingSceneSelectors)
            return;

        List<WorkshopStorySceneSection> sections = controller.GetSceneSections();
        if (index < 0 || index >= sections.Count)
            return;

        activeSceneId = sections[index].scene.id;
        activeDialogueCommand = null;
        RefreshCanvas();
    }

    private void RefreshSceneActorSelector(StorySceneDocument scene)
    {
        if (sceneActorDropdown == null)
            return;

        List<WorkshopStoryPointActorOption> actors = controller.GetVisibleActorOptions(scene?.id);
        if (actors.All(actor => actor == null || !string.Equals(actor.actorId, activeSceneActorId, StringComparison.OrdinalIgnoreCase)))
            activeSceneActorId = actors.FirstOrDefault()?.actorId;

        sceneActorDropdown.ClearOptions();
        sceneActorDropdown.AddOptions(actors.Select(actor => actor.displayName).ToList());
        sceneActorDropdown.value = Mathf.Max(0, actors.FindIndex(actor => actor != null
            && string.Equals(actor.actorId, activeSceneActorId, StringComparison.OrdinalIgnoreCase)));
        sceneActorDropdown.RefreshShownValue();
        sceneActorDropdown.interactable = actors.Count > 0;
        SetSelectorValueText(sceneActorDropdownValueText, sceneActorDropdown, "未添加角色");
    }

    private void OnSceneActorDropdownChanged(int index)
    {
        List<WorkshopStoryPointActorOption> actors = controller.GetVisibleActorOptions(activeSceneId);
        if (index >= 0 && index < actors.Count)
            activeSceneActorId = actors[index].actorId;
        SetSelectorValueText(sceneActorDropdownValueText, sceneActorDropdown, "未添加角色");
    }

    private void RefreshSceneContentSelector(IReadOnlyList<StoryCommandDocument> commands)
    {
        if (sceneContentDropdown == null)
            return;

        List<StoryCommandDocument> content = commands?.ToList() ?? new List<StoryCommandDocument>();
        isUpdatingSceneSelectors = true;
        try
        {
            sceneContentDropdown.ClearOptions();
            sceneContentDropdown.AddOptions(content.Select((command, index) => FormatSceneContentLabel(index, command)).ToList());
            int activeIndex = content.FindIndex(command => command != null && activeDialogueCommand != null
                && string.Equals(command.commandId, activeDialogueCommand.commandId, StringComparison.OrdinalIgnoreCase));
            sceneContentDropdown.value = Mathf.Max(0, activeIndex);
            sceneContentDropdown.RefreshShownValue();
            sceneContentDropdown.interactable = content.Count > 0;
            SetSelectorValueText(sceneContentDropdownValueText, sceneContentDropdown, "暂无内容");
        }
        finally
        {
            isUpdatingSceneSelectors = false;
        }
    }

    private void OnSceneContentDropdownChanged(int index)
    {
        if (isUpdatingSceneSelectors)
            return;

        List<StoryCommandDocument> commands = controller.GetSceneCommands(activeSceneId)
            .Where(command => command != null && (string.Equals(command.type, "say", StringComparison.OrdinalIgnoreCase)
                || string.Equals(command.type, "narrate", StringComparison.OrdinalIgnoreCase))).ToList();
        if (index < 0 || index >= commands.Count)
            return;

        activeDialogueCommand = commands[index];
        RefreshCanvas();
    }

    private static string FormatSceneContentLabel(int index, StoryCommandDocument command)
    {
        string type = string.Equals(command?.type, "say", StringComparison.OrdinalIgnoreCase) ? "对白" : "旁白";
        string preview = (command?.text ?? string.Empty).Replace('\n', ' ').Trim();
        if (preview.Length > 18)
            preview = preview.Substring(0, 18) + "…";
        return (index + 1) + ". " + type + " · " + (string.IsNullOrEmpty(preview) ? "未填写" : preview);
    }

    private void SetActiveActorSide(string side)
    {
        if (string.IsNullOrWhiteSpace(activeSceneActorId))
        {
            Hintbox.OpenHintboxWithContent("请先在“场景角色”中加入精灵。", 16);
            return;
        }

        if (!controller.SetSceneActorSide(activeSceneId, activeSceneActorId, side, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshCanvas();
    }

    private void MoveActiveActorDepth(bool outward)
    {
        if (string.IsNullOrWhiteSpace(activeSceneActorId))
        {
            Hintbox.OpenHintboxWithContent("请先选择一个场景角色。", 16);
            return;
        }

        if (!controller.MoveSceneActorDepth(activeSceneId, activeSceneActorId, outward, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshCanvas();
    }

    private void ToggleAutoLayoutMode()
    {
        if (!controller.ToggleSceneAutoLayoutMode(activeSceneId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshCanvas();
    }

    private void RefreshLayoutModeLabel(StorySceneDocument scene)
    {
        if (layoutModeButtonText == null)
            return;

        bool isBottomAligned = string.Equals(scene?.layout?.autoLayoutMode, "bottomAligned", StringComparison.OrdinalIgnoreCase);
        layoutModeButtonText.text = isBottomAligned ? "底边对齐" : "倒V布局";
    }

    private void ResetActiveSceneActorLayout()
    {
        if (string.IsNullOrWhiteSpace(activeSceneId))
            return;
        if (!controller.ResetSceneActorLayout(activeSceneId, out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }
        RefreshCanvas();
    }

    private void SaveDraft()
    {
        if (!controller.Save(out string error))
        {
            Hintbox.OpenHintboxWithContent(error, 16);
            return;
        }

        dirtyStateText.text = string.Empty;
        Hintbox.OpenHintboxWithContent("剧情点草稿已保存。", 16);
    }

    private void PreviewDraft()
    {
        StoryDocument document = controller.DraftDocument;
        StoryNodeDocument node = controller.DraftNode;
        if (document == null || node == null)
        {
            Hintbox.OpenHintboxWithContent("当前没有可预览的剧情点草稿。", 16);
            return;
        }

        resourcePicker?.Close();
        gameObject.SetActive(false);
        StoryPanel preview = StoryPanel.OpenPreview(document, node.id, ResumeAfterPreview);
        if (preview == null)
        {
            gameObject.SetActive(true);
            Hintbox.OpenHintboxWithContent("无法打开剧情预览。", 16);
        }
    }

    private void ResumeAfterPreview()
    {
        if (this == null || gameObject == null)
            return;

        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        RefreshCanvas();
    }

    private void RefreshOverlayLayering()
    {
        if (sceneImage != null)
            sceneImage.transform.SetAsFirstSibling();
        if (actorLayer != null)
            actorLayer.SetSiblingIndex(sceneImage == null ? 0 : sceneImage.transform.GetSiblingIndex() + 1);
        toolbar?.SetAsLastSibling();
        editorActions?.SetAsLastSibling();
    }

    private Text CreateToolbarButton(Transform parent, string label, Vector2 position, Vector2 dimensions, Action callback,
        bool anchorRight)
    {
        if (actionButtonPrefab == null)
            return null;

        GameObject item = Instantiate(actionButtonPrefab, parent);
        item.name = label + " Button";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = anchorRight ? Vector2.one : new Vector2(0f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = anchorRight ? Vector2.one : new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        IButton button = item.GetComponent<IButton>();
        button.button.onClick = new Button.ButtonClickedEvent();
        button.onPointerClickEvent = new UnityEvent();
        button.onPointerClickEvent.AddListener(callback.Invoke);

        Text text = item.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.font = font;
            text.fontSize = 15;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            text.raycastTarget = false;
        }
        return text;
    }

    private Dropdown CreateDropdown(Transform parent, Vector2 position, Vector2 dimensions, UnityAction<int> onChanged)
    {
        if (dropdownPrefab == null)
            return null;

        GameObject item = Instantiate(dropdownPrefab, parent);
        item.name = "Story Scene Selector";
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Dropdown dropdown = item.GetComponent<Dropdown>();
        if (dropdown == null)
            return null;

        dropdown.onValueChanged = new Dropdown.DropdownEvent();
        dropdown.onValueChanged.AddListener(onChanged);
        Text label = dropdown.captionText;
        if (label != null)
        {
            label.font = font;
            label.fontSize = 13;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Cyan;
        }
        Text option = dropdown.itemText;
        if (option != null)
        {
            option.font = font;
            option.fontSize = 13;
            option.color = Cyan;
        }
        return dropdown;
    }

    private Text CreateSelectorValueText(Dropdown dropdown)
    {
        if (dropdown == null)
            return null;

        Text text = CreateText("Selector Value", dropdown.transform, string.Empty, 13, TextAnchor.MiddleLeft, Cyan,
            Vector2.zero, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8f, 1f);
        rect.offsetMax = new Vector2(-26f, -1f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.transform.SetAsLastSibling();
        return text;
    }

    private static void SetSelectorValueText(Text text, Dropdown dropdown, string emptyText)
    {
        if (text == null)
            return;

        if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
        {
            text.text = emptyText ?? string.Empty;
            return;
        }

        int value = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
        text.text = dropdown.options[value]?.text ?? (emptyText ?? string.Empty);
    }

    private static void CopyRectTransform(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        return rect;
    }

    private Text CreateText(string name, Transform parent, string value, int size, TextAnchor alignment, Color color,
        Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 dimensions)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text text = obj.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private GameObject FindWorkshopActionButtonPrefab()
    {
        GameObject workshopPanel = ResourceManager.instance.GetPanel("Workshop");
        if (workshopPanel == null)
            return null;

        IButton button = workshopPanel.GetComponentsInChildren<IButton>(true)
            .FirstOrDefault(value => value.GetComponentInChildren<Text>(true)?.text == "导出 mod");
        return button?.gameObject;
    }

    private GameObject FindWorkshopDescriptionInputFieldPrefab()
    {
        GameObject workshopPanel = ResourceManager.instance.GetPanel("Workshop");
        if (workshopPanel == null)
            return null;

        return workshopPanel.GetComponentsInChildren<IInputField>(true)
            .FirstOrDefault(value => value.placeHolderText != null && value.placeHolderText.text == "输入叙述")
            ?.gameObject;
    }

    private GameObject FindWorkshopDropdownPrefab()
    {
        GameObject workshopPanel = ResourceManager.instance.GetPanel("Workshop");
        if (workshopPanel == null)
            return null;

        return workshopPanel.GetComponentsInChildren<IDropdown>(true).FirstOrDefault()?.gameObject;
    }
}
