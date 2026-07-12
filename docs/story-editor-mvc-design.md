# 自制剧情编辑器 MVC 与闭环设计

> 当前基线：旧的单体剧情 Panel 和上一版编辑器原型已经删除，Workshop 入口暂时停用。本文件描述下一版目标架构，不代表当前已有可用编辑器。

权威数据定义见：[自制剧情剧本数据模型与运行时上下文设计](story-data-model-design.md)。

## 1. 目标

编辑器要让作者完成以下闭环：

```text
创建 StoryData
    ↓
创建并组织 StoryPointData
    ↓
编辑场景、角色、命令和选择
    ↓
校验并保存 Mod/Stories/*.json
    ↓
使用保存后的数据预览
```

作者不需要直接接触 JSON、编码、资源真实路径或运行时命令对象。

## 2. MVC 分层

```text
StoryRepository
    └── 文件系统、序列化、校验

StoryEditorModel
    └── 剧本列表、当前剧本、当前剧情点和管理页状态

StoryManagementController
    └── 剧本级交互

StoryPointDraftModel
    └── 当前剧情点的编辑草稿

StoryPointEditorController
    └── 剧情点级交互

StoryManagementView / StoryPointEditorView
    └── 只展示状态并发出用户意图
```

### 2.1 StoryRepository

负责：

- 枚举 `Mod/Stories/*.json`。
- 加载和反序列化 `StoryData`。
- 保存、删除和刷新文件。
- 调用 `StoryValidator`。
- 使用 UTF-8 读写 `Mod/Stories/story_id.json`。
- 报告带有文件路径和数据位置的错误。

Repository 不保存 UI 选择状态，也不负责打开 Panel。

### 2.2 StoryEditorModel

负责剧本管理状态：

- 当前剧本列表。
- 当前选中的剧本路径和 `StoryData`。
- 当前选中的剧情点 ID。
- 当前是否有未保存的剧本级修改。
- 剧本级元数据编辑。
- 创建、删除、选择剧情点。

它不直接持有 Unity UI 对象，也不负责剧情点画布的临时布局状态。

### 2.3 StoryPointDraftModel

进入剧情点编辑时，从正式 `StoryPointData` 创建独立草稿：

```text
StoryPointData
    ↓ clone
StoryPointDraftModel
```

草稿负责：

- 剧情点角色集合。
- 场景集合和当前场景。
- 场景 layout。
- 剧情点 style 覆盖。
- 命令顺序和当前命令。
- 选择项、目标和条件。
- 草稿脏状态。

草稿不能持有 Unity UI 对象，也不能直接写入 JSON。

### 2.4 StoryManagementController

负责：

- 打开和关闭剧本管理页。
- 创建、删除、刷新和选择剧本。
- 编辑剧本数据。
- 创建、删除、重命名和选择剧情点。
- 设置入口剧情点。
- 进入剧情点编辑。
- 处理剧本级保存、预览和未保存确认。

### 2.5 StoryPointEditorController

负责：

- 创建和销毁剧情点草稿。
- 编辑角色集合和场景集合。
- 切换当前场景。
- 编辑场景背景和 layout。
- 调整命令顺序。
- 新增、删除和编辑命令。
- 编辑选项和条件。
- 调用资源选择器、角色选择器和文本编辑器。
- 保存草稿或放弃草稿。

Controller 不创建具体控件，也不拼接 JSON。

## 3. 页面状态

```text
ManagementState
├── 剧本列表
├── 剧情点列表
├── 剧本数据摘要
└── 进入剧情点编辑

PointEditorState
├── 当前剧情点草稿
├── 当前场景
├── 当前命令
└── 当前选择项
```

两个状态之间通过明确的进入、提交、放弃和取消流程切换：

```text
管理页
    ├── 进入编辑 → 创建 PointDraft
    ├── 保存返回 → 校验并提交 PointDraft
    ├── 放弃返回 → 丢弃 PointDraft
    └── 取消返回 → 保持 PointEditorState
```

存在未保存修改时不得静默关闭。

## 4. 剧本管理页职责

剧本管理页只负责剧本和剧情点组织，不直接展开所有命令字段：

- 剧本列表。
- 新建和删除剧本。
- 剧本标题、简介、任务地图、可重复属性。
- 剧情点列表和入口标记。
- 新建、删除、重命名剧情点。
- 进入剧情点编辑。
- 保存、预览和错误提示。

剧本数据和剧情点数据的具体字段以权威数据模型文档为准。

## 5. 剧情点编辑器职责

剧情点编辑器采用接近实际剧情播放的画布：

- 当前场景背景。
- 当前场景中的角色。
- 角色头像、名称和对白框。
- 旁白文本。
- 选择按钮。
- 当前命令序列和编辑工具栏。

画布表现属于 View，选中框、输入焦点、拖拽手柄和编辑提示不能写入 `StoryPointData`。

## 6. 保存与校验

### 6.1 剧情点草稿保存

```text
用户操作
    ↓
StoryPointDraftModel
    ↓
StoryValidator.Validate
    ↓ 成功
提交回 StoryEditorModel 的 StoryData
```

“应用剧情点”只提交到内存中的剧本数据；剧本级“保存”才写入 JSON。

### 6.2 校验范围

至少校验：

- 剧本 ID 和任务信息。
- 入口剧情点存在。
- 剧情点 ID 唯一。
- 命令 ID 唯一。
- 角色引用存在且属于当前剧情点。
- 场景角色引用合法。
- 跳转目标存在。
- 选项 ID 唯一且目标合法。
- 对白和旁白不为空。
- 剧情点有明确出口。
- 条件引用的选择和标记存在。

错误需要定位到：

```text
剧本 → 剧情点 → 场景/命令 → 选项/字段
```

## 7. 预览闭环

预览流程：

```text
检查草稿
    ↓
提交剧情点草稿
    ↓
保存 StoryData
    ↓
Database.ReloadStoryMod()
    ↓
StoryPanel.Open("mod:" + storyId, mapId)
```

预览不能直接播放未保存的 UI 草稿。正式预览与编辑器必须隔离选中框、输入控件和编辑提示。

当前阶段所有自制剧情使用 `replayable=true`，允许反复进入测试；选择历史只保存在本次运行上下文。

## 8. 当前数据处理原则

- 现有 `StoryDocument` 等类型只作为运行时重构的参考，不作为旧 JSON 兼容层。
- 新设计语义分别对应 `StoryData`、`StoryPointData`、`CommandData`。
- 当前唯一旧版“法拉的梦” JSON 在改造代码时直接改写为新格式。
- 未知扩展字段不能被静默丢弃；在无法保留时提示作者。

## 9. 当前不实现

- 任意元素自由拖拽持久化。
- 复杂变量、条件图和脚本代码。
- 命令级文本样式。
- 完整预览隔离存档。
- 多人协同编辑。

这些功能必须建立在 StoryData、StoryPointDraftModel 和运行时上下文稳定之后。
