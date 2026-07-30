# AI 逻辑迷雾实施方案

## 目标

给 AI 一套**逻辑层**战争迷雾：AI 的决策只能基于"它自己看得见的东西"，不再全知玩家单位/建筑的真实位置。**不做任何视觉呈现**（AI 视野不渲染、玩家侧画面不变），仅约束 AI 的目标获取与移动方向。

---

## 已确认决策

| 决策项 | 选择 | 说明 |
|--------|------|------|
| 呈现方式 | **仅逻辑迷雾，不渲染** | 只影响 AI 决策，不画 AI 视野 |
| A · 视野重算时机 | **每单位决策前重算** | 前一个单位前压后照亮的敌人，后一个单位能用；视野跟着单位走 |
| B · 目标记忆 | **只打当前可见** | 敌人进迷雾即"失联"，不追记忆位置 |
| C · 无目标时行为 | **偏向前沿/未探索方向游走** | 模拟侦察，主动找敌人，而非原地随机 |
| 阵营视野 | **同阵营共享** | 一个 AI 的所有单位+领土共享一份视野（并集） |

---

## 现状与关键约束（实施前必须知道）

### AI 唯一"全知"的地方
只有 [IAIManager.HandleSingleUnitTurn](Assets/Scripts/Core/Interfaces/IAIManager.cs) 的**选目标**环节：
```csharp
_unitRepository.AllPlayerUnits.Values                  // 遍历所有玩家单位
GameObject.FindGameObjectsWithTag("PlayerBuilding")    // 找所有玩家建筑
```
其余 AI 行为（打牌、建城、势力扩张、移民建城）只用**自己领土**信息，本就不全知，**无需改动**。所以逻辑迷雾的核心改动面很小：**只在这一处加"按 AI 视野过滤目标"**。

### 现有迷雾是"单阵营"的（玩家专属）
`HexCellData.IsExplored` / `IsVisible` / `movementCost` 都是每格**一个全局值**，语义属于玩家。AI **不能复用**这些字段做判断，否则玩家的探索状态会污染 AI。AI 需要**独立的一套视野**。

### 边界：不碰寻路的 movementCost（重要）
`movementCost` 与全局探索耦合——未探索格 `movementCost=MAX`，[CanEnterCell](Assets/Scripts/Core/Services/UnitMovementSystem.cs) 视其不可通行。让 AI 寻路也只走"自己见过的地方"，需要给每阵营独立一套 `movementCost`，属于大得多的重构，且会改变玩家寻路。**本方案不动寻路**：逻辑迷雾只作用于**目标获取**与**游走方向选择**，寻路仍用现有全局代价。

### 附带已知问题（本方案不处理，仅记录）
[UnitMovementController.OnMoveFinished](Assets/Scripts/Controllers/UnitMovementController.cs) 被玩家和 AI 单位共用，移动后会 `ExploreThisHexCell()` 点亮邻居（全局单状态）——即 **AI 移动会替玩家探索地图、把视野泄露给玩家**。这是既有的 fog-leak，和本需求无关；若要修需单独处理（AI 移动不触发玩家探索）。

---

## 设计

### 1. 阵营视野计算（泛化 FoV）

把现有 [FieldOfViewService](Assets/Scripts/Core/Services/FieldOfViewService.cs) 的核心 BFS 抽成可复用逻辑："给一组视野源（中心格 + 半径）→ 返回可见格集合"。
- **玩家版**：源=玩家单位(`ViewPoints`)+玩家领土，结果写入 `cell.IsVisible`（渲染用）。
- **AI 版**：源=某 AI 的单位(`ViewPoints`)+该 AI 领土(`CityViewRadius`)，只**返回集合**、不写任何格子字段（逻辑用）。

新增服务 `AIFogService`（或并入泛化后的 FoV），对每个 `aiIndex` 提供：
```
HashSet<HexCellData> ComputeVisible(int aiIndex)   // 当前可见集合（每次现算）
bool IsVisible(int aiIndex, HexCellData cell)       // 便捷查询
```
视野源数据已具备：AI 单位取自 `IUnitRepository.AllEnemyUnitGroups`（`GetEnemyUnitGroup(aiIndex)`），AI 领土取自 `EnemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[aiIndex]`。半径复用玩家侧同款（单位 `UnitData.ViewPoints`、领土 `CityViewRadius`）。

### 2. AI 探索记忆（仅支撑 C 的"未探索"，不用于锁敌）

为让 C 的"未探索方向"稳定（否则每回合视野变化会让同一格反复变回"未探索"、导致游走抖动），给每个 AI 维护一份**探索记忆**：
```
Dictionary<int, HashSet<HexCellData>> _aiExplored   // 历次可见的并集，单调增长
```
每次 `ComputeVisible(aiIndex)` 后把结果并入 `_aiExplored[aiIndex]`。
- 用途：**只用于导航**（判断"哪些格 AI 从没见过"= 未探索方向）。
- 与决策 B 不冲突：**锁敌只认当前可见集合**，探索记忆不参与选目标。

### 3. 目标过滤（决策 B）

在 `HandleSingleUnitTurn` 选目标前：
```
var vision = _aiFog.ComputeVisible(AIIndex);   // A：每单位决策前现算
```
- 玩家单位：只保留其所在格 ∈ vision 的。
- 玩家建筑：只保留其所在格 ∈ vision 的（当前可见才算，静态也不给记忆——严格遵循 B）。
- 过滤后的候选，仍走**现有**的"最近寻路代价选目标 → 贴近/攻击"逻辑，不改。

### 4. 前沿游走（决策 C）

当过滤后**无可见目标**时，替换原本的"纯随机可达格"为**前沿偏向**：
```
reachable = GetAllReachableHexesFromStartHex(...)          // 现有
对每个候选格 c 打分：score(c) = c 附近 R 圈内“未探索”(∉ _aiExplored[AIIndex]) 的格子数
选 score 最高者（并列随机）；若全为 0（周围都探索过）→ 退回纯随机
```
效果：AI 倾向走向迷雾边缘/没去过的方向，像在侦察，而非原地打转。R 取 1~2 即可。

### 5. 每单位决策前重算（决策 A）

`ComputeVisible` 在**每个单位**决策开头调用一次。因为 AI 单位在本回合内串行移动，前一个单位前压后，`AllEnemyUnitGroups` 里它的新位置已更新，后一个单位重算即可"看到"它照亮的新区域。地图数百格、单位数量少，每单位重算成本可忽略。

### 6. 移民单位（无需改动）

移民（`UnitID==0`）走 `HandleSettlerTurn`，只在自己可达范围找建城点，不涉及玩家信息——**保持原样**。

---

## 数据 / 服务改动

| 项 | 说明 |
|---|---|
| `AIFogService`（新建） | `ComputeVisible(aiIndex)` 现算可见集合；维护 `_aiExplored` 记忆；`IsVisible(aiIndex,cell)` |
| `FieldOfViewService`（泛化，可选） | 抽出 BFS "源+半径→集合" 供玩家版与 AI 版共用；玩家版行为不变 |
| DI（GameInstaller） | 绑定 `AIFogService`，注入 `IAIManager` |

---

## 涉及文件清单

| 文件 | 操作 |
|------|------|
| `Assets/Scripts/Core/Services/AIFogService.cs` | **新建**：AI 视野现算 + 探索记忆 |
| `Assets/Scripts/Core/Services/FieldOfViewService.cs` | 修改（可选）：抽出可复用 BFS，玩家版行为不变 |
| `Assets/Scripts/Core/Interfaces/IAIManager.cs` | 修改：`HandleSingleUnitTurn` 目标按视野过滤 + 无目标时前沿游走；注入 `AIFogService` |
| `Assets/Scripts/Infrastructure/Installers/GameInstaller.cs` | 修改：绑定 `AIFogService` |

---

## 实施阶段（每阶段可 git 提交、可回滚）

1. **视野服务**：`AIFogService.ComputeVisible` + 泛化 BFS。先写个临时日志验证"某 AI 视野集合"随其单位移动而变化。
2. **目标过滤（B + A）**：`HandleSingleUnitTurn` 接入过滤。验证：AI 只追它视野内的玩家单位/建筑；玩家躲进迷雾后 AI 转为游走。
3. **前沿游走（C）**：无目标时按"未探索邻近度"打分游走 + 探索记忆累积。验证：AI 在没敌人时向迷雾边缘扩散，而非原地打转。

---

## 边界与非目标

- **不改寻路**：`movementCost`/全局探索耦合不动；AI 仍可寻路到全局已探索区（逻辑迷雾只管"看不看得见敌人"和"往哪走"）。
- **不修 fog-leak**：AI 移动仍会触发玩家探索（既有行为），如需隔离另行处理。
- **玩家侧零改动**：玩家迷雾渲染与三态逻辑完全不受影响。
- **多 AI 通用**：所有逻辑按 `aiIndex` 分，天然支持多敌方阵营（当前实际只有 aiIndex=1）。

---

## 测试清单

| 测试项 | 预期结果 |
|--------|----------|
| 玩家单位在 AI 视野外 | AI 不将其列为目标，不会朝它移动 |
| 玩家单位进入 AI 视野 | 下一个 AI 单位决策时将其列为目标，正常寻路贴近/攻击 |
| 玩家躲入迷雾（离开 AI 视野） | AI 立即"失联"，不追记忆位置，转为前沿游走 |
| 同回合前压后照亮 | 前一个 AI 单位前压后，后一个单位能看到并响应新暴露的敌人 |
| 无可见敌人 | AI 向未探索/前沿方向游走，而非原地随机打转 |
| 全被探索区包围 | 前沿打分全 0，退回纯随机，不卡死 |
| 移民单位 | 行为不变（仍在自己可达范围找建城点） |
| 玩家侧画面 | 与改动前完全一致，无任何视觉变化 |
| 多敌方阵营（如启用） | 各 AI 用各自视野，互不干扰 |
