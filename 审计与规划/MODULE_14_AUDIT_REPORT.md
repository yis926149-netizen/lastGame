# 14. 自动化测试体系与可测试性审计报告

## 结论

本轮为**静态检查**（未在 Unity 内实际运行 Test Runner）。逐个核对了 `Assets/Tests/` 下全部 15 个测试文件，未发现明显 bug：

- 所有测试都对真实行为做断言，**不存在占位断言**（如 `Assert.IsTrue(true)`）、**无空测试体**。
- 测试中引用的全部被测方法/构造函数签名，均与当前实现一致（逐一核对，见下），**不存在与实现脱节的过时测试**。
- Mock 使用克制：只对外部依赖（数据 Provider、`IGameStateMachine`、`IUnitRepository` 等）打桩，核心逻辑（卡牌解锁映射、寻路、文化加成、结束判定、地形生成）用真实实现，**无过度 Mock 掩盖逻辑**。
- 随机相关测试使用固定种子（`TerrainGenerator.GenerateTerrainHeight(..., seed: 1234)`），可复现。
- 测试程序集 `Tests.asmdef` 配置正确，**不会进入 Player**：
  - `includePlatforms: ["Editor"]`
  - `defineConstraints: ["UNITY_INCLUDE_TESTS"]`
  - `autoReferenced: false`、`overrideReferences: true`、精确 `precompiledReferences: nunit.framework.dll`
- 全部为 EditMode 同步测试，**无 `WaitForSeconds` 等不稳定等待**。

未修复任何代码——因为没有发现明确的阻断性缺陷。以下为**性能/算法优化**与**覆盖缺口**，按你的要求先记录、后续再议。

---

## 现有覆盖清单（已核对为可信）

| 测试文件 | 被测对象 | 断言要点 |
|---|---|---|
| `CardServiceTests` | `CardService` | 首回合发定居者、解锁卡过滤、槽位占用/释放 |
| `CardUnlockRuleProviderTests` | `CardUnlockRuleProvider` | 科技/文化等级 → 解锁 ID 的累积映射（显式 TestCase）、无重复 |
| `TechCultureServiceTests` | `Tech_CultureTreeController` | 点数累积/升级、满级不回退、文化加成只改运行时不改模板、按等级作用于对应建筑 |
| `GameStateMachineTests` | `GameStateMachine` | 初始回合=1、`StartGame` 进入 PlayerPhase 且重置回合/阶段 |
| `HexMapServiceTests` | `HexMapService` | 坐标取格、邻居方向、世界→六边形映射、越界返回 false、角/中心邻居数 |
| `UnitMovementSystemTests` | `UnitMovementSystem` | 请求移动/取消移动的占用与移动力回滚、被占目标拒绝、`float.MaxValue` 不可通行、最短代价与可达范围 |
| `UnitRemovalServiceTests` | `UnitRemovalService` | 玩家/敌方移除幂等、清格一次、被他人占用不误清、按占用者引用兜底找格 |
| `UnitRepositoryTests` | `UnitRepository` | 增删与事件、敌方分组、玩家↔敌方登记迁移 |
| `DomainInvariantTests` | 多个静态/领域规则 | 资源掷点排除血包、地形种子可复现、六邻居择多、治疗封顶、城市索引不复用、势力范围重建、结束判定边界 |
| `MapControllerMeshTests` | `MapController.CreatMesh` | 纯视觉网格跳过碰撞体、法线数=顶点数、超 65535 顶点自动切 UInt32 |
| `GameInstallerTests` | `GameInstaller` | 缺引用时先抛异常且不注册任何绑定 |
| `InputCameraConfigurationTests` | 场景/脚本契约 | GUID 一致、相机旋转射线含 Map 层、Tick 单一属主、输入不依赖未定义 Layer |
| `ConsoleLogFormatterTests` / `ConsoleLogEntriesReflectorTests` / `ConsoleToolbarInjectorTests` | Editor 控制台工具 | 格式化顺序/去空白、反射兼容性有可读原因、注入幂等 |

---

## 覆盖缺口（P1/P2，需补测）

> 判据来自审计计划 §14 检查标准；以下为**尚未覆盖**项，非现有测试的缺陷。

### [P1] 缺少 PlayMode 冒烟测试

审计标准要求覆盖：两种启动路径、完整回合、部署与战斗、AI 阶段结束、城市易主、胜负。
当前 `Tests.asmdef` 仅 EditMode，**没有任何 PlayMode 测试程序集**。
建议：新增 `Tests.PlayMode.asmdef`（`includePlatforms: ["Editor"]` + `[UnityTest]`），至少覆盖：
1. `StartScene → GameScene` 一次完整开局；
2. 单回合 `PlayerPhase → AIPhase → SettlementPhase` 各进入/退出一次；
3. 部署单位卡 → 移动 → 攻击 → 击杀 → 单位/格子/仓库同步；
4. 返回菜单再开局无残留（服务、事件、静态状态、旧地图）。

### [P1] 缺少设计资产完整性 Editor 测试

审计标准要求：Missing Script、断引用、数据库数组/ID、构建场景、Tag/Layer 契约。
现有 `InputCameraConfigurationTests` 已用「读场景文本 + 断言 GUID/层」做了**部分**契约测试，但没有：
1. 扫描全部 Prefab/场景的 **Missing Script**（`m_Script: {fileID: 0}`）；
2. 校验 `UnitDatabaseSO` / `BuildingDatabaseSO` 数组长度与 ID 连续/无空引用；
3. 校验 `EditorBuildSettings.scenes` 与实际场景一致；
4. 校验 `Tag`/`Layer` 常量与 `TagManager.asset` 契约（现只硬编码断言了 Map 层 = bit 64）。

### [P2] AI 阶段行为未直接覆盖

`GameStateMachineTests` 只验证阶段切换，未对 `AIPhase`/`IAIManager`（注：该类名以 `I` 开头但实为 `MonoBehaviour`，属架构命名问题，见模块 16）的决策/移动/攻击做行为断言。
建议在 PlayMode 或以可注入桩覆盖：AI 找最近敌、可达范围内移动、阶段结束回调只触发一次。

### [P2] 缺少建议覆盖矩阵文档

审计标准要求「建立建议覆盖矩阵，不以单一行覆盖率代替行为覆盖」。目前无此矩阵，建议以「核心流程 × 已/未覆盖 × 自动化可行性」维护一张表。

---

## 性能 / 算法优化建议（后续再议）

以下均**不是 bug**，是测试或被测算法可优化处：

### [P2] 寻路复杂度：`UnitMovementSystem` 疑似基于列表的 Dijkstra
- `CalculateMinMovementCostBetweenTwoHexes` / `GetAllReachableHexesFromStartHex`（`UnitMovementSystem.cs:342 / 529`）以 `List<Vector3> allPoints` 作为全图输入，若内部用线性扫描找最小代价节点，复杂度约 O(N²)。
- 建议：改用优先队列（二叉堆）/ `SortedSet`，或对可达范围用有界 BFS 剪枝；地图变大时收益明显。
- 建议补一个「大地图（如 50×50）寻路耗时上限」的性能基准测试（`[Test]` + `Stopwatch` 断言，或 Unity `PerformanceTesting` 包）。

### [P2] `TerrainGenerator.OptimizeTerrain` 邻居择多
- `TerrainGenerator.cs:306` 对每格取六邻居择多，注意避免在内层循环重复分配临时集合/字典；可预分配固定长度的计数数组（高度值域已知且离散）。

### [P3] 测试内地图构造重复
- `HexMapServiceTests` / `UnitMovementSystemTests` / `UnitRemovalServiceTests` 各自手搓一份「5×5 六边形网格 + 邻居函数」桩。
- 建议抽一个 `TestMapFactory`（生成 `hexToCell` 与邻居 lambda），减少重复、避免三处坐标公式各自漂移。

### [P3] 反射调用 `SwitchOptions(ref Tech_Culture)`
- `TechCultureServiceTests` 通过反射 `Invoke` 调私有 `ref` 方法。因 `Tech_Culture` 是**引用类型**，`ref` 语义对字段可见性无影响，测试正确；但反射 + 私有方法使测试对重命名脆弱。
- 建议后续把该逻辑抽为可直接调用的内部方法（`internal` + `InternalsVisibleTo("Tests")`），去掉反射。

### [P3] 程序集分层评估
- `MainGame.asmdef` 为单程序集（无 `includePlatforms` 限制）。若后续要加 PlayMode 测试并做更细粒度隔离，可评估是否值得拆分核心逻辑与 Editor 相关代码；但**无实际收益时不建议为「分层」强拆**（与模块 16 结论一致）。

---

## 复核方式

- 静态：已逐文件读取 `Assets/Tests/*.cs`，并用 grep 核对全部被测符号在 `Assets/Scripts/` 中存在且签名匹配。
- 待办：在 Unity Editor 中打开 **Window → General → Test Runner**，运行 EditMode 全部用例，确认全绿（本次未执行 Unity）。
