# 六边形实时策略游戏

这是一个使用 **Unity 2022.3** 开发的 3D 六边形网格实时策略游戏。玩家通过拖拽卡牌部署单位和建筑，单位的移动和攻击全部自动化，与 AI 势力实时对抗。

## 项目概览

- **程序化地图**：生成六边形地块、地形、河流与资源。
- **探索系统**：全图始终可见，未探索地块通过 Shader 去饱和与半透明雾叠加呈现。玩家主动支付金币点击探索格，同时占领并收割资源。
- **实时游戏循环**：`GameLoop` 每帧驱动所有单位自动决策，玩家随时可通过卡牌部署单位，并可暂停/继续游戏。
- **单位自动化**：每种单位有独立的兵种策略——近战、远程——由 `UnitBrainBase` 统一调度，逐格移动、探测敌人、自动攻击。
- **卡牌系统**：拖拽卡牌向地图放置单位或建筑，是玩家唯一的交互方式。
- **城市与建筑**：玩家开局拥有一个主城及周围一环势力范围。通过探索地块或占领公共建筑扩张势力范围。可部署祭坛、攻防雕像等建筑。不再支持建新城。
- **公共建筑**：地图中立区域随机生成多格公共建筑，双方争夺——首次击破归属攻击方，之后易主不回中立。占领后势力范围自动扩张并收割资源。
- **战斗**：`CombatResolver` 瞬间结算伤害，`PlayAttackAnim` 负责外观表现；攻速由 `UnitData.AttackInterval` 控制，移动速度由 `UnitData.MovementPoints` 控制。
- **AI 对手**：AI 单位由 `AIUnitBrain` 持续驱动，全知索敌。
- **天赋卡牌系统**：游戏开始时和占领公共建筑后，玩家从 3 张随机天赋卡中选择 1 张，获得整局永久 Buff（攻击力 / 防御力 / 金币获取）。AI 达到同样条件时后台自动随机选卡。选卡时游戏自动暂停并伴有入场动画、选中特效和屏幕震动。

## 开发环境

| 项目 | 说明 |
| --- | --- |
| 引擎 | Unity `2022.3.62f3c1` |
| 语言 | C# |
| 依赖注入 | [Zenject](Assets/Plugins/Zenject/) |
| 动画与缓动 | [DOTween](Assets/DOTween_1_2_765/) |
| 主要 Unity 包 | Cinemachine 2.10.3、Post Processing 3.4.0、Shader Graph 14.1.0、TextMeshPro 3.0.9、Timeline 1.7.7 |
| 测试 | Unity Test Framework、NUnit、NSubstitute 5.1.0 |

完整的 UPM 依赖及版本见 [Packages/manifest.json](Packages/manifest.json)，编辑器版本见 [ProjectSettings/ProjectVersion.txt](ProjectSettings/ProjectVersion.txt)。

## 启动项目

1. 使用 Unity `2022.3.62f3c1` 打开仓库根目录。
2. 等待编辑器完成资源导入和脚本编译。
3. 打开 [Assets/Scenes/StartScene.unity](Assets/Scenes/StartScene.unity)。
4. 点击编辑器顶部的 **Play**，通过开始界面进入游戏。

项目的构建场景顺序为：

1. [StartScene.unity](Assets/Scenes/StartScene.unity)：开始界面和游戏入口。
2. [GameScene.unity](Assets/Scenes/GameScene.unity)：主要玩法场景。

场景配置以 [ProjectSettings/EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset) 为准。

## 操作方式

| 操作 | 功能 |
| --- | --- |
| 拖拽卡牌到地图 | 在有效地块部署对应单位或建筑 |
| 点击暂停/继续按钮 | 切换游戏暂停/继续 |
| 点击探索费用标签 | 支付金币探索未探索地块（获得占领权 + 收割资源） |
| `W` / `A` / `S` / `D` 或方向键 | 平移镜头 |
| 鼠标滚轮 | 缩放镜头 |
| `Ctrl + A` / `Ctrl + D` | 围绕屏幕中心对应的地图位置旋转镜头 |
| `G` | 显示或隐藏六边形网格 |

单位部署后完全自动化——自动探测敌人、追踪攻击。卡牌拖拽由 [CardController](Assets/Scripts/UI/CardController.cs) 和 [PlayerInputHandler](Assets/Scripts/Core/Services/PlayerInputHandler.cs) 处理，镜头操作由 [CameraController](Assets/Scripts/Controllers/CameraController.cs) 处理。

## 游戏循环

游戏采用**实时驱动**，由 [GameLoop](Assets/Scripts/Core/Services/GameLoop.cs) 每帧运行：

```text
GameLoop.Tick（每帧，暂停时跳过）
├── 遍历所有注册的 UnitBrainBase
│     └── 空闲单位 → OnStepFinished()
│           ├── CanAttack() → DoCombat()（瞬间结算伤害 + 播放动画）
│           └── ChooseNextStep() → MoveTo(下一格)
├── 遍历所有注册的 PublicBuildingBase
│     └── CheckDeath() → OnDeath() → OnCaptured()（易主）
└── 驱动回血计时器（农田/祭坛，每 5 秒）
```

- `GameLoop.IsPaused` 控制全局暂停——单位行为、移动动画、回血计时器、金币被动收入全部停止；卡牌操作和镜头控制不受影响。
- 暂停/继续通过 UI 按钮调用 `GameLoop.SetPaused()` 切换。

## 项目结构

```text
Assets/
├── Scenes/                         # 开始场景与主游戏场景
├── Shader/                         # 地形/水/河流迷雾集成 Shader 与迷雾混合 include
├── Scripts/
│   ├── AI/                         # AI 管理器、实体工厂、卡牌脑
│   ├── Controllers/                # 单位、建筑（普通与公共建筑）、镜头等表现与控制组件
│   ├── Core/
│   │   ├── Interfaces/             # 核心服务接口
│   │   ├── Models/                 # 地块、单位、建筑、卡牌等领域数据
│   │   └── Services/               # 地图、单位、卡牌、探索、游戏循环等实现
│   ├── Infrastructure/Installers/  # Zenject 依赖绑定入口
│   ├── Managers/                   # 地图生成、渲染、阵营管理、公共建筑生成、探索标签
│   ├── ScriptableObjects/          # 单位、建筑、公共建筑、地图、UI、地貌回血、天赋卡池配置
│   ├── TalentCard/                  # 天赋卡牌系统：Buff、数据、触发、UI、AI 自动选卡
│   ├── Units/                      # 单位行为基类、玩家/AI Brain、兵种策略
│   ├── UI/                         # 卡牌、信息面板及界面视图
│   └── Utilities/                  # 六边形、网格、种子服务、枚举等通用工具
└── Tests/                          # EditMode 单元测试

Docs/                               # DocFX 文档及服务说明
Packages/                           # Unity 包配置
ProjectSettings/                    # 编辑器、场景与项目设置
```

## 架构说明

项目将核心能力定义为接口，并通过 Zenject 注入具体实现：

- 接口位于 [Assets/Scripts/Core/Interfaces/](Assets/Scripts/Core/Interfaces/)。
- 实现主要位于 [Assets/Scripts/Core/Services/](Assets/Scripts/Core/Services/)。
- ScriptableObject 数据通过 `DataProviders` 提供给业务服务。
- [GameInstaller.cs](Assets/Scripts/Infrastructure/Installers/GameInstaller.cs) 负责主游戏场景中的地图、单位、卡牌、输入、UI 和游戏循环绑定。
- [GlobalServicesInstaller.cs](Assets/Scripts/Infrastructure/Installers/GlobalServicesInstaller.cs) 负责全局服务绑定。
- [GameFlowManager.cs](Assets/Scripts/Managers/GameFlowManager.cs) 负责主场景启动时的地图生成和游戏初始化流程。

主要服务包括：

| 服务 | 职责 |
| --- | --- |
| `IMapDataService` | 六边形坐标、地块数据和邻接查询 |
| `IUnitService` / `IUnitRepository` | 单位查询、管理与存储 |
| `IUnitMovement` | 移动和路径查询 |
| `ICardService` | 手牌、卡槽和卡牌生成 |
| `GameLoop` | 实时主循环：驱动单位决策、公共建筑死亡检测、管理暂停、积累游戏时间 |
| `CombatResolver` | 瞬间伤害结算（单位对单位 / 单位对建筑 / 多格公共建筑攻击转发） |
| `UnitMovementSystem` | 逐格移动动画与预留管理 |
| `UnitBrainBase` | 单位行为基类：`OnStepFinished` 决策骨架 + 回血/攻速计时器 |
| `PlayerUnitBrain` | 玩家单位 Brain：全知索敌 |
| `AIUnitBrain` | AI 单位 Brain：全知索敌 |
| `IUnitStrategy` | 兵种策略接口：`ChooseNextStep` / `CanAttack` / `DoCombat` |
| `MeleeStrategy` | 近战策略：朝敌人走，相邻格攻击（支持 NeutralBuilding） |
| `RangedStrategy` | 远程策略：射程内原地攻击，否则靠近（支持 NeutralBuilding） |
| `IExplorationService` | 探索地块：金币扣费、标记已探索、圈入势力范围、收割资源 |
| `ITerritoryService` | 势力范围占领与查询 |
| `GoldWallet` | 玩家/AI 金币钱包，支持被动收入 |
| `GoldIncomeService` | 每秒被动金币收入（ITickable） |
| `BuildingBase` | 建筑基类：血量、受击、血条、伤害公式 |
| `BuildingController` | 普通建筑控制器（城市、雕像、祭坛等），继承 BuildingBase |
| `PublicBuildingBase` | 公共建筑基类：两阶段 HP、多格管理、易主、势力范围扩展，继承 BuildingBase |
| `PublicBuildingGenerator` | 公共建筑随机生成器（地图生成后、势力范围初始化前） |
| `CostLabelRenderer` | 探索费用标签：Screen Space 渲染、Button 点击探索、金币不足压暗 |
| `IInputService` | 鼠标、键盘、射线和 UI 遮挡判断 |
| `IMeshGenerator` | 地形、河流、迷雾等网格数据生成 |
| `IUIManagerView` | 单位信息等 UI 更新 |
| `IFactionBuffService` | 阵营级天赋 Buff 管理：累积乘数/加数查询、永久 Buff 添加 |
| `TalentCardTriggerAdapter` | 天赋卡触发协调：抽卡、选卡事件分发 |
| `TalentCardSelectionUI` | 玩家天赋卡选择界面：3 张卡横排展示 + 入场/选中动画 |
| `AITalentCardAutoSelector` | AI 天赋卡后台自动随机选择 |
| `TalentCardBootstrap` | 天赋卡系统启动器：订阅公共建筑占领事件、触发开局选卡 |
| `CameraController` | 镜头控制：平移/缩放/旋转/边界限制，内置屏幕震动 `Shake()` |

## 探索系统

探索不再依赖视野或战争迷雾——**全图始终可见**，但未探索地块无法用于部署单位或建造建筑。

- **视觉**：未探索地块通过 Shader 去饱和 + 半透明雾叠加呈现（[FogBlend.cginc](Assets/Shader/FogBlend.cginc)），与已探索地块形成视觉区分。
- **探索方式**：
  - 玩家点击势力范围相邻未探索格上的费用标签，支付金币即可探索（标记已探索 + 圈入势力范围 + 收割资源）。
  - 占领公共建筑后，其势力范围自动标记为已探索并收割资源。
- **不可探索区域**：公共建筑占位格及其周围一环地块标记为不可探索——不显示费用标签，无法通过探索系统获得，只能通过占领公共建筑获取势力范围。
- **费用与收入**：基础探索费用来自 `ExplorationCost`，金币不足时标签压暗且不可点击。被动金币收入由 `GoldIncomeService` 每秒提供。地块收割奖励为 5 基础 + 资源加成。

## 公共建筑系统

详见 [公共建筑系统设计讨论.md](公共建筑系统设计讨论.md)。

- **生成**：地图生成后、势力范围初始化前，由 `PublicBuildingGenerator` 在陆地内部区域随机生成。
- **形态**：可单格或多格（根格 + 最多 3 子格），配置在 `PublicBuildingSO` 中。
- **归属**：开局中立（伪 AI 阵营），初始血量用 `captureHp`，归属后防守血量用 `defenseHp`。攻击中立公共建筑的任意占位格均可转发到根格。
- **易主**：首次击破归属攻击方，之后被敌方击破直接易主不回中立。
- **势力范围**：占领后所有占位格各自向外扩展一环，地块自动探索并收割资源。
- **不可探索**：公共建筑占位格及其周围一环在生成时标记为不可探索——不显示探索费用标签，玩家和 AI 均无法主动探索。这些地块只能通过占领公共建筑获取。

## AI 模块

AI 逻辑集中在 [Assets/Scripts/AI/](Assets/Scripts/AI/)，由 [AIManager](Assets/Scripts/AI/AIManager.cs) 编排初始化，实际运行时由 `AIUnitBrain` + `GameLoop` 持续驱动：

| 类 | 职责 |
| --- | --- |
| `AIManager` | 协调者：开局初始化、城市预制体传递 |
| `AIEntityFactory` | 敌方城市/单位/建筑的实例化与势力范围扩展 |
| `AICardBrain` | AI 抽卡状态、卡牌管线与出牌落点决策 |
| `AIUnitBrain` | AI 单位的实时行为 Brain：全知索敌、逐格决策、前沿游走 |
| `AIRandomProvider` | AI 各服务共享的随机源 |

玩家与 AI 共享的规则（抽卡生成、生成时的 UI 拼接）已收敛到 [CardGenerationRule](Assets/Scripts/Core/Services/CardGenerationRule.cs) 与 [SpawnUIWiring](Assets/Scripts/Core/Services/SpawnUIWiring.cs)。

## 测试

测试程序集仅面向 Unity Editor，现有测试位于 [Assets/Tests/](Assets/Tests/)，覆盖：

- 卡牌服务
- 六边形地图服务
- 单位移动系统
- 单位数据仓库
- 单位移除服务

运行方式：

1. 在编辑器中打开 **Window > General > Test Runner**。
2. 选择 **EditMode**。
3. 点击 **Run All**。

测试程序集配置见 [Assets/Tests/Tests.asmdef](Assets/Tests/Tests.asmdef)。

## 改造历史

项目最初为回合制策略游戏，于 2026-07-24 完成全面实时化改造，详见 [改造方案讨论总结.md](改造方案讨论总结.md)。主要变更：

- 删除科技/文化系统，所有卡牌无条件解锁
- `GameLoop` 替代 `GameStateMachine`，实现实时 Tick 驱动
- 单位全面自动化——Brain + Strategy 架构，玩家不再手动操作具体单位
- 暂停按钮替代回合推进
- 战斗逻辑与表现分离：`CombatResolver` 瞬间结算 + `PlayAttackAnim` 纯动画
- 移动速度由 `UnitData.MovementPoints` 控制，不同兵种有快慢差异
- 旧回合制代码（`GameStateMachine`、`AITacticalBrain`、`PlayerPhase`、`AIPhase`、`SettlementPhase`）已删除

2026-07-25 ~ 2026-07-26 新增探索系统与公共建筑系统：

- 移除三态战争迷雾（`FieldOfViewService`、`AIFogService`、`IsVisible`），全图始终可见
- 未探索地块通过 Shader 去饱和 + 半透明雾呈现
- 新增主动探索系统：点击费用标签支付金币探索地块，获得势力范围与资源
- 新增势力范围服务（`ITerritoryService`）：主城固有范围 + 探索占领 + 公共建筑占领
- 建新城功能移除
- 新增公共建筑系统：多格中立建筑、两阶段 HP、争夺易主
- 公共建筑占位格及周围一环标记为不可探索（`IsUnexplorable`），不显示费用标签，AI 自动跳过
- `BuildingBase` 抽象基类，`BuildingController` 与 `PublicBuildingBase` 均继承
- 建筑死亡检测统一纳入 `GameLoop.Tick`

2026-07-26 全项目性能优化，详见 [性能优化方案讨论.md](性能优化方案讨论.md)：

- 目标地图规模调整为 20×30（600 格）
- 世界坐标查格从 O(600) 线性扫描改为 O(1) 缓存 + 字典查询（`UnitMovementController.CurrentHexCoordinate` 懒缓存、`cellRadius` 预计算）
- Dijkstra 寻路 `allPoints.Contains` 从 O(600) 列表查找降为 O(1) HashSet 查找；`FindNearestEnemy` 新增六边形距离预筛，跳过远目标免 Dijkstra
- 单位移动不再触发全图视觉刷新（`_mapVisualEvent.Raise` 从 `OnMoveFinished` 移除）
- `OnMapVisualChanged` 精简为仅更新 FogMask；地形/水/河顶点探索色上传、`SyncCellObjectVisibility`、敌方 Renderer/Canvas 缓存全部删除
- 网格线每个子 Mesh 只存 12 顶点（不再拷贝整图缓冲）+ `addCollider: false`
- 过渡 submesh 按材质组合签名归组：submesh 从约 2800 级降到最多 36 级
- 地貌（LandForm）和资源（Resource）父节点加 `StaticBatchingUtility.Combine`
- `GetAllCells`/`GetAllHexCoordinates` 改为返回缓存列表；FogMask G 通道死代码删除
- 探索费用标签从每秒轮询改为金币事件驱动（`GoldWallet.OnGoldChanged`）
- `UnitBrainBase` 新增空闲决策节流（每 20 帧搜索一次目标，移动完成后强制搜索）
- `HexDistance` 提取到 `UnitBrainBase` 基类
- 清理 `MapRenderer` 中不再需要的 `_unitRepository` 注入

2026-07-26 新增天赋卡牌系统，详见 [天赋卡牌系统整合方案.md](天赋卡牌系统整合方案.md)：

- 从 card 项目（Roguelite 卡牌系统）迁移 Buff 系统，改为阵营级 `FactionBuffService`
- 新增 `TalentCardConfigSO` / `TalentCardPoolSO` ScriptableObject 数据层
- 触发时机：游戏开始 + 占领公共建筑
- 首批 3 张天赋卡：攻击力、防御力、金币获取（均为乘数型 Buff）
- `CombatResolver` 和 `GoldIncomeService` 接入阵营 Buff 乘数查询
- 选卡面板入场动画：暗幕淡入 + 3 张卡错开弹入（DOTween）
- 选中卡特效：放大闪白 + 震荡 + 屏幕震动（`CameraController.Shake()`）
- AI 后台自动随机选卡（`AITalentCardAutoSelector`）
- `GoldIncomeService` 注入 `GameLoop` 以响应暂停状态

## 当前说明

- 性能已针对 20×30（600 格）地图完成全项目优化（P0 + P1 全部落地），详见 [性能优化方案讨论.md](性能优化方案讨论.md)。
- 项目当前仍使用默认工程名称，尚未在仓库中确定正式游戏名称。
- 仓库中包含部分历史资源和第三方资源目录，其是否仍被场景或脚本引用需要单独确认后再清理。
- 仓库暂未提供明确的发布平台说明、CI 配置或自动化构建脚本。
