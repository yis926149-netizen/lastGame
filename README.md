# 六边形回合制策略游戏

这是一个使用 **Unity 2022.3** 开发的 3D 六边形网格回合制策略游戏。玩家需要探索程序化生成的地图，在战争迷雾中建造城市、发展科技与文化，并通过卡牌部署单位和建筑，与 AI 势力展开战斗。

## 项目概览

- **程序化地图**：生成六边形地块、地形、河流与资源，并通过战争迷雾控制可见区域。
- **回合制流程**：每回合依次执行玩家阶段、AI 阶段和结算阶段。
- **单位与战斗**：支持单位选择、移动范围预览、路径提示、近战和远程攻击。
- **城市与建筑**：可建立城市、扩张势力范围，并部署不同功能的建筑。
- **卡牌系统**：通过拖拽卡牌向地图放置单位或建筑。
- **科技与文化**：积累科技点和文化点，解锁新的单位、建筑及属性成长。
- **AI 对手**：AI 会发展科技文化、使用卡牌、建立城市，并控制单位移动和战斗。

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
| 鼠标左键点击己方单位 | 选择单位并显示可移动范围 |
| 鼠标左键点击地图 | 将已选择的单位移动到目标地块 |
| 鼠标右键点击敌方单位或建筑 | 发起攻击；近战单位会移动至可攻击位置 |
| 拖拽卡牌到地图 | 在有效地块部署对应单位或建筑 |
| `W` / `A` / `S` / `D` 或方向键 | 平移镜头 |
| 鼠标滚轮 | 缩放镜头 |
| `Ctrl + A` / `Ctrl + D` | 围绕屏幕中心对应的地图位置旋转镜头 |
| `G` | 显示或隐藏六边形网格 |

单位与卡牌输入由 [PlayerInputHandler.cs](Assets/Scripts/Core/Services/PlayerInputHandler.cs) 处理，镜头操作由 [CameraController.cs](Assets/Scripts/Controllers/CameraController.cs) 处理。

## 回合流程

游戏回合由状态机和阶段对象共同驱动：

```text
PlayerPhase → AIPhase → SettlementPhase → 下一回合
```

- **PlayerPhase**：玩家移动、攻击、使用卡牌及管理发展路线。
- **AIPhase**：AI 使用卡牌、发展势力并控制单位行动。
- **SettlementPhase**：处理回合结算，更新科技、文化等状态。

相关实现位于 [Assets/Scripts/Turn/](Assets/Scripts/Turn/) 和 [GameStateMachine.cs](Assets/Scripts/Core/Services/GameStateMachine.cs)。

## 项目结构

```text
Assets/
├── Scenes/                         # 开始场景与主游戏场景
├── Scripts/
│   ├── Controllers/                # 单位、建筑、镜头等表现与控制组件
│   ├── Core/
│   │   ├── Interfaces/             # 核心服务接口
│   │   ├── Models/                 # 地块、单位、建筑、卡牌等领域数据
│   │   └── Services/               # 地图、单位、输入、卡牌、状态机等实现
│   ├── Data/                       # 科技、文化、地形及卡牌数据
│   ├── Infrastructure/Installers/  # Zenject 依赖绑定入口
│   ├── Managers/                   # 地图生成、渲染、迷雾及阵营管理
│   ├── ScriptableObjects/          # 单位、建筑、地图和 UI 配置
│   ├── Turn/                       # 玩家、AI、结算阶段与命令队列
│   ├── UI/                         # 卡牌、信息面板及界面视图
│   └── Utilities/                  # 六边形、网格、枚举等通用工具
└── Tests/                          # EditMode 单元测试

Docs/                               # DocFX 文档及服务说明
Packages/                           # Unity 包配置
ProjectSettings/                    # 编辑器、场景与项目设置
```

## 架构说明

项目将核心能力定义为接口，并通过 Zenject 注入具体实现，以降低模块之间的直接耦合：

- 接口位于 [Assets/Scripts/Core/Interfaces/](Assets/Scripts/Core/Interfaces/)。
- 实现主要位于 [Assets/Scripts/Core/Services/](Assets/Scripts/Core/Services/)。
- ScriptableObject 数据通过 `DataProviders` 提供给业务服务。
- [GameInstaller.cs](Assets/Scripts/Infrastructure/Installers/GameInstaller.cs) 负责主游戏场景中的地图、单位、卡牌、输入、UI 和状态机绑定。
- [GlobalServicesInstaller.cs](Assets/Scripts/Infrastructure/Installers/GlobalServicesInstaller.cs) 负责全局服务绑定。
- [GameFlowManager.cs](Assets/Scripts/Managers/GameFlowManager.cs) 负责主场景启动时的地图生成和游戏初始化流程。

主要服务包括：

| 服务 | 职责 |
| --- | --- |
| `IMapDataService` | 六边形坐标、地块数据和邻接查询 |
| `IUnitService` / `IUnitRepository` | 单位查询、管理与存储 |
| `IUnitMovement` | 移动、攻击移动和路径查询 |
| `ICardService` | 手牌、卡槽和卡牌生成 |
| `ITechCultureService` | 科技与文化点数、等级和进度 |
| `IGameStateMachine` | 当前回合与阶段切换 |
| `IInputService` | 鼠标、键盘、射线和 UI 遮挡判断 |
| `IMeshGenerator` | 地形、河流、迷雾等网格数据生成 |
| `IUIManagerView` | 科技文化、单位信息等 UI 更新 |

更详细的接口职责和使用方式见 [Docs/index.md](Docs/index.md) 与 [Docs/services/](Docs/services/)。

## 测试

测试程序集仅面向 Unity Editor，现有测试位于 [Assets/Tests/](Assets/Tests/)，覆盖：

- 卡牌服务
- 游戏状态机
- 六边形地图服务
- 科技与文化服务
- 单位移动系统
- 单位数据仓库

运行方式：

1. 在编辑器中打开 **Window > General > Test Runner**。
2. 选择 **EditMode**。
3. 点击 **Run All**。

测试程序集配置见 [Assets/Tests/Tests.asmdef](Assets/Tests/Tests.asmdef)。

## 开发文档

[Docs/](Docs/) 使用 DocFX 组织核心服务文档：

- [服务文档首页](Docs/index.md)
- [地图数据服务](Docs/services/IMapDataService.md)
- [单位服务](Docs/services/IUnitService.md)
- [单位移动](Docs/services/IUnitMovement.md)
- [卡牌服务](Docs/services/ICardService.md)
- [科技文化服务](Docs/services/ITechCultureService.md)
- [游戏状态机](Docs/services/IGameStateMachine.md)

新增或调整核心服务时，请同步维护对应文档，避免 README 与实现产生偏差。

## 当前说明

- 项目当前仍使用默认工程名称，尚未在仓库中确定正式游戏名称。
- 仓库中包含部分历史资源和第三方资源目录，其是否仍被场景或脚本引用需要单独确认后再清理。
- 仓库暂未提供明确的发布平台说明、CI 配置或自动化构建脚本。
