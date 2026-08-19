# 六边形实时策略游戏

这是一个使用 **Unity 2022.3** 开发的 3D 六边形网格实时策略游戏。玩家通过拖拽卡牌部署单位和建筑，单位的移动和攻击全部自动化，与 AI 势力实时对抗。

## 项目概览

- **程序化地图**：生成六边形地块、地形、河流与资源。
- **动态地图系统**：地图地块支持运行时事务级变化——`MapMutationService`（BeginTransaction→Apply→Commit/Rollback）+ 唯一渲染后端 `ChunkMapRenderer`（8×8 分块双缓冲），变化带 CPU 顶点动画（错峰、顶出、并行动画、水面淡出）与交互锁。已落地竞技场（37 格状态机 + 边界山脉封闭墙一体化升起动画，宝箱摧毁后山脉与平台永久保留）与能力测试（V 键全图波浪、R/F 键指格微调，后者 2026-08-05 起屏蔽保留）。详见 [动态地图/动态地图变化系统-使用报告.md](动态地图/动态地图变化系统-使用报告.md)。
- **程序化山脉系统**：山脉不是"高地块"，而是叠加在地块之上的跨格连续隆起高度场。`RidgeGenerator` 在地图生成时确定性生成脊线 + 宽度化坡面格（SeedService "Mountain" 独立随机流，参数快照固化到格级数据）；`MountainGeometryBuilder` 构造 Low-poly 几何（顶面/过渡面/封口、Triplanar 三档色阶），材质契约见 `MountainMaterialContract`（`Custom/MountainLowPoly_Fog` + Transition 变体）；山格不可通行、不可部署、不可建造（`MountainCellRule` 统一资格），山脉格永久视觉可见（`MountainVisibilityRule`，免雾但不算已探索）；Chunk 重建按快照确定性复现，动态地图事务支持山体清除（永久）、水淹移除与动画顶出。
- **探索与后勤系统**：玩家主动支付金币点击探索格，同时占领并收割资源。后勤系统（`LogisticsService`）以主城为根对双方领地做 BFS 连通判断：断供地块对双方重新覆盖迷雾（地面与建筑一起），恢复供应后自动揭雾；迷雾遮罩由 FogMask 全量可逆重建驱动。详见 [后勤系统设计方案.md](游戏系统/后勤系统设计方案.md)。
- **探索奖励预生成**：地图生成时按 `ExplorationRewardConfigSO` 权重为每个可探索的陆地地块固化奖励快照（`ExplorationRewardData`：无 / 金币 / 军事单位 / 战术卡牌 / 建筑；公共建筑占位区与竞技场预留区等不可探索格不生成），探索结算只消费快照、不再即时掷骰（同一快照只结算一次）。探索费用按地块自身奖励类型决定（`explorationCostsByType`，默认 50），探索费用标签直接显示该格奖励类型图标（金币/军事/战术/建筑）。
- **金矿与收入规则**：新增金矿建筑类型（`BulidingType.GoldMine`，产出由 `BuildingConfigSO.goldIncomePerSecond` 配置化）；`IncomeEligibilityRule` 统一"归属 + 后勤畅通"收入资格（断供暂停产金），`BuildingIncomeRule` 汇总金矿建筑产出，`GoldIncomeService` 每秒收入 =（基础 + 金矿地貌 + 金矿建筑）× 天赋乘数。
- **实时游戏循环**：`GameLoop` 每帧驱动所有单位自动决策，玩家随时可通过卡牌部署单位，并可暂停/继续游戏。
- **单位自动化**：每种单位有独立的兵种策略——近战、远程——由 `UnitBrainBase` 统一调度，逐格移动、探测敌人、自动攻击。
- **卡牌系统**：拖拽卡牌向地图放置单位或建筑，是玩家唯一的交互方式。普通卡池数值由 Excel 数值库 `NormalCardPoolDatabaseSO` 驱动（当前 4 张：`unit.archer`、`unit.swordsman`、`building.arrow_tower`、`building.barracks`，`unit.archer` 为首张保底卡），资源引用（Prefab/Sprite/音效）仍走 `NormalCardPoolSO` / `UnitConfigSO` / `BuildingConfigSO` 等 ScriptableObject，详见 [普通卡池对象化改造方案.md](历史归档/普通卡池对象化改造方案.md) 与 [Excel配置表混合架构修改计划.md](Excel配置表混合架构修改计划.md)。
- **城市与建筑**：玩家开局拥有一个主城及周围一环势力范围。通过探索地块或占领公共建筑扩张势力范围。可部署祭坛、攻防雕像等建筑。不再支持建新城。势力范围边界渲染为实体城墙/城墩（玩家与 AI 各一套预制体，由 `SphereOfInfluenceRenderer` 按边界折线摆放）。
- **断供与吞并**：断供地块上的建筑全部失能（箭塔停火、兵营暂停生产），不再被自动索敌；敌方单位踩上失能建筑格可随格占领（建筑易主、不摧毁）；断供区域与敌方后勤网络共边相邻时整区域吞并（含建筑与公共建筑外一环）。详见 [断供迷雾与建筑失能吞并设计方案.md](游戏系统/断供迷雾与建筑失能吞并设计方案.md)。
- **公共建筑**：地图中立区域随机生成多格公共建筑，双方争夺——首次击破归属攻击方，之后易主不回中立。占领后势力范围自动扩张并收割资源。开局隐藏，通过浮标示意位置；任意单位进入建筑占位格外一环后全局发现，触发石柱/飞盘特效揭示建筑模型和相关地块。
- **战斗**：`CombatResolver` 瞬间结算伤害，`PlayAttackAnim` 负责外观表现；攻速由 `UnitData.AttackInterval` 控制，移动速度由 `UnitData.MovementPoints` 控制；攻击音效由 `UnitConfigSO.attackSfx` 配置驱动。
- **AI 对手**：AI 单位由 `AIUnitBrain` 持续驱动，全知索敌。
- **天赋卡牌系统**：游戏开始时和占领公共建筑后，玩家从 3 张随机天赋卡中选择 1 张，获得整局永久乘数型 Buff（伤害 `talent.damage`、城墙 `talent.building_hp`、财富 `talent.gold`，数值由 Excel 天赋卡表维护）。AI 达到同样条件时后台自动随机选卡。选卡时游戏自动暂停并伴有入场动画、选中特效和屏幕震动。
- **战术卡牌系统**：开局固定发放 2 张战术牌（战斗号令 `tactical.battle_order`、维修 `tactical.repair`），拖拽释放到目标格，作用于落点及其周围一环内的己方单位/建筑——维修群体回血、战斗号令临时提升攻击与移速，同名牌叠放显示数量、耗尽不自动补充，详见 [战术牌系统SO设计.md](游戏系统/战术牌系统SO设计.md)。
- **地图道具/资源系统**：地图随机生成可拾取资源（动物/宝箱/矿物/植物；回血包历史保留未启用），按拾取效果类型（攻击提升/防御提升/回血/金币）由 `MapResourceProvider` + `MapResourceCollectionService` 统一消费，详见 [地图道具系统总结.md](地图与地形/地图道具系统总结.md)。
- **Excel 数据驱动配置系统**：平衡数值统一由 `Config/Excel/游戏数值配置.xlsx` 维护，经 `Tools/ConfigExporter`（独立 .NET 8 工具）导出为 CSV/JSON，再由 Unity 菜单 `Tools/游戏配置/导入并校验` 生成 `Assets/GameConfig/Generated` 下只读数值库 SO，运行时通过各 `*ConfigProvider` 读取（阶段 6 起 Excel 为唯一数值源），详见 [Excel配置表混合架构修改计划.md](Excel配置表混合架构修改计划.md)。

## 开发环境

| 项目 | 说明 |
| --- | --- |
| 引擎 | Unity `2022.3.62f3c1` |
| 语言 | C# |
| 依赖注入 | [Zenject](Assets/Plugins/Zenject/) |
| 动画与缓动 | [DOTween](Assets/DOTween_1_2_765/) |
| 主要 Unity 包 | Cinemachine 2.10.7、Post Processing 3.4.0、Shader Graph 14.1.0、TextMeshPro 3.0.9、Timeline 1.7.7、Mathematics 1.2.6、UGUI 1.0.0 |
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
| 鼠标左键拖拽 | 平移镜头（指针不在 UI 上时） |
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
├── Shader/                         # 地形/水/河流/迷雾/网格/势力范围/探索特效等 Shader
├── GameConfig/                     # Excel 数值化运行库（Editor 导入器 + Runtime 只读 SO，阶段6 唯一数值源）
│   ├── Editor/                     # 导入器与校验（Import/Validation/Windows）
│   ├── Runtime/                    # Data（数值 DTO）与 Database（数值库 SO）、IGameBalanceDatabase
│   └── Generated/                  # 自动生成的只读数值库 SO（禁止手改）
├── Scripts/
│   ├── AI/                         # AI 管理器、实体工厂、卡牌脑、自动探索器、卡牌定时器、配置提供者
│   ├── Controllers/                # 单位、建筑（普通与公共建筑）、镜头、昼夜循环等表现与控制组件
│   ├── Core/
│   │   ├── Interfaces/             # 核心服务接口
│   │   ├── Models/                 # 地块、单位、建筑、卡牌等领域数据
│   │   └── Services/               # 地图、单位、卡牌、战斗、游戏循环等实现
│   │       ├── DataProviders/      # SO 数据提供者（数值读 Excel、资源引用读 SO）
│   │       ├── Exploration/        # 探索系统服务与接口
│   │       ├── MapMutation/        # 动态地图变化（事务/分帧提交/动画/交互锁）
│   │       ├── Resource/           # 金币钱包、被动收入与经济配置
│   │       ├── Territory/          # 势力范围与后勤服务（TerritoryService/LogisticsService/AnnexationService）
│   │       └── Visibility/         # 临时可见性服务（VisibilityLease）
│   ├── Data/                       # 六边形地块数据（地形、地貌、资源、建筑）映射
│   ├── Infrastructure/Installers/  # Zenject 依赖绑定入口
│   ├── Managers/                   # 地图生成/渲染、阵营管理、公共建筑生成、迷雾、势力范围渲染、探索特效、昼夜循环
│   ├── Scenes/StartScene/          # 开始场景 UI 控制器与场景管理
│   ├── ScriptableObjects/          # 单位/建筑配置、普通卡池、公共建筑、地图、UI、地貌、天赋卡、战术卡、地图资源配置
│   │   ├── UnitConfigS/            # 单位卡配置资产（UnitConfig-0~11、archer、swordsman）
│   │   ├── BuildingConfigS/        # 建筑卡配置资产（BuildingConfig-0~3、barracks、arrow_tower、gold_mine）
│   │   ├── MapLandForm/            # 地貌数据库与配置（含 Mountain/MountainConfig 程序化山脉配置）
│   │   ├── MapResource/            # 地图资源数据库与拾取效果（Animals/Chest/HealthPack/Minerals/Plants）
│   │   ├── TacticalCard/           # 战术牌 SO 实例（Repair/BattleOrder + Database）
│   │   ├── NormalCardPool.asset    # 普通卡池资源（cards 配置列表 + 首张保底卡）
│   │   └── TalentCard/             # 天赋卡 SO 实例
│   ├── TacticalCard/               # 战术卡牌系统：SO 数据、运行时实例、Presenter、战斗号令 Buff
│   │   └── Data/                   # 战术卡 SO 脚本
│   ├── TalentCard/                 # 天赋卡牌系统：Buff、数据、触发、UI、AI 自动选卡
│   │   ├── Buffs/                  # Buff 基类与实现
│   │   └── Data/                   # 天赋卡 SO 脚本
│   ├── Turn/                       # 回合制遗留组件（CommandQueue、MoveCommand、EndGame）
│   ├── Units/                      # 单位行为基类、玩家/AI Brain、兵种策略与工厂
│   ├── UI/                         # 卡牌、信息面板及界面视图
│   └── Utilities/                  # 六边形、网格、种子服务、枚举等通用工具
├── Tests/                          # EditMode 单元测试
├── Auido/                          # 音频资源
├── Editor/                         # 编辑器扩展
├── Materials/                      # 材质
├── Model/                          # 3D 模型
├── Particles/                      # 粒子特效
├── Plugins/                        # 第三方插件（Zenject 等）
├── Resources/                      # 运行时加载资源
├── Texture/                        # 贴图
├── UI/                             # UI 资源（预制体、图片等）
├── DOTween_1_2_765/                # DOTween 动画与缓动插件
└── _Quarantine/                    # 隔离/待清理资源
（另含第三方/历史资源目录：KayKit、Lana Studio、Toon_RTS、unity-chan!、VFXPACK_IMPACT_WALLCOEUR_FreeVersion、noise、Standard Assets、Video，是否仍被引用需单独确认）

Config/                             # Excel 数值配置：Excel 源、Schema、自动生成中间数据（CSV/JSON）
├── Excel/游戏数值配置.xlsx         # 策划唯一数值源
├── Schema/配置表结构.json          # 表结构 Schema
└── Generated/                      # 自动生成（Csv/、game-config.json、manifest，禁止手改）

Tools/ConfigExporter/               # 独立 .NET 8 Excel 导出工具（含单元测试）
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
| `ICardService` | 手牌、卡槽和卡牌生成（`GenerateNextCard()` 经 `CardGenerationRule` 从 Excel 卡池数值库解析为普通卡配置对象） |
| `GameLoop` | 实时主循环：驱动单位决策、公共建筑死亡检测、管理暂停、积累游戏时间 |
| `CombatResolver` | 瞬间伤害结算（单位对单位 / 单位对建筑 / 多格公共建筑攻击转发） |
| `UnitMovementSystem` | 逐格移动动画与预留管理 |
| `UnitBrainBase` | 单位行为基类：`OnStepFinished` 决策骨架 + 回血/攻速计时器 |
| `PlayerUnitBrain` | 玩家单位 Brain：全知索敌 |
| `AIUnitBrain` | AI 单位 Brain：全知索敌（位于 `Assets/Scripts/Units/`） |
| `IUnitStrategy` | 兵种策略接口：`ChooseNextStep` / `CanAttack` / `DoCombat`（位于 `Assets/Scripts/Units/`） |
| `MeleeStrategy` | 近战策略：朝敌人走，相邻格攻击（支持 NeutralBuilding） |
| `RangedStrategy` | 远程策略：射程内原地攻击，否则靠近（支持 NeutralBuilding） |
| `SettlerStrategy` | 移民策略：当前版本不攻击、不移动（建城功能已移除，预留改造为自动探索） |
| `UnitStrategyFactory` | 根据兵种类型创建对应策略实例 |
| `IExplorationService` | 探索地块：金币扣费、标记已探索、圈入势力范围、收割资源 |
| `IExplorationRule` | 探索合法性判断 |
| `IExplorationCostProvider` | 探索费用提供者 |
| `IPlayerResourceWallet` | 玩家资源钱包接口 |
| `ExplorationRewardSystem` | 探索奖励系统：监听探索完成事件，按地块预生成快照结算金币/单位/战术牌/建筑奖励（奖励池与档位由 Excel 驱动，`ExplorationRewardProvider` 解析） |
| `ITerritoryService` | 势力范围占领与查询 |
| `ILogisticsService` | 后勤服务：主城注册、按阵营 BFS 供应缓存（`IsLogisticsConnected`）、迷雾判定（`IsVisibleToFaction`）、重算事件与领地字典重建 |
| `AnnexationService` | 区域吞并：断供区与敌方后勤网络相邻即整体易主（批量归属写入、主城/中立公共建筑豁免、单次 `LogisticsChanged`） |
| `BuildingSupplyGate` | 建筑失能门控：`IsFunctional`（所在格归属 == 建筑阵营 && 后勤畅通），断供即失能，`LogisticsChanged` 驱动刷新 |
| `BuildingTransferService` | 建筑易主迁移：归属真相源/tag/视觉/HP/索引字典同步，公共建筑走 `OnCaptured` 全量（含外一环） |
| `GoldWallet` | 玩家/AI 金币钱包，支持被动收入 |
| `GoldIncomeService` | 每秒被动金币收入（基础 + 金矿地貌 + 金矿建筑）× 天赋乘数（ITickable） |
| `BuildingBase` | 建筑基类：血量、受击、血条、伤害公式、失能门控挂载、血条可见性同步（断供隐藏） |
| `BuildingController` | 普通建筑控制器（城市、雕像、祭坛等），继承 BuildingBase |
| `PublicBuildingBase` | 公共建筑基类：两阶段 HP、多格管理、易主、势力范围扩展，继承 BuildingBase |
| `PublicBuildingGenerator` | 公共建筑随机生成器（地图生成后、势力范围初始化前） |
| `PublicBuildingMarkerManager` | 公共建筑浮标管理器：运行时创建/销毁世界空间浮标，提供近似方向查询供单位趋向 |
| `PublicBuildingMarkerView` | 公共建筑浮标视觉组件：呼吸动画、图标设置、始终面向相机 |
| `ExplorationPillarPool` | 探索特效对象池：石柱升起/飞盘砸落表现；`PlayRevealEffect()` 无业务副作用的公共建筑发现特效 |
| `CostLabelRenderer` | 探索费用标签：Screen Space 渲染、Button 点击探索、按地块奖励类型显示费用与图标、金币不足压暗 |
| `FogManager` | 迷雾封皮/连接面片网格生成（迷雾遮罩 `_FogMaskTex` 与 Shader 参数见 `ChunkMapRenderer`） |
| `SphereOfInfluenceRenderer` | 势力范围实体城墙渲染：玩家/AI 各一套城墙/城墩预制体，按边界折线摆放（高度差过渡墙），预制体未指定则跳过该部件 |
| `IInputService` | 鼠标、键盘、射线和 UI 遮挡判断 |
| `IMeshGenerator` | 地形、河流、迷雾等网格数据生成 |
| `IUIManagerView` | 单位信息等 UI 更新 |
| `IFactionBuffService` | 阵营级天赋 Buff 管理：累积乘数/加数查询、永久 Buff 添加 |
| `TalentCardTriggerAdapter` | 天赋卡触发协调：抽卡、选卡事件分发 |
| `TalentCardSelectionUI` | 玩家天赋卡选择界面：3 张卡横排展示 + 入场/选中动画 |
| `AITalentCardAutoSelector` | AI 天赋卡后台自动随机选择 |
| `TalentCardBootstrap` | 天赋卡系统启动器：订阅公共建筑占领事件、触发开局选卡 |
| `TalentCardEffectApplier` | 天赋卡效果应用器 |
| `TalentCardPoolResolver` | 天赋卡池随机抽卡（遗留辅助，抽卡已由 `TalentCardProvider.DrawRandom` 接管） |
| `TalentCardProvider` | 天赋卡提供者：启用卡列表与 `DrawRandom` 抽卡、按 `talentId` 查 Excel 数值（数值唯一主源） |
| `TalentCardSlotVisual` | 天赋卡槽视觉显示 |
| `AIAutoExplorer` | AI 自动探索器：定时搜索邻接己方领地的未探索地块并自动探索（免费），按地块奖励类型做预算预筛、消费预生成奖励快照（`ITickable`） |
| `AICardTicker` | AI 卡牌定时器：每 5 秒驱动一次 AI 抽卡管线 |
| `CameraController` | 镜头控制：平移/缩放/旋转/边界限制，内置屏幕震动 `Shake()` |
| `MapMutationService` | 地块变化事务管线：`BeginTransaction`→`Apply(HexCellPatch)`→`Commit`/`Rollback`，水陆跨界双向重置、单位联动（途经取消/不可通行弹射/站立吸附/路径失效）、`MapChanged` 事件广播；山体事务（清除永久移除、水淹移除山体贡献、移除方向降级同步提交、新增方向动画顶出） |
| `ChunkMapRenderer` | 唯一渲染后端（8×8 offset-grid Chunk 双缓冲）：地形/河流/湖海/海岸/网格线/迷雾 `_FogMaskTex` 重建，动画 staging（UV2/UV3 + CPU 顶点插值）；程序化山脉山体槽（替换式拓扑、碰撞网格分离、稳定材质 + Transition 变体、拓扑签名防动画残留） |
| `MapVisualTransitionService` | 地图变化动画：错峰延迟（Simultaneous/CenterToOuter/Wave）、CPU 顶点动画、水面/河流淡出、单位与地貌模型跟随（`RegisterVisualFollower`）、并行动画冲突强制完成 |
| `MapSlicedCommitExecutor` | 分帧提交执行器：大范围变化每帧最多重建 N 个 Chunk，防卡顿（与动画互斥） |
| `MapInteractionGate` | 交互锁：动画/提交期间锁定受影响格（`IMapInteractionGate`） |
| `MapRaycastService` | 统一地图射线：屏幕坐标 → Chunk 地块（卡牌放置/高亮入口，`IMapRaycastService`） |
| `TemporaryVisibilityService` | 来源式临时点亮迷雾（`VisibilityLease`，如竞技场突起瞬间），多来源互不影响 |
| `ArenaEventManager` | 竞技场：37 格状态机（Inactive→Reserved→Activated→Destroyed），平台与边界山脉封闭墙一体化顶出动画（墙鞍部防镂空）、宝箱摧毁恢复并永久保留山脉、对局结束动画兜底 |
| `MapMutationDiagnostics` | 提交日志 + 脏格品红高亮诊断开关 |
| `MountainCellRule` | 山脉地块玩法规则（纯函数）：有效山体判定、派生移动力（山格/水域不可通行）、统一部署/建造资格（`CanSpawnUnitOnCell`/`CanBuildOnCell`） |
| `RidgeGenerator` | 山脉脊线生成器：脊线评分行走 + 宽度化坡面格，确定性随机流（SeedService "Mountain"），脊线/地貌/河流互斥 |
| `MountainGeometryBuilder` | 山体几何纯函数：确定性高度场（脊线噪声 + 格级噪声）+ Low-poly 顶面/过渡面/封口构造，山体槽 UV0 契约（ridgeKey01 / faceTier 编码） |
| `MountainMaterialContract` | 山体材质契约：`Custom/MountainLowPoly_Fog`（+ `_Transition` 变体）、3 档色阶编解码、表现参数校验 |
| `MountainVisibilityRule` | 山脉格永久视觉可见判定（免雾但不算已探索，纯函数无副作用，与几何贡献同口径） |
| `IncomeEligibilityRule` | 阵营收入资格（纯函数）：归属 + 后勤畅通，地貌金矿与建筑金矿共用同一口径 |
| `BuildingIncomeRule` | 金矿建筑收入汇总：按 `BuildingConfigSO.goldIncomePerSecond` 对合格金矿格求和 |
| `SimpleStartButton` | 开局按钮简化系统：场景启动延迟激活 + 呼吸缩放动效，点击直接加载 GameScene |
| `GlobalTimerService` / `GlobalTimerUI` | 全局倒计时服务与 HUD（默认 300 秒，≤60 秒红色脉冲紧急样式） |
| `EconomyConfigProvider` | 经济配置提供者：起始金币/基础收入/AI 补贴/收入间隔/探索与卡费回退（Excel 唯一主源） |
| `GameFlowConfigProvider` | 游戏流程配置提供者：时长/昼夜周期/结算延迟/迷雾过渡速度/吞并重算深度（Excel 唯一主源） |
| `MapGenConfigProvider` | 地图生成参数提供者：Perlin 频率与八度/竞技场半径/突起时长（Excel 唯一主源） |
| `CoreGameplayConfigProvider` | 核心玩法静态配置（`Configure()` 静态注入）：单位手感/兵营/箭塔/宝箱/手牌上限/公共建筑/战术卡槽位 |
| `FeelConfigProvider` | 表现静态配置（`Configure()` 静态注入）：相机震动频率/迷雾刷新间隔/卡牌暗淡/天赋震屏 |
| `MountainConfigProvider` | 山体静态配置（`Configure()` 静态注入）：脊线/高度场/材质参数（资源引用仍在 `MapGenerationConfigSO`） |
| `BattleFormulaRule` | 战斗公式系数（静态注入）：河流防御惩罚/攻击雕像加成/高地攻击与射程加成/近战警戒范围 |
| `LandFormEffectRule` | 地貌效果规则（静态注入）：防御加成/回血/金币加成/禁建（数值读 Excel） |
| `MapResourceProvider` | 地图资源提供者：生成权重/拾取效果/探索收割数值（Excel 唯一主源 + 资源 SO 引用） |
| `MapResourceCollectionService` | 地图资源统一消费服务：拾取后按效果类型结算（攻击提升/防御提升/回血/金币） |
| `MapLandFormProvider` | 地图地貌提供者：散落/簇生成权重与参数（Excel 唯一主源） |
| `LandFormSpawnRule` / `LandFormClusterSpawnRule` | 地貌散落与簇生成规则（参数读 Excel） |
| `ResourceSpawnRule` / `ResourceRewardRule` / `RewardBuildingRule` | 资源生成、探索收割奖励与奖励建筑放置规则 |
| `TacticalCardPresenter` | 战术牌 UI 与释放：固定锚点、同名牌叠放、拖拽到目标格执行群体回血/战斗号令、探索奖励发放入口 |
| `BattleOrderBuffService` | 「战斗号令」临时增益：ITickable 计时（暂停冻结），攻击/移速乘数 |
| `SunCycleController` | 昼夜循环：ITickable 驱动太阳角度/光照强度（参数读 Excel） |
| `HexHighlightRenderer` | 单格高亮渲染器（不依赖逐格 GridMesh） |
| `MapPresentationBootstrap` | 地图表现启动器（保留场景序列化 CostLabelPrefab，不再实现渲染后端） |
| `LandFormMarkerManager` | 地貌提示浮标管理（复用公共建筑浮标视图，金矿提示图标） |
| `IUIConfigProvider` / `UIConfigProvider` | UI 配置提供者：图标/预制体/卡面等资源引用 |
| `UIManagerPresenter` | UIManager Presenter |

## 数据驱动配置系统（Excel 数值化）

平衡数值已从零散 ScriptableObject / Inspector 字段 / C# 常量收口为「Excel 唯一数值源 + 自动生成只读 SO + Provider」的混合架构，方案与分阶段实施见 [Excel配置表混合架构修改计划.md](Excel配置表混合架构修改计划.md)（阶段 1~6 已全部完成）：

```text
Config/Excel/游戏数值配置.xlsx（策划唯一数值源）
   ↓ Tools/ConfigExporter（独立 .NET 8 工具，dotnet run）
Config/Generated/（规范化 CSV + game-config.json + manifest，禁止手改）
   ↓ Unity 菜单 Tools/游戏配置/导入并校验（GameConfigImporter）
Assets/GameConfig/Generated/*.asset（自动生成只读数值库 SO）
   ↓ GameInstaller 绑定 + *ConfigProvider / *Rule 静态注入
运行时逻辑（不再回退 Legacy 数值，缺失即抛异常）
```

要点：

- **职责边界**：Excel 只管平衡数值与玩法规则（HP/攻击/卡费/权重/费用/间隔/时长等）；Unity 资源 SO（Prefab/Sprite/音效/特效）仍人工维护，通过稳定字符串 ID（`unit.archer`、`building.gold_mine`、`tactical.repair`、`talent.damage`、`resource.animals`…）关联；旧整数 ID 仅以 `legacyId` 兼容。
- **工作表（21 张）**：单位、建筑、普通卡池、战术卡、天赋卡、天赋抽卡规则、探索奖励配置、探索奖励池、地图资源、地图资源全局、地图地貌、地图地貌全局、公共建筑、经济配置、游戏流程配置、AI 配置、战斗公式、地图生成参数、核心玩法配置、表现配置、山体配置。
- **生成目录**：`Assets/GameConfig/Generated/` 下每张表一个 `*Database.asset`（如 `UnitBalanceDatabase.asset`、`NormalCardPoolDatabase.asset`、`ExplorationRewardPoolDatabase.asset`），由导入器全量重建，Inspector 标注「自动生成，禁止手改」。
- **校验**：`Tools/游戏配置/仅校验` 输出校验报告；导出器 `--strict` 与 Unity 导入器做跨表引用/必填/枚举/主键唯一/总权重>0 等校验；运行时缺失配置抛 `InvalidOperationException`，不再静默回退。
- **确定性**：相同 Excel 输入生成字节级一致的 CSV/JSON/SO；`game-config.manifest.json` 记录 schema 版本、工作簿 SHA-256 与各表行数。
- **运行时不读 Excel/CSV/JSON**：Player 只读场景绑定的生成 SO。

## 探索与后勤系统

探索仍是玩家主动的领地扩张手段，但地图不再"全图始终可见"——迷雾由**后勤供应**动态驱动，可逆覆盖：

- **视觉**：迷雾遮罩（`_FogMaskTex`）由 [ChunkMapRenderer](Assets/Scripts/Managers/ChunkMapRenderer.cs) 全量重建（先清空 R 通道再按 `FogAlpha` 盖章；2026-08-04 起旧 `MapRenderer` 已删除，盖章重建迁入 Chunk 后端并订阅 `LogisticsChanged`），配合 [FogBlend.cginc](Assets/Shader/FogBlend.cginc) 去饱和 + 半透明雾叠加。已归属格按"归属方探索 + 后勤畅通"判断（双方观察一致）；中立格按观察方永久发现状态判断；断供后迷雾重新覆盖（含该格建筑模型），恢复供应后平滑揭雾（`FogTransitionManager` 过渡动画）。
- **探索方式**：
  - 玩家点击势力范围相邻未探索格上的费用标签，支付金币即可探索（标记已探索 + 圈入势力范围 + 收割资源）。
  - 占领公共建筑后，其势力范围自动标记为已探索并收割资源。
  - 公共建筑被发现时（单位接近触发），占位格及外一环自动探索但不改变归属，资源模型从隐藏变为可见。
- **不可探索区域**：公共建筑占位格及其周围一环地块标记为不可探索——不显示费用标签，无法通过探索系统获得，只能通过占领公共建筑获取势力范围。
- **费用与收入**：探索费用按地块自身预生成的奖励类型决定（`ExplorationRewardConfigSO.explorationCostsByType`，未配置或越界回退默认 50），费用标签同时显示该格奖励类型图标；金币不足时标签压暗且不可点击。被动金币收入由 `GoldIncomeService` 每秒提供（基础 + 金矿地貌加成 + 金矿建筑产出，断供时暂停产金，恢复供应自动恢复）。地块收割奖励为 5 基础 + 资源加成。
- **探索奖励**：奖励在地图生成流程中固化到每个可探索陆地地块（`ExplorationRewardData` 快照，类型权重与数值档位配置见 `ExplorationRewardConfigSO`；公共建筑占位区与竞技场预留区等不可探索格不生成），探索完成时玩家/AI 各自消费快照结算——金币入账、军事单位生成（溢出放相邻格）、战术卡牌发牌（玩家侧）、建筑放置（格子不合格或生成失败降级为金币），详见 [游戏系统/探索奖励随机机制设计讨论.md](游戏系统/探索奖励随机机制设计讨论.md)。

## 断供迷雾与建筑失能吞并

在后勤系统之上追加三条规则（决策记录见 [断供迷雾与建筑失能吞并设计方案.md](游戏系统/断供迷雾与建筑失能吞并设计方案.md)）：

- **断供迷雾覆盖建筑**：断供地块的迷雾覆盖范围延伸至建筑模型（`FogEnvironmentSelectiveEffect` 纳入 `PlayerBuilding`/`EnemyBuilding` 根节点），血条等建筑 UI 随之隐藏（隐藏整个建筑 Canvas）。
- **建筑断供失能**：所有建筑统一受 `BuildingSupplyGate` 门控——箭塔停火、兵营暂停（保留进度）；失能建筑不再是自动索敌目标（双方 `FindNearestEnemyBuilding` 过滤），但仍可被贴近攻击摧毁。
- **单位擦除层**：单位是透明队列（不在相机深度纹理中），雾化对象（金矿/资源/建筑）的深度裁剪看不到单位，会连带盖住单位——`FogEnvironmentUnitErase` 擦除 pass 把"可见单位"像素从雾化遮罩中清除，单位永不雾化（决策 8），移动中实时生效；单位血条/图标是世界空间 Canvas（同样不写深度），由 `FogEnvironmentUnitUIErase` 按屏幕矩形擦除，单位 UI 同样不被迷雾遮挡。
- **占领与区域吞并**：
  - 逐格占领：单位踩上敌方地块即占领（`UnitMovementSystem.TryCaptureEnemyCell` / 建筑摧毁后 `BuildingController.TryCaptureAfterBuildingDestroyed`）；失能建筑不阻挡占领，随格易主（`BuildingTransferService`，HP 回满）。
  - 区域吞并：`AnnexationService` 在每次后勤重算后扫描断供区域，与敌方后勤网络共边相邻即整区域易主（含建筑与公共建筑外一环），整个流程只触发一次 `LogisticsChanged`。
  - 豁免：主城格永不吞并；中立公共建筑格（伪阵营 Key ≥ 2）豁免占领与吞并；吞并格以 `(f, 0)` 并入吞并方主城单城字典（P0 约定）。

## 公共建筑系统

详见 [游戏系统/公共建筑系统设计讨论.md](游戏系统/公共建筑系统设计讨论.md)。

- **生成**：地图生成后、势力范围初始化前，由 `PublicBuildingGenerator` 在陆地内部区域随机生成。所有公共建筑预制体必须在其根 GameObject 上挂载 `PublicBuildingBase` 派生组件，否则会在启动时被预校验跳过。
- **形态**：可单格或多格（根格 + 最多 3 子格），配置在 `PublicBuildingSO` 中。
- **归属**：开局中立（伪 AI 阵营），初始血量用 `captureHp`，归属后防守血量用 `defenseHp`。攻击中立公共建筑的任意占位格均可转发到根格。
- **易主**：首次击破归属攻击方，之后被敌方击破直接易主不回中立。
- **发现机制**：公共建筑开局隐藏（`SetActive(false)`），只有浮标示意大致位置；任意单位进入建筑占位格外一环触发发现——模型渐显、占位格及外一环自动探索（不改变归属）、资源模型恢复、浮标消失，并复用探索系统的石柱/飞盘特效。
- **单位趋向**：双方单位通过 `UnitBrainBase.FindApproximateDirectionToHiddenBuilding()` 自动朝最近浮标方向单步移动，发现后路径失效并重新决策。
- **势力范围**：发现不改变归属；占领后所有占位格各自向外扩展一环，地块自动探索并收割资源（已发现但未收割的资源在占领时补偿收割）。
- **不可探索**：公共建筑占位格及其周围一环在生成时标记为不可探索——不显示探索费用标签，玩家和 AI 均无法主动探索。这些地块只能通过发现后占领公共建筑获取。发现前资源模型同步隐藏。
- **与后勤/吞并**：公共建筑区域断供时失能停摆；区域吞并时整体易主（含外一环，走 `OnCaptured` 全量）。中立公共建筑格（伪阵营 Key ≥ 2）豁免逐格占领与区域吞并；可见性按观察方永久发现状态判断（发现后对双方可见）。

## 战术卡牌系统

战术牌是区别于普通卡（部署单位/建筑）与天赋卡（永久 Buff）的第三种卡牌——开局固定发放、拖拽释放到目标格的即时效果卡，设计见 [战术牌系统SO设计.md](游戏系统/战术牌系统SO设计.md)：

- **卡牌**：战斗号令（`tactical.battle_order`，落点及周围一环内己方单位临时 +30% 攻击、+30% 移速，持续 5 秒）与维修（`tactical.repair`，落点及周围一环内己方建筑/单位群体回血 30%），开局各 ×1。
- **运行时**：`TacticalCardPresenter` 维护 `List<TacticalCardInstance>`（SO 模板 + 叠放数量），同名牌叠放显示数量徽标、耗尽后视图销毁、不自动补充；探索奖励「战术卡」类型经 `AddCard()` 补发（新类型占用空闲锚点槽位，最多 `CoreGameplayConfigProvider.TacticalCardSlotCount` 槽）。
- **效果**：`BattleOrderBuffService`（`ITickable` 计时，暂停冻结）维护战斗号令临时增益；维修在 `TryExecuteRepair()` 内对落点及周围一环己方建筑/单位回血。
- **数值来源**：启用卡列表与效果数值仅读 Excel `TacticalCardBalanceDatabaseSO`（阶段 6 唯一主源），资源（卡面/预制体）走 `TacticalCardSO` / `TacticalCardDatabaseSO`。
- **UI**：复用普通卡牌 `Card.prefab` 外观，两个固定锚点由 `GameInstaller` 注入；拖拽复用 `CardController` 逻辑，不做 hex 高亮，目标为落点格。

## 地图道具与资源系统

地图在生成时按权重随机散落可拾取资源，设计见 [地图道具系统总结.md](地图与地形/地图道具系统总结.md) 与 [地图资源配置设计案.md](地图与地形/地图资源配置设计案.md)：

- **资源类型（Excel `地图资源` 表）**：动物（下一次攻击提升）、宝箱（金币）、矿物（下一次防御提升）、植物（立即回血）；回血包（`resource.health_pack`）历史保留但 `enabled=false`，不参与生成。
- **拾取与收割**：`MapResourceCollectionService` 是单位拾取与探索/占领收割的唯一入口，基于 `HexCellData.TakeResource()` 原子消费防重复结算；拾取后按 `ResourcePickupEffectType`（攻击提升/防御提升/回血/金币）结算，宝箱金币仅玩家有效。
- **数据**：`MapResourceProvider` 从 Excel `MapResourceBalanceDatabaseSO` 取生成权重/拾取效果/探索收割数值，资源引用（模型/特效）走 `MapResourceDatabaseSO` / `MapResourceSO`；`ResourceSpawnRule` / `ResourceRewardRule` / `RewardBuildingRule` 分别负责散落生成、探索收割奖励与奖励建筑放置。
- **配置**：`地图资源全局` 表维护空地块权重与基础探索金币；金矿产出另见「金矿与收入规则」。

## AI 模块

AI 逻辑集中在 [Assets/Scripts/AI/](Assets/Scripts/AI/)，由 [AIManager](Assets/Scripts/AI/AIManager.cs) 编排初始化，实际运行时由 `AIUnitBrain` + `AIAutoExplorer` + `AICardTicker` + `GameLoop` 持续驱动：

| 类 | 职责 |
| --- | --- |
| `AIManager` | 协调者：开局初始化、城市预制体传递、AI 禁用控制 |
| `AIEntityFactory` | 敌方城市/单位/建筑的实例化与势力范围扩展 |
| `AICardBrain` | AI 抽卡状态、卡牌管线与出牌落点决策（手牌直接持有普通卡配置对象） |
| `AICardTicker` | AI 卡牌定时器：每 5 秒驱动一次 AI 抽卡管线（`ITickable`） |
| `AIAutoExplorer` | AI 自动探索器：定时免费探索邻接己方领地的未探索地块（`ITickable`） |
| `AIUnitBrain` | AI 单位的实时行为 Brain：全知索敌、逐格决策（位于 `Assets/Scripts/Units/`） |
| `AIRandomProvider` | AI 各服务共享的随机源 |
| `AIConfigProvider` | AI 配置提供者：出牌/探索节奏、全局操作间隔、出牌优先级与军事奖励溢出环数（Excel 唯一主源） |

玩家与 AI 共享的规则（抽卡生成、生成时的 UI 拼接）已收敛到 [CardGenerationRule](Assets/Scripts/Core/Services/CardGenerationRule.cs) 与 [SpawnUIWiring](Assets/Scripts/Core/Services/SpawnUIWiring.cs)。

## 测试

测试程序集仅面向 Unity Editor，现有测试位于 [Assets/Tests/](Assets/Tests/)，覆盖：

- 卡牌服务（`CardServiceTests`：首张保底卡（当前 `unit.archer`）、后续抽卡属于卡池、卡槽占用与释放）
- 卡牌解锁规则（`CardUnlockRuleProviderTests`：卡池内容即解锁内容、保底卡配置）
- 六边形地图服务（`HexMapServiceTests`）
- 单位移动系统（`UnitMovementSystemTests`）
- 单位数据仓库（`UnitRepositoryTests`）
- 单位移除服务（`UnitRemovalServiceTests`）
- 游戏安装器（`GameInstallerTests`）
- 领域不变量（`DomainInvariantTests`，含后勤连通、迷雾可见性、区域吞并、单次事件）
- 输入摄像机配置（`InputCameraConfigurationTests`）
- 地图控制器网格（`MapControllerMeshTests`）
- 三角形过渡网格（`TriangleTransitionMeshTests`）
- 地图变化服务（`MapMutationServiceTests`：事务协议/水陆双向重置/脏位/事件广播；`MapMutationStage5Tests`：归属接入/诊断/并行动画/分帧提交；`MapMutationMountainTests`：山脉变化事务）
- 程序化山脉（`RidgeGeneratorTests`：脊线行走/宽度化/确定性；`MountainGeometryTests`：Low-poly 几何构造；`MountainTopologyTests`/`MountainTopologyRouteTests`：拓扑签名与动画路由；`MountainCellRuleTests`：山格玩法规则；`MountainVisibilityRuleTests`/`MountainVisibilityResolverTests`：永久可见与迷雾合成；`MountainHighlightGateTests`：山格高亮门禁；`MountainMaterialContractTests`：材质契约；`MountainStage6/7*Tests`：源码契约、性能与视觉验收）
- 探索奖励预生成（`ExplorationRewardGenerationTests`：同种子确定性序列、快照一次性消费）
- 视觉过渡服务（`MapVisualTransitionServiceTests`：错峰/生命周期/Wave 行窗口脉冲回归）
- 卡牌生成规则（`CardGenerationRuleTests`：Excel 卡池 → 普通卡配置解析）
- Chunk 渲染后端（`ChunkRendererTests`）、迷雾连接面片（`FogConnectorMeshTests`）
- 地貌簇生成规则（`LandFormClusterSpawnRuleTests`）
- 临时可见性服务（`TemporaryVisibilityServiceTests`）
- 单位移动弹射（`UnitMovementEjectionTests`）
- Console 日志工具（`ConsoleLogFormatterTests`、`ConsoleLogEntriesReflectorTests`、`ConsoleToolbarInjectorTests`）

运行方式：

1. 在编辑器中打开 **Window > General > Test Runner**。
2. 选择 **EditMode**。
3. 点击 **Run All**。

测试程序集配置见 [Assets/Tests/Tests.asmdef](Assets/Tests/Tests.asmdef)。

## 改造历史

项目最初为回合制策略游戏，于 2026-07-24 完成全面实时化改造，详见 [历史归档/改造方案讨论总结.md](历史归档/改造方案讨论总结.md)。主要变更：

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

2026-07-26 全项目性能优化，详见 [视觉与渲染/性能优化方案讨论.md](视觉与渲染/性能优化方案讨论.md)：

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

2026-07-26 新增天赋卡牌系统，详见 [游戏系统/天赋卡牌系统整合方案.md](游戏系统/天赋卡牌系统整合方案.md)：

- 从 card 项目（Roguelite 卡牌系统）迁移 Buff 系统，改为阵营级 `FactionBuffService`
- 新增 `TalentCardConfigSO` / `TalentCardPoolSO` ScriptableObject 数据层
- 触发时机：游戏开始 + 占领公共建筑
- 首批 3 张天赋卡：攻击力、防御力、金币获取（均为乘数型 Buff）
- `CombatResolver` 和 `GoldIncomeService` 接入阵营 Buff 乘数查询
- 选卡面板入场动画：暗幕淡入 + 3 张卡错开弹入（DOTween）
- 选中卡特效：放大闪白 + 震荡 + 屏幕震动（`CameraController.Shake()`）
- AI 后台自动随机选卡（`AITalentCardAutoSelector`）
- `GoldIncomeService` 注入 `GameLoop` 以响应暂停状态

2026-07-27 ~ 2026-07-29 公共建筑发现系统与多项修复：

- 公共建筑新增开局隐藏 + 浮标提示 + 单位接近触发发现的完整机制
- 新增 `PublicBuildingMarkerManager` / `PublicBuildingMarkerView`：运行时创建世界空间浮标（呼吸动画 + 始终面向相机），发现后销毁
- `ExplorationPillarPool` 新增 `PlayRevealEffect()`：公共建筑发现时播放石柱/飞盘特效，不调用探索服务的占领/收割流程
- 双方单位共用 `FindApproximateDirectionToHiddenBuilding()`，自动趋向就近浮标；发现后全局路径失效
- `MeleeStrategy` / `RangedStrategy` 的 `CanAttack` / `DoCombat` 过滤未发现的隐藏公共建筑
- 数据库新增 `GoldMine` 公共建筑配置（金矿）
- 修复团结引擎（Tuanjie）→ Unity 迁移遗留的 `.meta` GUID 格式问题：部分 Base64 GUID 无法被 Unity 场景识别，导致 `GameFlowManager`、`MapRenderer`、`MapGenerator`、`GlobalServicesInstaller` 丢失组件引用
- 修复 `ProjectContext` AudioManager 共用 AudioSource 的 SFX 配置警告
- `.claude/debugging-playbook.md` 建立，沉淀 Zenject 初始化顺序、预校验缺失组件、资产 GUID 迁移等可复用经验

2026-08-02 后勤系统与断供迷雾/建筑失能吞并（详见 [后勤系统设计方案.md](游戏系统/后勤系统设计方案.md) 与 [断供迷雾与建筑失能吞并设计方案.md](游戏系统/断供迷雾与建筑失能吞并设计方案.md)）：

- 后勤系统落地：`LogisticsService` 按阵营 BFS 供应缓存、`RegisterMainCity`/`SetOwner`/`TransferOwner`/`ClearOwner`、`IsVisibleToFaction` 迷雾判定、`LogisticsChanged` 事件
- 迷雾改为全量可逆重建（`MapRenderer.RebuildFogMask` 清空后按 `FogAlpha` 盖章）+ `FogTransitionManager` 平滑过渡；断供后重新起雾
- 建筑迷雾：`FogEnvironmentSelectiveEffect` 纳入 `PlayerBuilding`/`EnemyBuilding` 根节点，断供地块建筑随地面一起被迷雾覆盖；建筑 Canvas（血条/生产进度条）按玩家视角可见性隐藏
- 建筑失能：`BuildingSupplyGate` 统一门控（箭塔停火、兵营暂停）；失能建筑不再被双方自动索敌（`PlayerUnitBrain`/`AIUnitBrain` 过滤）
- 占领规则：仅功能正常的建筑阻挡占领；失能建筑随格易主（`BuildingTransferService`，含归属/tag/视觉/HP/索引同步）；中立公共建筑格（Key ≥ 2）豁免
- 区域吞并：`AnnexationService` 在每次后勤重算后扫描断供区，与敌方网络相邻即整体易主（批量写入、主城豁免、公共建筑含外一环、单次 `LogisticsChanged`）
- 数据前置：领地字典一律从地块归属重建（`RebuildSphereOfInfluence`），公共建筑不再伪装城市条目；中立公共建筑可见性按观察方发现状态修复

2026-08-03 普通卡池对象化改造（详见 [普通卡池对象化改造方案.md](历史归档/普通卡池对象化改造方案.md)）：

- 卡池配置化：普通卡池由 `NormalCardPoolSO` 驱动（12 张单位卡 + 6 张建筑卡引用 + 首张移民卡保底），删除 `CardUnlockRuleProvider` 硬编码 ID 数组与科技/文化参数
- 数据库对象化：`UnitDatabaseSO`/`BuildingDatabaseSO` 平行列表重构为 `UnitConfigSO`/`BuildingConfigSO` 对象列表（资产已原地迁移，GUID 不变），Provider 按显式 ID 查配置，列表顺序不再影响运行结果
- 卡牌管线对象化：`CardService.GenerateNextCard()`、`CardPresenter`、`ICardView`/`CardController`、AI 手牌（`AIPlayerState`）全部改持配置对象，删除复合卡 ID（单位数量偏移）规则
- 开局改为纯随机 5 张手牌（首张触发移民保底），删除临时固定箭塔逻辑
- 探索奖励 `rewardUnitIDs` 改为 `UnitConfigSO[]` 引用（原 ID 2/5 已迁移）；空数组不再回退魔法 ID
- 攻击音效配置化：`UnitMovementController` 的 UnitID switch 改为读取 `UnitConfigSO.attackSfx`
- 兵营产出配置化：`BuildingConfigSO.producedUnit` 注入动态创建的 `BarracksSpawner`
- 建筑类型直接存储枚举，删除 `(buildingId + 1)` 推导与不可通行魔法判定；建筑 HP 12 项中后 6 项垃圾数据已按决策删除
- 单位策略类型配置化：`UnitConfigSO.strategyType` 替代 `UnitStrategyFactory` 中的 0/3/5/9 魔法数
- 一次性资产迁移工具（`Tools/Normal Card Pool/Migrate`）在迁移完成后已删除

2026-08-04 ~ 2026-08-05 动态地图系统（阶段二~五）落地，详见 [动态地图/动态地图变化与分块重建方案.md](动态地图/动态地图变化与分块重建方案.md) 与 [动态地图/动态地图变化系统-使用报告.md](动态地图/动态地图变化系统-使用报告.md)：

- 事务管线：`MapMutationService` 提供 `BeginTransaction→Apply(HexCellPatch)→Commit/Rollback`；水陆跨界自动双向重置；单位联动（途经取消/不可通行弹射/站立吸附/路径失效）
- 分块渲染：`ChunkMapRenderer` 取代旧 `MapRenderer` 成为唯一渲染后端（8×8 offset-grid Chunk 双缓冲）；全地图波浪压力测试（576 格/24 Chunk）通过（2026-08-05）
- 变化动画：CPU 顶点动画（2026-08-05 起：UV2/UV3 缓存 startY/targetY/delay、逐帧写 `mesh.vertices`）+ 三套 `*_Transition` Shader（keep-below clip 顶出、TerrainGhost、手写 ShadowCaster）；错峰模式 Simultaneous/CenterToOuter/Wave；动画期间交互锁、水面河流淡出、单位与地貌模型跟随、并行动画与冲突强制完成；动画管线七次实机修订史见 [动态地图/地图动画实机问题与修改总结.md](动态地图/地图动画实机问题与修改总结.md)
- 迷雾修复：`_FogMaskTex` 重建迁入 Chunk 后端 + 订阅 `LogisticsChanged`（2026-08-04）
- 竞技场：`ArenaEventManager` 37 格状态机（Inactive→Reserved→Activated→Destroyed），预留区初始化、突起动画、宝箱摧毁恢复、对局结束动画兜底
- 能力测试：V 键全图波浪（纯视觉脉冲、自动回落）；R/F 键指格永久 ±1 微调（2026-08-05 已屏蔽保留，方案见 [鼠标指格地形高度微调测试-RF键实现方案.md](历史归档/鼠标指格地形高度微调测试-RF键实现方案.md)）
- 规划文档：[程序化山脉实现方式讨论.md](程序化山脉实现方式讨论.md)（地貌型程序化山脉构思，2026-08-06 起已实现，见下文）

2026-08-06 ~ 2026-08-12 程序化山脉系统与多项玩法/表现更新：

- **程序化山脉系统全落地**（阶段 1~7.8，决策记录见 [程序化山脉实现方式讨论.md](程序化山脉实现方式讨论.md)）：
  - 生成：`RidgeGenerator` 评分行走脊线 + 宽度化坡面格（不参与地貌散落/簇权重池），参数快照固化到格级数据（`MountainRidgeData`），SeedService 新增 "Mountain" 随机流；地图生成顺序调整为 地形 → 山 → 其他地貌 → 河 → 资源
  - 几何：`MountainGeometryBuilder` 确定性高度场（脊线噪声 + 格级噪声）+ Low-poly 顶面/过渡面/封口构造，脊线连续修订（均值→脊线相邻对）、山-普通 rect 格界劈半；`MountainHash` 确定性哈希
  - 材质：`MountainMaterialContract` 定义山体槽 UV0 契约（ridgeKey01 / faceTier 3 档色阶），新增 `Custom/MountainLowPoly_Fog` 与 `_Transition` 稳定 shader 资产
  - 玩法：`MountainCellRule` 山格不可通行/不可部署/不可建造（寻路、卡牌放置、兵营生产、AI 出牌统一收口）；`MountainVisibilityRule` 山脉格永久视觉可见（免雾但不算已探索，与迷雾合成同口径）
  - 渲染：`ChunkMapRenderer` 山体槽（替换式拓扑、collision 碰撞网格分离、山脚融合槽、稳定材质实例化、Transition keep-below clip、提交前拓扑签名比对防动画残留）
  - 动态地图：`MapMutationService` 山体事务——清除 = 永久移除（`mountainCleared`）、水淹移除山体贡献保留海床、移除方向整笔降级同步提交、新增方向动画顶出；`HexCellPatch` 新增山脉字段组
  - 竞技场：`ArenaEventManager` 37 格平台 + 边界山脉封闭墙（`closedWallCols` 墙鞍部规则防镂空），平台与山脉一笔动画中心向外顶出，宝箱摧毁后解除限制、山脉永久保留
  - 测试：新增 `RidgeGeneratorTests` / `MountainGeometryTests` / `MountainTopologyTests` / `MountainTopologyRouteTests` / `MountainCellRuleTests` / `MountainVisibilityRuleTests` / `MountainVisibilityResolverTests` / `MountainHighlightGateTests` / `MountainMaterialContractTests` / `MountainStage6/7*Tests` / `MapMutationMountainTests`
- **探索奖励预生成**（当前工作区未提交）：奖励改为地图生成流程中固化到地块（`ExplorationRewardData`，SeedService "ExplorationReward" 流，GeneratorVersion 2；在公共建筑/竞技场预留区标记不可探索后生成，跳过不可探索格），玩家与 AI 结算只消费快照；探索费用按地块奖励类型决定（`explorationCostsByType`，默认 50）；费用标签第二个子物体显示奖励类型图标（`UIController.SetRewardTypeIcon`，新增 gold/unit/card/goldmine 图标资源）；`ExplorationRewardConfigSO.asset` 权重调整为 20/20/20/20/20；新增 `ExplorationRewardGenerationTests`
- **金矿建筑**：`BuildingConfigSO` 新增 `enemyBuildingModel` 与 `goldIncomePerSecond`，新增 `gold_mine.asset`（`BulidingType.GoldMine`）；`IncomeEligibilityRule`（归属 + 后勤畅通）与 `BuildingIncomeRule`（金矿产出汇总）落地，`GoldIncomeService` 每秒收入 =（基础 + 金矿地貌 + 金矿建筑）× 天赋乘数
- **势力范围实体城墙**：`SphereOfInfluenceRenderer` 改为纯实体模型渲染（玩家/AI 各一套城墙/城墩预制体 `wall-player`/`wall-AI`，部件独立判定），删除旧描边面片回退渲染
- **其他**：镜头新增鼠标左键拖拽平移（`CameraController.HandleMouseDrag`，UI 上不触发）；开局界面新增 `SimpleStartButton`（延迟激活 + 呼吸动效直接进游戏）；`GlobalTimerService`/`GlobalTimerUI` 全局倒计时 HUD（默认 300 秒、≤60 秒紧急脉冲，选卡面板触发启动）；天赋卡选卡面板背景图 shuffle 不重复；`EndGame` 平局（Draw）按胜利结算；`UnitMovementController` 死亡流程防护与 Animator 诊断、`CombatResolver` 死亡单位不再结算伤害；`MountainConfigSO` 新增 `debugSingleCellAndStraightRidge` 对照模式；地图生成配置更新（20×24、种子 20260811、`visualPerturbFrequency` 噪声采样频率配置化、主城按地图中列 + 边缘前一行定位）；建筑配置资产重命名（`BuildingConfig-4`→`barracks`、`BuildingConfig-5`→`arrow_tower`）

Excel 配置表混合架构改造（阶段 1~6 已完成，详见 [Excel配置表混合架构修改计划.md](Excel配置表混合架构修改计划.md)，各阶段完成说明见 `历史归档/阶段2~5运行时接入-完成说明与验证清单.md`）：

- 数据通路：`Config/Excel/游戏数值配置.xlsx` → `Tools/ConfigExporter`（独立 .NET 8 工具，含单元测试）导出 CSV/JSON → Unity 菜单 `Tools/游戏配置/导入并校验` 生成 `Assets/GameConfig/Generated` 只读 SO → `GameInstaller` 绑定 + `*ConfigProvider` 运行时读取
- 阶段 2：单位/建筑/普通卡池数值迁移（14 单位、7 建筑、4 张普通卡，`unit.archer` 首张保底；修正 `AttackInterval=0` 等异常值）
- 阶段 3：战术卡/天赋卡/天赋抽卡规则迁移（3 天赋卡 `talent.damage`/`talent.building_hp`/`talent.gold`、2 战术卡 `tactical.repair`/`tactical.battle_order`）
- 阶段 4：探索奖励、地图资源、地图地貌、公共建筑迁移（`ExplorationRewardProvider`/`MapResourceProvider`/`MapLandFormProvider`/`PublicBuildingDataProvider`）
- 阶段 5：经济、游戏流程、AI 节奏、地图生成、战斗公式、手感/表现、山体参数迁移（`EconomyConfigProvider`/`GameFlowConfigProvider`/`AIConfigProvider`/`MapGenConfigProvider` 等）
- 阶段 6：删除 `?? legacy` 回退，确立 Excel 唯一数值源（Provider/Rule 缺失即抛异常）；`TalentCardPoolResolver` 抽卡等死代码收口；旧 SO 仅保留资源引用
- 新增目录：`Assets/GameConfig`、`Config/`、`Tools/ConfigExporter`；[需配表数值统计.md](历史归档/需配表数值统计.md) 收口硬编码与重复定义

## 当前说明

- 性能已针对六边形地图完成全项目优化（P0 + P1 全部落地，当前地图配置 20×24 = 480 格），详见 [视觉与渲染/性能优化方案讨论.md](视觉与渲染/性能优化方案讨论.md)。
- 平衡数值已收口到 Excel 配置表（`Config/Excel/游戏数值配置.xlsx`，阶段 1~6 完成），运行时数值由 `Assets/GameConfig/Generated` 生成的只读 SO 提供。改数值流程：编辑 Excel → 运行 `Tools/ConfigExporter` → Unity 菜单 `Tools/游戏配置/导入并校验`，详见 [Excel配置表混合架构修改计划.md](Excel配置表混合架构修改计划.md)。
- 项目当前仍使用默认工程名称，尚未在仓库中确定正式游戏名称。
- 仓库中包含部分历史资源和第三方资源目录（KayKit、Lana Studio、Toon_RTS、unity-chan!、VFXPACK_IMPACT_WALLCOEUR_FreeVersion 等），其是否仍被场景或脚本引用需要单独确认后再清理。
- 仓库暂未提供明确的发布平台说明、CI 配置或自动化构建脚本；`Tools/ConfigExporter` 支持命令行/CI 无界面导出，但尚未接入 CI 门禁。
- 当前工作区可能包含未提交的配置生成物与探索奖励预生成改动（地块奖励快照、按奖励类型费用与标签图标，见改造历史）。
- 项目配套的设计文档分散在多个专题目录中：`游戏系统/`、`探索系统重构/`、`视觉与渲染/`、`地图与地形/`、`动态地图/`、`AI设计/`、`审计与规划/`、`历史归档/`；根目录保留活跃方案/讨论文档（`Excel配置表混合架构修改计划.md`、`程序化山脉实现方式讨论.md`（含山脉决策 ①~㉛ 记录）、`六边形网格覆盖层实现计划.md`），过时文档已归档至 `历史归档/`（阶段2~5运行时接入完成说明、需配表数值统计、策划案差异审计报告、普通卡池对象化改造方案、中间代码清理名单、鼠标指格地形高度微调测试-RF键实现方案等）。
