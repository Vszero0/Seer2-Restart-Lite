using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleFileBrowser;
using UnityEngine;

/// <summary>
/// 可视化编辑器中可供选择的地图或精灵资源。
/// 资源的持久引用始终是 ID；这里的名称只用于编辑器展示。
/// </summary>
public sealed class WorkshopStoryPointResourceOption
{
    public int id;
    public string name;
    public bool isMod;

    public string displayName
    {
        get
        {
            string label = string.IsNullOrWhiteSpace(name) ? id.ToString() : name + "  " + id;
            return (isMod ? "[当前 Mod] " : "[本体] ") + label;
        }
    }
}

/// <summary>
/// 已加入当前剧情点的角色候选。与精灵资源候选分开，避免把角色 ID 误当成数值资源 ID。
/// </summary>
public sealed class WorkshopStoryPointActorOption
{
    public string actorId;
    public string name;

    public string displayName => string.IsNullOrWhiteSpace(name) ? actorId : name;
}

public sealed class WorkshopStoryCustomSceneOption
{
    public string sceneResourceId;
    public string name;
    public string displayName => "[自制] " + (string.IsNullOrWhiteSpace(name) ? sceneResourceId : name);
}

public sealed class WorkshopStoryPointAddActorOption
{
    public string actorId;
    public int petId;
    public string name;
    public bool isPet;
    public bool isMod;

    public string displayName
    {
        get
        {
            string id = isPet ? petId.ToString() : actorId;
            string label = string.IsNullOrWhiteSpace(name) ? id : name + "  " + id;
            return (isPet ? "[精灵] " : "[剧本角色] ") + label;
        }
    }
}

/// <summary>
/// 由场景命令切分出的剧情点时间线区段。场景数据描述静态舞台，
/// 区段描述该舞台内依次发生的对白、旁白与其他演出命令。
/// </summary>
public sealed class WorkshopStorySceneSection
{
    public StorySceneDocument scene;
    public int commandStartIndex;
    public int commandEndIndex;

    public int contentCount => Mathf.Max(0, commandEndIndex - commandStartIndex - 1);
}

/// <summary>
/// 剧情点可视化编辑器的数据会话。
/// 编辑器始终操作剧本文件的深拷贝，只有显式保存才会写回当前源码母稿或 Mod 剧本。
/// </summary>
public sealed class WorkshopStoryNodeEditorModel
{
    private readonly WorkshopStoryRepository repository;

    public string StoryPath { get; private set; }
    public string NodeId { get; private set; }
    public StoryDocument DraftDocument { get; private set; }
    public StoryNodeDocument DraftNode { get; private set; }
    public bool HasUnsavedChanges { get; private set; }

    public WorkshopStoryNodeEditorModel(WorkshopStoryRepository repository)
    {
        this.repository = repository;
    }

    public bool Open(string storyPath, string nodeId, out string error)
    {
        StoryPath = storyPath;
        NodeId = nodeId;
        DraftDocument = null;
        DraftNode = null;
        HasUnsavedChanges = false;

        if (string.IsNullOrWhiteSpace(storyPath) || string.IsNullOrWhiteSpace(nodeId))
        {
            error = "缺少剧本或剧情点定位信息。";
            return false;
        }

        if (!repository.TryLoad(storyPath, out StoryDocument document, out error))
            return false;

        if (!StoryDocumentCodec.TryDeserialize(StoryDocumentCodec.Serialize(document), false,
                out StoryDocument draftDocument, out error))
        {
            return false;
        }

        EnsureCommandIds(draftDocument);
        EnsurePointResources(draftDocument);

        StoryNodeDocument draftNode = (draftDocument.nodes ?? Array.Empty<StoryNodeDocument>())
            .FirstOrDefault(node => node != null
                && string.Equals(node.id, nodeId, StringComparison.OrdinalIgnoreCase));
        if (draftNode == null)
        {
            error = "找不到要编辑的剧情点。";
            return false;
        }

        DraftDocument = draftDocument;
        DraftNode = draftNode;
        if (EnsureSceneSections(DraftNode))
            HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public string GetResourceSource(string resourcePath)
    {
        StoryResourceDefinition resource = (DraftDocument?.resourceDefinitions ?? Array.Empty<StoryResourceDefinition>())
            .FirstOrDefault(value => value != null
                && string.Equals(value.path, resourcePath, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(resource?.source) ? "auto" : resource.source;
    }

    public StorySceneResourceDocument GetStorySceneResource(string resourceId)
    {
        return DraftDocument?.GetSceneResource(resourceId);
    }

    public List<WorkshopStoryCustomSceneOption> GetCustomSceneOptions(string filter)
    {
        string query = (filter ?? string.Empty).Trim();
        return (DraftDocument?.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
            .Where(scene => scene != null
                && !string.IsNullOrWhiteSpace(scene.id)
                && !string.IsNullOrWhiteSpace(scene.backgroundResourcePath)
                && (string.IsNullOrWhiteSpace(query)
                    || scene.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || scene.id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            .Select(scene => new WorkshopStoryCustomSceneOption
            {
                sceneResourceId = scene.id,
                name = scene.displayName,
            })
            .ToList();
    }

    public bool RenameNode(string displayName, out string error)
    {
        if (DraftNode == null)
        {
            error = "当前没有可编辑的剧情点草稿。";
            return false;
        }

        DraftNode.displayName = string.IsNullOrWhiteSpace(displayName)
            ? "未命名剧情点"
            : displayName.Trim();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool UpdateCommandText(string commandId, string text, out string error)
    {
        if (DraftNode == null)
        {
            error = "当前没有可编辑的剧情点草稿。";
            return false;
        }

        StoryCommandDocument command = (DraftNode.commands ?? Array.Empty<StoryCommandDocument>())
            .FirstOrDefault(value => value != null
                && string.Equals(value.commandId, commandId, StringComparison.OrdinalIgnoreCase));
        if (command == null)
        {
            error = "找不到要编辑的剧情内容。";
            return false;
        }

        command.text = text ?? string.Empty;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool UpdateCommandExpression(string commandId, string expression, out string error)
    {
        if (DraftNode == null)
        {
            error = "当前没有可编辑的剧情点草稿。";
            return false;
        }

        StoryCommandDocument command = (DraftNode.commands ?? Array.Empty<StoryCommandDocument>())
            .FirstOrDefault(value => value != null
                && string.Equals(value.commandId, commandId, StringComparison.OrdinalIgnoreCase));
        if (command == null)
        {
            error = "找不到要设置表情的对白。";
            return false;
        }

        string type = (command.type ?? string.Empty).Trim().ToLowerInvariant();
        if ((type != "say" && type != "choice") || string.IsNullOrWhiteSpace(command.actor))
        {
            error = "只有已绑定角色的对白可以设置表情。";
            return false;
        }

        string normalized = StoryExpressionCatalog.Normalize(expression);
        if (!string.IsNullOrWhiteSpace(expression) && normalized == null)
        {
            error = "选择的表情不存在。";
            return false;
        }

        command.expression = normalized;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 剧情点的地图候选来自本体 XML 与当前 Mod/Maps，并可按名称或 ID 检索。
    /// 选择后仍通过 Map.GetMap 载入，确保背景与默认 BGM 遵循地图 XML。
    /// </summary>
    public List<WorkshopStoryPointResourceOption> GetMapOptions(string filter)
    {
        Dictionary<int, WorkshopStoryPointResourceOption> options = new Dictionary<int, WorkshopStoryPointResourceOption>();
        foreach (TextAsset asset in Resources.LoadAll<TextAsset>("Data/Maps"))
        {
            try
            {
                Map map = ResourceManager.GetXML<Map>(asset.text);
                AddMapOption(options, map);
            }
            catch
            {
                // 单个 XML 不应阻断其余地图的选择。
            }
        }

        if (Database.instance != null)
        {
            foreach (Map map in Database.instance.mapDict.Values)
                AddMapOption(options, map);
        }

        string modMapDirectory = Path.Combine(Application.persistentDataPath, "Mod", "Maps");
        try
        {
            if (Directory.Exists(modMapDirectory))
            {
                foreach (string path in Directory.GetFiles(modMapDirectory, "*.xml", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Map map = ResourceManager.GetXML<Map>(FileBrowserHelpers.ReadTextFromFile(path));
                        if (map != null && Map.IsMod(map.id))
                            AddMapOption(options, map);
                    }
                    catch
                    {
                        // 当前 Mod 中单个无效地图 XML 不应阻断其他资源的选择。
                    }
                }
            }
        }
        catch
        {
            // 当前 Mod 目录不可读时仍允许作者继续使用本体地图。
        }

        return FilterOptions(options.Values, filter);
    }

    public List<WorkshopStoryPointResourceOption> GetSelectedMapOptions()
    {
        Dictionary<int, WorkshopStoryPointResourceOption> available = GetMapOptions(string.Empty)
            .ToDictionary(option => option.id, option => option);
        return (DraftNode?.mapReferences ?? Array.Empty<StoryMapReferenceDocument>())
            .Where(reference => reference != null && reference.mapId != 0)
            .Select(reference => available.TryGetValue(reference.mapId, out WorkshopStoryPointResourceOption option)
                ? option
                : new WorkshopStoryPointResourceOption { id = reference.mapId, name = "地图" })
            .GroupBy(option => option.id)
            .Select(group => group.First())
            .OrderBy(option => option.id)
            .ToList();
    }

    public List<WorkshopStoryPointResourceOption> GetPetOptions(string filter)
    {
        if (Database.instance == null)
            return new List<WorkshopStoryPointResourceOption>();

        IEnumerable<WorkshopStoryPointResourceOption> options = Database.instance.petInfoDict.Values
            .Where(pet => pet != null)
            .Select(pet => new WorkshopStoryPointResourceOption
            {
                id = pet.id,
                name = pet.name,
                isMod = PetInfo.IsMod(pet.id),
            });
        return FilterOptions(options, filter);
    }

    public List<WorkshopStoryPointAddActorOption> GetAddActorOptions(string filter)
    {
        string query = (filter ?? string.Empty).Trim();
        IEnumerable<WorkshopStoryPointAddActorOption> pets = Database.instance == null
            ? Enumerable.Empty<WorkshopStoryPointAddActorOption>()
            : Database.instance.petInfoDict.Values
                .Where(pet => pet != null)
                .Select(pet => new WorkshopStoryPointAddActorOption
                {
                    petId = pet.id,
                    name = pet.name,
                    isPet = true,
                    isMod = PetInfo.IsMod(pet.id),
                });
        IEnumerable<WorkshopStoryPointAddActorOption> storyActors = (DraftDocument?.actors ?? Array.Empty<StoryActorDocument>())
            .Where(actor => actor != null && !string.Equals(actor.actorType, "pet", StringComparison.OrdinalIgnoreCase))
            .Select(actor => new WorkshopStoryPointAddActorOption
            {
                actorId = actor.id,
                name = actor.displayName,
                isPet = false,
                isMod = (actor.sprite ?? string.Empty).StartsWith("Mod/", StringComparison.OrdinalIgnoreCase),
            });

        return storyActors.Concat(pets)
            .Where(option => string.IsNullOrWhiteSpace(query)
                || option.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (option.isPet ? option.petId.ToString() : option.actorId ?? string.Empty)
                    .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(option => option.isPet ? 1 : 0)
            .ThenBy(option => option.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 将地图作为本剧情点的固定场景资源加入。首个场景同时成为起始 scene 命令，
    /// 后续场景只进入可用资源集合，等待作者在时间线中主动切换。
    /// </summary>
    public bool AddMapResource(Map map, out StorySceneDocument initialScene, out string error)
    {
        initialScene = null;
        if (DraftNode == null)
        {
            error = "当前没有可编辑的剧情点草稿。";
            return false;
        }

        if (map == null || map.id == 0)
        {
            error = "地图数据无效。";
            return false;
        }

        List<StoryMapReferenceDocument> maps = (DraftNode.mapReferences ?? Array.Empty<StoryMapReferenceDocument>()).ToList();
        bool added = maps.All(reference => reference == null || reference.mapId != map.id);
        if (added)
        {
            maps.Add(new StoryMapReferenceDocument { mapId = map.id });
            DraftNode.mapReferences = maps.ToArray();
        }

        if (!(DraftNode.scenes ?? Array.Empty<StorySceneDocument>()).Any(scene => scene != null))
        {
            initialScene = CreateSceneInternal(map.id);
            DraftNode.scenes = new[] { initialScene };
            InsertCommand(0, new StoryCommandDocument
            {
                commandId = CreateCommandId(),
                type = "scene",
                sceneId = initialScene.id,
            });
            added = true;
        }

        if (added)
            HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool RemoveMapResource(int mapId, out string error)
    {
        if (DraftNode == null || mapId == 0)
        {
            error = "没有可删除的地图资源。";
            return false;
        }

        if ((DraftNode.scenes ?? Array.Empty<StorySceneDocument>()).Any(scene => scene != null && scene.mapId == mapId))
        {
            error = "该地图仍被当前剧情点的场景使用，请先为场景更换地图或删除场景。";
            return false;
        }

        StoryMapReferenceDocument[] updated = (DraftNode.mapReferences ?? Array.Empty<StoryMapReferenceDocument>())
            .Where(reference => reference != null && reference.mapId != mapId)
            .ToArray();
        if (updated.Length == (DraftNode.mapReferences ?? Array.Empty<StoryMapReferenceDocument>()).Length)
        {
            error = "该地图不在本剧情点的资源池中。";
            return false;
        }

        DraftNode.mapReferences = updated;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool CreateScene(int mapId, out StorySceneDocument scene, out string error)
    {
        scene = null;
        if (DraftNode == null || mapId == 0)
        {
            error = "请选择一个有效地图后再新建场景。";
            return false;
        }

        EnsureMapReference(mapId);

        scene = CreateSceneInternal(mapId);
        DraftNode.scenes = (DraftNode.scenes ?? Array.Empty<StorySceneDocument>()).Append(scene).ToArray();
        InsertCommand((DraftNode.commands ?? Array.Empty<StoryCommandDocument>()).Length, new StoryCommandDocument
        {
            commandId = CreateCommandId(),
            type = "scene",
            sceneId = scene.id,
        });
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool CreateScene(string sceneResourceId, out StorySceneDocument scene, out string error)
    {
        scene = null;
        StorySceneResourceDocument resource = DraftDocument?.GetSceneResource(sceneResourceId);
        if (DraftNode == null || resource == null || string.IsNullOrWhiteSpace(resource.backgroundResourcePath))
        {
            error = "请选择一个已配置背景的自制场景。";
            return false;
        }

        scene = CreateSceneInternal(sceneResourceId);
        DraftNode.scenes = (DraftNode.scenes ?? Array.Empty<StorySceneDocument>()).Append(scene).ToArray();
        InsertCommand((DraftNode.commands ?? Array.Empty<StoryCommandDocument>()).Length, new StoryCommandDocument
        {
            commandId = CreateCommandId(),
            type = "scene",
            sceneId = scene.id,
        });
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public List<StoryBattleOption> GetBattleOptions(string filter) => StoryBattleCatalog.GetOptions(filter);

    public bool CreateBattleCommand(string sceneId, StoryBattleReferenceDocument reference,
        out StoryCommandDocument command, out string error)
    {
        command = null;
        WorkshopStorySceneSection section = GetSceneSections().FirstOrDefault(value => value?.scene != null
            && string.Equals(value.scene.id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (section == null)
        {
            error = "请先新建并选择一个场景。";
            return false;
        }
        if (!StoryBattleCatalog.TryResolve(reference, out _, out error))
            return false;

        command = new StoryCommandDocument
        {
            commandId = CreateCommandId(),
            type = "battle",
            battle = new StoryBattleReferenceDocument
            {
                mapId = reference.mapId,
                npcId = reference.npcId,
                battleId = reference.battleId,
            },
        };
        InsertCommand(section.commandEndIndex, command);
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public List<WorkshopStorySceneSection> GetSceneSections()
    {
        EnsureSceneSections(DraftNode);
        List<WorkshopStorySceneSection> sections = new List<WorkshopStorySceneSection>();
        List<StoryCommandDocument> commands = (DraftNode?.commands ?? Array.Empty<StoryCommandDocument>()).ToList();
        for (int index = 0; index < commands.Count; index++)
        {
            StoryCommandDocument command = commands[index];
            if (command == null || !string.Equals(command.type, "scene", StringComparison.OrdinalIgnoreCase))
                continue;

            StorySceneDocument scene = DraftNode?.GetScene(command.sceneId);
            if (scene == null)
                continue;

            int endIndex = index + 1;
            while (endIndex < commands.Count && !string.Equals(commands[endIndex]?.type, "scene", StringComparison.OrdinalIgnoreCase))
                endIndex++;
            sections.Add(new WorkshopStorySceneSection
            {
                scene = scene,
                commandStartIndex = index,
                commandEndIndex = endIndex,
            });
        }
        return sections;
    }

    public List<StoryCommandDocument> GetSceneCommands(string sceneId)
    {
        WorkshopStorySceneSection section = GetSceneSections()
            .FirstOrDefault(value => value?.scene != null && string.Equals(value.scene.id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (section == null)
            return new List<StoryCommandDocument>();

        return (DraftNode.commands ?? Array.Empty<StoryCommandDocument>())
            .Skip(section.commandStartIndex + 1)
            .Take(section.contentCount)
            .Where(command => command != null)
            .ToList();
    }

    public bool RemoveSceneTextCommand(string sceneId, string commandId, out string error)
    {
        if (DraftNode?.commands == null || string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(commandId))
        {
            error = "没有可删除的剧情内容。";
            return false;
        }

        WorkshopStorySceneSection section = GetSceneSections()
            .FirstOrDefault(value => value?.scene != null && string.Equals(value.scene.id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (section == null)
        {
            error = "当前场景无效。";
            return false;
        }

        int commandIndex = -1;
        for (int index = section.commandStartIndex + 1; index < section.commandEndIndex; index++)
        {
            StoryCommandDocument command = DraftNode.commands[index];
            if (command != null && string.Equals(command.commandId, commandId, StringComparison.OrdinalIgnoreCase))
            {
                commandIndex = index;
                break;
            }
        }

        if (commandIndex < 0)
        {
            error = "找不到要删除的剧情内容。";
            return false;
        }

        StoryCommandDocument target = DraftNode.commands[commandIndex];
        if (!IsSceneContentCommand(target))
        {
            error = "这里只能删除对白、旁白或选项。";
            return false;
        }

        if (IsChoiceCommandReferenced(target))
        {
            error = "该选择内容已用于剧情点后续规则，请先在入口页的“编辑连接”中处理。";
            return false;
        }
        if (string.Equals(target.type, "battle", StringComparison.OrdinalIgnoreCase)
            && IsBattleCommandReferenced(target.commandId))
        {
            error = "该战斗结果已用于剧情点后续规则，请先在“编辑连接”中处理。";
            return false;
        }

        List<StoryCommandDocument> commands = DraftNode.commands.ToList();
        commands.RemoveAt(commandIndex);
        DraftNode.commands = commands.ToArray();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool MoveSceneTextCommand(string sceneId, string commandId, bool moveDown, out string error)
    {
        if (DraftNode?.commands == null || string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(commandId))
        {
            error = "没有可调整顺序的剧情内容。";
            return false;
        }

        WorkshopStorySceneSection section = GetSceneSections()
            .FirstOrDefault(value => value?.scene != null && string.Equals(value.scene.id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (section == null)
        {
            error = "当前场景无效。";
            return false;
        }

        int currentIndex = -1;
        for (int index = section.commandStartIndex + 1; index < section.commandEndIndex; index++)
        {
            StoryCommandDocument command = DraftNode.commands[index];
            if (command != null && string.Equals(command.commandId, commandId, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = index;
                break;
            }
        }

        if (currentIndex < 0 || !IsSceneContentCommand(DraftNode.commands[currentIndex]))
        {
            error = "找不到要调整的旁白或对白。";
            return false;
        }

        int step = moveDown ? 1 : -1;
        int targetIndex = currentIndex + step;
        while (targetIndex > section.commandStartIndex && targetIndex < section.commandEndIndex
            && !IsSceneContentCommand(DraftNode.commands[targetIndex]))
        {
            targetIndex += step;
        }

        if (targetIndex <= section.commandStartIndex || targetIndex >= section.commandEndIndex)
        {
            error = moveDown ? "该内容已经位于当前场景末尾。" : "该内容已经位于当前场景开头。";
            return false;
        }

        StoryCommandDocument current = DraftNode.commands[currentIndex];
        DraftNode.commands[currentIndex] = DraftNode.commands[targetIndex];
        DraftNode.commands[targetIndex] = current;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool ConvertSceneContentToChoice(string sceneId, string commandId, out StoryCommandDocument command, out string error)
    {
        command = null;
        if (!TryGetSceneContentCommand(sceneId, commandId, out StoryCommandDocument target, out error))
            return false;

        if (string.Equals(target.type, "choice", StringComparison.OrdinalIgnoreCase))
        {
            command = target;
            error = string.Empty;
            return true;
        }

        if (!string.Equals(target.type, "say", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target.type, "narrate", StringComparison.OrdinalIgnoreCase))
        {
            error = "只有旁白或对白可以设为选项问题。";
            return false;
        }

        string choiceId = string.IsNullOrWhiteSpace(target.choiceId) ? target.commandId + ":choice" : target.choiceId;
        target.choiceOriginalType = target.type;
        target.type = "choice";
        target.choiceId = choiceId;
        target.choices = new[]
        {
            new StoryChoiceDocument { choiceId = choiceId, optionId = choiceId + ":1", text = "选项 1" },
            new StoryChoiceDocument { choiceId = choiceId, optionId = choiceId + ":2", text = "选项 2" },
        };
        command = target;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool RestoreChoiceToSceneContent(string sceneId, string commandId, out StoryCommandDocument command, out string error)
    {
        command = null;
        if (!TryGetChoiceCommand(sceneId, commandId, out StoryCommandDocument target, out error))
            return false;

        if (IsChoiceCommandReferenced(target))
        {
            error = "该选择内容已用于剧情点后续规则，请先在入口页的“编辑连接”中处理。";
            return false;
        }

        string originalType = (target.choiceOriginalType ?? string.Empty).Trim().ToLowerInvariant();
        if (originalType != "say" && originalType != "narrate")
            originalType = string.IsNullOrWhiteSpace(target.actor) ? "narrate" : "say";

        target.type = originalType;
        target.choiceOriginalType = null;
        target.choiceId = null;
        target.choices = null;
        command = target;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool AddChoiceOption(string sceneId, string commandId, out StoryChoiceDocument option, out string error)
    {
        option = null;
        if (!TryGetChoiceCommand(sceneId, commandId, out StoryCommandDocument command, out error))
            return false;

        List<StoryChoiceDocument> options = (command.choices ?? Array.Empty<StoryChoiceDocument>())
            .Where(value => value != null).ToList();
        string choiceId = string.IsNullOrWhiteSpace(command.choiceId) ? command.commandId + ":choice" : command.choiceId;
        command.choiceId = choiceId;
        int index = options.Count + 1;
        string optionId = choiceId + ":" + index;
        while (options.Any(value => string.Equals(value.optionId, optionId, StringComparison.OrdinalIgnoreCase)))
            optionId = choiceId + ":" + (++index);

        option = new StoryChoiceDocument { choiceId = choiceId, optionId = optionId, text = "选项 " + index };
        options.Add(option);
        command.choices = options.ToArray();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool UpdateChoiceOptionText(string sceneId, string commandId, string optionId, string text, out string error)
    {
        if (!TryGetChoiceOption(sceneId, commandId, optionId, out StoryChoiceDocument option, out error))
            return false;

        option.text = text ?? string.Empty;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool MoveChoiceOption(string sceneId, string commandId, string optionId, bool moveDown, out string error)
    {
        if (!TryGetChoiceCommand(sceneId, commandId, out StoryCommandDocument command, out error))
            return false;

        List<StoryChoiceDocument> options = (command.choices ?? Array.Empty<StoryChoiceDocument>())
            .Where(value => value != null).ToList();
        int currentIndex = options.FindIndex(value => string.Equals(value.optionId, optionId, StringComparison.OrdinalIgnoreCase));
        int targetIndex = currentIndex + (moveDown ? 1 : -1);
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= options.Count)
        {
            error = moveDown ? "该选项已经位于末尾。" : "该选项已经位于开头。";
            return false;
        }

        StoryChoiceDocument current = options[currentIndex];
        options[currentIndex] = options[targetIndex];
        options[targetIndex] = current;
        command.choices = options.ToArray();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool RemoveChoiceOption(string sceneId, string commandId, string optionId, out string error)
    {
        if (!TryGetChoiceCommand(sceneId, commandId, out StoryCommandDocument command, out error))
            return false;

        if (IsChoiceOptionReferenced(optionId))
        {
            error = "该选项已用于剧情点后续规则，请先在入口页的“编辑连接”中处理。";
            return false;
        }

        List<StoryChoiceDocument> options = (command.choices ?? Array.Empty<StoryChoiceDocument>())
            .Where(value => value != null).ToList();
        if (options.Count <= 2)
        {
            error = "选项问题至少需要保留两个选项。";
            return false;
        }

        int index = options.FindIndex(value => string.Equals(value.optionId, optionId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            error = "找不到要删除的选项。";
            return false;
        }

        options.RemoveAt(index);
        command.choices = options.ToArray();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool RemoveScene(string sceneId, bool removeSectionContent, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        if (scene == null)
        {
            error = "找不到要删除的场景。";
            return false;
        }

        WorkshopStorySceneSection section = GetSceneSections()
            .FirstOrDefault(value => value?.scene != null && string.Equals(value.scene.id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (section != null && section.contentCount > 0 && !removeSectionContent)
        {
            error = "该场景包含 " + section.contentCount + " 条剧情内容，需要确认后一起删除。";
            return false;
        }

        if (section != null && (DraftNode.commands ?? Array.Empty<StoryCommandDocument>())
            .Skip(section.commandStartIndex + 1)
            .Take(section.contentCount)
            .Any(IsChoiceCommandReferenced))
        {
            error = "该场景包含已用于剧情点后续规则的选择内容，请先在入口页的“编辑连接”中处理。";
            return false;
        }

        List<StoryCommandDocument> commands = (DraftNode.commands ?? Array.Empty<StoryCommandDocument>()).ToList();
        if (section != null)
            commands.RemoveRange(section.commandStartIndex, section.commandEndIndex - section.commandStartIndex);

        DraftNode.scenes = (DraftNode.scenes ?? Array.Empty<StorySceneDocument>())
            .Where(value => value != null && !string.Equals(value.id, sceneId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        DraftNode.commands = commands.ToArray();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool SetSceneMap(string sceneId, int mapId, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        if (scene == null || mapId == 0)
        {
            error = "场景或地图无效。";
            return false;
        }

        if (scene.mapId != mapId)
        {
            scene.mapId = mapId;
            scene.sceneResourceId = null;
            EnsureMapReference(mapId);
            HasUnsavedChanges = true;
        }
        error = string.Empty;
        return true;
    }

    public bool SetSceneResource(string sceneId, string sceneResourceId, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        StorySceneResourceDocument resource = DraftDocument?.GetSceneResource(sceneResourceId);
        if (scene == null || resource == null || string.IsNullOrWhiteSpace(resource.backgroundResourcePath))
        {
            error = "场景或自制场景资源无效。";
            return false;
        }

        scene.mapId = 0;
        scene.sceneResourceId = resource.id;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool SetSceneTransition(string sceneId, string type, float duration, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        if (scene == null)
        {
            error = "当前场景无效。";
            return false;
        }

        StoryTransitionDocument value = new StoryTransitionDocument { type = type, duration = duration };
        string normalizedType = value.normalizedType;
        if (normalizedType == "inherit" || normalizedType != (type ?? string.Empty).Trim().ToLowerInvariant())
        {
            error = "请选择有效的场景转场效果。";
            return false;
        }

        value.type = normalizedType;
        value.duration = value.normalizedDuration;
        scene.transition = value;
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool AddScene(Map map, out StorySceneDocument scene, out string error)
    {
        scene = null;
        if (DraftNode == null)
        {
            error = "当前没有可编辑的剧情点草稿。";
            return false;
        }

        if (map == null || map.id == 0)
        {
            error = "地图数据无效。";
            return false;
        }

        scene = (DraftNode.scenes ?? Array.Empty<StorySceneDocument>())
            .FirstOrDefault(value => value != null && value.mapId == map.id);
        if (scene != null)
        {
            error = string.Empty;
            return true;
        }

        scene = new StorySceneDocument
        {
            id = CreateUniqueSceneId(map.id),
            mapId = map.id,
            actors = Array.Empty<StorySceneActorLayoutDocument>(),
        };
        DraftNode.scenes = (DraftNode.scenes ?? Array.Empty<StorySceneDocument>()).Append(scene).ToArray();

        if (!(DraftNode.commands ?? Array.Empty<StoryCommandDocument>())
            .Any(command => command != null && string.Equals(command.type, "scene", StringComparison.OrdinalIgnoreCase)))
        {
            InsertCommand(0, new StoryCommandDocument
            {
                commandId = CreateCommandId(),
                type = "scene",
                sceneId = scene.id,
            });
        }

        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 以精灵 ID 创建（或复用）剧本角色，并加入当前剧情点和指定场景。
    /// 角色资源路径统一从 PetUIInfo 的默认皮肤推导，避免作者手写头像、立绘和名称。
    /// </summary>
    public bool AddPetActor(int petId, string sceneId, out StoryActorDocument actor, out string error)
    {
        actor = null;
        if (DraftDocument == null || DraftNode == null)
        {
            error = "当前没有可编辑的剧情点草稿。";
            return false;
        }

        StorySceneDocument scene = DraftNode.GetScene(sceneId);
        if (scene == null)
        {
            error = "请先选择本剧情点要使用的地图。";
            return false;
        }

        PetInfo pet = Database.instance?.GetPetInfo(petId);
        if (pet == null)
        {
            error = "找不到精灵 ID：" + petId;
            return false;
        }

        string actorId = "pet_" + pet.id;
        actor = DraftDocument.GetActor(actorId);
        int skinId = pet.ui == null || pet.ui.defaultSkinId == 0 ? pet.id : pet.ui.defaultSkinId;
        if (actor == null)
        {
            string stageSpritePath = GetPreferredPetStageSpritePath(pet, skinId);
            string resourcePrefix = PetInfo.IsMod(skinId) ? "Mod/" : string.Empty;
            actor = new StoryActorDocument
            {
                id = actorId,
                actorType = "pet",
                petId = pet.id.ToString(),
                name = pet.name,
                sprite = stageSpritePath,
                icon = resourcePrefix + "Pets/icon/" + skinId,
                battleSprite = resourcePrefix + "Pets/battle/" + skinId,
            };
            DraftDocument.actors = (DraftDocument.actors ?? Array.Empty<StoryActorDocument>()).Append(actor).ToArray();
        }

        StoryActorDocument resolvedActor = actor;
        string selectedActorId = resolvedActor.id;
        if (!(DraftNode.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
            .Any(reference => reference != null && string.Equals(reference.actorId, selectedActorId, StringComparison.OrdinalIgnoreCase)))
        {
            DraftNode.actorReferences = (DraftNode.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
                .Append(new StoryActorReferenceDocument { actorId = selectedActorId })
                .ToArray();
        }

        StorySceneActorLayoutDocument placement = scene.GetActorLayout(selectedActorId);
        if (placement == null)
        {
            StorySceneActorLayoutDocument[] layouts = scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>();
            int leftCount = layouts.Count(value => value != null && value.normalizedPlacementMode == "auto" && value.normalizedSide == "left");
            int rightCount = layouts.Count(value => value != null && value.normalizedPlacementMode == "auto" && value.normalizedSide == "right");
            string side = leftCount <= rightCount ? "left" : "right";
            int order = layouts.Count(value => value != null && value.normalizedPlacementMode == "auto" && value.normalizedSide == side);
            bool faceLeft = side == "right";
            placement = new StorySceneActorLayoutDocument
            {
                actorId = selectedActorId,
                placementMode = "auto",
                side = side,
                order = order,
                scale = 1f,
                faceLeft = faceLeft,
                flipIcon = side == "right",
            };
            scene.actors = layouts.Append(placement).ToArray();
            NormalizeAutoLayoutOrders(scene);
        }

        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool AddPetActor(int petId, out StoryActorDocument actor, out string error)
    {
        actor = null;
        if (DraftDocument == null || DraftNode == null)
        {
            error = "当前没有可编辑的剧情点草稿。";
            return false;
        }

        PetInfo pet = Database.instance?.GetPetInfo(petId);
        if (pet == null)
        {
            error = "找不到精灵 ID：" + petId;
            return false;
        }

        string actorId = "pet_" + pet.id;
        actor = DraftDocument.GetActor(actorId);
        if (actor == null)
        {
            int skinId = pet.ui == null || pet.ui.defaultSkinId == 0 ? pet.id : pet.ui.defaultSkinId;
            string stageSpritePath = GetPreferredPetStageSpritePath(pet, skinId);
            string resourcePrefix = PetInfo.IsMod(skinId) ? "Mod/" : string.Empty;
            actor = new StoryActorDocument
            {
                id = actorId,
                actorType = "pet",
                petId = pet.id.ToString(),
                name = pet.name,
                sprite = stageSpritePath,
                icon = resourcePrefix + "Pets/icon/" + skinId,
                battleSprite = resourcePrefix + "Pets/battle/" + skinId,
            };
            DraftDocument.actors = (DraftDocument.actors ?? Array.Empty<StoryActorDocument>()).Append(actor).ToArray();
        }

        string resolvedActorId = actor.id;
        if (!(DraftNode.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
            .Any(reference => reference != null && string.Equals(reference.actorId, resolvedActorId, StringComparison.OrdinalIgnoreCase)))
        {
            DraftNode.actorReferences = (DraftNode.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
                .Append(new StoryActorReferenceDocument { actorId = resolvedActorId })
                .ToArray();
            HasUnsavedChanges = true;
        }

        error = string.Empty;
        return true;
    }

    public bool AddStoryActor(string actorId, string sceneId, out StoryActorDocument actor, out string error)
    {
        actor = DraftDocument?.GetActor(actorId);
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        if (actor == null || scene == null)
        {
            error = "剧本角色或当前场景无效。";
            return false;
        }

        string resolvedActorId = actor.id;
        if (!(DraftNode.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
            .Any(reference => reference != null && string.Equals(reference.actorId, resolvedActorId, StringComparison.OrdinalIgnoreCase)))
        {
            DraftNode.actorReferences = (DraftNode.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
                .Append(new StoryActorReferenceDocument { actorId = resolvedActorId })
                .ToArray();
        }

        if (scene.GetActorLayout(resolvedActorId) == null)
        {
            scene.actors = (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
                .Append(CreateDefaultActorLayout(scene, resolvedActorId))
                .ToArray();
            NormalizeAutoLayoutOrders(scene);
        }

        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    private static string GetPreferredPetStageSpritePath(PetInfo pet, int skinId)
    {
        string resourcePrefix = PetInfo.IsMod(skinId) ? "Mod/" : string.Empty;
        string idlePath = resourcePrefix + "Pets/pet/" + skinId;
        Sprite idleSprite = pet?.ui?.idleImage;
        if (idleSprite != null)
            return idlePath;

        string battlePath = resourcePrefix + "Pets/battle/" + skinId;
        Sprite battleSprite = pet?.ui?.battleImage;
        return battleSprite != null ? battlePath : idlePath;
    }

    public bool SetActorVisible(string actorId, string sceneId, bool visible, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        if (scene == null || DraftDocument?.GetActor(actorId) == null || !(DraftNode.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
            .Any(reference => reference != null && string.Equals(reference.actorId, actorId, StringComparison.OrdinalIgnoreCase)))
        {
            error = "角色或场景无效。";
            return false;
        }

        StorySceneActorLayoutDocument placement = scene.GetActorLayout(actorId);
        if (visible && placement == null)
        {
            scene.actors = (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
                .Append(CreateDefaultActorLayout(scene, actorId))
                .ToArray();
            HasUnsavedChanges = true;
        }
        else if (!visible && placement != null)
        {
            scene.actors = (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
                .Where(value => value != null && !string.Equals(value.actorId, actorId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            HasUnsavedChanges = true;
        }

        error = string.Empty;
        return true;
    }

    public bool RemovePointActor(string actorId, out string error)
    {
        if (DraftNode == null || string.IsNullOrWhiteSpace(actorId))
        {
            error = "没有可删除的角色资源。";
            return false;
        }

        if ((DraftNode.commands ?? Array.Empty<StoryCommandDocument>()).Any(command => command != null
            && string.Equals(command.actor, actorId, StringComparison.OrdinalIgnoreCase)))
        {
            error = "该精灵已被对白或显示命令引用，请先删除或改绑相关命令。";
            return false;
        }

        foreach (StorySceneDocument scene in DraftNode.scenes ?? Array.Empty<StorySceneDocument>())
        {
            if (scene == null)
                continue;

            scene.actors = (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
                .Where(layout => layout != null && !string.Equals(layout.actorId, actorId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        DraftNode.actorReferences = (DraftNode.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
            .Where(reference => reference != null && !string.Equals(reference.actorId, actorId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public List<WorkshopStoryPointActorOption> GetVisibleActorOptions(string sceneId)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        if (scene == null)
            return new List<WorkshopStoryPointActorOption>();

        return (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
            .Where(layout => layout != null && !string.IsNullOrWhiteSpace(layout.actorId))
            .Select(layout => DraftDocument?.GetActor(layout.actorId))
            .Where(actor => actor != null)
            .Select(actor => new WorkshopStoryPointActorOption { actorId = actor.id, name = actor.displayName })
            .ToList();
    }

    public bool SetSceneActorSide(string sceneId, string actorId, string side, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        StorySceneActorLayoutDocument layout = scene?.GetActorLayout(actorId);
        if (scene == null || layout == null)
        {
            error = "请先将该精灵加入当前场景。";
            return false;
        }

        string normalizedSide = string.Equals(side, "right", StringComparison.OrdinalIgnoreCase) ? "right" : "left";
        if (layout.normalizedSide == normalizedSide && layout.normalizedPlacementMode == "auto")
        {
            error = string.Empty;
            return true;
        }

        int order = (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
            .Count(value => value != null && !string.Equals(value.actorId, actorId, StringComparison.OrdinalIgnoreCase)
                && value.normalizedPlacementMode == "auto" && value.normalizedSide == normalizedSide);
        layout.placementMode = "auto";
        layout.side = normalizedSide;
        layout.order = order;
        layout.faceLeft = normalizedSide == "right";
        layout.flipIcon = normalizedSide == "right";
        NormalizeAutoLayoutOrders(scene);
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool ResetSceneActorLayout(string sceneId, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        if (scene == null)
        {
            error = "当前场景无效。";
            return false;
        }

        List<StorySceneActorLayoutDocument> layouts = (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
            .Where(value => value != null && !string.IsNullOrWhiteSpace(value.actorId)).ToList();
        int leftOrder = 0;
        int rightOrder = 0;
        for (int index = 0; index < layouts.Count; index++)
        {
            StorySceneActorLayoutDocument layout = layouts[index];
            bool useLeft = leftOrder <= rightOrder;
            layout.placementMode = "auto";
            layout.side = useLeft ? "left" : "right";
            layout.order = useLeft ? leftOrder++ : rightOrder++;
            layout.faceLeft = !useLeft;
            layout.flipIcon = !useLeft;
        }

        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool MoveSceneActorDepth(string sceneId, string actorId, bool outward, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        StorySceneActorLayoutDocument layout = scene?.GetActorLayout(actorId);
        if (scene == null || layout == null || layout.normalizedPlacementMode != "auto")
        {
            error = "请先将该角色加入当前场景的自动布局。";
            return false;
        }

        List<StorySceneActorLayoutDocument> sideLayouts = (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
            .Where(value => value != null && value.normalizedPlacementMode == "auto"
                && value.normalizedSide == layout.normalizedSide)
            .OrderBy(value => value.order)
            .ThenBy(value => value.actorId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int currentIndex = sideLayouts.IndexOf(layout);
        int targetIndex = currentIndex + (outward ? 1 : -1);
        if (targetIndex < 0 || targetIndex >= sideLayouts.Count)
        {
            error = outward ? "该角色已经位于本侧最外层。" : "该角色已经位于本侧最内层。";
            return false;
        }

        StorySceneActorLayoutDocument other = sideLayouts[targetIndex];
        sideLayouts[targetIndex] = layout;
        sideLayouts[currentIndex] = other;
        for (int index = 0; index < sideLayouts.Count; index++)
            sideLayouts[index].order = index;

        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public bool ToggleSceneAutoLayoutMode(string sceneId, out string error)
    {
        StorySceneDocument scene = DraftNode?.GetScene(sceneId);
        if (scene == null)
        {
            error = "当前场景无效。";
            return false;
        }

        if (scene.layout == null)
            scene.layout = new StoryLayoutDocument();
        scene.layout.autoLayoutMode = string.Equals(scene.layout.autoLayoutMode, "bottomAligned", StringComparison.OrdinalIgnoreCase)
            ? "invertedV"
            : "bottomAligned";
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    public List<WorkshopStoryPointActorOption> GetCurrentActorOptions()
    {
        return (DraftNode?.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
            .Where(reference => reference != null && !string.IsNullOrWhiteSpace(reference.actorId))
            .Select(reference => DraftDocument?.GetActor(reference.actorId))
            .Where(actor => actor != null)
            .Select(actor => new WorkshopStoryPointActorOption
            {
                actorId = actor.id,
                name = actor.displayName,
            })
            .ToList();
    }

    public bool CreateNarrationCommand(string sceneId, out StoryCommandDocument command, out string error)
    {
        return CreateTextCommand("narrate", null, sceneId, out command, out error);
    }

    public bool CreateNarrationCommand(out StoryCommandDocument command, out string error)
    {
        return CreateNarrationCommand(GetSceneSections().FirstOrDefault()?.scene?.id, out command, out error);
    }

    public bool CreateSayCommand(string actorId, out StoryCommandDocument command, out string error)
    {
        if (DraftDocument?.GetActor(actorId) == null || !(DraftNode?.actorReferences ?? Array.Empty<StoryActorReferenceDocument>())
                .Any(reference => reference != null && string.Equals(reference.actorId, actorId, StringComparison.OrdinalIgnoreCase)))
        {
            command = null;
            error = "对白角色必须先加入本剧情点。";
            return false;
        }

        return CreateTextCommand("say", actorId, null, out command, out error);
    }

    public bool RemoveScene(string sceneId, out string error)
    {
        return RemoveScene(sceneId, false, out error);
    }

    public bool CreateSayCommand(string actorId, string sceneId, out StoryCommandDocument command, out string error)
    {
        if (DraftNode?.GetScene(sceneId)?.GetActorLayout(actorId) == null)
        {
            command = null;
            error = "对白角色必须先显示在当前场景中。";
            return false;
        }

        return CreateTextCommand("say", actorId, sceneId, out command, out error);
    }

    /// <summary>
    /// 空剧情点在画布中始终提供一个“旁白输入槽”。只有作者第一次实际输入时，
    /// 才将该槽落为真正的 narrate 命令，保持新建节点的数据本身为空。
    /// </summary>
    public bool EnsureNarrationCommand(out StoryCommandDocument command, out string error)
    {
        return CreateNarrationCommand(GetSceneSections().FirstOrDefault()?.scene?.id, out command, out error);
    }

    public bool Save(out string error)
    {
        if (DraftDocument == null || string.IsNullOrWhiteSpace(StoryPath))
        {
            error = "当前没有可保存的剧情点草稿。";
            return false;
        }

        if (!repository.TrySaveDraft(StoryPath, DraftDocument, out error))
            return false;

        HasUnsavedChanges = false;
        return true;
    }

    private static void EnsureCommandIds(StoryDocument document)
    {
        foreach (StoryNodeDocument node in document?.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node?.commands == null)
                continue;

            for (int index = 0; index < node.commands.Length; index++)
            {
                StoryCommandDocument command = node.commands[index];
                if (command != null && string.IsNullOrWhiteSpace(command.commandId))
                    command.commandId = node.id + ":" + index;
            }
        }
    }

    private static void AddMapOption(Dictionary<int, WorkshopStoryPointResourceOption> options, Map map)
    {
        if (map == null || map.id == 0 || options.ContainsKey(map.id))
            return;

        options[map.id] = new WorkshopStoryPointResourceOption
        {
            id = map.id,
            name = map.name,
            isMod = Map.IsMod(map.id),
        };
    }

    private static List<WorkshopStoryPointResourceOption> FilterOptions(IEnumerable<WorkshopStoryPointResourceOption> options, string filter)
    {
        string query = (filter ?? string.Empty).Trim();
        return options
            .Where(option => option != null && (string.IsNullOrEmpty(query)
                || option.id.ToString().Contains(query)
                || (!string.IsNullOrEmpty(option.name) && option.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
            .OrderBy(option => option.isMod ? 0 : 1)
            .ThenBy(option => option.id)
            .ToList();
    }

    private bool CreateTextCommand(string type, string actorId, string sceneId, out StoryCommandDocument command, out string error)
    {
        command = null;
        if (DraftNode == null)
        {
            error = "当前没有可编辑的剧情点草稿。";
            return false;
        }

        WorkshopStorySceneSection section = GetSceneSections().FirstOrDefault(value => value?.scene != null
            && (string.IsNullOrWhiteSpace(sceneId) || string.Equals(value.scene.id, sceneId, StringComparison.OrdinalIgnoreCase)));
        if (section == null)
        {
            error = "请先新建场景并选择地图。";
            return false;
        }

        command = new StoryCommandDocument
        {
            commandId = CreateCommandId(),
            type = type,
            actor = actorId,
            text = string.Empty,
        };
        InsertCommand(section.commandEndIndex, command);
        HasUnsavedChanges = true;
        error = string.Empty;
        return true;
    }

    private bool IsMapAvailable(int mapId)
    {
        return mapId != 0 && (DraftNode?.mapReferences ?? Array.Empty<StoryMapReferenceDocument>())
            .Any(reference => reference != null && reference.mapId == mapId);
    }

    // 地图资源池只作为序列化后的依赖索引保留；作者通过“场景背景”选择地图时自动维护它。
    private void EnsureMapReference(int mapId)
    {
        if (DraftNode == null || mapId == 0 || IsMapAvailable(mapId))
            return;

        DraftNode.mapReferences = (DraftNode.mapReferences ?? Array.Empty<StoryMapReferenceDocument>())
            .Where(reference => reference != null && reference.mapId != 0)
            .Append(new StoryMapReferenceDocument { mapId = mapId })
            .ToArray();
    }

    private StorySceneDocument CreateSceneInternal(int mapId)
    {
        return new StorySceneDocument
        {
            id = CreateUniqueSceneId(mapId),
            mapId = mapId,
            actors = Array.Empty<StorySceneActorLayoutDocument>(),
        };
    }

    private StorySceneDocument CreateSceneInternal(string sceneResourceId)
    {
        return new StorySceneDocument
        {
            id = CreateUniqueSceneId(sceneResourceId),
            sceneResourceId = sceneResourceId,
            actors = Array.Empty<StorySceneActorLayoutDocument>(),
        };
    }

    private static StorySceneActorLayoutDocument CreateDefaultActorLayout(StorySceneDocument scene, string actorId)
    {
        StorySceneActorLayoutDocument[] layouts = scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>();
        int leftCount = layouts.Count(value => value != null && value.normalizedPlacementMode == "auto" && value.normalizedSide == "left");
        int rightCount = layouts.Count(value => value != null && value.normalizedPlacementMode == "auto" && value.normalizedSide == "right");
        string side = leftCount <= rightCount ? "left" : "right";
        return new StorySceneActorLayoutDocument
        {
            actorId = actorId,
            placementMode = "auto",
            side = side,
            order = layouts.Count(value => value != null && value.normalizedSide == side),
            scale = 1f,
            faceLeft = side == "right",
            flipIcon = side == "right",
        };
    }

    private static void NormalizeAutoLayoutOrders(StorySceneDocument scene)
    {
        if (scene?.actors == null)
            return;

        foreach (string side in new[] { "left", "right" })
        {
            List<StorySceneActorLayoutDocument> layouts = scene.actors
                .Where(value => value != null && value.normalizedPlacementMode == "auto" && value.normalizedSide == side)
                .OrderBy(value => value.order)
                .ThenBy(value => value.actorId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (int index = 0; index < layouts.Count; index++)
                layouts[index].order = index;
        }
    }

    private bool TryGetSceneContentCommand(string sceneId, string commandId, out StoryCommandDocument command, out string error)
    {
        command = null;
        if (DraftNode?.commands == null || string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(commandId))
        {
            error = "没有可编辑的剧情内容。";
            return false;
        }

        WorkshopStorySceneSection section = GetSceneSections().FirstOrDefault(value => value?.scene != null
            && string.Equals(value.scene.id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (section == null)
        {
            error = "当前场景无效。";
            return false;
        }

        for (int index = section.commandStartIndex + 1; index < section.commandEndIndex; index++)
        {
            StoryCommandDocument candidate = DraftNode.commands[index];
            if (candidate != null && string.Equals(candidate.commandId, commandId, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsSceneContentCommand(candidate))
                {
                    error = "当前命令不是可编辑的场景内容。";
                    return false;
                }

                command = candidate;
                error = string.Empty;
                return true;
            }
        }

        error = "找不到当前场景中的剧情内容。";
        return false;
    }

    private bool TryGetChoiceCommand(string sceneId, string commandId, out StoryCommandDocument command, out string error)
    {
        if (!TryGetSceneContentCommand(sceneId, commandId, out command, out error))
            return false;

        if (!string.Equals(command.type, "choice", StringComparison.OrdinalIgnoreCase))
        {
            error = "请先将当前旁白或对白设为选项问题。";
            return false;
        }

        return true;
    }

    private bool TryGetChoiceOption(string sceneId, string commandId, string optionId,
        out StoryChoiceDocument option, out string error)
    {
        option = null;
        if (!TryGetChoiceCommand(sceneId, commandId, out StoryCommandDocument command, out error))
            return false;

        option = (command.choices ?? Array.Empty<StoryChoiceDocument>()).FirstOrDefault(value => value != null
            && string.Equals(value.optionId, optionId, StringComparison.OrdinalIgnoreCase));
        if (option == null)
        {
            error = "找不到该选项。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsSceneContentCommand(StoryCommandDocument command)
    {
        return command != null && (string.Equals(command.type, "say", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command.type, "narrate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command.type, "choice", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command.type, "battle", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsChoiceCommandReferenced(StoryCommandDocument command)
    {
        if (command == null || !string.Equals(command.type, "choice", StringComparison.OrdinalIgnoreCase))
            return false;

        return (command.choices ?? Array.Empty<StoryChoiceDocument>())
            .Any(option => option != null && IsChoiceOptionReferenced(option.optionId));
    }

    private bool IsChoiceOptionReferenced(string optionId)
    {
        if (string.IsNullOrWhiteSpace(optionId))
            return false;

        foreach (StoryNodeDocument node in DraftDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            foreach (StoryNodeTransitionDocument transition in node?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            {
                if (ConditionReferencesOption(transition?.condition, optionId))
                    return true;
            }

            foreach (StoryCommandDocument command in node?.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (ConditionReferencesOption(command?.condition, optionId)
                    || ConditionReferencesOption(command?.displayCondition, optionId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ConditionReferencesOption(ConditionGroupDocument group, string optionId)
    {
        foreach (StoryConditionClauseDocument clause in group?.clauses ?? Array.Empty<StoryConditionClauseDocument>())
        {
            if ((clause?.conditions ?? Array.Empty<StoryConditionDocument>())
                .Any(condition => condition != null
                    && string.Equals(condition.optionId, optionId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return (group?.conditions ?? Array.Empty<StoryConditionDocument>())
            .Any(condition => condition != null
                && string.Equals(condition.optionId, optionId, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsurePointResources(StoryDocument document)
    {
        foreach (StoryNodeDocument node in document?.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node == null)
                continue;

            List<StoryMapReferenceDocument> maps = (node.mapReferences ?? Array.Empty<StoryMapReferenceDocument>())
                .Where(reference => reference != null && reference.mapId != 0)
                .ToList();
            foreach (StorySceneDocument scene in node.scenes ?? Array.Empty<StorySceneDocument>())
            {
                if (scene != null && scene.mapId != 0 && maps.All(reference => reference.mapId != scene.mapId))
                    maps.Add(new StoryMapReferenceDocument { mapId = scene.mapId });
            }
            node.mapReferences = maps.ToArray();
        }
    }

    private bool EnsureSceneSections(StoryNodeDocument node)
    {
        if (node == null)
            return false;

        List<StorySceneDocument> scenes = (node.scenes ?? Array.Empty<StorySceneDocument>())
            .Where(scene => scene != null && !string.IsNullOrWhiteSpace(scene.id))
            .ToList();
        if (scenes.Count == 0)
            return false;

        List<StoryCommandDocument> commands = (node.commands ?? Array.Empty<StoryCommandDocument>()).ToList();
        bool changed = false;
        if (!commands.Any(command => command != null && string.Equals(command.type, "scene", StringComparison.OrdinalIgnoreCase)))
        {
            commands.Insert(0, new StoryCommandDocument
            {
                commandId = CreateCommandIdFor(node, commands),
                type = "scene",
                sceneId = scenes[0].id,
            });
            changed = true;
        }

        foreach (StorySceneDocument scene in scenes)
        {
            if (commands.Any(command => command != null && string.Equals(command.type, "scene", StringComparison.OrdinalIgnoreCase)
                && string.Equals(command.sceneId, scene.id, StringComparison.OrdinalIgnoreCase)))
                continue;

            commands.Add(new StoryCommandDocument
            {
                commandId = CreateCommandIdFor(node, commands),
                type = "scene",
                sceneId = scene.id,
            });
            changed = true;
        }

        if (changed)
            node.commands = commands.ToArray();
        return changed;
    }

    private static string CreateCommandIdFor(StoryNodeDocument node, IEnumerable<StoryCommandDocument> commands)
    {
        int index = commands?.Count() ?? 0;
        string id = node.id + ":" + index;
        while ((commands ?? Enumerable.Empty<StoryCommandDocument>()).Any(command => command != null
            && string.Equals(command.commandId, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = node.id + ":" + (++index);
        }
        return id;
    }

    private string CreateUniqueSceneId(int mapId)
    {
        string baseId = "scene_" + mapId.ToString().Replace('-', 'm');
        string sceneId = baseId;
        int suffix = 2;
        while (DraftNode.GetScene(sceneId) != null)
            sceneId = baseId + "_" + suffix++;
        return sceneId;
    }

    private bool IsBattleCommandReferenced(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            return false;
        foreach (StoryNodeDocument node in DraftDocument?.nodes ?? Array.Empty<StoryNodeDocument>())
        foreach (StoryNodeTransitionDocument transition in node?.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
        foreach (StoryConditionDocument condition in EnumerateConditions(transition?.condition))
        {
            if (condition != null
                && string.Equals(condition.type, "battleResult", StringComparison.OrdinalIgnoreCase)
                && string.Equals(condition.commandId, commandId, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IEnumerable<StoryConditionDocument> EnumerateConditions(ConditionGroupDocument group)
    {
        if (group?.clauses != null && group.clauses.Length > 0)
            return group.clauses.SelectMany(clause => clause?.conditions ?? Array.Empty<StoryConditionDocument>());
        return group?.conditions ?? Array.Empty<StoryConditionDocument>();
    }

    private string CreateUniqueSceneId(string sceneResourceId)
    {
        string safeId = new string((sceneResourceId ?? "custom")
            .Select(value => char.IsLetterOrDigit(value) || value == '_' || value == '-' ? value : '_')
            .ToArray());
        string baseId = "scene_" + (string.IsNullOrWhiteSpace(safeId) ? "custom" : safeId);
        string sceneId = baseId;
        int suffix = 2;
        while (DraftNode.GetScene(sceneId) != null)
            sceneId = baseId + "_" + suffix++;
        return sceneId;
    }

    private string CreateCommandId()
    {
        int index = (DraftNode.commands ?? Array.Empty<StoryCommandDocument>()).Length;
        string commandId = DraftNode.id + ":" + index;
        while ((DraftNode.commands ?? Array.Empty<StoryCommandDocument>())
            .Any(command => command != null && string.Equals(command.commandId, commandId, StringComparison.OrdinalIgnoreCase)))
        {
            commandId = DraftNode.id + ":" + (++index);
        }
        return commandId;
    }

    private void InsertCommand(int index, StoryCommandDocument command)
    {
        List<StoryCommandDocument> commands = (DraftNode.commands ?? Array.Empty<StoryCommandDocument>()).ToList();
        commands.Insert(Mathf.Clamp(index, 0, commands.Count), command);
        DraftNode.commands = commands.ToArray();
    }

}
