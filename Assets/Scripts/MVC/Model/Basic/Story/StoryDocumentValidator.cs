using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 剧本文档的结构与引用校验。
/// 资源存在性校验由 StoryResourceValidator 负责，运行时不依赖此类的内部细节。
/// </summary>
public static class StoryValidator
{
    private static readonly HashSet<string> commandTypes = new HashSet<string>
    {
        "scene",
        "show",
        "hide",
        "say",
        "narrate",
        "choice",
        "jump",
        "mission",
        "teleport",
        "end",
    };

    public static bool Validate(StoryDocument document, out string error)
    {
        List<string> errors = new List<string>();

        if (document == null)
        {
            error = "剧情文件为空或不是合法 JSON";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.id))
            errors.Add("story.id 不能为空");

        if (!document.HasSupportedStatus)
            errors.Add("story.status 只支持 draft 或 published");

        if (document.mission == null)
        {
            errors.Add("mission 不能为空");
        }
        else
        {
            if (document.mission.id >= 0)
                errors.Add("mission.id 必须是负数，避免和官方任务 ID 冲突");

            if (document.mission.mapId < 0)
                errors.Add("mission.mapId 不能小于 0");

            if (string.IsNullOrWhiteSpace(document.mission.title) && string.IsNullOrWhiteSpace(document.title))
                errors.Add("mission.title 或 story.title 至少需要填写一个");
        }

        Dictionary<string, StoryActorDocument> actorDict = StoryResourceValidator.ValidateActors(document, errors);
        StoryResourceValidator.ValidateResources(document, errors);
        Dictionary<string, StoryNodeDocument> nodeDict = ValidateNodes(document, errors);

        string entry = string.IsNullOrWhiteSpace(document.entry) ? "start" : document.entry;
        if (nodeDict.Count > 0 && !nodeDict.ContainsKey(entry))
            errors.Add("entry 指向的节点不存在：" + entry);

        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            ValidateNodeFlow(node, nodeDict, entry, errors);
            ValidateNodeScenes(node, actorDict, errors);
            ValidateNodeCommands(node, actorDict, nodeDict, errors);
            ValidateNodeTransitions(node, nodeDict, errors);
        }

        if (nodeDict.Count > 0 && !(document.nodes ?? Array.Empty<StoryNodeDocument>())
            .Any(node => node != null && node.isEnding))
        {
            errors.Add("剧本至少需要一个明确的结束节点");
        }

        error = string.Join("\n", errors);
        return errors.Count == 0;
    }

    public static bool ValidateDraft(StoryDocument document, out string error)
    {
        List<string> errors = new List<string>();
        if (document == null)
        {
            error = "剧情文件为空或不是合法 JSON";
            return false;
        }

        if (!document.isDraft)
            errors.Add("story.status 必须为 draft");
        if (!document.HasSupportedStatus)
            errors.Add("story.status 只支持 draft 或 published");
        if (string.IsNullOrWhiteSpace(document.id))
            errors.Add("story.id 不能为空");
        if (string.IsNullOrWhiteSpace(document.entry))
            errors.Add("entry 不能为空");

        Dictionary<string, StoryNodeDocument> nodeDict = ValidateNodes(document, errors);
        if (!string.IsNullOrWhiteSpace(document.entry) && !nodeDict.ContainsKey(document.entry))
            errors.Add("entry 指向的剧情点不存在：" + document.entry);

        foreach (StoryNodeDocument node in document.nodes ?? Array.Empty<StoryNodeDocument>())
        {
            ValidateNodeFlow(node, nodeDict, document.entry, errors);
            ValidateDraftStableIds(node, errors);
            ValidateNodeTransitions(node, nodeDict, errors);
        }

        if (nodeDict.Count > 0 && !(document.nodes ?? Array.Empty<StoryNodeDocument>())
            .Any(node => node != null && node.isEnding))
        {
            errors.Add("剧本至少需要一个明确的结束节点");
        }

        error = string.Join("\n", errors);
        return errors.Count == 0;
    }

    private static void ValidateDraftStableIds(StoryNodeDocument node, List<string> errors)
    {
        if (node == null)
            return;

        HashSet<string> commandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> choiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> optionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryCommandDocument command in node.commands ?? Array.Empty<StoryCommandDocument>())
        {
            if (command == null)
                continue;
            if (string.IsNullOrWhiteSpace(command.commandId) || !commandIds.Add(command.commandId))
                errors.Add("node[" + node.id + "] 的 commandId 不能为空且不能重复");
            if (!string.Equals(command.type, "choice", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(command.choiceId) || !choiceIds.Add(command.choiceId))
                errors.Add("node[" + node.id + "] 的 choiceId 不能为空且不能重复");
            foreach (StoryChoiceDocument option in command.choices ?? Array.Empty<StoryChoiceDocument>())
            {
                if (option == null)
                    continue;
                if (string.IsNullOrWhiteSpace(option.optionId) || !optionIds.Add(option.optionId))
                    errors.Add("node[" + node.id + "] 的 optionId 不能为空且不能重复");
                if (!string.Equals(option.choiceId, command.choiceId, StringComparison.OrdinalIgnoreCase))
                    errors.Add("node[" + node.id + "] 的选项 choiceId 与所属选择命令不一致");
            }
        }
    }

    private static void ValidateNodeScenes(StoryNodeDocument node, Dictionary<string, StoryActorDocument> actorDict, List<string> errors)
    {
        if (node?.scenes == null)
            return;

        HashSet<string> sceneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StorySceneDocument scene in node.scenes)
        {
            if (scene == null)
                continue;

            string location = "node[" + node.id + "].scenes[" + (scene.id ?? string.Empty) + "]";
            if (string.IsNullOrWhiteSpace(scene.id) || !sceneIds.Add(scene.id))
                errors.Add(location + " 存在重复或为空的场景 id");

            if (scene.mapId == 0)
                errors.Add(location + ".mapId 必须是有效地图 ID");
            else
                StoryResourceValidator.ValidateMap(scene.mapId, errors, location + ".mapId");
            StoryResourceValidator.ValidatePath(scene.bgmResourcePath, "audio", "auto", errors, location + ".bgmResourcePath");
            ValidateTransition(scene.transition, false, errors, location + ".transition");

            HashSet<string> sceneActorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StorySceneActorLayoutDocument actorLayout in scene.actors ?? Array.Empty<StorySceneActorLayoutDocument>())
            {
                string actorId = actorLayout?.actorId;
                if (string.IsNullOrWhiteSpace(actorId) || !actorDict.ContainsKey(actorId))
                    errors.Add(location + ".actors 引用了不存在的角色：" + actorId);
                else if (!sceneActorIds.Add(actorId))
                    errors.Add(location + ".actors 存在重复的角色：" + actorId);

                if (actorLayout == null)
                    continue;

                if (!string.Equals(actorLayout.normalizedPlacementMode, actorLayout.placementMode, StringComparison.OrdinalIgnoreCase))
                    errors.Add(location + ".actors[" + actorId + "].placementMode 只支持 auto 或 manual");

                if (!string.Equals(actorLayout.normalizedSide, actorLayout.side, StringComparison.OrdinalIgnoreCase))
                    errors.Add(location + ".actors[" + actorId + "].side 只支持 left 或 right");

                if (actorLayout.order < 0)
                    errors.Add(location + ".actors[" + actorId + "].order 不能小于 0");

                if (actorLayout.scale <= 0f)
                    errors.Add(location + ".actors[" + actorId + "].scale 必须大于 0");
            }
        }
    }

    private static Dictionary<string, StoryNodeDocument> ValidateNodes(StoryDocument document, List<string> errors)
    {
        Dictionary<string, StoryNodeDocument> nodeDict = new Dictionary<string, StoryNodeDocument>();
        if (document.nodes == null || document.nodes.Length == 0)
        {
            errors.Add("nodes 不能为空");
            return nodeDict;
        }

        foreach (StoryNodeDocument node in document.nodes)
        {
            if (node == null)
                continue;

            if (string.IsNullOrWhiteSpace(node.id))
            {
                errors.Add("nodes 中存在 id 为空的节点");
                continue;
            }

            if (nodeDict.ContainsKey(node.id))
            {
                errors.Add("nodes 存在重复节点 id：" + node.id);
                continue;
            }

            nodeDict[node.id] = node;
        }

        return nodeDict;
    }

    private static void ValidateNodeFlow(
        StoryNodeDocument node,
        Dictionary<string, StoryNodeDocument> nodeDict,
        string entry,
        List<string> errors)
    {
        if (node == null)
            return;

        string flowRole = string.IsNullOrWhiteSpace(node.flowRole)
            ? "sequence"
            : node.flowRole.Trim().ToLower();
        string location = "node[" + node.id + "]";
        if (flowRole != "sequence" && flowRole != "branch")
            errors.Add(location + ".flowRole must be sequence or branch");

        if (string.Equals(node.id, entry, StringComparison.OrdinalIgnoreCase) && flowRole == "branch")
            errors.Add(location + " entry node must use sequence flowRole");

        if (flowRole == "branch")
        {
            if (!string.IsNullOrWhiteSpace(node.fallbackNodeId))
            {
                ValidateTarget(node.fallbackNodeId, nodeDict, errors, location + ".fallbackNodeId");
                if (string.Equals(node.fallbackNodeId, node.id, StringComparison.OrdinalIgnoreCase))
                    errors.Add(location + ".fallbackNodeId 不能重新进入当前剧情点");
            }
        }
        else if (!string.IsNullOrWhiteSpace(node.fallbackNodeId))
        {
            errors.Add(location + ".fallbackNodeId is only valid for branch nodes");
        }
    }

    private static void ValidateNodeCommands(
        StoryNodeDocument node,
        Dictionary<string, StoryActorDocument> actorDict,
        Dictionary<string, StoryNodeDocument> nodeDict,
        List<string> errors)
    {
        if (node == null || node.commands == null)
            return;

        HashSet<string> commandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> choiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> optionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < node.commands.Length; i++)
        {
            StoryCommandDocument command = node.commands[i];
            string location = "node[" + node.id + "].commands[" + i + "]";
            if (command == null)
            {
                errors.Add(location + " 不能为空");
                continue;
            }

            if (string.IsNullOrWhiteSpace(command.commandId) || !commandIds.Add(command.commandId))
                errors.Add(location + ".commandId 不能为空且在剧情点内不能重复");

            string type = (command.type ?? string.Empty).Trim().ToLower();
            if (string.IsNullOrEmpty(type))
            {
                errors.Add(location + ".type 不能为空");
                continue;
            }

            if (!commandTypes.Contains(type))
            {
                errors.Add(location + ".type 不支持：" + command.type);
                continue;
            }

            switch (type)
            {
                case "scene":
                    if (string.IsNullOrWhiteSpace(command.sceneId)
                        && command.mapId == 0
                        && string.IsNullOrWhiteSpace(command.bg)
                        && string.IsNullOrWhiteSpace(command.args))
                    {
                        errors.Add(location + " 需要 sceneId、mapId、bg 或 args");
                    }

                    if (command.mapId != 0)
                        StoryResourceValidator.ValidateMap(command.mapId, errors, location + ".mapId");
                    StoryResourceValidator.ValidatePath(command.bgmResourcePath, "audio", "auto", errors, location + ".bgmResourcePath");
                    ValidateConditionGroup(command.condition, errors, location + ".condition");
                    ValidateConditionGroup(command.displayCondition, errors, location + ".displayCondition");
                    break;
                case "show":
                    ValidateActorReference(command, actorDict, errors, location, true);
                    break;
                case "hide":
                    if ((command.actor ?? string.Empty).Trim().ToLower() != "all")
                        ValidateActorReference(command, actorDict, errors, location, false);
                    break;
                case "say":
                    ValidateActorReference(command, actorDict, errors, location, false);
                    if (string.IsNullOrWhiteSpace(command.text))
                        errors.Add(location + ".text 不能为空");
                    break;
                case "narrate":
                    if (string.IsNullOrWhiteSpace(command.text))
                        errors.Add(location + ".text 不能为空");
                    break;
                case "choice":
                    if (string.IsNullOrWhiteSpace(command.choiceId) || !choiceIds.Add(command.choiceId))
                        errors.Add(location + ".choiceId 不能为空且在剧情点内不能重复");
                    ValidateChoices(command, nodeDict, optionIds, errors, location);
                    if (!string.IsNullOrWhiteSpace(command.actor))
                        ValidateActorReference(command, actorDict, errors, location, false);
                    ValidateConditionGroup(command.condition, errors, location + ".condition");
                    break;
                case "jump":
                    ValidateTarget(command.target, nodeDict, errors, location);
                    ValidateConditionGroup(command.condition, errors, location + ".condition");
                    break;
                case "mission":
                    if (string.IsNullOrWhiteSpace(command.args))
                        errors.Add(location + " 需要 args，例如：-10001 complete");
                    break;
                case "teleport":
                    if (!int.TryParse(command.args, out _))
                        errors.Add(location + " 需要数字地图 ID args");
                    break;
            }
        }
    }

    private static void ValidateNodeTransitions(
        StoryNodeDocument node,
        Dictionary<string, StoryNodeDocument> nodeDict,
        List<string> errors)
    {
        if (node == null || node.transitions == null)
            return;

        HashSet<string> transitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int defaultCount = 0;
        for (int i = 0; i < node.transitions.Length; i++)
        {
            StoryNodeTransitionDocument transition = node.transitions[i];
            string location = "node[" + node.id + "].transitions[" + i + "]";
            if (transition == null)
            {
                errors.Add(location + " 不能为空");
                continue;
            }

            if (string.IsNullOrWhiteSpace(transition.transitionId) || !transitionIds.Add(transition.transitionId))
                errors.Add(location + ".transitionId 不能为空且不能重复");

            string targetType = string.IsNullOrWhiteSpace(transition.targetType)
                ? "node"
                : transition.targetType.Trim().ToLowerInvariant();
            if (targetType != "node" && targetType != "end")
            {
                errors.Add(location + ".targetType 只支持 node 或 end");
            }
            else if (targetType == "end")
            {
                if (!string.IsNullOrWhiteSpace(transition.targetNodeId))
                    errors.Add(location + " 结束连接不能配置 targetNodeId");
            }
            else
            {
                ValidateTarget(transition.targetNodeId, nodeDict, errors, location);
                if (transition.isDefault
                    && string.Equals(transition.targetNodeId, node.id, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(location + " 默认后续不能重新进入当前剧情点");
                }
            }
            ValidateTransition(transition.transition, true, errors, location + ".transition");
            if (transition.isEnd && transition.transition != null)
                errors.Add(location + ".transition 结束连接不使用场景转场");
            if (transition.isDefault)
            {
                defaultCount++;
                if (transition.condition != null && transition.condition.hasConditions)
                    errors.Add(location + " 默认连接不能配置 condition");
                if (i != node.transitions.Length - 1)
                    errors.Add(location + " 默认连接必须位于 transitions 的最后");
                continue;
            }

            if (transition.condition == null || !transition.condition.hasConditions)
            {
                errors.Add(location + " 条件分支必须至少配置一个条件");
                continue;
            }

            ValidateConditionGroup(transition.condition, errors, location + ".condition");
            ValidateChoiceConditionReferences(node, transition.condition, nodeDict, errors, location + ".condition");
        }

        if (defaultCount > 1)
            errors.Add("node[" + node.id + "].transitions 最多只能有一个默认连接");
    }

    private static void ValidateTransition(
        StoryTransitionDocument transition,
        bool allowInherit,
        List<string> errors,
        string location)
    {
        if (transition == null)
            return;

        string type = (transition.type ?? string.Empty).Trim().ToLowerInvariant();
        bool supported = type == "none" || type == "fade" || type == "crossfade"
            || type == "wipeleft" || type == "wiperight"
            || type == "wipeup" || type == "wipedown"
            || type == "pushleft" || type == "pushright"
            || type == "pushup" || type == "pushdown"
            || type == "zoomcross" || type == "radial"
            || (allowInherit && type == "inherit");
        if (!supported)
            errors.Add(location + ".type 使用了不支持的转场类型");
        if (transition.duration < .1f || transition.duration > 2f)
            errors.Add(location + ".duration 必须在 0.1 到 2 秒之间");
    }

    private static void ValidateConditionGroup(ConditionGroupDocument group, List<string> errors, string location)
    {
        if (group == null)
            return;

        StoryConditionClauseDocument[] clauses = group.clauses ?? Array.Empty<StoryConditionClauseDocument>();
        if (clauses.Length == 0)
        {
            string op = string.IsNullOrWhiteSpace(group.operatorType) ? string.Empty : group.operatorType.Trim().ToUpper();
            if (op != "AND" && op != "OR")
                errors.Add(location + ".operatorType 必须是 AND 或 OR");
        }

        for (int clauseIndex = 0; clauseIndex < clauses.Length; clauseIndex++)
        {
            if (clauses[clauseIndex]?.conditions == null || clauses[clauseIndex].conditions.Length == 0)
            {
                errors.Add(location + ".clauses[" + clauseIndex + "] 至少需要一个条件");
                continue;
            }

            Dictionary<string, string> positiveChoices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> signedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StoryConditionDocument condition in clauses[clauseIndex].conditions)
            {
                if (condition == null || !string.Equals(condition.type, "choiceSelected", StringComparison.OrdinalIgnoreCase))
                    continue;

                string conditionKey = (condition.choiceId ?? string.Empty) + "|" + (condition.optionId ?? string.Empty);
                string signedKey = (condition.negated ? "!" : string.Empty) + conditionKey;
                if (!signedConditions.Add(signedKey))
                    errors.Add(location + ".clauses[" + clauseIndex + "] 包含重复条件：" + condition.optionId);
                if (signedConditions.Contains((condition.negated ? string.Empty : "!") + conditionKey))
                    errors.Add(location + ".clauses[" + clauseIndex + "] 同时要求选择和不选择同一选项：" + condition.optionId);

                if (!condition.negated && !string.IsNullOrWhiteSpace(condition.choiceId))
                {
                    if (positiveChoices.TryGetValue(condition.choiceId, out string selectedOptionId)
                        && !string.Equals(selectedOptionId, condition.optionId, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(location + ".clauses[" + clauseIndex + "] 要求同一选择题同时选择多个选项");
                    }
                    else
                    {
                        positiveChoices[condition.choiceId] = condition.optionId;
                    }
                }
            }
        }

        foreach (StoryConditionDocument condition in EnumerateConditions(group))
        {
            if (condition == null || string.IsNullOrWhiteSpace(condition.type))
                errors.Add(location + " 中存在无效条件");
        }
    }

    private static void ValidateChoiceConditionReferences(
        StoryNodeDocument ownerNode,
        ConditionGroupDocument group,
        Dictionary<string, StoryNodeDocument> nodeDict,
        List<string> errors,
        string location)
    {
        foreach (StoryConditionDocument condition in EnumerateConditions(group))
        {
            if (condition == null || !string.Equals(condition.type, "choiceSelected", StringComparison.OrdinalIgnoreCase))
                continue;

            StoryNodeDocument choiceNode = ownerNode;
            if (!string.IsNullOrWhiteSpace(condition.pointId)
                && !nodeDict.TryGetValue(condition.pointId, out choiceNode))
            {
                errors.Add(location + " 引用了不存在的剧情点：" + condition.pointId);
                continue;
            }

            StoryCommandDocument choiceCommand = null;
            foreach (StoryCommandDocument command in choiceNode?.commands ?? Array.Empty<StoryCommandDocument>())
            {
                if (command != null
                    && string.Equals(command.commandId, condition.commandId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(command.choiceId, condition.choiceId, StringComparison.OrdinalIgnoreCase))
                {
                    choiceCommand = command;
                    break;
                }
            }

            bool optionExists = false;
            foreach (StoryChoiceDocument option in choiceCommand?.choices ?? Array.Empty<StoryChoiceDocument>())
            {
                if (option != null && string.Equals(option.optionId, condition.optionId, StringComparison.OrdinalIgnoreCase))
                {
                    optionExists = true;
                    break;
                }
            }

            if (!optionExists)
                errors.Add(location + " 引用了不存在的选择项：" + condition.optionId);
        }
    }

    private static IEnumerable<StoryConditionDocument> EnumerateConditions(ConditionGroupDocument group)
    {
        if (group?.clauses != null && group.clauses.Length > 0)
        {
            foreach (StoryConditionClauseDocument clause in group.clauses)
            foreach (StoryConditionDocument condition in clause?.conditions ?? Array.Empty<StoryConditionDocument>())
                yield return condition;
            yield break;
        }

        foreach (StoryConditionDocument condition in group?.conditions ?? Array.Empty<StoryConditionDocument>())
            yield return condition;
    }

    private static void ValidateActorReference(
        StoryCommandDocument command,
        Dictionary<string, StoryActorDocument> actorDict,
        List<string> errors,
        string location,
        bool requireVisual)
    {
        if (string.IsNullOrWhiteSpace(command.actor))
        {
            errors.Add(location + ".actor 不能为空");
            return;
        }

        if (!actorDict.TryGetValue(command.actor, out StoryActorDocument actor))
        {
            errors.Add(location + ".actor 指向不存在的角色：" + command.actor);
            return;
        }

        if (requireVisual && string.IsNullOrWhiteSpace(actor.displaySprite) && string.IsNullOrWhiteSpace(command.args))
            errors.Add(location + " 的角色缺少 sprite/icon，且命令没有提供 args");
    }

    private static void ValidateChoices(
        StoryCommandDocument command,
        Dictionary<string, StoryNodeDocument> nodeDict,
        HashSet<string> optionIds,
        List<string> errors,
        string location)
    {
        if (command.choices == null || command.choices.Length == 0)
        {
            errors.Add(location + ".choices 不能为空");
            return;
        }

        for (int i = 0; i < command.choices.Length; i++)
        {
            StoryChoiceDocument choice = command.choices[i];
            string choiceLocation = location + ".choices[" + i + "]";
            if (choice == null)
            {
                errors.Add(choiceLocation + " 不能为空");
                continue;
            }

            if (string.IsNullOrWhiteSpace(choice.text))
                errors.Add(choiceLocation + ".text 不能为空");

            if (string.IsNullOrWhiteSpace(choice.optionId) || !optionIds.Add(choice.optionId))
                errors.Add(choiceLocation + ".optionId 不能为空且在剧情点内不能重复");

            if (!string.Equals(choice.choiceId, command.choiceId, StringComparison.OrdinalIgnoreCase))
                errors.Add(choiceLocation + ".choiceId 必须与所属选择命令一致");

            if (!string.IsNullOrWhiteSpace(choice.target))
                ValidateTarget(choice.target, nodeDict, errors, choiceLocation);
        }
    }

    private static void ValidateTarget(
        string target,
        Dictionary<string, StoryNodeDocument> nodeDict,
        List<string> errors,
        string location)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            errors.Add(location + ".target 不能为空");
            return;
        }

        if (!nodeDict.ContainsKey(target))
            errors.Add(location + ".target 指向不存在的节点：" + target);
    }
}
