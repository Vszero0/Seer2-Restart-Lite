using System;
using System.Collections.Generic;
using System.Linq;
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

    public string displayName => string.IsNullOrWhiteSpace(name)
        ? id.ToString()
        : name + "  " + id;
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
/// 编辑器始终操作剧本文件的深拷贝，只有显式保存才会写回 Mod/Stories。
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

    /// <summary>
    /// 剧情点的地图候选来自本体 XML 与当前已载入地图；未在列表中的 Mod 地图由编辑器按 ID 载入。
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
        if (!string.Equals(target.type, "say", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target.type, "narrate", StringComparison.OrdinalIgnoreCase))
        {
            error = "这里只能删除对白或旁白。";
            return false;
        }

        List<StoryCommandDocument> commands = DraftNode.commands.ToList();
        commands.RemoveAt(commandIndex);
        DraftNode.commands = commands.ToArray();
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
            EnsureMapReference(mapId);
            HasUnsavedChanges = true;
        }
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
            actor = new StoryActorDocument
            {
                id = actorId,
                actorType = "pet",
                petId = pet.id.ToString(),
                name = pet.name,
                sprite = "Pets/pet/" + skinId,
                icon = "Pets/icon/" + skinId,
                battleSprite = "Pets/battle/" + skinId,
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
            int leftCount = layouts.Count(value => value != null && value.normalizedSide == "left");
            int rightCount = layouts.Count(value => value != null && value.normalizedSide == "right");
            string side = leftCount <= rightCount ? "left" : "right";
            int order = layouts.Count(value => value != null && value.normalizedSide == side);
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
        }

        EnsureOpeningShowCommand(scene.id, selectedActorId);
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
            actor = new StoryActorDocument
            {
                id = actorId,
                actorType = "pet",
                petId = pet.id.ToString(),
                name = pet.name,
                sprite = "Pets/pet/" + skinId,
                icon = "Pets/icon/" + skinId,
                battleSprite = "Pets/battle/" + skinId,
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
            error = "没有可删除的精灵资源。";
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
        int order = (scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
            .Where(value => value != null && !string.Equals(value.actorId, actorId, StringComparison.OrdinalIgnoreCase)
                && value.normalizedPlacementMode == "auto" && value.normalizedSide == normalizedSide)
            .Count();
        layout.placementMode = "auto";
        layout.side = normalizedSide;
        layout.order = order;
        layout.faceLeft = normalizedSide == "right";
        layout.flipIcon = normalizedSide == "right";
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

        if (!repository.TrySave(StoryPath, DraftDocument, out error))
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
            error = "请先点击背景选择本剧情点的地图。";
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

    private static StorySceneActorLayoutDocument CreateDefaultActorLayout(StorySceneDocument scene, string actorId)
    {
        StorySceneActorLayoutDocument[] layouts = scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>();
        int leftCount = layouts.Count(value => value != null && value.normalizedSide == "left");
        int rightCount = layouts.Count(value => value != null && value.normalizedSide == "right");
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

    private void EnsureOpeningShowCommand(string sceneId, string actorId)
    {
        List<StoryCommandDocument> commands = (DraftNode.commands ?? Array.Empty<StoryCommandDocument>()).ToList();
        int sceneIndex = commands.FindIndex(command => command != null
            && string.Equals(command.type, "scene", StringComparison.OrdinalIgnoreCase)
            && string.Equals(command.sceneId, sceneId, StringComparison.OrdinalIgnoreCase));
        if (sceneIndex < 0)
        {
            return;
        }

        int insertIndex = sceneIndex + 1;
        while (insertIndex < commands.Count && string.Equals(commands[insertIndex]?.type, "show", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(commands[insertIndex].actor, actorId, StringComparison.OrdinalIgnoreCase))
                return;
            insertIndex++;
        }
        commands.Insert(insertIndex, new StoryCommandDocument
        {
            commandId = CreateCommandId(),
            type = "show",
            actor = actorId,
        });
        DraftNode.commands = commands.ToArray();
    }
}
