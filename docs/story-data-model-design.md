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
├── replayable
└── storyStyleOverride?
```

它负责描述跨剧情点共享的信息：

- 剧本身份和显示名称。
- Mod 任务注册信息。
- 入口剧情点。
- 本剧本需要引用的资源定义，包括角色、地图、背景、BGM、NPC 和其他表现资源。
- 剧情是否允许重复进入；测试阶段所有自制剧情默认可重复。
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
├── mapId
├── bgmResourcePath?
├── actorIds[]
└── layout
```

场景负责确定：

- 当前剧情使用的地图 ID；该 ID 用于定位对应的地图背景图片，不代表 Unity Scene。
- 当前场景进入时自动播放的 BGM。
- 当前场景允许出现的剧情点角色。
- 角色立绘位置、缩放和层叠方式。

执行 `scene` 命令时只切换剧情背景图片和 BGM，不切换 Unity Scene、不传送玩家。进入新场景后，运行时按照新场景的角色集合和 layout 重新加载角色；角色的显示或隐藏状态仍由 `show` / `hide` 命令控制。

一个剧情点内部可以有：

```text
scene_a → 对话 → 选择 → scene_b → 旁白 → 选择
```

命令序列负责时间顺序，场景数据负责当前场景的静态上下文。

## 3. 命令模型

命令不是单纯的标签。序列化后的 `type` 只是命令类型标识，完整命令由类型、明确字段和执行语义组成：

```text
CommandData = commandId + type + type-specific fields + execution semantics
```

当前代码中的 `StoryCommandDocument` 是兼容载体，运行时通过 `ToCommand()` 转换成可执行的 `StoryCommand`。

第一阶段不使用统一的字符串 `parameters` 承载命令内容，而是为每种 `type` 使用明确字段。第一阶段也不支持命令嵌套；`commands[]` 始终表示当前剧情点内的平面有序序列。条件命令和条件显示通过条件字段实现，不通过嵌套命令实现。

### 3.1 命令类型

| 类型 | 作用 | 主要数据 |
| --- | --- | --- |
| `scene` | 设置或切换剧情背景场景 | `sceneId`、`mapId`、`bgmResourcePath` |
| `show` | 显示角色 | `actorId` |
| `hide` | 隐藏角色 | `actorId` |
| `say` | 角色对白 | `actorId`、`text` |
| `narrate` | 旁白 | `text` |
| `choice` | 显示选择并等待玩家选择 | `choices[]` |
| `jump` | 跳转剧情点 | `targetPointId`、条件 |
| `mission` | 修改任务状态 | 任务参数 |
| `teleport` | 执行传送 | 地图参数 |
| `end` | 结束剧情 | 无 |

第一阶段命令使用明确的类型字段，不允许将业务数据塞入统一字符串 `parameters`。对白不绑定表情、动作或音效，`show` 不负责位置和动画。`scene` 负责同时切换背景和 BGM；命令条件和条件显示通过 `ConditionGroup` 表达。

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

运行时从当前命令开始执行。`choice` 会暂停命令执行并等待玩家输入，记录选择后继续执行当前剧情点的下一条命令，不立即跳转。当前剧情点结束后，由后续 `jump` 根据 `choiceHistory` 判断目标剧情点。`jump` 会离开当前剧情点；`end` 结束当前剧本。选择可以通过条件再次跳回当前剧情点。

编辑器可以调整命令顺序，但保存前必须通过命令参数和引用校验。

### 3.4 ChoiceData

第一阶段的选择只记录玩家选择，不直接携带跳转目标：

```text
ChoiceData
├── choiceId
└── options[]
    ├── optionId
    └── text
```

玩家选择后继续执行当前剧情点的下一条命令。剧情点结束后，后续 `jump` 命令通过 `ConditionGroup` 查询一个或多个选择结果，再决定目标剧情点；目标也可以是当前剧情点，从而支持重复进入和循环分支。

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

`StoryData.resourceDefinitions[]` 是本剧本的可选资源声明表。它只登记本剧本会用到的资源元数据，不复制资源文件本身；命令或角色字段也可以直接引用未登记的逻辑路径，只要资源实际存在并通过类型校验：

```text
StoryResourceDefinition
├── path                逻辑资源路径，同时作为剧本内资源 ID
├── kind                资源语义类型
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
    └── path, kind, source

StoryData.actorDefinitions[]
    └── actorId, actorType, name, spriteResourcePath, iconResourcePath, battleResourcePath?, default layout

StoryPointData.actorReferences[]
    └── actorId, optional point override

SceneData.actorIds[]
    └── 当前场景允许出现的角色
```

约束如下：

1. `say`、`show`、`hide` 引用的角色必须存在于剧情点角色集合。
2. 场景的 `actorIds` 必须来自剧情点角色集合。
3. 角色定义中的立绘、头像和战斗图使用逻辑资源路径；精灵角色通过 `petId` 推导本体资源，NPC 和自定义角色使用各自的资源定义。如果存在资源注册声明，则必须通过 `kind` 校验。
4. 场景通过 `mapId` 引用地图背景，通过 `bgmResourcePath` 引用 BGM；地图不表示 Unity Scene，也不触发真实传送。
5. 资源路径只维护在 `StoryResourceDefinition` 或明确的资源覆盖字段中。
6. 场景 layout 只描述当前场景角色的表现，不修改资源定义。

角色名称固定使用游戏中的角色名称，不提供剧情点级名称覆盖。同一角色在本剧本中使用同一套立绘；第一阶段不设计表情、姿势或受伤状态等资源变体。角色定义必须包含默认位置、默认缩放和默认朝向，编辑器以左右两侧的默认角色区域初始化，作者之后可以拖动调整。

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

新模型不再使用剧本级 `layout`；布局只存在于 `SceneData.layout`。旧“法拉的梦”中的全局布局在改写 JSON 时直接移动到入口场景。

## 6. style 继承

文本样式采用字段级合并：

```text
RuntimeDefaultStyle
    覆盖 StoryData.style
    覆盖 StoryPointData.style
```

第一阶段开放运行时默认值、剧本级覆盖和剧情点级覆盖三层；命令级样式暂不实现；对白文本也不单独绑定 style。

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

选择历史只属于本次运行实例，不写入存档；重复进入剧情时重新建立本次运行上下文。它不能混入 `StoryData`。测试阶段所有自制剧情都允许重复进入。

### 7.2 条件

条件可以挂在命令、选项目标或跳转目标上：

```text
BranchTarget
├── targetPointId
└── conditionGroup?
```

统一使用 `ConditionGroup` 表达条件组合：

```text
ConditionGroup
├── operatorType: AND / OR
└── conditions[]
```

条件节点可以是：

- `ChoiceSelected`：某个选择项是否被选中。
- `ChoiceSequenceMatched`：选择历史是否匹配指定序列。
- `StoryFlag`：剧本运行标记。
- `MissionState`：任务状态。

由于 Unity `JsonUtility` 的字段限制，JSON 使用 `operatorType` 表示组合运算符。`ConditionGroup.operatorType` 由作者显式选择，不规定默认必须是 AND 或 OR。编辑器应要求作者明确选择组合逻辑，避免依赖数组顺序猜测语义。

第一阶段的 `ConditionGroup` 不嵌套其他条件组；需要更复杂的逻辑时，先通过多个 `jump` 命令和多个条件组表达。后续如果需要嵌套条件，应改用非递归的条件树序列化结构，不能直接把 `ConditionGroup` 作为自身的数组字段。

## 8. 持久化数据与运行时数据边界

```text
Mod/Stories/story_id.json
    └── StoryData，仅保存作者制作内容

StoryRuntimeContext
    └── 保存本次运行进度和选择历史，不写入存档
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
Mod/Stories/story_id.json
```

预览使用保存后的 `StoryData`，不直接播放尚未提交的 UI 草稿。当前只有一份旧版“法拉的梦” JSON，改造代码时直接修改为新格式，不额外设计旧 JSON 兼容层。

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

## 11. 版本规则与当前未实现项

1. 新格式增加字段时必须提高 `schemaVersion`。
2. 当前只有一份旧版“法拉的梦” JSON，改造代码时直接修改为新格式，不额外维护旧 JSON 迁移层。
3. 资源缺失、资源类型不匹配、引用 ID 不存在或条件目标不存在时，校验失败并阻止剧情加载。
4. 条件编辑器、预览状态隔离和复杂条件可视化属于后续编辑器能力，不改变当前数据模型。
