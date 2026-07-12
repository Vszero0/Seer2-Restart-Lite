# 自制剧情数据作用域与剧情点上下文设计

> 本文档的具体字段契约以 [自制剧情剧本数据模型与运行时上下文设计](story-data-model-design.md) 为准；本文重点解释作用域、继承和剧情点内上下文。

## 1. 设计结论

自制剧情的基本创作单位是“剧情点”，而不是单条对白或单个场景。一个剧情点描述一段连续的剧情过程，可以包含多个场景、多个角色、多个对话段落、多个旁白和多个选择。

配置数据的作用域区分如下：

```text
运行时默认值
    ↓
剧本级配置
    ↓
剧情点级配置
    ↓
剧情点内场景配置
    ↓
当前内容命令的临时表现
```

其中：

- `layout` 绑定剧情点内的具体场景，因为角色集合和场景关系在剧情点中才是明确的。
- `style` 采用“运行时默认值 → 剧本覆盖 → 剧情点覆盖”的继承机制。

## 2. 剧本层定义

`StoryDocument` 描述整个可保存、可预览、可注册为 Mod 任务的剧本：

```text
StoryDocument
├── schemaVersion
├── id / title / entry
├── mission
├── actors[]                  剧本角色资源注册表
├── style                     可选的剧本级文本样式覆盖
└── nodes[]                   剧情点图
```

剧本层负责保存：

- 标题、简介和唯一 ID。
- Mod 任务信息。
- 剧情点入口。
- 可被多个剧情点复用的角色资源定义。
- 剧本级文本样式覆盖。

剧本层不负责具体角色在场景中的位置。角色位置取决于当前剧情点和当前场景。

## 3. 剧情点层定义

`StoryNodeDocument` 是作者实际编辑的主要单位：

```text
StoryNodeDocument
├── id
├── actors[]                  本剧情点涉及的角色 ID
├── scenes[]                  本剧情点涉及的场景上下文
├── style                     可选的剧情点级文本样式覆盖
└── commands[]                按顺序执行的内容命令
```

### 3.1 剧情点角色集合

剧情点显式维护涉及的角色集合，用于：

- 限制角色选择器候选范围。
- 验证 `show`、`hide`、`say` 引用的角色。
- 为剧情点编辑画布预加载头像、立绘和名称。
- 判断当前场景允许哪些角色出现。

角色资源仍从 `StoryDocument.actors` 按 ID 查找，剧情点只保存引用和必要的剧情点级覆盖。

```text
StoryDocument.actors
    └── actorId

StoryNodeDocument.actors
    └── actorId
```

### 3.2 剧情点场景集合

一个剧情点可以包含多个场景。这里的场景是剧情点内部的场景上下文，不等同于游戏中的完整地图场景。

```text
StoryNodeSceneDocument
├── id
├── mapId
├── background
├── actorIds[]
└── layout
```

场景上下文负责说明：

- 当前剧情发生在哪张地图或地图区域。
- 当前场景使用什么背景。
- 当前场景允许哪些剧情点角色出现。
- 当前场景下角色立绘如何布局。

剧情命令通过 `sceneId` 或 `scene` 命令切换当前场景。剧情点内部可以出现多个场景及其各自的对话和选择。

### 3.3 layout 的新作用域

`layout` 从剧本全局配置调整为场景配置：

```text
StoryNodeSceneDocument.layout
```

它可以描述：

- 角色立绘间距、高度和底部位置。
- 左右角色区域之间的间隔。
- 同侧角色的叠放偏移。
- 角色在当前场景中的固定槽位。

同一个角色可以在不同剧情点、不同场景中拥有不同位置，而不会污染其他剧情点。

## 4. style 的继承设计

文本样式采用三级解析：

```text
ResolvedStyle =
    RuntimeDefaultStyle
    覆盖 StoryDocument.style
    覆盖 StoryNodeDocument.style
```

如果未来需要单条对白特殊样式，可以继续增加 `StoryCommandDocument.style`，但第一阶段不开放命令级样式，避免作者重复配置。

### 4.1 字段级覆盖

样式对象必须区分“未配置”和“明确设置为默认值”。例如 `bold = false` 可能表示关闭加粗，也可能表示作者没有配置该字段。

建议使用显式覆盖语义：

```text
StoryTextStyleOverride
├── font                 为空表示继承
├── fontSize             0 表示继承
├── textColor            为空表示继承
├── outlineColor         为空表示继承
├── outlineWidth         负值表示继承
├── bold                 实际值
└── boldSpecified        是否覆盖 bold
```

具体字段需要适配 Unity `JsonUtility`，不能只依赖 C# nullable 字段。

## 5. 剧情点内的分支上下文

一个剧情点内部可能在多个位置出现选择：

```text
选择点 A：选择选项 1
    ↓
继续剧情
    ↓
选择点 B：选择选项 2
    ↓
本剧情点的选择序列 = [A:1, B:2]
```

这组选择结果属于当前剧情运行上下文，可供后续剧情点进行条件判断。

### 5.1 选择必须具有稳定 ID

不能只使用数组下标作为选择标识，因为作者调整选项顺序后，下标会变化。

建议扩展为：

```text
StoryChoiceDocument
├── id
├── text
└── target
```

运行时记录：

```text
StoryChoiceSelection
├── nodeId
├── commandId
├── choiceId
└── optionId
```

其中 `commandId`、`choiceId` 和 `optionId` 应由编辑器生成并持久化。

### 5.2 分支历史

当前剧本运行实例维护有序的分支历史：

```text
StoryBranchContext
└── selections[]
    ├── nodeId
    ├── commandId
    ├── choiceId
    └── optionId
```

它可以支持：

- 是否在某个剧情点选择过某个选项。
- 某个选择点的最终选择是什么。
- 是否按指定顺序完成多个选择。
- 是否满足多个选择结果的组合条件。

### 5.3 条件判断方向

后续可以在跳转命令或选项目标上增加结构化条件：

```text
choice option
├── text
├── target
└── conditions[]
```

第一阶段建议支持：

```text
ChoiceSelected
ChoiceSequenceMatched
StoryFlag
MissionState
```

条件系统不与 UI 绑定，编辑器只生成合法数据，运行时 StoryScript 负责解释执行。

## 6. 对编辑器 MVC 的影响

新的 Model 分层应调整为：

```text
StoryRepository
    └── 剧本文件加载、保存、删除、校验

StoryEditorModel
    └── 当前剧本、剧情点列表、选择状态

StoryNodeDraftModel
    ├── 当前剧情点角色集合
    ├── 当前剧情点场景集合
    ├── 当前场景 layout
    ├── 当前剧情点 style 覆盖
    ├── commands 顺序
    └── choices 与目标
```

Controller 分工为：

```text
StoryManagementController
    ├── 创建/删除/选择剧本
    ├── 编辑剧本元数据
    ├── 创建/删除/选择剧情点
    └── 进入剧情点编辑

StoryNodeEditorController
    ├── 编辑剧情点角色集合
    ├── 编辑剧情点场景集合
    ├── 编辑场景 layout
    ├── 编辑剧情点 style 覆盖
    ├── 编辑命令顺序
    └── 编辑分支条件
```

View 不直接修改 `StoryDocument`。剧情点编辑先进入 `StoryNodeDraftModel`，点击应用或保存时再提交，并由 `StoryValidator` 统一校验。

## 7. 对现有数据结构的调整方向

1. 新格式将 layout 移入剧情点场景。
2. `StoryDocument.style` 保留，定义为剧本级样式覆盖。
3. `StoryNodeDocument` 增加剧情点级 `style` 和场景集合。
4. `StoryCommandDocument.scene` 或 `sceneId` 用于切换剧情点内场景。
5. `StoryChoiceDocument` 增加稳定 ID。
6. 为旧 JSON 保留读取兼容逻辑，将旧的全局 layout 映射为入口剧情点的默认场景布局。

在上述模型确定前，不开始新的 Panel 和剧情点编辑 UI 实现。
