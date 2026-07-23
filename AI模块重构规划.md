# AI 模块重构规划（Tier 0 / 1 / 2）

> 本文档为**规划**，不含代码改动。目标：完成 Tier 0（改名归位）+ Tier 1（巨类拆分）+ Tier 2（消除玩家/AI 重复）。每个 Tier 独立可提交、可回滚，按序推进。

---

## 现状体检

`IAIManager` 是一个 **810 行、26 个方法的 MonoBehaviour 巨类**，注入 **13 个依赖**，职责横跨 5 个领域：

| 职责簇 | 方法 | 约行数 |
|---|---|---|
| **实体生成** | `AICityGenerator` / `AIUnitGenerator` / `AIBuildingGenerator` | ~200 |
| **卡牌经济** | `InitializeAICardState` / `GenerateAICardID` / `ExecuteAITurnCardPipeline` / `DealFromNextCardIfPossible` / `PlayAICards` / `GetCardPriority` / `TryPlaySingleCard` / `TrySpawnUnitFromCard` / `TrySpawnBuildingFromCard` / `IsValidSpawnCellForUnit` / `IsValidSpawnCellForBuilding` | ~150 |
| **战术回合** | `ExecuteAITurn` / `HandleSingleUnitTurn` / `ChooseFrontierTarget` / `HandleSettlerTurn` / `TryFoundCityWithSettler` / `TryReapChest` / `HexDistance` / `IsValidCityCell` | ~250 |
| **科技文化** | `ApplyTechCultureProgress` / `AddInstantTechCulturePoints` | ~50 |
| **初始化** | `AIInit` / `IsUnoccupiedLandCell` | ~40 |

### 三个真问题

1. **命名+位置误导**：具体 MonoBehaviour 却叫 `IAIManager`（`I` 前缀是接口约定），还放在 `Core/Interfaces/`。任何人第一眼都会以为它是接口——答辩时会被挑。
2. **与玩家侧平行重复、已在漂移**：
   - 生成：`AIUnitGenerator`/`AIBuildingGenerator` vs `CardPresenter.SpawnUnit`/`SpawnBuilding`——Instantiate + 注入 + `CharacterData` + Canvas + 血条 + 势力范围样板几乎一样。
   - 抽卡：`GenerateAICardID` vs `CardService.GenerateNextCardID`——镜像实现，**首回合移民卡条件已不一致**（玩家还要求 `CurrentTurn==1`，AI 只看标志位）。
   - 科文推进：`ApplyTechCultureProgress` vs 玩家技文树推进。
3. **单 AI 硬编码 vs 多阵营基建矛盾**：`AIIndex` 是常量 `1`，但 `EnemyModelManager` 的数据结构都按 `aiIndex` 分组（为多 AI 设计）。本次重构**不做多阵营化（Tier 3）**，但拆分时要为其留出参数化空间。

---

## Tier 0 · 改名 + 归位（零风险，先做）

**目标**：消除命名误导，不动任何逻辑。

**改动**：
1. `IAIManager`（class）→ 重命名为 `AIManager`。
2. 文件从 `Assets/Scripts/Core/Interfaces/IAIManager.cs` 移到 `Assets/Scripts/AI/AIManager.cs`（新建 `AI/` 目录，后续拆分类也放这里）。
3. 若需要真接口，抽一个精简的：
   ```csharp
   public interface IAIManager { void AIInit(); IEnumerator ExecuteAITurn(); }
   ```
   放 `Core/Interfaces/`；`AIManager : MonoBehaviour, IAIManager`。
4. 同步更新所有引用（`GameInstaller` 绑定、`AIPhase`、`GameFlowManager` 注入）。

**影响面**：`GameInstaller.cs`、`AIPhase.cs`、`GameFlowManager.cs`（当前引用 `IAIManager` 的 3 处）。
**验证**：编译通过、AI 行为与改前完全一致（纯符号/文件移动）。

> 注意 Unity 特有：`.cs` 改名/移动必须连同 `.meta` 一起，MonoBehaviour 类名变更后场景/预制体上的组件引用若按类名序列化需检查（本类是代码注入 `FromComponentInHierarchy`，风险低，但要确认场景里挂载对象的组件未变红）。

---

## Tier 1 · 巨类拆分（低风险，纯搬方法）

**目标**：把 810 行按职责簇拆成协作类，逻辑一行不改，只搬家 + 建立引用。

### 目标结构

```
AIManager（协调者，MonoBehaviour）
  ├── AIInit()           —— 编排开局
  └── ExecuteAITurn()    —— 编排每回合：先 CardBrain 再 TacticalBrain
        依赖：
        ├── AIEntityFactory   实体生成
        ├── AICardBrain       卡牌经济与出牌决策
        ├── AITacticalBrain   回合内单位行动
        └── AITechCultureProgress  科文推进（也可并入 CardBrain）
```

| 新类 | 收纳的方法 | 说明 |
|---|---|---|
| `AIEntityFactory` | `AICityGenerator` / `AIUnitGenerator` / `AIBuildingGenerator` | 敌方实体实例化；Tier 2 将与玩家共享底层 |
| `AICardBrain` | `InitializeAICardState` / `GenerateAICardID` / `ExecuteAITurnCardPipeline` / `DealFromNextCardIfPossible` / `PlayAICards` / `GetCardPriority` / `TryPlaySingleCard` / `TrySpawn*FromCard` / `IsValidSpawnCellFor*` | 持有 `AICardState`；出牌调用 `AIEntityFactory` |
| `AITacticalBrain` | `ExecuteAITurn` 的单位循环 / `HandleSingleUnitTurn` / `ChooseFrontierTarget` / `HandleSettlerTurn` / `TryFoundCityWithSettler` / `TryReapChest` / `HexDistance` / `IsValidCityCell` | 战术决策；**AI 逻辑迷雾（`_aiFog` 过滤 + 前沿游走）归此类** |
| `AITechCultureProgress` | `ApplyTechCultureProgress` / `AddInstantTechCulturePoints` | 科文等级推进 |

`AIManager` 保留 `AIInit` / `ExecuteAITurn` 的**编排骨架**，具体逻辑委托给上述类。

### 关键决策

- **拆成独立类，不用 partial class**。独立类边界清晰、利于单测与答辩讲解（"AI 分卡牌脑和战术脑"）；partial 只是物理分文件、逻辑仍纠缠。
- **状态归属**：`AIPlayerState`（`Card` + `TechCulture`）由 `AIManager` 持有，通过构造注入传给各 Brain，避免状态散落。
- **`AIIndex` 参数化预留**：各 Brain 方法签名把 `aiIndex` 作为参数（当前传常量 1），为 Tier 3 多阵营留口，但本阶段不启用。
- **DI 方式**：新类用 Zenject `Container.Bind<...>().AsSingle()`；非 MonoBehaviour 的 Brain 用普通类 + 构造注入。

### 风险与验证
- 纯搬方法，风险主要在"漏搬字段/依赖"和"跨类调用可见性"。逐类搬、每搬一类编译一次。
- 验证：开局 AI 建城/出兵、AI 回合追击/游走、建城扩张、科文升级——逐项对照拆分前行为。

---

## Tier 2 · 消除玩家/AI 重复（中风险，高收益）

**目标**：把玩家与 AI 各写一份的三块逻辑收敛到共享服务，根治"漂移 bug"。

### 2.1 实体生成去重

**现状重复**：`CardPresenter.SpawnUnit/SpawnBuilding`（玩家）与 `AIEntityFactory`（AI）都做：Instantiate → SetParent → tag → AddComponent → `_container.Inject` → 建 `CharacterData`/`BuildingData` → 面板数据 → Canvas/血条。差异仅：父物体（`PlayerUnit` vs `EnemyUnit`）、tag、`PlayerIndex`、势力范围归属、初始可见性。

**方案**：抽 `UnitSpawnService` / `BuildingSpawnService`，入参用一个"阵营上下文"结构：
```csharp
struct SpawnContext { int playerIndex; string parentName; string tag; bool startHidden; }
```
共享方法产出实体 + `CharacterData`/`BuildingData` + UI；阵营差异由 `SpawnContext` 决定。玩家侧与 `AIEntityFactory` 都改为调用它。

**收益**：单位/建筑生成只有一份真源，血条/面板/注入逻辑不再各改各的。
**风险**：动玩家部署路径，需回归玩家出牌、单位面板、血条显示。

### 2.2 抽卡生成去重

**现状重复**：`CardService.GenerateNextCardID`（玩家）与 `AIManager.GenerateAICardID`（AI）镜像，首回合移民卡条件已漂移。

**方案**：抽纯函数 `CardGenerationRule.GenerateNextCardId(techLv, cultureLv, hasGivenFirstSettler, random)`（无副作用，返回卡 ID + 是否已给保底）。玩家与 AI 各自持有自己的 `hasGivenFirstSettler`/`random` 状态，但**规则本体唯一**。
- 首回合保底条件统一（明确"第一张必为移民卡"的判据，两边一致）。

**收益**：解锁/抽卡规则改一处两边生效，杜绝漂移。
**风险**：低——纯函数抽取，玩家侧仅替换内部实现。

### 2.3 科文推进去重

**现状重复**：`AIManager.ApplyTechCultureProgress` 与玩家技文树每回合推进逻辑相同（按产量累积、够阈值升级）。

**方案**：抽 `TechCultureProgressRule.Advance(state, techCostTable, cultureCostTable)`，玩家与 AI 的科文状态都套用同一推进算法。

**收益**：升级曲线/成本表逻辑唯一。
**风险**：低——两边套同一纯函数。

### Tier 2 推进顺序
先做 **2.2 抽卡**（风险最低、已知有漂移 bug，收益直接）→ **2.3 科文**（低风险）→ **2.1 生成**（收益最大但动玩家部署，最后做、留足回归时间）。

---

## 总体推进与回滚

| 阶段 | 风险 | 建议时机 | 回滚 |
|---|---|---|---|
| Tier 0 改名归位 | 极低 | 立即 | 还原文件名/位置与引用 |
| Tier 1 巨类拆分 | 低 | 紧接 Tier 0 | 各 Brain 方法搬回 `AIManager` |
| Tier 2.2 抽卡去重 | 低 | Tier 1 后 | 恢复两处独立 `Generate*CardID` |
| Tier 2.3 科文去重 | 低 | 2.2 后 | 恢复独立推进逻辑 |
| Tier 2.1 生成去重 | 中 | 最后、留回归时间 | 恢复 `CardPresenter`/`AIEntityFactory` 各自生成 |

每阶段单独 git 提交。**Tier 3（多阵营化）不在本次范围**，但 Tier 1 拆分时以 `aiIndex` 参数化预留接口。

---

## 涉及文件清单（预估）

| 文件 | Tier | 操作 |
|------|------|------|
| `Core/Interfaces/IAIManager.cs` → `AI/AIManager.cs` | 0 | 重命名 + 移动；可选抽 `IAIManager` 精简接口 |
| `AI/AIEntityFactory.cs` | 1 | **新建** |
| `AI/AICardBrain.cs` | 1 | **新建** |
| `AI/AITacticalBrain.cs` | 1 | **新建**（含 AI 逻辑迷雾战术部分）|
| `AI/AITechCultureProgress.cs` | 1 | **新建** |
| `Infrastructure/Installers/GameInstaller.cs` | 0/1 | 更新绑定 |
| `Turn/AIPhase.cs`、`Managers/GameFlowManager.cs` | 0 | 更新引用 |
| `Core/Services/UnitSpawnService.cs` / `BuildingSpawnService.cs` | 2.1 | **新建**（共享生成）|
| `Core/Services/CardService.cs` + `AICardBrain` | 2.2 | 改为调用共享 `CardGenerationRule` |
| `Core/Services/CardPresenter.cs` | 2.1 | 改为调用共享生成服务 |
| `Controllers/Tech_CultureTreeController.cs` + `AITechCultureProgress` | 2.3 | 改为调用共享推进规则 |

---

## 测试清单

| 测试项 | 预期 |
|--------|------|
| 编译（各 Tier 后） | 无错误，场景组件引用不红 |
| AI 开局 | 正常建城 + 出初始单位 |
| AI 回合：有可见敌人 | 正常追击/攻击（迷雾逻辑不变）|
| AI 回合：无可见敌人 | 前沿游走（迷雾逻辑不变）|
| AI 建城扩张 / 科文升级 | 与重构前一致 |
| 玩家抽卡/首回合移民卡 | 与重构前一致（2.2 后重点验证）|
| 玩家部署单位/建筑 + 面板/血条 | 与重构前一致（2.1 后重点验证）|
| 玩家科文升级曲线 | 与重构前一致（2.3 后验证）|
