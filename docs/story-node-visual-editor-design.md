# 剧情点可视化编辑器设计

> 本文档只定义剧情点编辑体验。数据字段、场景上下文、layout、style、命令和运行时状态以 [自制剧情剧本数据模型与运行时上下文设计](story-data-model-design.md) 为准。

## 1. 目标

进入一个 `StoryPointData` 后，作者在接近实际播放的 16:9 画布中编辑：

- 场景背景。
- 场景中的角色。
- 角色头像、名称和立绘。
- 角色对白和旁白。
- 选择项。
- 内容命令顺序。
- 场景切换和场景 layout。

作者不需要直接编辑 JSON，也不需要理解运行时 `StoryCommand`。

## 2. 编辑对象和状态

```text
StoryPointData
    ↓ clone
StoryPointDraftModel
    ↓
PointEditorState
```

编辑器只操作 `StoryPointDraftModel`。草稿包含：

- 当前剧情点角色集合。
- 场景集合。
- 当前场景 ID。
- 当前场景背景和 layout。
- 剧情点 style 覆盖。
- 命令序列。
- 当前命令和选项。
- 脏状态。

返回时：

- 保存并返回：校验草稿并提交到 `StoryData`。
- 放弃并返回：丢弃草稿。
- 取消：继续编辑。

## 3. 16:9 画布规范

- 项目默认分辨率为 1920×1080。
- UI 参考画布为 960×540。
- 所有剧情表现和编辑工具坐标以 960×540 为基准。
- 编辑器根节点使用现有 CanvasScaler，不使用超出参考画布的固定窗口。
- 编辑器辅助元素不能出现在正式预览中。

## 4. 页面区域

```text
┌──────────────────────────────────────────────┐
│ 返回   剧情点名称   未保存标记        保存    │
├──────────────────────────────────────────────┤
│                                              │
│              剧情场景表现区域                 │
│       背景 / 角色 / 选中区域 / 提示            │
│                                              │
├──────────────────────────────────────────────┤
│ 头像  角色名                                   │
│       对白/旁白编辑区域                        │
├──────────────────────────────────────────────┤
│ 内容顺序：场景 → 显示 → 对白 → 选择 → ...      │
└──────────────────────────────────────────────┘
```

编辑器 View 可拆分为：

```text
StoryPointEditorView
├── StoryPointToolbarView
├── StorySceneStageView
├── StoryDialogueEditorView
├── StoryCommandTimelineView
├── StoryResourcePickerView
└── StoryActorPickerView
```

## 5. 场景编辑

剧情点场景不是单独的剧情点，而是当前 `StoryPointData.scenes[]` 中的上下文。

编辑流程：

1. 在场景列表中选择或新建场景。
2. 选择地图和背景资源。
3. 选择当前场景允许出现的剧情点角色。
4. 编辑当前场景 layout。
5. 在命令时间线中插入或选择 `scene` 切换命令。

资源选择后只修改草稿中的路径，不复制和覆盖文件。

## 6. 角色编辑

角色资源定义来自剧本角色表，剧情点只保存引用：

```text
StoryData.actorDefinitions
    ↓ 引用
StoryPointData.actorReferences
    ↓ 场景筛选
SceneData.actorIds
```

点击画布中的角色或角色占位区域，可以：

- 选择已有剧情点角色。
- 将剧本角色加入当前剧情点。
- 将角色加入当前场景。
- 修改当前场景中的位置和布局参数。

删除角色前必须检查是否仍被 `show`、`hide` 或 `say` 命令引用。

## 7. 对白、旁白和文本样式

- `say` 编辑角色和对白文本。
- `narrate` 编辑旁白文本。
- 文本样式解析使用运行时默认 → 剧本级 style → 剧情点级 style。
- 第一阶段不在每条命令上开放独立 style。
- 专有名词、富文本和悬浮解释由剧情文本表现系统负责，编辑器不复制另一套解析器。

## 8. 命令时间线

时间线显示命令顺序，但不把命令类型误认为标签：

| 内容 | 持久化语义 |
| --- | --- |
| 场景切换 | `scene` + `sceneId` |
| 显示角色 | `show` + `actorId` |
| 隐藏角色 | `hide` + `actorId` |
| 角色对白 | `say` + `actorId` + `text` |
| 旁白 | `narrate` + `text` |
| 选择 | `choice` + `options[]` |
| 跳转 | `jump` + `targetPointId` |
| 任务/传送 | `mission` / `teleport` |
| 结束 | `end` |

每个命令拥有稳定 `commandId`。时间线排序只改变数组顺序，不改变命令 ID。

## 9. 选择编辑

一个剧情点可以包含多个选择命令。每个选择命令和选项都拥有稳定 ID：

```text
ChoiceData
├── choiceId
└── options[]
    ├── optionId
    ├── text
    ├── targetPointId
    └── conditions[]
```

目标剧情点必须从当前剧本已有剧情点中选择，不能只允许作者手写 ID。

选择历史属于 `StoryRuntimeContext`，编辑器只负责维护合法选择和条件数据。

## 10. MVC 交互边界

```text
StoryPointEditorView
    └── 发出用户意图
        ↓
StoryPointEditorController
        └── 修改 StoryPointDraftModel
            ↓
        StoryValidator
            ↓
        提交 StoryData
```

View 不直接读写 JSON、`Database` 或正式 `StoryData`。

资源选择器只返回资源选择结果；角色选择器只返回角色引用或创建请求；条件编辑器只返回结构化条件。

## 11. 保存和预览

```text
画布操作
    ↓
StoryPointDraftModel
    ↓ 应用
StoryData 内存副本
    ↓ 剧本保存
Mod/Stories/*.json
    ↓
Database.ReloadStoryMod
    ↓
StoryPanel 正式预览
```

预览必须使用保存后的数据，不能把草稿 UI 直接当成运行时剧情播放。

## 12. 第一阶段范围

第一阶段实现：

- 场景列表和当前场景切换。
- 背景选择和预览。
- 剧情点角色集合和场景角色集合。
- `show`、`hide`、`say`、`narrate` 编辑。
- 命令顺序调整。
- 基础 `choice` 和 `jump` 编辑。
- 草稿保存、放弃和校验。

暂不实现：

- 任意元素自由拖拽持久化。
- 复杂条件图编辑器。
- 命令级样式。
- 多人协同编辑。
- 运行时存档回滚。
