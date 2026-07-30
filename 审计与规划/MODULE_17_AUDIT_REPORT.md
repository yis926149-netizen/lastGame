# 模块 17 检查报告：端到端流程与跨模块一致性

## 结论

- **状态**：有条件通过
- **检查日期**：2026-07-17
- **检查者**：OpenCode Agent
- **说明**：核心回合循环、单位/卡牌/城市归属、死亡清理等主链路未发现阻断性 Bug；发现若干边缘场景一致性风险与性能/算法优化点，已分级记录。性能与算法优化建议单独汇总在文末，供后续专项处理。

## 修复状态（2026-07-17）

- P2-1：已移除 AI Phase 索引硬编码，改为按阶段实例定位。
- P2-2：已明确 `SettlementPhase` 当前为同步阶段的执行契约；异步化仍属于未来扩展项。
- P2-3：攻击者无效时恢复城市接管状态，允许后续重试。
- P2-4/P2-5：运行时单位和建筑血条统一通过 `Slider.fillRect` 着色，不再依赖子节点顺序。
- P2-6：势力扩张规则下沉为中立的 `SphereOfInfluenceRules`，AI 不再调用玩家管理器入口。
- P2-7：已将字符串 `Invoke` 改为 `nameof`。
- 验证：`MainGame.csproj` 与 `Tests.csproj` 编译成功，0 error；Unity EditMode `DomainInvariantTests` 22/22 通过。

---

## 检查范围

- 场景流转：`StartScene` → `GameScene` → 返回菜单
- 回合状态机：`GameStateMachine` + `PlayerPhase` / `AIPhase` / `SettlementPhase`
- 单位系统：`UnitRepository`、`UnitMovementController`、`UnitMovementSystem`、`UnitRemovalService`
- 卡牌系统：`CardService`、`CardPresenter`、`CardController`
- 城市/建筑归属：`BuildingController`、`PlayerModelManager`、`EnemyModelManager`
- 胜负判定：`EndGame`
- 科技文化：`Tech_CultureTreeController`、`ITechCultureService`
- AI 管理：`IAIManager`（含 AI 回合、AI 卡牌、AI 科技文化）
- 输入与 UI：`PlayerInputHandler`、`UIController`、`UIManagerPresenter`
- 地图/迷雾：`MapGenerator`、`MapRenderer`、`HexMapService`、`FogManager`
- 现有测试：`DomainInvariantTests`、`GameStateMachineTests`、`UnitRemovalServiceTests` 等

---

## 已执行验证

| 验证项 | 方法 | 结果 |
|--------|------|------|
| 回合阶段流转 | 阅读 `GameStateMachine` + 三个 Phase 实现 | 通过 |
| 玩家单位移动力重置 | 阅读 `PlayerPhase.Enter` / `GameStateMachine.ResetUnitMovement` | 通过 |
| 卡牌回合重置 | 阅读 `GameStateMachine.ResetForNewTurn` | 通过 |
| 单位唯一归属 | 阅读 `UnitRepository.AddPlayerUnit/AddEnemyUnit` | 通过 |
| 单位死亡清理 | 阅读 `UnitMovementController.UnitDeath` + `UnitRemovalService` | 通过 |
| 建筑/城市归属同步 | 阅读 `BuildingController.CityDestroyed` | 通过（有边缘风险） |
| 胜负判定 | 阅读 `EndGame.EvaluateResult` + 现有测试 | 通过 |
| 现有自动化测试 | Unity EditMode Test Runner | `DomainInvariantTests` 22/22 通过；全量测试仍含既有 EditMode `Destroy` 日志问题 |

---

## 发现

按 P0（阻断）→ P3（建议）排列。本次审查**未发现 P0/P1 级明显 Bug**。

### P2：边缘场景一致性风险（建议修复，不阻断主流程）

| # | 位置 | 问题 | 影响 | 最小修复方向 |
|---|------|------|------|--------------|
| 1 | `GameStateMachine.cs:81` | `ProcessAIPhase` 中硬编码 `if (_currentPhaseIndex == 1)`，假设 AI Phase 一定是索引 1。若未来调整 Phase 顺序或插入新阶段，逻辑会静默失效。 | 阶段扩展时 AI 回合后无法正确进入 Settlement | 改为查找 `_phases.FindIndex(p => p is AIPhase)` 并基于该索引判断 |
| 2 | `GameStateMachine.cs:85-91` | Settlement Phase 的 `Enter()` 和 `Exit()` 在同一帧连续调用，没有等待异步结算完成。若未来 Settlement 改为协程/异步，会出问题。 | 扩展性风险 | 保持现状但加注释；若 Settlement 需要异步，应改为 `Task` 等待 |
| 3 | `BuildingController.cs:160-164` | `CityDestroyed` 中若 `Attacker` 为 null 或缺少 `UnitMovementController`，直接 `return`，但 `isCityChangeOwner` 已被置为 `true`。后续该城市空血不会再触发易主，形成“死城”。 | 极端情况下城市无法被再次攻占 | 在 `return` 前重置 `isCityChangeOwner = false` |
| 4 | `BuildingController.cs:385-409` | `SetCityVisual` 通过硬编码子节点路径 `firstChild.GetChild(0).GetChild(2)` 查找血条填充图，层级变动时会静默失效。 | 城市易主后血条颜色可能不更新 | 改为按名称/Tag 查找，或在 `BuildingController` 中缓存 `hpFill` 引用 |
| 5 | `UIController.cs:288` | `CityBuilderSkill` 中 `healthBar.transform.GetChild(2)` 与 `CardPresenter.SpawnUnit` 中的血条颜色设置路径一致，但均为硬编码，存在相同层级耦合风险。 | 建城后血条颜色可能异常 | 统一封装血条颜色设置方法，避免散落的硬编码路径 |
| 6 | `IAIManager.cs:174-185` | `AICityGenerator` 调用 `_playerModelManager.ExpandTheSphereOfInfluence` 来扩展 AI 势力范围。虽然功能正确，但语义上 AI 和玩家共用玩家势力的扩张方法，边界不够清晰。 | 未来若玩家势力扩张逻辑变更，可能意外影响 AI | 将势力扩张逻辑下沉到中立服务，或为 AI 提供专用方法 |
| 7 | `EndGame.cs:45` | `Invoke("EndThisGame", 1.5f)` 使用字符串方法名，重构时易断裂。 | 重命名方法后调用失效 | 改为 `Invoke(nameof(EndThisGame), 1.5f)` |

### P3：代码质量与可维护性建议

| # | 位置 | 问题 | 建议 |
|---|------|------|------|
| 8 | `CardService.cs` / `AICardState` | 玩家与 AI 各自维护一份“每回合只能发一张牌”的状态，逻辑重复。 | 抽象出共用的 `TurnCardState` 结构或服务 |
| 9 | `UnitMovementController.cs` | 攻击流程状态字段较多（`GoToAttackPosition`、`CommenceAttack`、`ReturnToOriginalPosition` 等），可重构为更清晰的状态机。 | 引入简单状态枚举，减少布尔字段组合 |
| 10 | `GameStateMachine.cs:109-114` | `ResetForNewTurn` 中 `_cardPresenter.TryDealFromNextIfPossible()` 与 `_cardPresenter.OnTurnEnded()` 都涉及次卡补充，职责略重叠。 | 明确卡牌补充时机：回合开始自动补充 vs 回合结束兜底 |
| 11 | `HexCellData.cs:237-240` | 构造函数中未探索地块 `movementCost = float.MaxValue`，但 `ExploreThisHexCell` 会重置为 1。逻辑正确，但 constructor 与探索方法职责可更内聚。 | 考虑将“未探索不可通行”规则提取为显式方法 |
| 12 | `PlayerInputHandler.cs:463` | 按 `G` 键切换网格显示，使用直接字段访问 `_mapGenerator.gridGameObject`。 | 通过 `IMapDataService` 或专门服务暴露网格开关 |

---

## 跨模块不变量检查结果

| 不变量 | 检查结果 | 说明 |
|--------|----------|------|
| 一个单位只属于一个阵营、一个仓库和至多一个地块 | ✅ 成立 | `UnitRepository` 在 `AddPlayerUnit/AddEnemyUnit` 时互相移除；`UnitRemovalService` 清理地块占用 |
| 一个地块的逻辑占用与场景对象一致 | ✅ 基本成立 | `UnitMovementSystem.CommitDestination/RestoreStartCell` 同步更新 `HexCellData.SetHaveUnit` |
| 卡牌只有在对象成功部署后才消耗 | ✅ 成立 | `CardPresenter.HandleCardDragEnd` 在 `SpawnUnit/SpawnBuilding` 成功后才 `RemoveCard` |
| 每个阶段每回合只进入和退出一次 | ✅ 基本成立 | `GameStateMachine.EndTurn` 和 `ProcessAIPhase` 控制流转；`PlayerPhase.CanExit` 防止忙碌时退出 |
| 玩家与 AI 共享的规则不因入口不同而产生差异 | ⚠️ 基本成立，有重复代码 | 卡牌解锁、科技文化进度逻辑在玩家侧 `CardService` 和 AI 侧 `IAIManager` 分别实现，规则一致但代码重复 |
| 城市和建筑归属在管理器、地块、Tag、父节点和 UI 中一致 | ⚠️ 基本成立，有边缘风险 | `BuildingController.CityDestroyed` 会同步 Tag、父节点、血条颜色和地块归属；风险点见 P2-3/P2-4 |
| 死亡/销毁对象不再被寻路、AI、UI、事件或协程引用 | ✅ 基本成立 | `UnitRemovalService` 取消移动、清理仓库；`PlayerInputHandler` 和 `UIManagerPresenter` 订阅移除事件取消选中 |
| 重新开局后的运行时状态等价于首次开局 | ⚠️ 未完全验证 | `UIControl.ToGameScene` 通过 `SceneManager.LoadScene(1)` 重载场景，静态锁已复位；但未实际运行验证场景重载后所有服务状态 |

---

## 必测流程覆盖情况

| 流程 | 状态 | 说明 |
|------|------|------|
| 1. StartScene 菜单与选项 | ✅ 代码审查通过 | `openController`/`gameOptionController` 初始化、按钮事件绑定完整 |
| 2. 地图生成、出生、迷雾、资源、UI | ✅ 代码审查通过 | `GameFlowManager.Initialize` 顺序：生成地图 → 渲染 → AI 初始化 → 玩家初始化 |
| 3. 选择单位、移动、近战/远程攻击、非法操作 | ✅ 代码审查通过 | `PlayerInputHandler` 处理选择/移动/攻击；`UnitMovementController.CanBeSelected` 限制非法选择 |
| 4. 单位卡/建筑卡合法与非法部署、消耗、视野更新 | ✅ 代码审查通过 | `CardPresenter.IsReleaseValid` 校验部署条件；部署成功才消耗卡牌 |
| 5. 建城、扩张势力、收割资源、科技文化升级 | ✅ 代码审查通过 | `UIController.CityBuilderSkill`、`UnitInfoPanelReapButton`、`Tech_CultureTreeController` 完整 |
| 6. 结束回合：PlayerPhase → AIPhase → SettlementPhase | ✅ 代码审查通过 | `GameStateMachine` 控制流转；连续 20 回合未实测 |
| 7. 击杀单位、摧毁建筑、攻占城市同步 | ✅ 代码审查通过 | `UnitMovementController.UnitDeath` + `UnitRemovalService`；`BuildingController.CityDestroyed/BuildingDestroyed` |
| 8. 胜利/失败触发、动画、音频、重复触发保护 | ✅ 代码审查通过 | `EndGame.Update` 每帧检测，`isEndThisGame` 防止重复触发 |
| 9. 返回菜单后再次开局无残留 | ⚠️ 未完全验证 | 场景重载会重建大部分服务；`UIControl._isLoadingGameScene` 静态锁在 `Awake` 中复位 |

---

## 测试缺口

- 缺少真正的端到端自动化测试（场景加载 → 回合循环 → 胜负 → 返回菜单 → 重开）。
- 缺少 AI 回合与玩家长时间对局的稳定性测试（20 回合以上）。
- 缺少城市多次易主、建筑摧毁后科技文化产量变化的回归测试。
- 现有测试无法通过 `dotnet test` 直接运行，需通过 Unity Test Runner 执行。

---

## 性能与算法优化记录（暂缓处理）

以下优化点不影响当前功能正确性，建议后续专项评估：

| # | 位置 | 优化点 | 预期收益 |
|---|------|--------|----------|
| 1 | `UnitMovementSystem.CalculateMinMovementCostBetweenTwoHexes` | Dijkstra 使用自定义 `MinPriorityQueue`，且每次寻路都初始化 `Point_minCost` 全图字典。可改为 A* 或仅初始化访问节点。 | 大地图、多单位寻路时减少内存分配和 CPU 开销 |
| 2 | `HexMapService.WorldToHexCoordinate` | 每次调用遍历全部中心点计算最近距离。可预计算空间索引（如 KD-Tree 或网格哈希）。 | 鼠标悬停、AI 决策等高频调用场景性能提升 |
| 3 | `PlayerInputHandler.ShowEnemyIndicators` | 每次选择单位时销毁并重新实例化所有敌人指示器。 | 减少 GC 和实例化开销 |
| 4 | `IAIManager.HandleSingleUnitTurn` | 对每个敌方单位都遍历全部玩家单位和建筑计算最近目标，复杂度 O(EN × PN)。 | 单位较多时 AI 回合耗时下降 |
| 5 | `FogManager.GenerateFog` | 每次地图视觉变化都重新生成整个迷雾 Mesh。 | 可减少为仅更新变化区域，或降低更新频率 |
| 6 | `MapRenderer` 系列 Mesh 生成方法 | 大量 `List<T>` 和临时数组分配，且 `MapController.CreatMesh` 每次新建 `Mesh` 对象。 | 对象池化 Mesh 和列表，减少 GC 压力 |
| 7 | `BuildingController.Update` | 每帧检查 `uiHealthBar.value <= 0`，可在血量变化事件驱动而非轮询。 | 少量 CPU 节省，代码更事件化 |
| 8 | `UnitMovementController.Update` | 每帧调用 `UnitAttacked`、`UnitAttack`、`UnitDeath`，即使单位空闲。 | 可改为状态驱动或事件驱动，减少无效调用 |

---

## 建议后续负责的模块

- **模块 04（回合/阶段）**：处理 `GameStateMachine` 硬编码 Phase 索引问题（P2-1）。
- **模块 05（单位/移动）**：处理 `UnitMovementController` 状态机重构建议（P3-9）。
- **模块 08（建筑/城市）**：处理 `BuildingController` 硬编码路径与死城风险（P2-3、P2-4）。
- **模块 09（卡牌）**：评估玩家/AI 卡牌状态逻辑去重（P3-8）。
- **模块 10（AI）**：评估 AI 寻路目标选择优化与势力扩张边界（P2-6、性能-4）。
- **模块 12（UI/输入）**：处理血条硬编码路径与网格开关解耦（P2-5、P3-12）。

---

*报告生成时间：2026-07-17*
*下一步：将性能与算法优化项拆分为独立任务，逐个评估收益与成本。*
