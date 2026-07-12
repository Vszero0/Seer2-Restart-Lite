using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleFileBrowser;
using UnityEngine;

public sealed class WorkshopStoryEditorModel
{
    private const string StoryDirectory = "/Mod/Stories/";
    private readonly List<string> storyPaths = new List<string>();

    public IReadOnlyList<string> StoryPaths => storyPaths;
    public string SelectedStoryPath { get; private set; }
    public StoryDocument Document { get; private set; }
    public string SelectedNodeId { get; private set; }
    public int SelectedCommandIndex { get; private set; } = -1;
    public int SelectedChoiceIndex { get; private set; } = -1;
    public string SelectedActorId { get; private set; }
    public bool IsDirty { get; private set; }

    public IReadOnlyList<StoryNodeDocument> Nodes => Document?.nodes ?? Array.Empty<StoryNodeDocument>();
    public IReadOnlyList<StoryActorDocument> Actors => Document?.actors ?? Array.Empty<StoryActorDocument>();

    public StoryNodeDocument SelectedNode => (Document?.nodes ?? Array.Empty<StoryNodeDocument>())
        .FirstOrDefault(x => x != null && x.id == SelectedNodeId);

    public StoryCommandDocument SelectedCommand
    {
        get
        {
            StoryNodeDocument node = SelectedNode;
            if (node?.commands == null || SelectedCommandIndex < 0 || SelectedCommandIndex >= node.commands.Length)
                return null;

            return node.commands[SelectedCommandIndex];
        }
    }

    public void ReloadStoryPaths()
    {
        EnsureStoryDirectory();
        storyPaths.Clear();
        storyPaths.AddRange(Directory.GetFiles(GetStoryDirectory(), "*.json")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    public string GetStoryDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            StoryDocument document = JsonUtility.FromJson<StoryDocument>(FileBrowserHelpers.ReadTextFromFile(path));
            if (!string.IsNullOrWhiteSpace(document?.title))
                return document.title;
        }
        catch (Exception)
        {
            // The list still needs to expose an invalid file so the editor can report its error.
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    public bool SelectStory(string path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !FileBrowserHelpers.FileExists(path))
        {
            error = "找不到剧情文件";
            return false;
        }

        try
        {
            StoryDocument document = JsonUtility.FromJson<StoryDocument>(FileBrowserHelpers.ReadTextFromFile(path));
            if (!StoryValidator.Validate(document, out error))
            {
                ClearDocumentSelection(path);
                return false;
            }

            SelectedStoryPath = path;
            Document = document;
            SelectedNodeId = document.entry;
            SelectedCommandIndex = -1;
            SelectedChoiceIndex = -1;
            SelectedActorId = document.actors?.FirstOrDefault(x => x != null)?.id;
            IsDirty = false;
            return true;
        }
        catch (Exception exception)
        {
            error = "读取剧情失败：" + exception.Message;
            ClearDocumentSelection(path);
            return false;
        }
    }

    public bool CreateStory(out string path, out string error)
    {
        path = null;
        error = string.Empty;
        try
        {
            EnsureStoryDirectory();
            string id = CreateUniqueStoryId();
            int missionId = GetNextMissionId();
            StoryNodeDocument entryNode = new StoryNodeDocument
            {
                id = "start",
                commands = new[]
                {
                    new StoryCommandDocument { type = "scene", bg = "Maps/bg/121" },
                    new StoryCommandDocument { type = "show", actor = "fala" },
                    new StoryCommandDocument { type = "say", actor = "fala", text = "欢迎来到自制剧情预览。" },
                    new StoryCommandDocument
                    {
                        type = "choice",
                        choices = new[]
                        {
                            new StoryChoiceDocument { text = "继续查看分支。", target = "continue" },
                            new StoryChoiceDocument { text = "直接结束预览。", target = "finish" },
                        },
                    },
                }
            };
            StoryNodeDocument continueNode = new StoryNodeDocument
            {
                id = "continue",
                commands = new[]
                {
                    new StoryCommandDocument { type = "narrate", text = "这是新建剧情的开场旁白。" },
                    new StoryCommandDocument { type = "jump", target = "finish" },
                }
            };
            StoryNodeDocument finishNode = new StoryNodeDocument
            {
                id = "finish",
                commands = new[]
                {
                    new StoryCommandDocument { type = "mission", args = missionId + " complete" },
                    new StoryCommandDocument { type = "end" },
                }
            };
            Document = new StoryDocument
            {
                schemaVersion = 1,
                id = id,
                title = "新建剧情 " + DateTime.Now.ToString("yyyyMMdd HHmmss"),
                entry = "start",
                mission = new StoryMissionDocument
                {
                    id = missionId,
                    title = "新建剧情",
                    summary = "这是一个可预览的新建剧情模板。",
                    replayable = true,
                    mapId = 121,
                },
                actors = new[]
                {
                    new StoryActorDocument
                    {
                        id = "fala",
                        name = "法拉",
                        sprite = "Pets/pet/10",
                        icon = "Pets/icon/10",
                        side = "left",
                        faceLeft = false,
                    }
                },
                nodes = new[] { entryNode, continueNode, finishNode },
            };

            path = GetStoryPath(id);
            SelectedStoryPath = path;
            SelectedNodeId = entryNode.id;
            SelectedCommandIndex = -1;
            SelectedChoiceIndex = -1;
            SelectedActorId = "fala";
            IsDirty = true;
            return true;
        }
        catch (Exception exception)
        {
            error = "创建剧情失败：" + exception.Message;
            return false;
        }
    }

    public bool Save(out string error)
    {
        error = string.Empty;
        if (Document == null || string.IsNullOrWhiteSpace(SelectedStoryPath))
        {
            error = "当前没有正在编辑的剧情";
            return false;
        }

        if (!StoryValidator.Validate(Document, out error))
            return false;

        try
        {
            FileBrowserHelpers.WriteTextToFile(SelectedStoryPath, JsonUtility.ToJson(Document, true));
            IsDirty = false;
            Database.instance?.ReloadStoryMod();
            ReloadStoryPaths();
            return true;
        }
        catch (Exception exception)
        {
            error = "保存剧情失败：" + exception.Message;
            return false;
        }
    }

    public bool DeleteSelected(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(SelectedStoryPath))
        {
            error = "没有选中的剧情";
            return false;
        }

        try
        {
            FileBrowserHelpers.DeleteFile(SelectedStoryPath);
            SelectedStoryPath = null;
            Document = null;
            SelectedNodeId = null;
            SelectedCommandIndex = -1;
            SelectedChoiceIndex = -1;
            SelectedActorId = null;
            IsDirty = false;
            ReloadStoryPaths();
            return true;
        }
        catch (Exception exception)
        {
            error = "删除剧情失败：" + exception.Message;
            return false;
        }
    }

    public void SelectNode(string nodeId)
    {
        SelectedNodeId = nodeId;
        SelectedCommandIndex = -1;
        SelectedChoiceIndex = -1;
    }

    public StoryActorDocument SelectedActor => (Document?.actors ?? Array.Empty<StoryActorDocument>())
        .FirstOrDefault(x => x != null && x.id == SelectedActorId);

    public void SelectActor(string actorId)
    {
        SelectedActorId = actorId;
    }

    public StoryActorDocument AddActor()
    {
        if (Document == null)
            return null;

        List<StoryActorDocument> actors = (Document.actors ?? Array.Empty<StoryActorDocument>()).Where(x => x != null).ToList();
        int index = 1;
        string id;
        do
        {
            id = "actor_" + index++;
        } while (actors.Any(x => x.id == id));

        StoryActorDocument actor = new StoryActorDocument
        {
            id = id,
            name = "新角色",
            side = "left",
            faceLeft = false,
            scale = 1f,
        };
        actors.Add(actor);
        Document.actors = actors.ToArray();
        SelectedActorId = id;
        MarkDirty();
        return actor;
    }

    public bool UpdateSelectedActor(string id, string name, string sprite, string icon, string side, out string error)
    {
        error = string.Empty;
        StoryActorDocument actor = SelectedActor;
        id = (id ?? string.Empty).Trim();
        if (actor == null || string.IsNullOrWhiteSpace(id))
        {
            error = "角色 ID 不能为空";
            return false;
        }
        if ((Document.actors ?? Array.Empty<StoryActorDocument>()).Any(x => x != actor && string.Equals(x?.id, id, StringComparison.OrdinalIgnoreCase)))
        {
            error = "角色 ID 已存在";
            return false;
        }

        string oldId = actor.id;
        actor.id = id;
        actor.name = name;
        actor.sprite = sprite;
        actor.icon = icon;
        actor.side = string.Equals(side, "right", StringComparison.OrdinalIgnoreCase) ? "right" : "left";
        ReplaceActorReferences(oldId, id);
        SelectedActorId = id;
        MarkDirty();
        return true;
    }

    public bool RemoveSelectedActor(out string error)
    {
        error = string.Empty;
        StoryActorDocument actor = SelectedActor;
        if (actor == null)
        {
            error = "没有选中的角色";
            return false;
        }
        if (GetNodes().SelectMany(x => x.commands ?? Array.Empty<StoryCommandDocument>()).Any(x => x?.actor == actor.id))
        {
            error = "该角色仍被对白或显示命令使用，请先解除引用";
            return false;
        }

        List<StoryActorDocument> actors = (Document.actors ?? Array.Empty<StoryActorDocument>()).Where(x => x != actor).ToList();
        Document.actors = actors.ToArray();
        SelectedActorId = actors.FirstOrDefault()?.id;
        MarkDirty();
        return true;
    }

    public void SelectCommand(int index)
    {
        SelectedCommandIndex = index;
        SelectedChoiceIndex = SelectedCommand?.choices != null && SelectedCommand.choices.Length > 0 ? 0 : -1;
    }

    public StoryNodeDocument AddNode()
    {
        if (Document == null)
            return null;

        List<StoryNodeDocument> nodes = GetNodes();
        string id = CreateUniqueNodeId(nodes);
        StoryNodeDocument node = new StoryNodeDocument
        {
            id = id,
            commands = new[] { new StoryCommandDocument { type = "end" } },
        };
        nodes.Add(node);
        Document.nodes = nodes.ToArray();
        SelectedNodeId = id;
        SelectedCommandIndex = -1;
        SelectedChoiceIndex = -1;
        MarkDirty();
        return node;
    }

    public bool RemoveSelectedNode(out string error)
    {
        error = string.Empty;
        StoryNodeDocument node = SelectedNode;
        if (Document == null || node == null)
        {
            error = "没有选中的剧情点";
            return false;
        }

        if (node.id == Document.entry)
        {
            error = "入口剧情点不能删除，请先设置其他入口";
            return false;
        }

        if (IsNodeReferenced(node.id))
        {
            error = "该剧情点仍被跳转或选项引用，请先解除连接";
            return false;
        }

        List<StoryNodeDocument> nodes = GetNodes();
        nodes.Remove(node);
        Document.nodes = nodes.ToArray();
        SelectedNodeId = nodes.FirstOrDefault()?.id;
        SelectedCommandIndex = -1;
        SelectedChoiceIndex = -1;
        MarkDirty();
        return true;
    }

    public bool RenameSelectedNode(string id, out string error)
    {
        error = string.Empty;
        StoryNodeDocument node = SelectedNode;
        id = (id ?? string.Empty).Trim();
        if (node == null || string.IsNullOrWhiteSpace(id))
        {
            error = "剧情点 ID 不能为空";
            return false;
        }

        if (GetNodes().Any(x => x != node && string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase)))
        {
            error = "剧情点 ID 已存在";
            return false;
        }

        string oldId = node.id;
        node.id = id;
        if (Document.entry == oldId)
            Document.entry = id;
        ReplaceNodeReferences(oldId, id);
        SelectedNodeId = id;
        SelectedChoiceIndex = -1;
        MarkDirty();
        return true;
    }

    public void SetEntryNode()
    {
        if (SelectedNode == null || Document == null)
            return;

        Document.entry = SelectedNode.id;
        MarkDirty();
    }

    public StoryCommandDocument AddCommand(string type)
    {
        StoryNodeDocument node = SelectedNode;
        if (node == null)
            return null;

        List<StoryCommandDocument> commands = (node.commands ?? Array.Empty<StoryCommandDocument>()).ToList();
        StoryCommandDocument command = new StoryCommandDocument { type = type };
        if (type == "scene")
            command.bg = "Maps/bg/121";
        if (type == "narrate")
            command.text = "请输入旁白内容。";
        if (type == "say")
        {
            command.actor = GetFirstActorId();
            command.text = "请输入对白内容。";
        }
        if (type == "choice")
            command.choices = new[] { new StoryChoiceDocument { text = "选项", target = GetFirstOtherNodeId() } };
        if (type == "show")
            command.actor = GetFirstActorId();
        if (type == "hide")
            command.actor = GetFirstActorId();
        if (type == "jump")
            command.target = GetFirstOtherNodeId();
        if (type == "mission")
            command.args = (Document.mission?.id ?? -10001) + " complete";
        if (type == "teleport")
            command.args = (Document.mission?.mapId ?? 121).ToString();

        commands.Add(command);
        node.commands = commands.ToArray();
        SelectedCommandIndex = commands.Count - 1;
        SelectedChoiceIndex = command.choices != null && command.choices.Length > 0 ? 0 : -1;
        MarkDirty();
        return command;
    }

    public void RemoveSelectedCommand()
    {
        StoryNodeDocument node = SelectedNode;
        if (node?.commands == null || SelectedCommandIndex < 0 || SelectedCommandIndex >= node.commands.Length)
            return;

        List<StoryCommandDocument> commands = node.commands.ToList();
        commands.RemoveAt(SelectedCommandIndex);
        node.commands = commands.ToArray();
        SelectedCommandIndex = Mathf.Clamp(SelectedCommandIndex - 1, -1, commands.Count - 1);
        SelectedChoiceIndex = -1;
        MarkDirty();
    }

    public void UpdateMetadata(string title, string summary, string missionTitle, string mapText, bool replayable)
    {
        if (Document == null)
            return;

        Document.title = title;
        if (Document.mission == null)
            Document.mission = new StoryMissionDocument();
        Document.mission.title = missionTitle;
        Document.mission.summary = summary;
        Document.mission.replayable = replayable;
        if (int.TryParse(mapText, out int mapId))
            Document.mission.mapId = mapId;
        MarkDirty();
    }

    public void UpdateSelectedCommand(string actor, string text, string bg, string target, string args)
    {
        StoryCommandDocument command = SelectedCommand;
        if (command == null)
            return;

        command.actor = actor;
        command.text = text;
        command.bg = bg;
        command.target = target;
        command.args = args;
        MarkDirty();
    }

    public void AddChoice()
    {
        StoryCommandDocument command = SelectedCommand;
        if (!IsCommandType(command, "choice"))
            return;

        List<StoryChoiceDocument> choices = (command.choices ?? Array.Empty<StoryChoiceDocument>()).ToList();
        choices.Add(new StoryChoiceDocument { text = "新选项", target = GetFirstOtherNodeId() });
        command.choices = choices.ToArray();
        SelectedChoiceIndex = choices.Count - 1;
        MarkDirty();
    }

    public void RemoveChoice(int index)
    {
        StoryCommandDocument command = SelectedCommand;
        if (!IsCommandType(command, "choice") || command.choices == null || index < 0 || index >= command.choices.Length)
            return;

        List<StoryChoiceDocument> choices = command.choices.ToList();
        choices.RemoveAt(index);
        command.choices = choices.ToArray();
        SelectedChoiceIndex = Mathf.Clamp(SelectedChoiceIndex - (index <= SelectedChoiceIndex ? 1 : 0), -1, choices.Count - 1);
        MarkDirty();
    }

    public void SelectChoice(int index)
    {
        StoryCommandDocument command = SelectedCommand;
        if (!IsCommandType(command, "choice") || command.choices == null || index < 0 || index >= command.choices.Length)
            return;

        SelectedChoiceIndex = index;
    }

    public void UpdateSelectedChoice(int index, string text, string target)
    {
        StoryCommandDocument command = SelectedCommand;
        if (!IsCommandType(command, "choice") || command.choices == null || index < 0 || index >= command.choices.Length)
            return;

        command.choices[index].text = text;
        command.choices[index].target = target;
        MarkDirty();
    }

    private static bool IsCommandType(StoryCommandDocument command, string type)
    {
        return command != null && string.Equals(command.type, type, StringComparison.OrdinalIgnoreCase);
    }

    public bool Validate(out string error)
    {
        return StoryValidator.Validate(Document, out error);
    }

    private void MarkDirty()
    {
        IsDirty = true;
    }

    private List<StoryNodeDocument> GetNodes()
    {
        return (Document?.nodes ?? Array.Empty<StoryNodeDocument>()).Where(x => x != null).ToList();
    }

    private bool IsNodeReferenced(string id)
    {
        return GetNodes().SelectMany(x => x.commands ?? Array.Empty<StoryCommandDocument>())
            .Any(x => x != null && (x.target == id || (x.choices ?? Array.Empty<StoryChoiceDocument>()).Any(c => c?.target == id)));
    }

    private void ReplaceNodeReferences(string oldId, string newId)
    {
        foreach (StoryCommandDocument command in GetNodes().SelectMany(x => x.commands ?? Array.Empty<StoryCommandDocument>()))
        {
            if (command == null)
                continue;
            if (command.target == oldId)
                command.target = newId;
            foreach (StoryChoiceDocument choice in command.choices ?? Array.Empty<StoryChoiceDocument>())
            {
                if (choice?.target == oldId)
                    choice.target = newId;
            }
        }
    }

    private void ReplaceActorReferences(string oldId, string newId)
    {
        foreach (StoryCommandDocument command in GetNodes().SelectMany(x => x.commands ?? Array.Empty<StoryCommandDocument>()))
        {
            if (command?.actor == oldId)
                command.actor = newId;
        }
    }

    private string GetFirstActorId()
    {
        return (Document?.actors ?? Array.Empty<StoryActorDocument>()).FirstOrDefault(x => x != null)?.id ?? string.Empty;
    }

    private string GetFirstOtherNodeId()
    {
        return GetNodes().FirstOrDefault(x => x.id != SelectedNodeId)?.id ?? SelectedNodeId ?? string.Empty;
    }

    private static string CreateUniqueNodeId(List<StoryNodeDocument> nodes)
    {
        int index = 1;
        string id;
        do
        {
            id = "point_" + index++;
        } while (nodes.Any(x => x.id == id));
        return id;
    }

    private string CreateUniqueStoryId()
    {
        string prefix = "story_" + DateTime.Now.ToString("yyyyMMddHHmmss");
        string id = prefix;
        int suffix = 2;
        while (FileBrowserHelpers.FileExists(GetStoryPath(id)))
            id = prefix + "_" + suffix++;
        return id;
    }

    private int GetNextMissionId()
    {
        int result = -10001;
        foreach (string path in storyPaths)
        {
            try
            {
                StoryDocument document = JsonUtility.FromJson<StoryDocument>(FileBrowserHelpers.ReadTextFromFile(path));
                if (document?.mission != null && document.mission.id <= result)
                    result = document.mission.id - 1;
            }
            catch (Exception)
            {
                // Invalid files are reported by the editor and do not block new story creation.
            }
        }
        return result;
    }

    private static string GetStoryDirectory()
    {
        return Application.persistentDataPath + StoryDirectory;
    }

    private static string GetStoryPath(string id)
    {
        return GetStoryDirectory().TrimEnd('/', '\\') + "/" + id + ".json";
    }

    private static void EnsureStoryDirectory()
    {
        string directory = GetStoryDirectory();
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private void ClearDocumentSelection(string path)
    {
        SelectedStoryPath = path;
        Document = null;
        SelectedNodeId = null;
        SelectedCommandIndex = -1;
        SelectedChoiceIndex = -1;
        SelectedActorId = null;
        IsDirty = false;
    }
}
