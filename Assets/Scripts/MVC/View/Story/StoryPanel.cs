using System;
using System.Linq;
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

    private readonly Dictionary<string, string> actorIcons = new Dictionary<string, string>();

    private StoryScript story;
    private string pendingStoryId;
    private int fallbackMapId;
    private int commandIndex;
    private bool isBuilt;
    private bool isClosing;
    private bool waitingForChoice;

    private Image sceneImage;
    private GameObject exitButton;

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
            return Resources.Load<TextAsset>(StoryResourceRoot + storyId) != null;

        string modStoryId = storyId.Substring(ModStoryPrefix.Length);
        StoryDocument document = Database.instance.GetStoryInfo(modStoryId);
        if (document == null || !document.IsValid)
        {
            error = "找不到对应的Mod剧情，或剧情文件格式错误";
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
        CreateExitButton();
    }

    private void LoadStory(string storyId, int fallbackMapId)
    {
        if (!TryBuildStoryScript(storyId, out story))
        {
            SetDialogue("\u7CFB\u7EDF", "\u672A\u627E\u5230\u5267\u60C5\u811A\u672C\uFF1A" + storyId);
            return;
        }

        commandIndex = 0;
        isClosing = false;
        waitingForChoice = false;
        actorIcons.Clear();
        ClearDialogHandlers();

        if (fallbackMapId != 0)
            SetScene("Maps/bg/" + fallbackMapId);

        ShowNextCommand();
    }

    private static bool TryBuildStoryScript(string storyId, out StoryScript story)
    {
        story = null;
        if (IsModStory(storyId))
        {
            string modStoryId = storyId.Substring(ModStoryPrefix.Length);
            StoryDocument document = Database.instance.GetStoryInfo(modStoryId);
            if (document == null || !document.IsValid)
                return false;

            story = document.ToScript();
            return story != null;
        }

        TextAsset textAsset = Resources.Load<TextAsset>(StoryResourceRoot + storyId);
        if (textAsset == null)
            return false;

        story = StoryParser.Parse(textAsset.text);
        return story != null;
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
                    SetScene(GetArgValue(command.args, "bg", command.args));
                    continue;
                case StoryCommandType.Show:
                    RegisterActor(command.args);
                    continue;
                case StoryCommandType.Hide:
                    UnregisterActor(command.args);
                    continue;
                case StoryCommandType.Say:
                    SetDialogue(command.speaker, command.text);
                    return;
                case StoryCommandType.Narrate:
                    SetNarration(command.text);
                    return;
                case StoryCommandType.Choice:
                    ShowChoices(command.choices);
                    return;
                case StoryCommandType.Jump:
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

    private void RegisterActor(string args)
    {
        string[] tokens = SplitArgs(args);
        if (tokens.Length < 2)
            return;

        actorIcons[tokens[0]] = NormalizeSpritePath(tokens[1]);
    }

    private void UnregisterActor(string args)
    {
        string[] tokens = SplitArgs(args);
        if (tokens.Length == 0 || tokens[0] == "all")
        {
            actorIcons.Clear();
            return;
        }

        actorIcons.Remove(tokens[0]);
    }

    private void SetDialogue(string speaker, string content)
    {
        waitingForChoice = false;
        DialogManager.instance.SetStoryDialogReplyClickHandler(null);
        DialogManager.instance.ClearStoryChoices();
        DialogManager.instance.SetStoryDialogBackgroundClickHandler(Advance);
        DialogManager.instance.OpenStoryDialog(CreateDialogInfo(speaker, content, new List<NpcButtonHandler>()));
        BringExitButtonToFront();
    }

    private void SetNarration(string content)
    {
        waitingForChoice = false;
        DialogManager.instance.SetStoryDialogReplyClickHandler(null);
        DialogManager.instance.ClearStoryChoices();
        DialogManager.instance.SetStoryDialogBackgroundClickHandler(Advance);
        DialogManager.instance.OpenStoryDialog(CreateDialogInfo(NarratorName, content, new List<NpcButtonHandler>()));
        BringExitButtonToFront();
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
        DialogManager.instance.OpenStoryDialog(CreateDialogInfo(NarrationPrompt, ChoicePrompt, new List<NpcButtonHandler>()));
        DialogManager.instance.ShowStoryChoices(choices.Select(x => x.text).ToList(), choiceIndex =>
        {
            if (choiceIndex < 0 || choiceIndex >= choices.Count)
                return;

            waitingForChoice = false;
            ClearChoiceHandler();
            JumpTo(choices[choiceIndex].label);
            ShowNextCommand();
        });
        BringExitButtonToFront();
    }

    private DialogInfo CreateDialogInfo(string speaker, string content, List<NpcButtonHandler> replyHandlers)
    {
        string iconPath = GetSpeakerIconPath(speaker);
        bool hasIcon = !string.IsNullOrEmpty(iconPath);

        return new DialogInfo
        {
            id = "story",
            iconId = hasIcon ? iconPath : "none",
            iconSize = hasIcon ? DefaultIconSize : "0,0",
            iconPos = hasIcon ? DefaultIconPos : "0,0",
            name = speaker ?? string.Empty,
            rawContent = content ?? string.Empty,
            functionHandler = new List<NpcButtonHandler>(),
            replyHandler = replyHandlers ?? new List<NpcButtonHandler>()
        };
    }

    private string GetSpeakerIconPath(string speaker)
    {
        if (string.IsNullOrEmpty(speaker))
            return string.Empty;

        return actorIcons.TryGetValue(speaker, out string path) ? path : string.Empty;
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

        Sprite sprite = NpcInfo.GetIcon(path);
        if (sprite != null && sprite != SpriteSet.Empty)
            return sprite;

        return ResourceManager.instance.Get<Sprite>(path);
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
