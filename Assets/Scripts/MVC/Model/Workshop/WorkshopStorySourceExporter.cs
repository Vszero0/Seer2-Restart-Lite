using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

public enum WorkshopStorySourceMissionType
{
    Side = 1,
    Daily = 2,
    Event = 3,
}

public sealed class WorkshopStorySourceReward
{
    public int itemId;
    public int amount;
}

public sealed class WorkshopStorySourceRewardOption
{
    public int itemId;
    public string name;
    public string displayName => (name ?? string.Empty) + "  " + itemId;
}

public sealed class WorkshopStorySourceExportRequest
{
    public WorkshopStorySourceMissionType missionType = WorkshopStorySourceMissionType.Side;
    public string title;
    public bool replayable;
    public string rewardMode = "once";
    public List<WorkshopStorySourceReward> rewards = new List<WorkshopStorySourceReward>();
}

public sealed class WorkshopStorySourceExportResult
{
    public int missionId;
    public string storyResourcePath;
    public bool updatedExisting;
    public bool migratedExisting;
    public int previousMissionId;
    public StorySourceExportBindingDocument binding;
}

/// <summary>
/// Unity Editor 中将当前 Mod 剧本母版实装为源码内置支线、日常或活动任务。
/// 编译后的客户端不包含写入源码工程的执行路径。
/// </summary>
public sealed class WorkshopStorySourceExporter
{
    private const int StoryMissionMapPlaceholder = 0;

    private sealed class AssetCopy
    {
        public string sourcePath;
        public string destinationPath;
        public string resourcePath;
    }

    public bool CanExport => Application.isEditor;

    public bool TryExport(
        StoryDocument source,
        WorkshopStorySourceExportRequest request,
        out WorkshopStorySourceExportResult result,
        out string error)
    {
        result = null;
        error = string.Empty;
#if !UNITY_EDITOR
        error = "源码任务导出只在 Unity Editor 中可用。";
        return false;
#else
        if (!TryPrepareDocument(source, request, out StoryDocument document,
                out List<AssetCopy> assetCopies, out error))
            return false;

        string category = GetCategoryName(request.missionType);
        string countElement = GetCountElementName(request.missionType);
        int missionBase = GetMissionBase(request.missionType);
        string safeStoryId = MakeSafePathSegment(document.id, "story");
        string storyResourcePath = category + "/" + safeStoryId;
        if (string.IsNullOrWhiteSpace(Path.GetDirectoryName(Application.dataPath)))
        {
            error = "无法确定当前 Unity 项目根目录。";
            return false;
        }

        string missionDirectory = Path.Combine(Application.dataPath, "Resources", "Data", "Missions");
        string storyDirectory = Path.Combine(Application.dataPath, "Resources", "Data", "Stories", category);
        string versionPath = Path.Combine(Application.dataPath, "Resources", "Data", "System", "version.xml");
        if (!TryReadVersionCount(versionPath, countElement, out int currentCount, out string versionText, out error))
            return false;

        bool updatedExisting = false;
        int missionId = 0;
        int previousMissionId = source.sourceExport?.missionId ?? 0;
        if (source.sourceExport != null
            && previousMissionId > 0
            && source.sourceExport.missionType == (int)request.missionType
            && string.Equals(source.sourceExport.storyResourcePath, storyResourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            updatedExisting = TryFindMissionById(missionDirectory, previousMissionId,
                storyResourcePath, (int)request.missionType, out missionId);
        }
        if (!updatedExisting)
        {
            updatedExisting = TryFindExistingMission(
                missionDirectory, storyResourcePath, (int)request.missionType, out missionId);
        }
        bool migratedExisting = source.sourceExport != null
            && source.sourceExport.missionId > 0
            && (!string.Equals(source.sourceExport.storyResourcePath, storyResourcePath,
                StringComparison.OrdinalIgnoreCase)
                || source.sourceExport.missionType != (int)request.missionType);
        int oldMissionId = source.sourceExport?.missionId ?? 0;
        string oldStoryResourcePath = source.sourceExport?.storyResourcePath;
        if (!updatedExisting)
        {
            missionId = missionBase + currentCount + 1;
            string expectedMissionPath = Path.Combine(missionDirectory, missionId + ".xml");
            if (File.Exists(expectedMissionPath))
            {
                error = "任务计数与源码文件不一致，预期的新任务文件已经存在：" + expectedMissionPath;
                return false;
            }
        }
        if (!TryRemapMissionReferences(document, source.mission?.id ?? 0, missionId, out error))
            return false;

        document.sourceExport = new StorySourceExportBindingDocument
        {
            missionId = missionId,
            missionType = (int)request.missionType,
            storyResourcePath = storyResourcePath,
        };

        MissionInfo missionInfo = BuildMissionInfo(document, request, missionId, storyResourcePath);
        string missionXml = SerializeMission(missionInfo);
        string storyJson = StoryDocumentCodec.Serialize(document);
        string updatedVersionText = updatedExisting
            ? versionText
            : ReplaceVersionCount(versionText, countElement, currentCount + 1);
        if (string.IsNullOrEmpty(updatedVersionText))
        {
            error = "无法更新 version.xml 中的任务数量。";
            return false;
        }

        string storyPath = Path.Combine(storyDirectory, safeStoryId + ".json");
        string missionPath = Path.Combine(missionDirectory, missionId + ".xml");
        Dictionary<string, byte[]> backups = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        List<string> newlyCreatedFiles = new List<string>();
        try
        {
            Directory.CreateDirectory(storyDirectory);
            Directory.CreateDirectory(missionDirectory);
            foreach (AssetCopy copy in assetCopies)
                CopyWithBackup(copy.sourcePath, copy.destinationPath, backups, newlyCreatedFiles);

            WriteTextWithBackup(storyPath, storyJson, backups, newlyCreatedFiles);
            WriteTextWithBackup(missionPath, missionXml, backups, newlyCreatedFiles);
            if (!updatedExisting)
                WriteTextWithBackup(versionPath, updatedVersionText, backups, newlyCreatedFiles);

            if (migratedExisting && oldMissionId > 0 && oldMissionId != missionId)
            {
                string oldMissionPath = Path.Combine(missionDirectory, oldMissionId + ".xml");
                CaptureBackup(oldMissionPath, backups, newlyCreatedFiles);
                if (File.Exists(oldMissionPath))
                    File.Delete(oldMissionPath);
            }
            if (migratedExisting && !string.IsNullOrWhiteSpace(oldStoryResourcePath)
                && !string.Equals(oldStoryResourcePath, storyResourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                string oldStoryPath = GetGeneratedStoryPath(oldStoryResourcePath);
                if (!string.IsNullOrWhiteSpace(oldStoryPath) && File.Exists(oldStoryPath))
                {
                    CaptureBackup(oldStoryPath, backups, newlyCreatedFiles);
                    File.Delete(oldStoryPath);
                }
            }

            UnityEditor.AssetDatabase.Refresh();
            ConfigureImportedSprites(assetCopies);
            UnityEditor.AssetDatabase.Refresh();
        }
        catch (Exception exception)
        {
            RestoreFiles(backups, newlyCreatedFiles);
            UnityEditor.AssetDatabase.Refresh();
            error = "导出源码任务失败：" + exception.Message;
            return false;
        }

        result = new WorkshopStorySourceExportResult
        {
            missionId = missionId,
            storyResourcePath = storyResourcePath,
            updatedExisting = updatedExisting,
            migratedExisting = migratedExisting && oldMissionId != missionId,
            previousMissionId = oldMissionId,
            binding = document.sourceExport,
        };
        return true;
#endif
    }

#if UNITY_EDITOR
    private static bool TryPrepareDocument(
        StoryDocument source,
        WorkshopStorySourceExportRequest request,
        out StoryDocument document,
        out List<AssetCopy> assetCopies,
        out string error)
    {
        document = null;
        assetCopies = new List<AssetCopy>();
        error = string.Empty;
        if (source == null || request == null)
        {
            error = "没有可导出的剧本文档。";
            return false;
        }

        if (!Enum.IsDefined(typeof(WorkshopStorySourceMissionType), request.missionType))
        {
            error = "源码导出只支持支线、日常和活动任务。";
            return false;
        }

        request.title = (request.title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(request.title))
        {
            error = "任务标题不能为空。";
            return false;
        }

        request.rewardMode = string.Equals(request.rewardMode, "always", StringComparison.OrdinalIgnoreCase)
            ? "always"
            : "once";
        request.rewards ??= new List<WorkshopStorySourceReward>();
        request.rewards = request.rewards
            .Where(reward => reward != null && reward.itemId != 0 && reward.amount != 0)
            .ToList();
        if (request.rewards.Count > 3)
        {
            error = "源码任务最多配置三项奖励。";
            return false;
        }

        HashSet<int> rewardIds = new HashSet<int>();
        foreach (WorkshopStorySourceReward reward in request.rewards)
        {
            ItemInfo item = ItemInfo.database.FirstOrDefault(value => value != null && value.id == reward.itemId);
            if (item == null)
            {
                error = "找不到奖励道具 ID：" + reward.itemId;
                return false;
            }
            if (ItemInfo.IsMod(reward.itemId) || ItemInfo.IsMod(item.getId))
            {
                error = "源码任务不能使用当前 Mod 独有奖励道具：" + item.name;
                return false;
            }
            if (reward.amount <= 0)
            {
                error = "奖励数量必须大于 0：" + item.name;
                return false;
            }
            if (!rewardIds.Add(reward.itemId))
            {
                error = "奖励道具不能重复：" + item.name;
                return false;
            }
        }

        if (!StoryDocumentCodec.TryDeserialize(
                StoryDocumentCodec.Serialize(source), false, out document, out error))
            return false;

        document.status = "published";
        document.title = request.title;
        document.replayable = request.replayable;
        document.mission ??= new StoryMissionDocument();
        document.mission.title = request.title;
        document.mission.summary = document.summary;
        document.mission.mapId = StoryMissionMapPlaceholder;
        document.mission.replayable = request.replayable;
        RemoveOwnMissionCompletionCommands(document, source.mission?.id ?? 0);

        if (!StoryValidator.Validate(document, out error))
        {
            error = "剧本尚不能导出为源码任务：\n" + error;
            return false;
        }

        if (!TryRewriteOwnedResources(document, assetCopies, out error))
            return false;
        return true;
    }

    private static bool TryRewriteOwnedResources(
        StoryDocument document,
        List<AssetCopy> copies,
        out string error)
    {
        error = string.Empty;
        string storyId = document.id ?? string.Empty;
        string safeStoryId = MakeSafePathSegment(storyId, "story");
        Dictionary<string, AssetCopy> copyBySource = new Dictionary<string, AssetCopy>(StringComparer.OrdinalIgnoreCase);

        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node == null)
                continue;
            foreach (StorySceneDocument scene in node.scenes ?? Array.Empty<StorySceneDocument>())
            {
                if (scene == null)
                    continue;
                if (Map.IsMod(scene.mapId))
                {
                    error = "源码任务不能直接引用当前 Mod 地图：" + scene.mapId;
                    return false;
                }
                if (!TryRewritePath(ref scene.bgmResourcePath, storyId, safeStoryId, copyBySource, out error))
                    return false;
            }

            foreach (StoryCommandDocument command in node.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command == null)
                    continue;
                if (command.mapId != 0 && Map.IsMod(command.mapId))
                {
                    error = "源码任务命令不能直接引用当前 Mod 地图：" + command.mapId;
                    return false;
                }
                if (!TryRewritePath(ref command.bgmResourcePath, storyId, safeStoryId, copyBySource, out error)
                    || !TryRewritePath(ref command.bg, storyId, safeStoryId, copyBySource, out error))
                    return false;
            }
        }

        foreach (StoryActorDocument actor in document.actors ?? Array.Empty<StoryActorDocument>())
        {
            if (actor == null)
                continue;
            if (int.TryParse(actor.petId, out int petId) && PetInfo.IsMod(petId))
            {
                error = "源码任务不能直接引用当前 Mod 精灵：" + actor.displayName;
                return false;
            }
            if (!TryRewritePath(ref actor.sprite, storyId, safeStoryId, copyBySource, out error)
                || !TryRewritePath(ref actor.icon, storyId, safeStoryId, copyBySource, out error)
                || !TryRewritePath(ref actor.battleSprite, storyId, safeStoryId, copyBySource, out error)
                || !TryRewritePath(ref actor.independentIcon, storyId, safeStoryId, copyBySource, out error))
                return false;
        }

        foreach (StoryResourceDefinition resource in document.resourceDefinitions ?? Array.Empty<StoryResourceDefinition>())
        {
            if (resource == null)
                continue;
            if (!TryRewritePath(ref resource.path, storyId, safeStoryId, copyBySource, out error))
                return false;
            bool migratedOwnedResource = (resource.path ?? string.Empty)
                .StartsWith("Builtin/StoryAssets/", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(resource.source, "mod", StringComparison.OrdinalIgnoreCase)
                && !migratedOwnedResource)
            {
                error = "源码任务仍包含当前 Mod 专用资源：" + resource.path;
                return false;
            }
            if (!string.IsNullOrWhiteSpace(resource.path))
                resource.source = "builtin";
        }

        foreach (StorySceneResourceDocument scene in document.sceneResources ?? Array.Empty<StorySceneResourceDocument>())
        {
            if (scene == null)
                continue;
            if (!TryRewritePath(ref scene.backgroundResourcePath, storyId, safeStoryId, copyBySource, out error)
                || !TryRewritePath(ref scene.defaultBgmResourcePath, storyId, safeStoryId, copyBySource, out error))
                return false;
        }

        copies.AddRange(copyBySource.Values);
        return true;
    }

    private static bool TryRewritePath(
        ref string path,
        string storyId,
        string safeStoryId,
        Dictionary<string, AssetCopy> copyBySource,
        out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "none", StringComparison.OrdinalIgnoreCase))
            return true;

        string normalized = path.Replace('\\', '/').Trim();
        string ownedPrefix = "Mod/Stories/" + storyId + "/Assets/";
        if (normalized.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryBuildOwnedAssetCopy(normalized, ownedPrefix, safeStoryId, out AssetCopy copy, out error))
                return false;
            copyBySource[copy.sourcePath] = copy;
            path = copy.resourcePath;
            return true;
        }

        string legacyPrefix = "Mod/Stories/Assets/" + storyId + "/";
        if (normalized.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryBuildOwnedAssetCopy(normalized, legacyPrefix, safeStoryId, out AssetCopy copy, out error))
                return false;
            copyBySource[copy.sourcePath] = copy;
            path = copy.resourcePath;
            return true;
        }

        if (normalized.StartsWith("Mod/", StringComparison.OrdinalIgnoreCase))
        {
            error = "源码任务不能直接引用当前 Mod 专用资源：" + normalized;
            return false;
        }

        path = normalized;
        return true;
    }

    private static bool TryBuildOwnedAssetCopy(
        string normalizedPath,
        string ownedPrefix,
        string safeStoryId,
        out AssetCopy copy,
        out string error)
    {
        copy = null;
        error = string.Empty;
        string sourceRelative = normalizedPath.Substring("Mod/".Length);
        string sourcePath = Path.Combine(Application.persistentDataPath, "Mod", sourceRelative);
        sourcePath = ResolvePhysicalResourcePath(sourcePath);
        if (string.IsNullOrEmpty(sourcePath))
        {
            error = "找不到剧本自有资源：" + normalizedPath;
            return false;
        }

        string suffix = normalizedPath.Substring(ownedPrefix.Length).Replace('/', Path.DirectorySeparatorChar);
        string extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(Path.GetExtension(suffix)))
            suffix += extension;
        string destinationPath = Path.Combine(Application.dataPath, "Resources", "StoryAssets", safeStoryId, suffix);
        string resourceSuffix = Path.ChangeExtension(suffix, null).Replace('\\', '/');
        copy = new AssetCopy
        {
            sourcePath = sourcePath,
            destinationPath = destinationPath,
            resourcePath = "Builtin/StoryAssets/" + safeStoryId + "/" + resourceSuffix,
        };
        return true;
    }

    private static string ResolvePhysicalResourcePath(string path)
    {
        if (File.Exists(path))
            return path;
        foreach (string extension in new[] { ".png", ".mp3", ".wav", ".ogg" })
        {
            if (File.Exists(path + extension))
                return path + extension;
        }
        return string.Empty;
    }

    private static void RemoveOwnMissionCompletionCommands(StoryDocument document, int sourceMissionId)
    {
        if (sourceMissionId == 0)
            return;
        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node == null)
                continue;
            node.commands = (node.commands ?? Array.Empty<StoryCommandDocument>())
                .Where(command => !IsOwnCompletionCommand(command, sourceMissionId))
                .ToArray();
        }
    }

    private static bool IsOwnCompletionCommand(StoryCommandDocument command, int missionId)
    {
        if (command == null || !string.Equals(command.type, "mission", StringComparison.OrdinalIgnoreCase))
            return false;
        string[] tokens = StoryCommandArguments.Split(command.args);
        return tokens.Length >= 2
            && int.TryParse(tokens[0], out int targetMissionId)
            && targetMissionId == missionId
            && string.Equals(tokens[1], "complete", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRemapMissionReferences(
        StoryDocument document,
        int sourceMissionId,
        int targetMissionId,
        out string error)
    {
        error = string.Empty;
        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            if (node == null)
                continue;
            foreach (StoryCommandDocument command in node.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command == null)
                    continue;
                if (!TryRemapConditionGroup(command.condition, sourceMissionId, targetMissionId, out error)
                    || !TryRemapConditionGroup(command.displayCondition, sourceMissionId, targetMissionId, out error))
                    return false;
                if (!string.Equals(command.type, "mission", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] tokens = StoryCommandArguments.Split(command.args);
                if (tokens.Length == 0 || !int.TryParse(tokens[0], out int referencedId) || referencedId >= 0)
                    continue;
                if (referencedId != sourceMissionId)
                {
                    error = "源码任务不能引用其他 Mod 任务：" + referencedId;
                    return false;
                }
                tokens[0] = targetMissionId.ToString();
                command.args = string.Join(" ", tokens);
            }
            foreach (StoryNodeTransitionDocument transition in node.transitions ?? Array.Empty<StoryNodeTransitionDocument>())
            {
                if (!TryRemapConditionGroup(transition?.condition, sourceMissionId, targetMissionId, out error))
                    return false;
            }
        }
        return true;
    }

    private static bool TryRemapConditionGroup(
        ConditionGroupDocument group,
        int sourceMissionId,
        int targetMissionId,
        out string error)
    {
        error = string.Empty;
        IEnumerable<StoryConditionDocument> conditions = (group?.conditions ?? Array.Empty<StoryConditionDocument>())
            .Concat((group?.clauses ?? Array.Empty<StoryConditionClauseDocument>())
                .Where(clause => clause != null)
                .SelectMany(clause => clause.conditions ?? Array.Empty<StoryConditionDocument>()));
        foreach (StoryConditionDocument condition in conditions)
        {
            if (condition == null || condition.missionId >= 0)
                continue;
            if (condition.missionId != sourceMissionId)
            {
                error = "源码任务条件不能引用其他 Mod 任务：" + condition.missionId;
                return false;
            }
            condition.missionId = targetMissionId;
        }
        return true;
    }

    private static MissionInfo BuildMissionInfo(
        StoryDocument document,
        WorkshopStorySourceExportRequest request,
        int missionId,
        string storyResourcePath)
    {
        return new MissionInfo
        {
            id = missionId,
            typeId = (int)request.missionType,
            replayable = request.replayable,
            rewardMode = request.replayable ? request.rewardMode : "once",
            autoCompleteStory = true,
            title = request.title,
            rewards = request.rewards.Select(reward => new Item(reward.itemId, reward.amount)).ToList(),
            checkpoints = new List<MissionCheckpoint>
            {
                new MissionCheckpoint
                {
                    id = "default",
                    mapId = StoryMissionMapPlaceholder,
                    storyId = storyResourcePath,
                    intro = document.summary ?? string.Empty,
                },
            },
        };
    }

    private static string SerializeMission(MissionInfo mission)
    {
        XmlSerializer serializer = new XmlSerializer(typeof(MissionInfo));
        XmlSerializerNamespaces namespaces = new XmlSerializerNamespaces();
        namespaces.Add(string.Empty, string.Empty);
        StringBuilder builder = new StringBuilder();
        XmlWriterSettings settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "    ",
            NewLineChars = "\n",
        };
        using (XmlWriter writer = XmlWriter.Create(builder, settings))
            serializer.Serialize(writer, mission, namespaces);
        return builder.ToString() + "\n";
    }

    private static bool TryFindExistingMission(
        string missionDirectory,
        string storyResourcePath,
        int typeId,
        out int missionId)
    {
        missionId = 0;
        if (!Directory.Exists(missionDirectory))
            return false;
        foreach (string path in Directory.GetFiles(missionDirectory, "*.xml", SearchOption.TopDirectoryOnly))
        {
            try
            {
                XmlDocument xml = new XmlDocument();
                xml.Load(path);
                XmlElement root = xml.DocumentElement;
                if (root == null || !int.TryParse(root.GetAttribute("type"), out int existingType)
                    || existingType != typeId)
                    continue;
                XmlNode storyNode = root.SelectSingleNode("checkpoint/branch/story");
                if (!string.Equals(storyNode?.InnerText?.Trim(), storyResourcePath,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (int.TryParse(root.GetAttribute("id"), out missionId))
                    return true;
            }
            catch
            {
                // 单个无效任务 XML 由项目自身校验处理，不阻断查找其他绑定。
            }
        }
        return false;
    }

    private static bool TryFindMissionById(
        string missionDirectory,
        int missionId,
        string storyResourcePath,
        int typeId,
        out int foundMissionId)
    {
        foundMissionId = 0;
        if (missionId <= 0 || !Directory.Exists(missionDirectory))
            return false;

        string path = Path.Combine(missionDirectory, missionId + ".xml");
        if (!File.Exists(path))
            return false;
        try
        {
            XmlDocument xml = new XmlDocument();
            xml.Load(path);
            XmlElement root = xml.DocumentElement;
            if (root == null || !int.TryParse(root.GetAttribute("id"), out int existingId)
                || existingId != missionId
                || !int.TryParse(root.GetAttribute("type"), out int existingType)
                || existingType != typeId)
                return false;
            XmlNode storyNode = root.SelectSingleNode("checkpoint/branch/story");
            if (!string.Equals(storyNode?.InnerText?.Trim(), storyResourcePath,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            foundMissionId = existingId;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetGeneratedStoryPath(string storyResourcePath)
    {
        string normalized = (storyResourcePath ?? string.Empty).Replace('\\', '/').Trim('/');
        string[] segments = normalized.Split('/');
        if (segments.Length != 2
            || !string.Equals(segments[0], "Side", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(segments[0], "Daily", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(segments[0], "Event", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        string storyId = MakeSafePathSegment(segments[1], string.Empty);
        if (string.IsNullOrWhiteSpace(storyId))
            return string.Empty;
        return Path.Combine(Application.dataPath, "Resources", "Data", "Stories",
            segments[0], storyId + ".json");
    }

    private static bool TryReadVersionCount(
        string versionPath,
        string elementName,
        out int count,
        out string text,
        out string error)
    {
        count = 0;
        text = string.Empty;
        error = string.Empty;
        if (!File.Exists(versionPath))
        {
            error = "找不到源码版本配置：" + versionPath;
            return false;
        }
        text = File.ReadAllText(versionPath, Encoding.UTF8);
        Match match = Regex.Match(text, "<" + elementName + ">\\s*(\\d+)\\s*</" + elementName + ">",
            RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out count))
        {
            error = "version.xml 中缺少有效的 " + elementName + "。";
            return false;
        }
        return true;
    }

    private static string ReplaceVersionCount(string text, string elementName, int count)
    {
        string pattern = "(<" + elementName + ">)\\s*\\d+\\s*(</" + elementName + ">)";
        return Regex.Replace(text, pattern,
            match => match.Groups[1].Value + count + match.Groups[2].Value,
            RegexOptions.IgnoreCase);
    }

    private static string GetCategoryName(WorkshopStorySourceMissionType type)
    {
        return type == WorkshopStorySourceMissionType.Daily ? "Daily"
            : type == WorkshopStorySourceMissionType.Event ? "Event"
            : "Side";
    }

    private static string GetCountElementName(WorkshopStorySourceMissionType type)
    {
        return type == WorkshopStorySourceMissionType.Daily ? "dailyCount"
            : type == WorkshopStorySourceMissionType.Event ? "eventCount"
            : "sideCount";
    }

    private static int GetMissionBase(WorkshopStorySourceMissionType type)
    {
        return type == WorkshopStorySourceMissionType.Daily ? 20000
            : type == WorkshopStorySourceMissionType.Event ? 30000
            : 10000;
    }

    private static string MakeSafePathSegment(string value, string fallback)
    {
        string safe = Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9_-]", "_").Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static void WriteTextWithBackup(
        string path,
        string text,
        Dictionary<string, byte[]> backups,
        List<string> newlyCreatedFiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        CaptureBackup(path, backups, newlyCreatedFiles);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static void CopyWithBackup(
        string sourcePath,
        string destinationPath,
        Dictionary<string, byte[]> backups,
        List<string> newlyCreatedFiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
        CaptureBackup(destinationPath, backups, newlyCreatedFiles);
        File.Copy(sourcePath, destinationPath, true);
    }

    private static void CaptureBackup(
        string path,
        Dictionary<string, byte[]> backups,
        List<string> newlyCreatedFiles)
    {
        if (backups.ContainsKey(path) || newlyCreatedFiles.Contains(path))
            return;
        if (File.Exists(path))
            backups[path] = File.ReadAllBytes(path);
        else
            newlyCreatedFiles.Add(path);
    }

    private static void RestoreFiles(Dictionary<string, byte[]> backups, IEnumerable<string> newlyCreatedFiles)
    {
        foreach (KeyValuePair<string, byte[]> backup in backups)
            File.WriteAllBytes(backup.Key, backup.Value);
        foreach (string path in newlyCreatedFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
            string metaPath = path + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }
    }

    private static void ConfigureImportedSprites(IEnumerable<AssetCopy> copies)
    {
        foreach (AssetCopy copy in copies)
        {
            if (!string.Equals(Path.GetExtension(copy.destinationPath), ".png", StringComparison.OrdinalIgnoreCase))
                continue;
            string assetPath = "Assets" + copy.destinationPath.Substring(Application.dataPath.Length)
                .Replace('\\', '/');
            UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(assetPath)
                as UnityEditor.TextureImporter;
            if (importer == null || importer.textureType == UnityEditor.TextureImporterType.Sprite)
                continue;
            importer.textureType = UnityEditor.TextureImporterType.Sprite;
            importer.spriteImportMode = UnityEditor.SpriteImportMode.Single;
            importer.SaveAndReimport();
        }
    }
#endif
}
