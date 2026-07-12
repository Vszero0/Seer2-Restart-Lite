# 自制剧情剧本数据模型与运行时上下文设计

## 1. 文档定位

本文档是自制剧情系统的数据契约，优先级高于各个 UI 和编辑器专题文档。

本文档解决四个问题：

1. 一个剧本由什么组成。
2. 剧情点、场景、命令和分支之间如何组织。
3. 哪些数据属于作者制作内容，哪些数据属于玩家本次运行状态。
4. Model、Controller、Repository 的边界是什么。

当前项目中的 `StoryDocument`、`StoryNodeDocument` 和 `StoryCommandDocument` 是这一设计的兼容基础，但本文档描述的是下一版稳定语义，不代表所有字段已经完成代码迁移。

## 2. 总体分层

自制剧情使用两层作者数据，加一层运行时上下文：

```text
StoryData                       剧本数据
├── resourceDefinitions[]        剧本资源定义
└── StoryPointData[]             剧情点数据
    ├── SceneData[]              场景上下文
    ├── ActorReference[]          角色引用
    ├── CommandData[]             有序命令
    └── PointStyleOverride        剧情点样式覆盖

StoryRuntimeContext              本次游玩状态
├── currentPointId
├── currentSceneId
├── currentCommandId/index
├── visibleActors
└── choiceHistory[]
```

### 2.1 StoryData

`StoryData` 是作者制作并保存的完整剧本。当前代码中对应 `StoryDocument`。

```text
StoryData
├── schemaVersion
├── id
├── title
├── description/summary
├── entryPointId
├── missionData
├── resourceDefinitions[]
└── storyStyleOverride?
```

它负责描述跨剧情点共享的信息：

- 剧本身份和显示名称。
- Mod 任务注册信息。
- 入口剧情点。
- 本剧本需要引用的资源定义，包括角色、地图、背景、BGM、NPC 和其他表现资源。
- 剧本级文本样式覆盖。

它不负责当前角色的位置、当前背景、当前命令索引或玩家选择历史。

### 2.2 StoryPointData

`StoryPointData` 是作者编辑和连接的基本单位。当前代码中对应 `StoryNodeDocument`。

```text
StoryPointData
├── id
├── displayName?
├── actorReferences[]
├── scenes[]
├── pointStyleOverride?
└── commands[]
```

一个剧情点表示一段连续的剧情过程，不要求只有一个场景。它可以包含：

- 多个场景切换。
- 多个角色之间的对话。
- 多段旁白。
- 多个选择点。
- 根据选择继续、跳转或结束。

### 2.3 SceneData

`SceneData` 是剧情点内部的场景上下文，不等同于 Unity 的完整游戏场景对象。

```text
SceneData
├── id
├── mapResourceId?
├── backgroundResourceId?
├── bgmResourceId?
├── actorIds[]
└── layout
```

场景负责确定：

- 当前剧情发生的地图或地图区域。
- 当前背景资源。
- 当前场景允许出现的剧情点角色。
- 角色立绘位置、缩放和层叠方式。

一个剧情点内部可以有：

```text
scene_a → 对话 → 选择 → scene_b → 旁白 → 选择
```

命令序列负责时间顺序，场景数据负责当前场景的静态上下文。

## 3. 命令模型

命令不是单纯的标签。序列化后的 `type` 只是命令类型标识，完整命令由类型、参数和执行语义组成：

```text
CommandData = commandId + type + parameters + execution semantics
```

当前代码中的 `StoryCommandDocument` 是兼容载体，运行时通过 `ToCommand()` 转换成可执行的 `StoryCommand`。

### 3.1 命令类型

| 类型 | 作用 | 主要数据 |
| --- | --- | --- |
| `scene` | 设置或切换场景 | `sceneId`、背景兼容字段 |
| `show` | 显示角色 | `actorId` |
| `hide` | 隐藏角色 | `actorId` |
| `say` | 角色对白 | `actorId`、`text` |
| `narrate` | 旁白 | `text` |
| `choice` | 显示选择并等待玩家选择 | `choices[]` |
| `jump` | 跳转剧情点 | `targetPointId`、条件 |
| `mission` | 修改任务状态 | 任务参数 |
| `teleport` | 执行传送 | 地图参数 |
| `end` | 结束剧情 | 无 |

### 3.2 命令 ID 与标签

以下概念需要区分：

- `commandId`：命令自身的稳定身份，供编辑器、错误定位和分支历史引用。
- `pointId`：剧情点身份，供入口和跳转引用。
- `choiceId`：选择命令身份。
- `optionId`：某个选择项身份。
- `type`：命令类型，不是跳转标签。

数组下标只表示当前显示顺序，不能作为长期引用。编辑器新增命令和选项时应生成稳定 ID；调整顺序不能改变 ID。

### 3.3 命令执行顺序

同一剧情点内，`commands[]` 是有序序列：

```text
scene
show
say
choice
hide
end
```

运行时从当前命令开始执行。`choice` 会暂停命令执行并等待玩家输入；`jump` 会离开当前剧情点；`end` 结束当前剧本。

编辑器可以调整命令顺序，但保存前必须通过命令参数和引用校验。

## 4. 剧本资源定义与场景引用

本体资源目录表明，剧情可引用的资源不应被限制为角色。当前本体资源至少包含以下资源域：

```text
Resources/
├── BGM/          音频，按分类目录组织，主要为 .mp3
├── Maps/         地图背景、战斗背景、路径、动物等图片或动图资源
├── Npc/          NPC 图片资源
├── Pets/         精灵头像、立绘、战斗图等图片资源
├── Panel/        UI 面板图片资源
└── Activities/   活动图片及平台相关资源
```

Mod 资源也沿用类似的逻辑资源路径，并由资源加载层决定从本体资源还是 Mod 资源中解析。剧情数据不应把物理文件路径、资源类型和角色语义混为一体。

### 4.1 StoryResourceDefinition

`StoryData.resourceDefinitions[]` 是本剧本的资源注册表。它只注册本剧本会用到的资源，不复制资源文件本身：

```text
StoryResourceDefinition
├── id                  剧本内稳定资源 ID
├── kind                资源语义类型
├── path                逻辑资源路径
├── source?             builtin / mod / auto
└── metadata?           可选的编辑器展示信息
```

`kind` 第一阶段建议支持：

| kind | 用途 | 典型逻辑路径 |
| --- | --- | --- |
| `sprite` | 通用图片或立绘 | `Sprites/...`、`Activities/...` |
| `actorSprite` | 精灵/NPC 的剧情立绘 | `Pets/pet/10`、`Npc/10001` |
| `actorIcon` | 角色头像或图标 | `Pets/icon/10`、`Npc/10001` |
| `mapBackground` | 地图或剧情背景 | `Maps/bg/121` |
| `audio` | BGM 或其他音频 | `BGM/101/BGM_1` |
| `map` | 地图上下文标识 | 地图 ID 或地图逻辑资源标识 |
| `ui` | 剧情专用 UI 或装饰资源 | `Panel/...` |

`kind` 是编辑器和校验使用的语义提示，不应直接决定所有加载细节；最终资源加载仍由统一资源解析器根据逻辑路径、资源类型和来源处理。

### 4.2 资源解析规则

剧情 JSON 保存的是逻辑资源路径，不直接保存 `Application.persistentDataPath` 下的物理路径。资源注册表中的 `source` 用于表达来源偏好：

- `auto`：优先查找当前 Mod 资源，找不到时回退本体资源；这是面向作者的默认行为。
- `mod`：只查找当前 Mod 资源，资源缺失时校验或运行时报告错误。
- `builtin`：只查找本体资源，不允许被 Mod 同名资源覆盖。

逻辑路径通常不包含最终文件扩展名，由资源解析器根据 `kind` 和资源加载方式补全。例如图片可以使用 `Maps/bg/121`，音频可以使用 `BGM/101/BGM_1`。如果某类资源必须保留扩展名，应由该资源类型的解析器统一处理。

当前项目的 `ResourceManager.GetLocalAddressables` 已经按照 `Resources/` 与 `Mod/` 两个资源根目录加载图片和音频；新剧情资源层应在此基础上统一来源策略，而不是让每个命令自行拼接物理路径。

### 4.3 角色定义是资源引用的组合

角色不是独立于资源系统的特殊文件类型，而是由角色身份信息和多个资源引用组成：

```text
StoryData.resourceDefinitions[]
    └── resourceId, kind, path, source

StoryData.actorDefinitions[]
    └── actorId, name, spriteResourceId, iconResourceId, default properties

StoryPointData.actorReferences[]
    └── actorId, optional point override

SceneData.actorIds[]
    └── 当前场景允许出现的角色
```

约束如下：

1. `say`、`show`、`hide` 引用的角色必须存在于剧情点角色集合。
2. 场景的 `actorIds` 必须来自剧情点角色集合。
3. 角色定义引用的 `spriteResourceId`、`iconResourceId` 必须存在于剧本资源注册表，且资源类型匹配。
4. 场景背景、BGM 和地图上下文也应引用资源注册表中的资源，不能在多个命令中重复维护未经登记的路径。
5. 资源路径只维护在 `StoryResourceDefinition` 或明确的资源覆盖字段中。
6. 场景 layout 只描述当前场景角色的表现，不修改资源定义。

这样同一个角色可以在不同剧情点或不同场景拥有不同位置，同一首 BGM 或同一张背景也可以被多个剧情点复用，而不会污染资源定义。

## 5. layout 作用域

新的 layout 作用域为场景级：

```text
SceneData.layout
```

建议包含：

```text
SceneLayout
├── actorSpacing
├── actorHeight
├── actorBottom
├── centerGap
├── stackOffset
└── actorSlots[]?
```

如果剧情点中的多个场景需要相同布局，可以通过编辑器复制布局值，但不默认共享同一个可变对象。

当前 `StoryDocument.layout` 只作为旧 JSON 兼容字段：读取旧数据时，可以将其迁移为入口剧情点第一个场景的默认 layout；新编辑器不再把它作为剧本级布局配置入口。

## 6. style 继承

文本样式采用字段级合并：

```text
RuntimeDefaultStyle
    覆盖 StoryData.style
    覆盖 StoryPointData.style
    覆盖 当前命令样式（未来扩展）
```

第一阶段只开放前三级，命令级样式暂不实现。

### 6.1 覆盖语义

必须区分“没有配置”和“明确设置为默认值”。例如 `bold: false` 可能是作者明确关闭加粗，也可能是没有设置。

推荐使用明确的 override 语义：

```text
TextStyleOverride
├── font                    空值 = 继承
├── fontSize                0 = 继承
├── textColor               空值 = 继承
├── outlineColor            空值 = 继承
├── outlineWidth            负值 = 继承
├── bold                    实际值
└── boldSpecified           是否覆盖 bold
```

具体序列化方式需要适配 Unity `JsonUtility`，不能直接依赖 nullable 字段。

## 7. 分支和运行时上下文

作者制作的剧本数据不保存玩家当前走到了哪里。玩家本次游玩的状态保存在 `StoryRuntimeContext`：

```text
StoryRuntimeContext
├── storyId
├── currentPointId
├── currentSceneId
├── currentCommandId/index
├── visibleActors
└── choiceHistory[]
```

### 7.1 选择历史

剧情点内部允许出现多个选择，因此选择历史必须是有序集合：

```text
ChoiceHistoryEntry
├── pointId
├── commandId
├── choiceId
└── optionId
```

例如：

```text
point_a / choice_a / option_1
point_a / choice_b / option_2
```

可以表示玩家在同一个剧情点的两个选择位置分别做出了不同选择。

选择历史默认属于本次运行实例。是否写入存档、重复进入时是否清空，需要由任务和存档策略另行决定，但不能混入 `StoryData`。

### 7.2 条件

后续条件可以挂在选项目标或跳转目标上：

```text
BranchTarget
├── targetPointId
└── conditions[]
```

第一阶段建议支持：

- `ChoiceSelected`：某个选择项是否被选中。
- `ChoiceSequenceMatched`：选择历史是否匹配指定序列。
- `StoryFlag`：剧本运行标记。
- `MissionState`：任务状态。

多个条件的组合关系必须显式定义，默认采用 AND；需要 OR 时使用条件组表达，避免依赖数组顺序猜测语义。

## 8. 持久化数据与运行时数据边界

```text
Mod/Stories/*.json
    └── StoryData，仅保存作者制作内容

SaveSystem / StoryRuntimeContext
    └── 保存玩家本次剧情进度和选择历史（如果任务需要）
```

编辑器临时草稿不直接写入 JSON：

```text
StoryData
    ↓ clone
StoryPointDraft
    ↓ 用户编辑
StoryValidator
    ↓ 提交
StoryData
    ↓ 保存
Mod/Stories/*.json
```

预览使用保存后的 `StoryData`，不直接播放尚未提交的 UI 草稿。

## 9. MVC 和 Repository 职责

```text
StoryRepository
├── Load
├── Save
├── Delete
├── Enumerate
└── Validate / migrate

StoryEditorModel
├── 当前剧本
├── 剧本列表
├── 当前剧情点
└── 管理页选择状态

StoryPointDraftModel
├── 角色集合
├── 场景集合
├── layout/style 覆盖
├── 命令序列
└── 选项与条件

StoryManagementController
└── 剧本级操作

StoryPointEditorController
└── 剧情点级操作
```

View 只负责展示和发出用户意图，不直接访问文件系统、`Database` 或 `StoryDocument`。

## 10. 现有代码映射

| 新设计语义 | 当前代码 |
| --- | --- |
| `StoryData` | `StoryDocument` |
| `StoryPointData` | `StoryNodeDocument` |
| `CommandData` | `StoryCommandDocument` |
| 运行时命令 | `StoryCommand` |
| 选项数据 | `StoryChoiceDocument` |
| 运行时选项 | `StoryChoice` |
| 任务数据 | `StoryMissionDocument` |
| 角色定义 | `StoryActorDocument` |
| 文件加载 | `SaveSystem.TryLoadStoryMod()` |
| 任务注册 | `Database.ReloadStoryMod()` |
| 运行时播放 | `StoryPanel` / `StoryScript` |

当前 `WorkshopStoryEditorModel` 可作为编辑业务参考，但它仍然混合文件读写、正式数据修改和选择状态；下一版应按本文档拆分 `StoryRepository`、`StoryEditorModel` 和 `StoryPointDraftModel`。

## 11. 版本迁移原则

1. 新格式增加字段时必须提高 `schemaVersion`。
2. 旧 JSON 缺少场景时，读取器创建一个默认场景。
3. 旧的全局 layout 映射到入口剧情点默认场景。
4. 旧的 `scene.bg` 和 `scene.args` 在读取时转换为场景背景。
5. 旧选项没有 ID 时，读取器生成稳定 ID，并在下一次保存时写回。
6. 无法安全推断的数据不得静默丢弃，应报告迁移警告。

## 12. 当前明确不冻结的内容

以下内容需要在实现前单独确认：

- 场景是由 `scenes[]` 声明并由命令引用，还是完全由 `scene` 命令隐式创建。
- 角色在场景切换时默认保留、隐藏还是重置。
- 选择历史是否写入玩家存档。
- 选择条件的完整表达式和编辑方式。
- 命令是否允许局部样式覆盖。
- 旧 JSON 的最终迁移策略。
