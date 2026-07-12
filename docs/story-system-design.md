# 自制剧情系统设计

## 1. 文档定位

本文档描述自制剧情的整体目标、数据层、运行时层和编辑器边界。

权威数据契约见：[自制剧情剧本数据模型与运行时上下文设计](story-data-model-design.md)。

专项文档：

- [自制剧情编辑器 MVC 与闭环设计](story-editor-mvc-design.md)
- [剧情点可视化编辑器设计](story-node-visual-editor-design.md)
- [自制剧情数据作用域与剧情点上下文设计](story-node-data-scope-design.md)
- [剧情文本表现系统设计](story-text-presentation-design.md)

## 2. 当前基线

当前项目已经具备运行时自制剧情原型：

```text
Mod/Stories/*.json
    ↓
SaveSystem.TryLoadStoryMod
    ↓
Database.ReloadStoryMod
    ↓
StoryDocument.ToMissionInfo / ToScript
    ↓
StoryPanel / StoryScript / DialogManager
```

当前运行时数据和校验代码位于：

- `StoryDocument`、`StoryValidator`、`StoryCommandDocument`：`Assets/Scripts/MVC/Model/Basic/Story/StoryScript.cs`
- Mod 剧情加载：`Assets/Scripts/System/SaveSystem.cs`
- Mod 任务注册：`Assets/Scripts/System/Database.cs`
- 正式剧情播放：`Assets/Scripts/MVC/View/Story/StoryPanel.cs`

旧的 `WorkshopStoryPanel` 和上一版编辑器原型已经删除。Workshop 的自制剧情入口暂时停用，下一版编辑器从数据模型和 MVC 边界重新设计，不保留旧 Panel 的迁移路线。

## 3. 功能愿景

作者可以通过 Workshop 中的自制剧情入口，以结构化可视化方式制作剧情，而不需要直接编辑 JSON。

基本创作层级为：

```text
StoryData                         剧本数据
└── StoryPointData[]               剧情点数据
    ├── SceneData[]                场景上下文
    ├── ActorReference[]            角色引用
    ├── CommandData[]               有序命令
    └── ChoiceData / 条件
```

一个剧情点是一段连续剧情，可以包含多个场景、角色、对白、旁白和选择。多个剧情点通过入口、跳转和选择目标组成有向图。

## 4. 核心概念

### 4.1 StoryData

整个可保存、可预览、可注册为 Mod 任务的剧本。当前代码兼容类型为 `StoryDocument`。

包含：

- 剧本 ID、标题和简介。
- 入口剧情点 ID。
- Mod 任务信息。
- 剧本资源定义，包括角色、地图、背景、BGM、NPC 和其他剧情表现资源。
- 剧本级文本样式覆盖。
- 剧情点集合。

剧本数据不保存玩家当前命令位置、角色当前显示状态或本次选择历史。

### 4.2 StoryPointData

剧情点是作者编辑和连接的基本单位。当前代码兼容类型为 `StoryNodeDocument`。

包含：

- 稳定剧情点 ID。
- 本剧情点涉及的角色引用。
- 本剧情点的场景集合。
- 剧情点级文本样式覆盖。
- 有序命令集合。

### 4.3 SceneData

场景是剧情点内部的上下文，不等同于 Unity 完整游戏场景。

场景负责：

- 地图 ID 对应的背景图片。
- 进入场景时自动播放的 BGM。
- 当前场景允许出现的角色。
- 当前场景角色 layout。

一个剧情点可以通过 `scene` 命令在多个剧情背景场景之间切换。这里不切换 Unity Scene，也不传送玩家；切换后按新场景的角色集合和 layout 重新加载角色，角色显示状态仍由 `show` / `hide` 命令控制。

### 4.4 CommandData

命令不是单纯标签。序列化字段 `type` 只是命令类型标识，完整命令由类型、明确字段和执行语义组成。第一阶段不使用统一的字符串 `parameters`，也不支持命令嵌套。

第一阶段命令：

```text
scene / show / hide / say / narrate
choice / jump / mission / teleport / end
```

稳定 ID 与命令类型必须区分：

- `pointId`：剧情点身份。
- `commandId`：命令身份。
- `choiceId`：选择命令身份。
- `optionId`：选择项身份。
- `type`：命令类型。

数组下标只表示执行和显示顺序，不能作为长期引用。

### 4.5 StoryRuntimeContext

运行时上下文表示玩家本次游玩的状态：

```text
StoryRuntimeContext
├── currentPointId
├── currentSceneId
├── currentCommandId / index
├── visibleActors
└── choiceHistory[]
```

选择历史可以记录同一剧情点内多个选择的有序结果，并供后续条件判断使用。选择后继续执行当前剧情点；剧情点结束后，后续 `jump` 根据 `choiceHistory` 决定进入哪个剧情点。它不属于作者保存的 `StoryData`，也不写入存档。

## 5. 运行时层

运行时继续复用现有 `StoryScript` 和 `StoryCommand`，但目标职责如下：

```text
StoryPanel
└── StoryController / StoryPlayer
    └── StoryPresentationView
        ├── StorySceneView
        ├── StoryActorView
        ├── StoryDialogueView
        └── StoryChoiceView
```

职责边界：

- `StoryController / StoryPlayer`：推进命令、处理等待、跳转和条件。
- `StoryPresentationView`：组合场景、角色、对白和选项表现。
- `StoryPanel`：管理全屏容器和生命周期。
- `DialogManager`：作为现有对白表现实现逐步收敛，不应成为剧情数据或流程控制的直接依赖。

编辑器和运行时可以共享资源加载、角色布局和文本表现组件，但不能共享同一份 UI 生命周期状态。

## 6. 编辑器层

编辑器目标结构：

```text
StoryRepository
    ├── 加载 / 保存 / 删除 / 枚举
    └── 校验

StoryEditorModel
    ├── 当前 StoryData
    ├── 剧本列表
    └── 当前选择状态

StoryManagementController
    └── 剧本级操作

StoryPointDraftModel
    └── 当前剧情点的临时编辑数据

StoryPointEditorController
    └── 场景、角色、命令、选项和样式操作
```

View 不直接读写文件，也不直接修改正式 `StoryData`。剧情点编辑先进入草稿，提交后再写回 Model，最终由 Repository 保存。

详细职责见 [自制剧情编辑器 MVC 与闭环设计](story-editor-mvc-design.md)。

## 7. 资源规则

资源继续复用现有资源系统，但 JSON 保存逻辑资源路径，资源路径本身作为资源 ID，不强制要求每个资源提前注册：

- 可选资源声明使用 `kind` 提供编辑器提示和严格类型校验。
- `source` 支持 `auto`、`mod`、`builtin`；`auto` 遵循 Mod 优先、本体回退。
- 资源缺失时校验失败并阻止剧情加载。
- 资源选择器只负责枚举、选择和预览，不复制或覆盖文件。

## 8. 保存和预览

```text
用户编辑
    ↓
StoryPointDraftModel
    ↓ StoryValidator
提交到 StoryData
    ↓
StoryRepository.Save
    ↓
Database.ReloadStoryMod
    ↓
StoryPanel 只读预览
```

预览必须使用保存后的数据，不能直接播放尚未提交的 UI 草稿。

当前阶段所有自制剧情使用 `replayable=true`，允许反复进入测试；选择历史只保存在本次运行上下文。

## 9. 代码映射与版本规则

当前代码类型作为兼容基础：

| 设计语义 | 当前代码 |
| --- | --- |
| `StoryData` | `StoryDocument` |
| `StoryPointData` | `StoryNodeDocument` |
| `CommandData` | `StoryCommandDocument` |
| 运行时命令 | `StoryCommand` |
| 选择数据 | `StoryChoiceDocument` |

当前代码类型仅作为新模型的实现参考，不保留旧 JSON 兼容要求。现有唯一旧版“法拉的梦” JSON 在改造代码时直接修改为新格式，不额外维护迁移层。新字段仍通过 `schemaVersion` 管理。

## 10. 当前未实现但已确定边界的内容

- 条件编辑器的复杂可视化。
- 预览模式与正式任务状态的完整隔离。
- 词典维护和文本表现编辑器。
- 运行时存档回滚。

这些是后续能力，不改变已经确定的场景、分支、资源和文本数据模型。
