using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using SimpleFileBrowser;
using UnityEngine;
using UnityEngine.UI;

public class WorkshopAllView : Module
{
    [SerializeField] private List<GameObject> createModObjectList, checkModObjectList;
    [SerializeField] private Panel allSkillPanel, allBuffPanel, allItemPanel, skillListPanel, itemListPanel;

    public void NeverCreateMod() {
        createModObjectList.ForEach(x => x.SetActive(true));
        checkModObjectList.ForEach(x => x.SetActive(false));
    }

    public void CheckCurrentMod() {
        createModObjectList.ForEach(x => x.SetActive(false));
        checkModObjectList.ForEach(x => x.SetActive(true));
    }
    
    public void SetAllSkillPanelActive(bool active) {
        allSkillPanel.SetActive(active);
    }

    public void SetAllBuffPanelActive(bool active) {
        allBuffPanel.SetActive(active);
    }

    public void SetAllItemPanelActive(bool active) {
        allItemPanel.SetActive(active);
    }
    
    public void SetSkillListPanelActive(bool active) {
        skillListPanel.SetActive(active);
    }

    public void SetItemListPanelActive(bool active) {
        itemListPanel.SetActive(active);
    }
}

public class WorkshopStoryPanel : Panel
{
    private const string StoryDirectory = "/Mod/Stories/";
    private static readonly Color ButtonNormalColor = new Color(0f, 0.18f, 0.22f, 1f);
    private static readonly Color ButtonHoverColor = new Color(0f, 0.34f, 0.4f, 1f);
    private static readonly Color ButtonPressedColor = new Color(0f, 0.12f, 0.16f, 1f);
    private static readonly Color StorySelectedColor = new Color(0f, 0.42f, 0.48f, 1f);
    private static readonly Color StorySelectedHoverColor = new Color(0f, 0.52f, 0.58f, 1f);

    private Font uiFont;
    private RectTransform storyListContent;
    private RectTransform metadataEditorRoot;
    private RectTransform mapTagRoot;
    private RectTransform mapSuggestionRoot;
    private RectTransform actorTagRoot;
    private RectTransform actorSuggestionRoot;
    private Text metadataText;
    private InputField titleInput;
    private InputField summaryInput;
    private InputField mapInput;
    private InputField actorInput;
    private SimpleDropdown replayableDropdown;
    private Button editButton;
    private Button saveButton;
    private Button cancelEditButton;
    private Button previewButton;
    private Button deleteButton;
    private Text statusText;
    private StoryDocument selectedDocument;
    private string selectedStoryPath;
    private int storyListItemIndex;
    private readonly List<StoryListItem> storyListItems = new List<StoryListItem>();
    private readonly List<int> editingMapIds = new List<int>();
    private readonly List<StoryActorDocument> editingActors = new List<StoryActorDocument>();
    private List<MapOption> mapOptions;
    private List<PetOption> petOptions;

    public static WorkshopStoryPanel Open()
    {
        WorkshopStoryPanel existing = GameObject.FindFirstObjectByType<WorkshopStoryPanel>();
        if (existing != null)
        {
            existing.transform.SetAsLastSibling();
            return existing;
        }

        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null)
            return null;

        GameObject obj = new GameObject("Workshop Story Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(WorkshopStoryPanel));
        obj.transform.SetParent(canvas.transform, false);
        obj.transform.SetAsLastSibling();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = obj.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.82f);
        image.raycastTarget = true;

        return obj.GetComponent<WorkshopStoryPanel>();
    }

    public override void Init()
    {
        base.Init();
        uiFont = FindFont();
        BuildCleanUI();
        RefreshStoryList();
    }

    private void BuildCleanUI()
    {
        RectTransform window = CreateImage("Window", transform, new Color(0f, 0.035f, 0.045f, 0.98f)).rectTransform;
        PlaceRect(window, new Rect(0f, 0f, 880f, 540f));
        AddOutline(window.gameObject, new Color(0.05f, 0.95f, 1f, 0.45f), new Vector2(1.5f, -1.5f));

        RectTransform header = CreateImage("Header", window, new Color(0f, 0.11f, 0.13f, 0.86f)).rectTransform;
        PlaceRect(header, new Rect(0f, 241f, 880f, 58f));

        CreateText("Title", header, "\u5267\u60C5\u7F16\u8F91", 28, TextAnchor.MiddleCenter, new Rect(0f, 0f, 420f, 42f), Color.cyan);
        CreateButton("Close", header, "\u5173\u95ED", new Rect(380f, 0f, 88f, 34f), ClosePanel);

        RectTransform leftPanel = CreateImage("Story List Panel", window, new Color(0f, 0f, 0f, 0.72f)).rectTransform;
        PlaceRect(leftPanel, new Rect(-270f, -26f, 300f, 410f));
        AddOutline(leftPanel.gameObject, new Color(0f, 0.68f, 0.78f, 0.35f), new Vector2(1f, -1f));

        CreateText("List Label", leftPanel, "\u5F53\u524D Mod \u5267\u672C", 18, TextAnchor.MiddleLeft, new Rect(0f, 178f, 260f, 28f), Color.cyan);
        CreateButton("New", leftPanel, "\u65B0\u5EFA\u5267\u672C", new Rect(-64f, 139f, 118f, 34f), NewStory);
        CreateButton("Reload", leftPanel, "\u5237\u65B0", new Rect(76f, 139f, 92f, 34f), RefreshStoryList);
        storyListContent = CreateScrollContent("Story List", leftPanel, new Rect(0f, -48f, 260f, 326f));

        RectTransform metadataPanelRoot = CreateImage("Metadata Root", window, new Color(0f, 0f, 0f, 0.72f)).rectTransform;
        PlaceRect(metadataPanelRoot, new Rect(170f, -26f, 520f, 410f));
        AddOutline(metadataPanelRoot.gameObject, new Color(0f, 0.68f, 0.78f, 0.35f), new Vector2(1f, -1f));

        CreateText("Metadata Label", metadataPanelRoot, "\u5267\u672C\u5143\u6570\u636E", 18, TextAnchor.MiddleLeft, new Rect(-146f, 178f, 190f, 28f), Color.cyan);
        editButton = CreateButton("Edit", metadataPanelRoot, "\u7F16\u8F91", new Rect(-10f, 178f, 80f, 34f), BeginEditSelectedStory);
        editButton.interactable = false;
        deleteButton = CreateButton("Delete", metadataPanelRoot, "\u5220\u9664", new Rect(92f, 178f, 80f, 34f), RequestDeleteSelectedStory);
        deleteButton.interactable = false;
        previewButton = CreateButton("Preview", metadataPanelRoot, "\u9884\u89C8", new Rect(194f, 178f, 92f, 34f), PreviewSelectedStory);
        previewButton.interactable = false;
        saveButton = CreateButton("Save", metadataPanelRoot, "\u4FDD\u5B58", new Rect(92f, 178f, 80f, 34f), SaveEditedStoryMetadata);
        cancelEditButton = CreateButton("Cancel Edit", metadataPanelRoot, "\u53D6\u6D88", new Rect(194f, 178f, 92f, 34f), CancelEditStoryMetadata);

        RectTransform metadataPanel = CreateImage("Metadata Panel", metadataPanelRoot, new Color(0f, 0.025f, 0.03f, 0.84f)).rectTransform;
        PlaceRect(metadataPanel, new Rect(0f, -26f, 480f, 336f));
        metadataText = CreateText("Metadata", metadataPanel, string.Empty, 15, TextAnchor.UpperLeft, new Rect(0f, 0f, 440f, 296f), Color.white);
        BuildMetadataEditor(metadataPanel);
        SetMetadataEditMode(false);

        RectTransform statusPanel = CreateImage("Status Panel", window, new Color(0f, 0.07f, 0.08f, 0.72f)).rectTransform;
        PlaceRect(statusPanel, new Rect(0f, -246f, 820f, 34f));
        statusText = CreateText("Status", statusPanel, string.Empty, 14, TextAnchor.MiddleLeft, new Rect(0f, 0f, 780f, 24f), Color.white);
    }

    private void BuildMetadataEditor(RectTransform metadataPanel)
    {
        metadataEditorRoot = CreateRect("Metadata Editor", metadataPanel, new Rect(0f, 0f, 440f, 296f));

        CreateText("Title Label", metadataEditorRoot, "\u6807\u9898", 15, TextAnchor.MiddleLeft, new Rect(-190f, 126f, 64f, 24f), Color.cyan);
        titleInput = CreateInputField("Title Input", metadataEditorRoot, string.Empty, 15, new Rect(38f, 126f, 354f, 30f), "\u8F93\u5165\u5267\u672C\u6807\u9898");

        CreateText("Summary Label", metadataEditorRoot, "\u7B80\u4ECB", 15, TextAnchor.MiddleLeft, new Rect(-190f, 82f, 64f, 24f), Color.cyan);
        summaryInput = CreateInputField("Summary Input", metadataEditorRoot, string.Empty, 15, new Rect(38f, 76f, 354f, 48f), "\u8F93\u5165\u4EFB\u52A1\u7B80\u4ECB", true);

        CreateText("Replayable Label", metadataEditorRoot, "\u53EF\u91CD\u590D", 15, TextAnchor.MiddleLeft, new Rect(-190f, 35f, 76f, 24f), Color.cyan);
        replayableDropdown = CreateSimpleDropdown("Replayable Dropdown", metadataEditorRoot, new Rect(-36f, 35f, 116f, 30f), new List<string> { "\u662F", "\u5426" });

        CreateText("Map Label", metadataEditorRoot, "\u5730\u56FE", 15, TextAnchor.MiddleLeft, new Rect(-190f, -8f, 64f, 24f), Color.cyan);
        mapInput = CreateInputField("Map Input", metadataEditorRoot, string.Empty, 15, new Rect(-36f, -8f, 220f, 30f), "\u8F93\u5165\u5730\u56FE ID/\u540D\u79F0");
        mapInput.onValueChanged.AddListener(UpdateMapSuggestions);
        mapInput.onEndEdit.AddListener(_ => TryAddMapTagFromInput());
        CreateButton("Add Map", metadataEditorRoot, "\u6DFB\u52A0", new Rect(154f, -8f, 70f, 30f), TryAddMapTagFromInput);

        mapTagRoot = CreateImage("Map Tags", metadataEditorRoot, new Color(0f, 0f, 0f, 0.28f)).rectTransform;
        PlaceRect(mapTagRoot, new Rect(0f, -45f, 430f, 30f));
        AddOutline(mapTagRoot.gameObject, new Color(0f, 0.85f, 1f, 0.18f), new Vector2(1f, -1f));

        mapSuggestionRoot = CreateImage("Map Suggestions", metadataEditorRoot, new Color(0f, 0.08f, 0.1f, 0.98f)).rectTransform;
        PlaceRect(mapSuggestionRoot, new Rect(-36f, -62f, 220f, 86f));
        AddOutline(mapSuggestionRoot.gameObject, new Color(0f, 0.85f, 1f, 0.36f), new Vector2(1f, -1f));
        mapSuggestionRoot.gameObject.SetActive(false);

        CreateText("Actor Label", metadataEditorRoot, "\u89D2\u8272", 15, TextAnchor.MiddleLeft, new Rect(-190f, -76f, 64f, 24f), Color.cyan);
        actorInput = CreateInputField("Actor Input", metadataEditorRoot, string.Empty, 15, new Rect(-36f, -76f, 220f, 30f), "\u8F93\u5165\u7CBE\u7075\u540D");
        actorInput.onValueChanged.AddListener(UpdateActorSuggestions);
        actorInput.onEndEdit.AddListener(_ => TryAddActorTagFromInput());
        CreateButton("Add Actor", metadataEditorRoot, "\u6DFB\u52A0", new Rect(154f, -76f, 70f, 30f), TryAddActorTagFromInput);

        actorSuggestionRoot = CreateImage("Actor Suggestions", metadataEditorRoot, new Color(0f, 0.08f, 0.1f, 0.98f)).rectTransform;
        PlaceRect(actorSuggestionRoot, new Rect(-36f, -130f, 220f, 86f));
        AddOutline(actorSuggestionRoot.gameObject, new Color(0f, 0.85f, 1f, 0.36f), new Vector2(1f, -1f));
        actorSuggestionRoot.gameObject.SetActive(false);

        actorTagRoot = CreateImage("Actor Tags", metadataEditorRoot, new Color(0f, 0f, 0f, 0.32f)).rectTransform;
        PlaceRect(actorTagRoot, new Rect(0f, -122f, 430f, 50f));
        AddOutline(actorTagRoot.gameObject, new Color(0f, 0.85f, 1f, 0.22f), new Vector2(1f, -1f));
    }

    private void NewStory()
    {
        try
        {
            EnsureStoryDirectory();
            string storyId = CreateUniqueStoryId();
            int missionId = GetNextStoryMissionId();
            string title = "新建剧情 " + DateTime.Now.ToString("yyyyMMdd HHmm");
            string filePath = GetStoryPath(storyId);
            string json = BuildStoryTemplateJson(storyId, missionId, title);

            if (!TryParseStoryJson(json, out _, out string validationError))
            {
                SetStatus("新建剧本模板校验失败：" + validationError, true);
                return;
            }

            FileBrowserHelpers.WriteTextToFile(filePath, json);
            RefreshStoryList();
            LoadStory(filePath);
            SetStatus("已创建剧本：" + FileBrowserHelpers.GetFilename(filePath));
        }
        catch (Exception e)
        {
            SetStatus("新建剧本失败：" + e.Message, true);
            Hintbox.OpenHintboxWithContent("新建剧本失败：\n" + e.Message, 16).SetSize(560, 260);
        }
    }

    private string CreateUniqueStoryId()
    {
        string baseId = "story_" + DateTime.Now.ToString("yyyyMMddHHmmss");
        string storyId = baseId;
        int suffix = 2;

        while (StoryIdExists(storyId) || FileBrowserHelpers.FileExists(GetStoryPath(storyId)))
        {
            storyId = baseId + "_" + suffix;
            suffix++;
        }

        return storyId;
    }

    private bool StoryIdExists(string storyId)
    {
        foreach (string path in GetStoryPaths())
        {
            try
            {
                StoryDocument document = JsonUtility.FromJson<StoryDocument>(FileBrowserHelpers.ReadTextFromFile(path));
                if (document != null && string.Equals(document.id, storyId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception)
            {
                // Invalid files are shown in the list but do not block a generated id.
            }
        }

        return false;
    }

    private int GetNextStoryMissionId()
    {
        int missionId = -10001;
        foreach (string path in GetStoryPaths())
        {
            try
            {
                StoryDocument document = JsonUtility.FromJson<StoryDocument>(FileBrowserHelpers.ReadTextFromFile(path));
                if (document?.mission != null && document.mission.id <= missionId)
                    missionId = document.mission.id - 1;
            }
            catch (Exception)
            {
                // Ignore invalid files when allocating a new mission id.
            }
        }

        return missionId;
    }

    private string BuildStoryTemplateJson(string storyId, int missionId, string title)
    {
        string escapedStoryId = JsonEscape(storyId);
        string escapedTitle = JsonEscape(title);
        string missionIdText = missionId.ToString();
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("  \"schemaVersion\": 1,");
        builder.AppendLine("  \"id\": \"" + escapedStoryId + "\",");
        builder.AppendLine("  \"title\": \"" + escapedTitle + "\",");
        builder.AppendLine("  \"entry\": \"start\",");
        builder.AppendLine("  \"layout\": {");
        builder.AppendLine("    \"actorSpacing\": 150,");
        builder.AppendLine("    \"actorHeight\": 238,");
        builder.AppendLine("    \"actorBottom\": 166,");
        builder.AppendLine("    \"centerGap\": 118,");
        builder.AppendLine("    \"stackOffset\": 32");
        builder.AppendLine("  },");
        builder.AppendLine("  \"mission\": {");
        builder.AppendLine("    \"id\": " + missionIdText + ",");
        builder.AppendLine("    \"title\": \"" + escapedTitle + "\",");
        builder.AppendLine("    \"replayable\": true,");
        builder.AppendLine("    \"mapId\": 121,");
        builder.AppendLine("    \"mapIds\": [121],");
        builder.AppendLine("    \"summary\": \"这是一个可预览的新建剧情模板。\"");
        builder.AppendLine("  },");
        builder.AppendLine("  \"actors\": [],");
        builder.AppendLine("  \"nodes\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"id\": \"start\",");
        builder.AppendLine("      \"commands\": [");
        builder.AppendLine("        {");
        builder.AppendLine("          \"type\": \"scene\",");
        builder.AppendLine("          \"bg\": \"Maps/bg/121\"");
        builder.AppendLine("        },");
        builder.AppendLine("        {");
        builder.AppendLine("          \"type\": \"narrate\",");
        builder.AppendLine("          \"text\": \"这是新建剧本的开场旁白。你可以从这里开始改写剧情。\"");
        builder.AppendLine("        },");
        builder.AppendLine("        {");
        builder.AppendLine("          \"type\": \"choice\",");
        builder.AppendLine("          \"choices\": [");
        builder.AppendLine("            {");
        builder.AppendLine("              \"text\": \"继续查看模板流程。\",");
        builder.AppendLine("              \"target\": \"continue\"");
        builder.AppendLine("            },");
        builder.AppendLine("            {");
        builder.AppendLine("              \"text\": \"直接结束预览。\",");
        builder.AppendLine("              \"target\": \"finish\"");
        builder.AppendLine("            }");
        builder.AppendLine("          ]");
        builder.AppendLine("        }");
        builder.AppendLine("      ]");
        builder.AppendLine("    },");
        builder.AppendLine("    {");
        builder.AppendLine("      \"id\": \"continue\",");
        builder.AppendLine("      \"commands\": [");
        builder.AppendLine("        {");
        builder.AppendLine("          \"type\": \"narrate\",");
        builder.AppendLine("          \"text\": \"这里展示了分支跳转。后续编辑器会把节点、对白和选项拆成可视化表单。\"");
        builder.AppendLine("        },");
        builder.AppendLine("        {");
        builder.AppendLine("          \"type\": \"jump\",");
        builder.AppendLine("          \"target\": \"finish\"");
        builder.AppendLine("        }");
        builder.AppendLine("      ]");
        builder.AppendLine("    },");
        builder.AppendLine("    {");
        builder.AppendLine("      \"id\": \"finish\",");
        builder.AppendLine("      \"commands\": [");
        builder.AppendLine("        {");
        builder.AppendLine("          \"type\": \"mission\",");
        builder.AppendLine("          \"args\": \"" + missionIdText + " complete\"");
        builder.AppendLine("        },");
        builder.AppendLine("        {");
        builder.AppendLine("          \"type\": \"end\"");
        builder.AppendLine("        }");
        builder.AppendLine("      ]");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private void RefreshStoryList()
    {
        if (storyListContent == null)
            return;

        string previousSelectedPath = selectedStoryPath;
        for (int i = storyListContent.childCount - 1; i >= 0; i--)
            Destroy(storyListContent.GetChild(i).gameObject);

        storyListItemIndex = 0;
        storyListItems.Clear();
        EnsureStoryDirectory();
        List<string> storyPaths = GetStoryPaths().ToList();
        if (storyPaths.Count == 0)
        {
            selectedDocument = null;
            ShowEmptyMetadata("当前 Mod/Stories 中暂无剧情。");
            CreateListLabel("暂无剧本");
            return;
        }

        foreach (string path in storyPaths)
        {
            string label = GetStoryListLabel(path);
            Button button = CreateListButton(label, path);
            string capturedPath = path;
            button.onClick.AddListener(() => LoadStory(capturedPath));
        }

        string refreshedSelectedPath = storyPaths.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(previousSelectedPath)
            && string.Equals(path, previousSelectedPath, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(refreshedSelectedPath))
        {
            LoadStory(refreshedSelectedPath);
            return;
        }

        ShowEmptyMetadata("请选择左侧剧本查看元数据。");
    }

    private void LoadStory(string path)
    {
        selectedStoryPath = path;
        UpdateStoryListSelection();
        SetMetadataEditMode(false);
        string json = FileBrowserHelpers.ReadTextFromFile(path);

        if (TryParseStoryJson(json, out StoryDocument document, out string error))
        {
            selectedDocument = document;
            ShowStoryMetadata(document, path);
            return;
        }
        else
        {
            selectedDocument = null;
            ShowInvalidMetadata(path, error);
        }
    }

    private void PreviewSelectedStory()
    {
        if (selectedDocument == null)
        {
            SetStatus("请先选择一个格式正确的剧本。", true);
            return;
        }

        Database.instance?.ReloadStoryMod();
        StoryPanel.Open("mod:" + selectedDocument.id, selectedDocument.mission.mapId);
    }

    private void BeginEditSelectedStory()
    {
        if (selectedDocument == null || string.IsNullOrWhiteSpace(selectedStoryPath))
        {
            SetStatus("请先选择一个格式正确的剧本。", true);
            return;
        }

        StoryDocument document = LoadSelectedStoryDocument();
        if (document == null)
            return;

        selectedDocument = document;
        StoryMissionDocument mission = document.mission ?? new StoryMissionDocument();
        titleInput.text = mission.title ?? document.title ?? string.Empty;
        summaryInput.text = mission.summary ?? string.Empty;
        SetEditingMapIds(mission);
        replayableDropdown.SetValue(mission.replayable ? 0 : 1);

        editingActors.Clear();
        foreach (StoryActorDocument actor in document.actors ?? Array.Empty<StoryActorDocument>())
            editingActors.Add(CopyActor(actor));

        RenderActorTags();
        SetMetadataEditMode(true);
        SetStatus("正在编辑剧本元数据。");
    }

    private void CancelEditStoryMetadata()
    {
        SetMetadataEditMode(false);
        if (!string.IsNullOrWhiteSpace(selectedStoryPath))
            LoadStory(selectedStoryPath);
    }

    private void SaveEditedStoryMetadata()
    {
        if (string.IsNullOrWhiteSpace(selectedStoryPath))
        {
            SetStatus("请先选择一个剧本。", true);
            return;
        }

        StoryDocument document = LoadSelectedStoryDocument();
        if (document == null)
            return;

        string title = (titleInput.text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            SetStatus("标题不能为空。", true);
            return;
        }

        if (!TryAddActorTagFromInput(true))
            return;
        if (!TryAddMapTagFromInput(true))
            return;
        if (editingMapIds.Count == 0)
        {
            SetStatus("\u8BF7\u81F3\u5C11\u9009\u62E9\u4E00\u4E2A\u5730\u56FE\u3002", true);
            return;
        }

        document.title = title;
        document.mission ??= new StoryMissionDocument();
        document.mission.title = title;
        document.mission.summary = summaryInput.text ?? string.Empty;
        document.mission.mapIds = editingMapIds.Distinct().ToArray();
        document.mission.mapId = document.mission.mapIds[0];
        document.mission.replayable = replayableDropdown.Value == 0;
        document.actors = editingActors.Select(CopyActor).ToArray();

        if (!StoryValidator.Validate(document, out string validationError))
        {
            SetStatus("保存失败：剧本校验未通过。", true);
            Hintbox.OpenHintboxWithContent("保存失败：\n" + validationError, 14).SetSize(720, 360);
            return;
        }

        try
        {
            FileBrowserHelpers.WriteTextToFile(selectedStoryPath, JsonUtility.ToJson(document, true));
            string path = selectedStoryPath;
            SetMetadataEditMode(false);
            RefreshStoryList();
            LoadStory(path);
            SetStatus("已保存剧本元数据：" + title);
        }
        catch (Exception e)
        {
            SetStatus("保存剧本失败：" + e.Message, true);
            Hintbox.OpenHintboxWithContent("保存剧本失败：\n" + e.Message, 16).SetSize(560, 260);
        }
    }

    private StoryDocument LoadSelectedStoryDocument()
    {
        try
        {
            string json = FileBrowserHelpers.ReadTextFromFile(selectedStoryPath);
            if (TryParseStoryJson(json, out StoryDocument document, out string error))
                return document;

            SetStatus("剧本格式错误，无法编辑：" + error, true);
            return null;
        }
        catch (Exception e)
        {
            SetStatus("读取剧本失败：" + e.Message, true);
            return null;
        }
    }

    private void RequestDeleteSelectedStory()
    {
        if (string.IsNullOrWhiteSpace(selectedStoryPath))
        {
            SetStatus("请先选择一个剧本。", true);
            return;
        }

        string path = selectedStoryPath;
        string filename = FileBrowserHelpers.GetFilename(path);
        Hintbox hintbox = Hintbox.OpenHintbox();
        hintbox.SetTitle("提示");
        hintbox.SetContent("确定要删除这个剧本吗？\n【" + filename + "】\n\n删除后无法恢复。", 16, FontOption.Arial);
        hintbox.SetOptionNum(2);
        hintbox.SetOptionCallback(() => DeleteSelectedStoryFile(path, filename));
    }

    private void DeleteSelectedStoryFile(string path, string filename)
    {
        try
        {
            if (!IsStoryJsonPath(path))
            {
                SetStatus("删除失败：目标文件不在 Mod/Stories 中。", true);
                return;
            }

            if (!FileBrowserHelpers.FileExists(path))
            {
                selectedDocument = null;
                selectedStoryPath = null;
                RefreshStoryList();
                SetStatus("剧本文件已不存在：" + filename, true);
                return;
            }

            FileBrowserHelpers.DeleteFile(path);
            selectedDocument = null;
            selectedStoryPath = null;
            RefreshStoryList();
            SetStatus("已删除剧本：" + filename);
        }
        catch (Exception e)
        {
            SetStatus("删除剧本失败：" + e.Message, true);
            Hintbox.OpenHintboxWithContent("删除剧本失败：\n" + e.Message, 16).SetSize(560, 260);
        }
    }

    private string GetStoryListLabel(string path)
    {
        try
        {
            StoryDocument document = JsonUtility.FromJson<StoryDocument>(FileBrowserHelpers.ReadTextFromFile(path));
            string title = document?.mission?.title;
            if (string.IsNullOrWhiteSpace(title))
                title = document?.title;
            if (string.IsNullOrWhiteSpace(title))
                title = document?.id;
            return string.IsNullOrWhiteSpace(title) ? FileBrowserHelpers.GetFilename(path) : title;
        }
        catch (Exception)
        {
            return FileBrowserHelpers.GetFilename(path) + "（错误）";
        }
    }

    private void ShowEmptyMetadata(string message)
    {
        selectedDocument = null;
        selectedStoryPath = null;
        SetMetadataEditMode(false);
        if (metadataText != null)
            metadataText.text = message;
        if (editButton != null)
            editButton.interactable = false;
        if (previewButton != null)
            previewButton.interactable = false;
        if (deleteButton != null)
            deleteButton.interactable = false;
        UpdateStoryListSelection();
        SetStatus("\u5DF2\u5237\u65B0\u5267\u672C\u5217\u8868\u3002");
    }

    private void ShowInvalidMetadata(string path, string error)
    {
        if (metadataText != null)
        {
            metadataText.text =
                "文件：" + FileBrowserHelpers.GetFilename(path) + "\n" +
                "路径：" + path + "\n\n" +
                "状态：格式错误\n\n" +
                error;
        }

        if (previewButton != null)
            previewButton.interactable = false;
        if (editButton != null)
            editButton.interactable = false;
        if (deleteButton != null)
            deleteButton.interactable = true;

        SetStatus("剧本格式错误：" + FileBrowserHelpers.GetFilename(path), true);
    }

    private void ShowStoryMetadata(StoryDocument document, string path)
    {
        int actorCount = document.actors?.Length ?? 0;
        int nodeCount = document.nodes?.Length ?? 0;
        int commandCount = document.nodes?.Sum(x => x?.commands?.Length ?? 0) ?? 0;
        string actors = GetActorSummary(document);
        string commandSummary = GetCommandSummary(document);
        string title = document.mission?.title ?? document.title ?? document.id;

        if (metadataText != null)
        {
            metadataText.text =
                "标题：" + title + "\n" +
                "简介：" + (document.mission?.summary ?? "无") + "\n\n" +
                "入口节点：" + (string.IsNullOrWhiteSpace(document.entry) ? "start" : document.entry) + "\n" +
                "地图：" + GetMissionMapSummary(document.mission) + "\n" +
                "可重复：" + ((document.mission?.replayable ?? true) ? "是" : "否") + "\n\n" +
                "角色：" + actorCount + " 个" + actors + "\n" +
                "节点：" + nodeCount + " 个\n" +
                "命令：" + commandCount + " 条" + commandSummary + "\n\n" +
                "内部 Story ID：" + document.id + "\n" +
                "内部 Mission ID：" + (document.mission?.id ?? 0) + "\n" +
                "文件：" + FileBrowserHelpers.GetFilename(path);
        }

        if (previewButton != null)
            previewButton.interactable = true;
        if (editButton != null)
            editButton.interactable = true;
        if (deleteButton != null)
            deleteButton.interactable = true;

        SetStatus("已选择：" + title);
    }

    private string GetActorSummary(StoryDocument document)
    {
        if (document.actors == null || document.actors.Length == 0)
            return string.Empty;

        return "\n  " + string.Join("\n  ", document.actors
            .Where(x => x != null)
            .Select(x => x.displayName + " (" + x.id + ")"));
    }

    private string GetCommandSummary(StoryDocument document)
    {
        if (document.nodes == null)
            return string.Empty;

        var commandGroups = document.nodes
            .Where(x => x?.commands != null)
            .SelectMany(x => x.commands)
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.type))
            .GroupBy(x => x.type.Trim().ToLower())
            .OrderBy(x => x.Key)
            .Select(x => x.Key + " x" + x.Count())
            .ToList();

        return commandGroups.Count == 0 ? string.Empty : "\n  " + string.Join(" / ", commandGroups);
    }

    private void SetMetadataEditMode(bool editing)
    {
        if (metadataText != null)
            metadataText.gameObject.SetActive(!editing);
        if (metadataEditorRoot != null)
            metadataEditorRoot.gameObject.SetActive(editing);

        if (editButton != null)
            editButton.gameObject.SetActive(!editing);
        if (deleteButton != null)
            deleteButton.gameObject.SetActive(!editing);
        if (previewButton != null)
            previewButton.gameObject.SetActive(!editing);
        if (saveButton != null)
            saveButton.gameObject.SetActive(editing);
        if (cancelEditButton != null)
            cancelEditButton.gameObject.SetActive(editing);

        if (!editing)
        {
            HideMapSuggestions();
            HideActorSuggestions();
        }
    }

    private void SetEditingMapIds(StoryMissionDocument mission)
    {
        editingMapIds.Clear();
        if (mission?.mapIds != null)
        {
            foreach (int mapId in mission.mapIds)
            {
                if (mapId != 0 && !editingMapIds.Contains(mapId))
                    editingMapIds.Add(mapId);
            }
        }

        if (editingMapIds.Count == 0 && mission != null && mission.mapId != 0)
            editingMapIds.Add(mission.mapId);
        if (editingMapIds.Count == 0)
            editingMapIds.Add(121);

        if (mapInput != null)
            mapInput.text = string.Empty;
        HideMapSuggestions();
        RenderMapTags();
    }

    private void TryAddMapTagFromInput()
    {
        TryAddMapTagFromInput(false);
    }

    private bool TryAddMapTagFromInput(bool requireValidInput)
    {
        if (mapInput == null)
            return true;

        string input = (mapInput.text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(input))
            return true;

        if (!TryFindMapOption(input, out MapOption map))
        {
            SetStatus("\u6CA1\u6709\u627E\u5230\u5730\u56FE\uFF1A" + input, true);
            return !requireValidInput;
        }

        AddMapTag(map);
        return true;
    }

    private void AddMapTag(MapOption map)
    {
        if (map == null)
            return;

        if (editingMapIds.Contains(map.Id))
        {
            SetStatus("\u5730\u56FE\u5DF2\u5B58\u5728\uFF1A" + map.Label);
            if (mapInput != null)
                mapInput.text = string.Empty;
            HideMapSuggestions();
            return;
        }

        editingMapIds.Add(map.Id);
        if (mapInput != null)
            mapInput.text = string.Empty;
        HideMapSuggestions();
        RenderMapTags();
        SetStatus("\u5DF2\u6DFB\u52A0\u5730\u56FE\uFF1A" + map.Label);
    }

    private void UpdateMapSuggestions(string input)
    {
        if (mapSuggestionRoot == null)
            return;

        for (int i = mapSuggestionRoot.childCount - 1; i >= 0; i--)
            Destroy(mapSuggestionRoot.GetChild(i).gameObject);

        string keyword = (input ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            mapSuggestionRoot.gameObject.SetActive(false);
            return;
        }

        List<MapOption> matches = GetMapOptions()
            .Where(x => !editingMapIds.Contains(x.Id))
            .Where(x => x.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 || x.Id.ToString().Contains(keyword))
            .Take(5)
            .ToList();

        if (matches.Count == 0)
        {
            mapSuggestionRoot.gameObject.SetActive(false);
            return;
        }

        mapSuggestionRoot.gameObject.SetActive(true);
        mapSuggestionRoot.SetAsLastSibling();
        for (int i = 0; i < matches.Count; i++)
        {
            MapOption map = matches[i];
            Button button = CreateButton("Map Suggestion", mapSuggestionRoot, map.Label, new Rect(0f, 0f, 198f, 22f), () => AddMapTag(map));
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -7f - i * 26f);
            rect.sizeDelta = new Vector2(-12f, 22f);
        }
    }

    private void HideMapSuggestions()
    {
        if (mapSuggestionRoot != null)
            mapSuggestionRoot.gameObject.SetActive(false);
    }

    private void RenderMapTags()
    {
        if (mapTagRoot == null)
            return;

        for (int i = mapTagRoot.childCount - 1; i >= 0; i--)
            Destroy(mapTagRoot.GetChild(i).gameObject);

        float x = 8f;
        const float y = 4f;
        const float tagHeight = 22f;
        const float spacing = 6f;
        const float maxWidth = 414f;

        for (int i = 0; i < editingMapIds.Count; i++)
        {
            int index = i;
            MapOption map = GetMapOptionById(editingMapIds[i]);
            string label = map?.Label ?? editingMapIds[i].ToString();
            float width = Mathf.Clamp(48f + label.Length * 12f, 86f, 176f);
            if (x + width > maxWidth)
                break;

            Button tagButton = CreateButton("Map Tag", mapTagRoot, label + " \u00D7", new Rect(0f, 0f, width, tagHeight), () =>
            {
                editingMapIds.RemoveAt(index);
                RenderMapTags();
            });
            Image image = tagButton.GetComponent<Image>();
            if (image != null)
                image.color = new Color(0.03f, 0.22f, 0.26f, 1f);

            RectTransform rect = tagButton.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, tagHeight);

            x += width + spacing;
        }
    }

    private void TryAddActorTagFromInput()
    {
        TryAddActorTagFromInput(false);
    }

    private bool TryAddActorTagFromInput(bool requireValidInput)
    {
        if (actorInput == null)
            return true;

        string input = (actorInput.text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(input))
            return true;

        if (editingActors.Any(x => string.Equals(x.displayName, input, StringComparison.OrdinalIgnoreCase)))
        {
            actorInput.text = string.Empty;
            SetStatus("角色已存在：" + input);
            return true;
        }

        if (!TryFindPetOption(input, out PetOption pet))
        {
            SetStatus("没有找到精灵名：" + input, true);
            return !requireValidInput;
        }

        AddActorTag(pet);
        return true;
    }

    private void AddActorTag(PetOption pet)
    {
        if (pet == null)
            return;

        if (editingActors.Any(x => string.Equals(x.displayName, pet.Name, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("角色已存在：" + pet.Name);
            actorInput.text = string.Empty;
            HideActorSuggestions();
            return;
        }

        editingActors.Add(CreateActorFromPet(pet));
        actorInput.text = string.Empty;
        HideActorSuggestions();
        RenderActorTags();
        SetStatus("已添加角色：" + pet.Name);
    }

    private void UpdateActorSuggestions(string input)
    {
        if (actorSuggestionRoot == null)
            return;

        for (int i = actorSuggestionRoot.childCount - 1; i >= 0; i--)
            Destroy(actorSuggestionRoot.GetChild(i).gameObject);

        string keyword = (input ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            actorSuggestionRoot.gameObject.SetActive(false);
            return;
        }

        List<PetOption> matches = GetPetOptions()
            .Where(x => !editingActors.Any(actor => string.Equals(actor.displayName, x.Name, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 || x.Id.ToString().Contains(keyword))
            .Take(5)
            .ToList();

        if (matches.Count == 0)
        {
            actorSuggestionRoot.gameObject.SetActive(false);
            return;
        }

        actorSuggestionRoot.gameObject.SetActive(true);
        actorSuggestionRoot.SetAsLastSibling();
        for (int i = 0; i < matches.Count; i++)
        {
            PetOption pet = matches[i];
            Button button = CreateButton("Actor Suggestion", actorSuggestionRoot, pet.Id + " " + pet.Name, new Rect(0f, 0f, 198f, 22f), () => AddActorTag(pet));
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -7f - i * 26f);
            rect.sizeDelta = new Vector2(-12f, 22f);
        }
    }

    private void HideActorSuggestions()
    {
        if (actorSuggestionRoot != null)
            actorSuggestionRoot.gameObject.SetActive(false);
    }

    private void RenderActorTags()
    {
        if (actorTagRoot == null)
            return;

        for (int i = actorTagRoot.childCount - 1; i >= 0; i--)
            Destroy(actorTagRoot.GetChild(i).gameObject);

        float x = 10f;
        float y = 6f;
        const float tagHeight = 22f;
        const float spacing = 4f;
        const float maxWidth = 410f;

        for (int i = 0; i < editingActors.Count; i++)
        {
            int index = i;
            string label = editingActors[i].displayName;
            float width = Mathf.Clamp(46f + label.Length * 16f, 72f, 154f);
            if (x + width > maxWidth)
            {
                x = 10f;
                y += tagHeight + spacing;
            }

            Button tagButton = CreateButton("Actor Tag", actorTagRoot, label + " \u00D7", new Rect(0f, 0f, width, tagHeight), () =>
            {
                editingActors.RemoveAt(index);
                RenderActorTags();
            });
            Image image = tagButton.GetComponent<Image>();
            if (image != null)
                image.color = new Color(0.03f, 0.26f, 0.22f, 1f);

            Text text = tagButton.GetComponentInChildren<Text>();
            if (text != null)
                text.color = new Color(0.64f, 1f, 0.78f, 1f);

            RectTransform rect = tagButton.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, tagHeight);

            x += width + spacing;
        }
    }

    private StoryActorDocument CreateActorFromPet(PetOption pet)
    {
        string baseActorId = "pet_" + pet.Id;
        string actorId = baseActorId;
        int suffix = 2;
        while (editingActors.Any(x => string.Equals(x.id, actorId, StringComparison.OrdinalIgnoreCase)))
        {
            actorId = baseActorId + "_" + suffix;
            suffix++;
        }

        return new StoryActorDocument
        {
            id = actorId,
            name = pet.Name,
            sprite = "Pets/pet/" + pet.Id,
            icon = "Pets/icon/" + pet.Id,
            side = "left",
            slot = GetNextEditingActorSlot("left"),
            faceLeft = true,
            flipIcon = false,
            scale = 1f,
        };
    }

    private int GetNextEditingActorSlot(string side)
    {
        int maxSlot = editingActors
            .Where(x => string.Equals(x.normalizedSide, side, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.slot)
            .DefaultIfEmpty(0)
            .Max();
        return maxSlot + 1;
    }

    private static StoryActorDocument CopyActor(StoryActorDocument actor)
    {
        if (actor == null)
            return null;

        return new StoryActorDocument
        {
            id = actor.id,
            name = actor.name,
            sprite = actor.sprite,
            icon = actor.icon,
            side = actor.side,
            slot = actor.slot,
            faceLeft = actor.faceLeft,
            flipIcon = actor.flipIcon,
            scale = actor.scale,
        };
    }

    private bool TryFindPetOption(string input, out PetOption pet)
    {
        pet = null;
        if (int.TryParse(input, out int petId))
            pet = GetPetOptions().FirstOrDefault(x => x.Id == petId);

        pet ??= GetPetOptions().FirstOrDefault(x => string.Equals(x.Name, input, StringComparison.OrdinalIgnoreCase));
        return pet != null;
    }

    private List<PetOption> GetPetOptions()
    {
        if (petOptions != null)
            return petOptions;

        petOptions = new List<PetOption>();
        TextAsset textAsset = Resources.Load<TextAsset>("Data/Pets/basic");
        if (textAsset == null)
            return petOptions;

        string[] lines = textAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 3 || !int.TryParse(cols[0], out int id))
                continue;

            string name = cols[2].Trim();
            if (!string.IsNullOrEmpty(name))
                petOptions.Add(new PetOption(id, name));
        }

        return petOptions;
    }

    private List<MapOption> GetMapOptions()
    {
        if (mapOptions != null)
            return mapOptions;

        mapOptions = new List<MapOption>();
        string mapDirectory = Path.Combine(Application.dataPath, "Resources", "Data", "Maps");
        if (Directory.Exists(mapDirectory))
        {
            foreach (string path in Directory.GetFiles(mapDirectory, "*.xml"))
            {
                try
                {
                    string filename = Path.GetFileNameWithoutExtension(path);
                    if (!int.TryParse(filename, out int id))
                        continue;

                    string text = FileBrowserHelpers.ReadTextFromFile(path);
                    string name = ExtractXmlAttribute(text, "name");
                    mapOptions.Add(new MapOption(id, string.IsNullOrEmpty(name) ? filename : name));
                }
                catch (Exception)
                {
                    // Skip a malformed source map file without breaking the editor panel.
                }
            }
        }
        else
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>("Data/Maps"))
            {
                if (asset == null || !int.TryParse(asset.name, out int id))
                    continue;

                string name = ExtractXmlAttribute(asset.text, "name");
                mapOptions.Add(new MapOption(id, string.IsNullOrEmpty(name) ? asset.name : name));
            }
        }

        mapOptions = mapOptions
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .OrderBy(x => x.Id)
            .ToList();
        return mapOptions;
    }

    private MapOption GetMapOptionById(int mapId)
    {
        return GetMapOptions().FirstOrDefault(x => x.Id == mapId) ?? new MapOption(mapId, "\u672A\u77E5\u5730\u56FE");
    }

    private bool TryFindMapOption(string input, out MapOption map)
    {
        map = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string keyword = input.Trim();
        if (int.TryParse(keyword, out int mapId))
            map = GetMapOptions().FirstOrDefault(x => x.Id == mapId);

        map ??= GetMapOptions().FirstOrDefault(x => string.Equals(x.Name, keyword, StringComparison.OrdinalIgnoreCase));
        if (map != null)
            return true;

        List<MapOption> fuzzy = GetMapOptions()
            .Where(x => x.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 || x.Id.ToString().Contains(keyword))
            .Take(2)
            .ToList();
        if (fuzzy.Count == 1)
        {
            map = fuzzy[0];
            return true;
        }

        return false;
    }

    private string GetMissionMapSummary(StoryMissionDocument mission)
    {
        if (mission == null)
            return "0";

        int[] ids = mission.mapIds != null && mission.mapIds.Length > 0
            ? mission.mapIds
            : new[] { mission.mapId };

        return string.Join(" / ", ids
            .Where(x => x != 0)
            .Distinct()
            .Select(x => GetMapOptionById(x).Label));
    }

    private static string ExtractXmlAttribute(string text, string attribute)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(attribute))
            return string.Empty;

        string pattern = attribute + "=\"";
        int start = text.IndexOf(pattern, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        start += pattern.Length;
        int end = text.IndexOf('"', start);
        return end < 0 ? string.Empty : text.Substring(start, end - start);
    }

    private bool TryParseStoryJson(string json, out StoryDocument document, out string error)
    {
        document = null;
        error = string.Empty;

        try
        {
            document = JsonUtility.FromJson<StoryDocument>(json);
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }

        return StoryValidator.Validate(document, out error);
    }

    private void EnsureStoryDirectory()
    {
        string modPath = Application.persistentDataPath + "/Mod";
        if (!FileBrowserHelpers.DirectoryExists(modPath))
            FileBrowserHelpers.CreateFolderInDirectory(Application.persistentDataPath, "Mod");

        string storyDir = GetStoryDirectory();
        if (!FileBrowserHelpers.DirectoryExists(storyDir))
            FileBrowserHelpers.CreateFolderInDirectory(modPath, "Stories");
    }

    private IEnumerable<string> GetStoryPaths()
    {
        string storyDir = GetStoryDirectory();
        if (!FileBrowserHelpers.DirectoryExists(storyDir))
            yield break;

        foreach (var entry in FileBrowserHelpers.GetEntriesInDirectory(storyDir, true).OrderBy(x => x.Path))
        {
            if (!entry.IsDirectory && entry.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                yield return entry.Path;
        }
    }

    private static string GetStoryDirectory()
    {
        return Application.persistentDataPath + StoryDirectory;
    }

    private static string GetStoryPath(string storyId)
    {
        return GetStoryDirectory().TrimEnd('/', '\\') + "/" + storyId + ".json";
    }

    private static bool IsStoryJsonPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && FileBrowserHelpers.IsPathDescendantOfAnother(path, GetStoryDirectory());
    }

    private static string JsonEscape(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private Font FindFont()
    {
        Text[] texts = GameObject.FindObjectsByType<Text>(FindObjectsSortMode.None);
        foreach (Text text in texts)
        {
            if (text.font != null)
                return text.font;
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    private static void PlaceRect(RectTransform rect, Rect layout)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(layout.x, layout.y);
        rect.sizeDelta = new Vector2(layout.width, layout.height);
    }

    private RectTransform CreateRect(string name, Transform parent, Rect rect)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform transformRect = obj.GetComponent<RectTransform>();
        PlaceRect(transformRect, rect);
        return transformRect;
    }

    private static Outline AddOutline(GameObject obj, Color color, Vector2 distance)
    {
        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
        return outline;
    }

    private Text CreateText(string name, Transform parent, string content, int size, TextAnchor anchor, Rect rect, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform transformRect = obj.GetComponent<RectTransform>();
        transformRect.anchorMin = new Vector2(0.5f, 0.5f);
        transformRect.anchorMax = new Vector2(0.5f, 0.5f);
        transformRect.pivot = new Vector2(0.5f, 0.5f);
        transformRect.anchoredPosition = new Vector2(rect.x, rect.y);
        transformRect.sizeDelta = new Vector2(rect.width, rect.height);

        Text text = obj.GetComponent<Text>();
        text.font = uiFont;
        text.text = content;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private InputField CreateInputField(string name, Transform parent, string value, int size, Rect rect, string placeholder, bool multiline = false)
    {
        Image image = CreateImage(name, parent, new Color(0f, 0.12f, 0.14f, 1f));
        RectTransform transformRect = image.rectTransform;
        PlaceRect(transformRect, rect);
        AddOutline(image.gameObject, new Color(0f, 0.82f, 0.95f, 0.45f), new Vector2(1f, -1f));

        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(transformRect, false);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);
        Text text = textObj.GetComponent<Text>();
        text.font = uiFont;
        text.fontSize = size;
        text.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;

        GameObject placeholderObj = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        placeholderObj.transform.SetParent(transformRect, false);
        RectTransform placeholderRect = placeholderObj.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(8f, 4f);
        placeholderRect.offsetMax = new Vector2(-8f, -4f);
        Text placeholderText = placeholderObj.GetComponent<Text>();
        placeholderText.font = uiFont;
        placeholderText.fontSize = size;
        placeholderText.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
        placeholderText.color = new Color(0.8f, 0.9f, 0.92f, 0.42f);
        placeholderText.text = placeholder;

        InputField input = image.gameObject.AddComponent<InputField>();
        input.targetGraphic = image;
        input.textComponent = text;
        input.placeholder = placeholderText;
        input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        input.text = value;
        return input;
    }

    private SimpleDropdown CreateSimpleDropdown(string name, Transform parent, Rect rect, List<string> options)
    {
        Button button = CreateButton(name, parent, string.Empty, rect, null);
        Text label = button.GetComponentInChildren<Text>();
        if (label != null)
            label.alignment = TextAnchor.MiddleLeft;

        Text arrow = CreateText("Arrow", button.transform, "\u25BE", 14, TextAnchor.MiddleCenter, new Rect(rect.width * 0.5f - 14f, 0f, 22f, rect.height), Color.cyan);
        arrow.raycastTarget = false;

        RectTransform listRoot = CreateImage(name + " Options", parent, new Color(0f, 0.08f, 0.1f, 0.98f)).rectTransform;
        PlaceRect(listRoot, new Rect(rect.x, rect.y - 114f, rect.width, 188f));
        AddOutline(listRoot.gameObject, new Color(0f, 0.82f, 0.95f, 0.45f), new Vector2(1f, -1f));
        listRoot.gameObject.SetActive(false);
        listRoot.SetAsLastSibling();

        SimpleDropdown dropdown = new SimpleDropdown(button, label, listRoot);
        dropdown.SetOptions(options ?? new List<string>());
        button.onClick.AddListener(() =>
        {
            HideMapSuggestions();
            HideActorSuggestions();
            dropdown.Toggle(this);
        });
        return dropdown;
    }

    private Button CreateSimpleDropdownOption(RectTransform parent, string label, int index, Action callback)
    {
        Button button = CreateButton("Option", parent, label, new Rect(0f, 0f, 10f, 24f), callback);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -8f - index * 28f);
        rect.sizeDelta = new Vector2(-12f, 24f);
        Text text = button.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.alignment = TextAnchor.MiddleLeft;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = new Vector2(-8f, 0f);
        }
        return button;
    }

    private Dropdown CreateDropdown(string name, Transform parent, Rect rect, List<string> options)
    {
        Image image = CreateImage(name, parent, new Color(0f, 0.12f, 0.14f, 1f));
        RectTransform transformRect = image.rectTransform;
        PlaceRect(transformRect, rect);
        AddOutline(image.gameObject, new Color(0f, 0.82f, 0.95f, 0.45f), new Vector2(1f, -1f));

        Dropdown dropdown = image.gameObject.AddComponent<Dropdown>();
        dropdown.targetGraphic = image;

        Text label = CreateText("Label", transformRect, string.Empty, 14, TextAnchor.MiddleLeft, new Rect(0f, 0f, rect.width - 28f, rect.height), Color.white);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 2f);
        labelRect.offsetMax = new Vector2(-28f, -2f);

        Text arrow = CreateText("Arrow", transformRect, "\u25BE", 14, TextAnchor.MiddleCenter, new Rect(0f, 0f, 24f, rect.height), Color.cyan);
        RectTransform arrowRect = arrow.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1f, 0f);
        arrowRect.anchorMax = new Vector2(1f, 1f);
        arrowRect.pivot = new Vector2(1f, 0.5f);
        arrowRect.anchoredPosition = new Vector2(-4f, 0f);
        arrowRect.sizeDelta = new Vector2(24f, 0f);

        RectTransform template = CreateDropdownTemplate(transformRect, rect.width);
        Text itemLabel = template.GetComponentInChildren<Toggle>(true).GetComponentInChildren<Text>(true);

        dropdown.template = template;
        dropdown.captionText = label;
        dropdown.itemText = itemLabel;
        dropdown.options = (options ?? new List<string>()).Select(x => new Dropdown.OptionData(x)).ToList();
        dropdown.RefreshShownValue();
        template.gameObject.SetActive(false);
        return dropdown;
    }

    private RectTransform CreateDropdownTemplate(Transform parent, float width)
    {
        GameObject templateObj = new GameObject("Template", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        templateObj.transform.SetParent(parent, false);
        RectTransform templateRect = templateObj.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -2f);
        templateRect.sizeDelta = new Vector2(0f, 160f);
        templateObj.GetComponent<Image>().color = new Color(0f, 0.08f, 0.1f, 0.98f);
        AddOutline(templateObj, new Color(0f, 0.82f, 0.95f, 0.45f), new Vector2(1f, -1f));

        GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewportObj.transform.SetParent(templateRect, false);
        RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(4f, 4f);
        viewportRect.offsetMax = new Vector2(-4f, -4f);
        viewportObj.GetComponent<Image>().color = Color.white;
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportRect, false);
        RectTransform contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = contentObj.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        ContentSizeFitter fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject itemObj = new GameObject("Item", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
        itemObj.transform.SetParent(contentRect, false);
        RectTransform itemRect = itemObj.GetComponent<RectTransform>();
        itemRect.sizeDelta = new Vector2(width, 28f);
        Image itemImage = itemObj.GetComponent<Image>();
        itemImage.color = new Color(0f, 0.16f, 0.19f, 1f);
        Toggle toggle = itemObj.GetComponent<Toggle>();
        toggle.targetGraphic = itemImage;

        Text itemText = CreateText("Item Label", itemObj.transform, string.Empty, 14, TextAnchor.MiddleLeft, new Rect(0f, 0f, width - 16f, 26f), Color.white);
        RectTransform itemTextRect = itemText.GetComponent<RectTransform>();
        itemTextRect.anchorMin = Vector2.zero;
        itemTextRect.anchorMax = Vector2.one;
        itemTextRect.offsetMin = new Vector2(8f, 2f);
        itemTextRect.offsetMax = new Vector2(-8f, -2f);

        ScrollRect scrollRect = templateObj.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        return templateRect;
    }

    private Button CreateButton(string name, Transform parent, string label, Rect rect, Action callback)
    {
        Image image = CreateImage(name, parent, new Color(0f, 0.18f, 0.22f, 1f));
        RectTransform transformRect = image.rectTransform;
        transformRect.anchorMin = new Vector2(0.5f, 0.5f);
        transformRect.anchorMax = new Vector2(0.5f, 0.5f);
        transformRect.pivot = new Vector2(0.5f, 0.5f);
        transformRect.anchoredPosition = new Vector2(rect.x, rect.y);
        transformRect.sizeDelta = new Vector2(rect.width, rect.height);

        Button button = image.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonNormalColor;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor = ButtonPressedColor;
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        button.onClick.AddListener(() => callback?.Invoke());
        AddOutline(image.gameObject, new Color(0f, 0.85f, 1f, 0.55f), new Vector2(1f, -1f));

        CreateText("Text", transformRect, label, 16, TextAnchor.MiddleCenter, new Rect(0f, 0f, rect.width, rect.height), Color.cyan);
        return button;
    }


    private RectTransform CreateScrollContent(string name, Transform parent, Rect rect)
    {
        Image image = CreateImage(name, parent, new Color(0f, 0f, 0f, 0.65f));
        RectTransform root = image.rectTransform;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(rect.x, rect.y);
        root.sizeDelta = new Vector2(rect.width, rect.height);

        return root;
    }

    private Button CreateListButton(string label, string path)
    {
        Button button = CreateButton("Story", storyListContent, label, new Rect(0f, 0f, 230f, 34f), null);
        PlaceListItem(button.GetComponent<RectTransform>(), 36f);
        Text text = button.GetComponentInChildren<Text>();
        StoryListItem item = new StoryListItem(path, button, text);
        storyListItems.Add(item);
        ApplyStoryListItemStyle(item, string.Equals(path, selectedStoryPath, StringComparison.OrdinalIgnoreCase));
        return button;
    }

    private void CreateListLabel(string label)
    {
        Text text = CreateText("Empty", storyListContent, label, 15, TextAnchor.MiddleCenter, new Rect(0f, 0f, 230f, 34f), Color.white);
        PlaceListItem(text.GetComponent<RectTransform>(), 36f);
    }

    private void PlaceListItem(RectTransform rect, float height)
    {
        const float spacing = 8f;
        float y = -storyListItemIndex * (height + spacing);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, y - 10f);
        rect.sizeDelta = new Vector2(-20f, height);

        storyListItemIndex++;
    }

    private void UpdateStoryListSelection()
    {
        foreach (StoryListItem item in storyListItems)
        {
            bool isSelected = !string.IsNullOrWhiteSpace(selectedStoryPath)
                && string.Equals(item.Path, selectedStoryPath, StringComparison.OrdinalIgnoreCase);
            ApplyStoryListItemStyle(item, isSelected);
        }
    }

    private static void ApplyStoryListItemStyle(StoryListItem item, bool isSelected)
    {
        if (item == null || item.Button == null)
            return;

        ColorBlock colors = item.Button.colors;
        colors.normalColor = isSelected ? StorySelectedColor : ButtonNormalColor;
        colors.highlightedColor = isSelected ? StorySelectedHoverColor : ButtonHoverColor;
        colors.pressedColor = ButtonPressedColor;
        colors.selectedColor = colors.normalColor;
        item.Button.colors = colors;

        if (item.Button.targetGraphic != null)
            item.Button.targetGraphic.color = colors.normalColor;

        if (item.Label != null)
            item.Label.color = isSelected ? new Color(1f, 0.92f, 0.32f, 1f) : Color.cyan;
    }

    private class StoryListItem
    {
        public readonly string Path;
        public readonly Button Button;
        public readonly Text Label;

        public StoryListItem(string path, Button button, Text label)
        {
            Path = path;
            Button = button;
            Label = label;
        }
    }

    private class MapOption
    {
        public readonly int Id;
        public readonly string Name;
        public string Label => Id + " " + Name;

        public MapOption(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    private class PetOption
    {
        public readonly int Id;
        public readonly string Name;

        public PetOption(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    private class SimpleDropdown
    {
        private readonly Button button;
        private readonly Text label;
        private readonly RectTransform listRoot;
        private List<string> options = new List<string>();
        private int pageStart;
        private const int MaxVisibleOptions = 5;

        public int Value { get; private set; }

        public SimpleDropdown(Button button, Text label, RectTransform listRoot)
        {
            this.button = button;
            this.label = label;
            this.listRoot = listRoot;
        }

        public void SetOptions(List<string> newOptions)
        {
            options = newOptions ?? new List<string>();
            Value = Mathf.Clamp(Value, 0, Mathf.Max(0, options.Count - 1));
            RefreshLabel();
        }

        public void SetValue(int value)
        {
            Value = Mathf.Clamp(value, 0, Mathf.Max(0, options.Count - 1));
            RefreshLabel();
            Hide();
        }

        public void Toggle(WorkshopStoryPanel owner)
        {
            if (listRoot == null || owner == null)
                return;

            bool shouldShow = !listRoot.gameObject.activeSelf;
            Hide();
            if (shouldShow)
            {
                pageStart = Mathf.Clamp(Value - 2, 0, Mathf.Max(0, options.Count - MaxVisibleOptions));
                Show(owner);
            }
        }

        public void Hide()
        {
            if (listRoot != null)
                listRoot.gameObject.SetActive(false);
        }

        private void Show(WorkshopStoryPanel owner)
        {
            for (int i = listRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(listRoot.GetChild(i).gameObject);

            int row = 0;
            if (pageStart > 0)
            {
                owner.CreateSimpleDropdownOption(listRoot, "\u25B2 \u4E0A\u4E00\u9875", row++, () =>
                {
                    pageStart = Mathf.Max(0, pageStart - MaxVisibleOptions);
                    Show(owner);
                });
            }

            int count = Mathf.Min(MaxVisibleOptions, options.Count - pageStart);
            for (int i = 0; i < count; i++)
            {
                int index = pageStart + i;
                owner.CreateSimpleDropdownOption(listRoot, options[index], row++, () =>
                {
                    SetValue(index);
                    Hide();
                });
            }

            if (pageStart + count < options.Count)
            {
                owner.CreateSimpleDropdownOption(listRoot, "\u25BC \u4E0B\u4E00\u9875", row++, () =>
                {
                    pageStart = Mathf.Min(Mathf.Max(0, options.Count - MaxVisibleOptions), pageStart + MaxVisibleOptions);
                    Show(owner);
                });
            }

            listRoot.gameObject.SetActive(row > 0);
            listRoot.SetAsLastSibling();
        }

        private void RefreshLabel()
        {
            if (label != null)
                label.text = options.Count == 0 ? string.Empty : options[Mathf.Clamp(Value, 0, options.Count - 1)];

            if (button != null && button.targetGraphic != null)
                button.targetGraphic.color = ButtonNormalColor;
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError ? new Color(1f, 0.45f, 0.35f, 1f) : Color.white;
    }
}
