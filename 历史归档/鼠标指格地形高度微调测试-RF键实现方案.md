# 鼠标指格地形高度微调测试（R/F 键）实现方案

> 定位：**动态地图能力测试追加项**——鼠标指针指示的地块按 R 地形高度永久 +1、按 F 永久 -1，用于人工构造各种极端地形情况（超高、负高、水位跨界、单位脚下变水等）。
> 日期：2026-08-05。
> 关联：`动态地图/动态地图变化系统-使用报告.md`（调用方模板）、`Assets/Scripts/Managers/MapWaveTestController.cs`（同款测试控制器范式）、`动态地图/全地图波浪式上下变化测试.md`。
> 与波浪测试的本质区别：波浪测试是**纯视觉脉冲 + 自动回落**；本测试是**逻辑数据永久修改**（直接压事务管线），不留恢复路径，专为"破坏性极端情况"而生。

---

## 一、需求与验收目标

| 项 | 内容 |
|---|---|
| 输入 | 鼠标悬停地图地块（青色高亮指示）；按住 R 高度逐级 +1、按住 F 逐级 -1（单按立即生效，按住 10Hz 连发） |
| 语义 | **永久**修改逻辑 `Height`（经 `MapMutationService` 事务管线，非直接写字段），动画结束后不回弹 |
| 范围 | 全图任意格：**不跳过水域格**（与波浪测试相反）——故意让 ±1 跨越水位线触发水陆双向重置，这正是极端情况测试点 |
| 验收 | 1) 指针格有高亮且随指针移动刷新；2) 按 R/F 单格高度单调 ±1，mesh 即时重建；3) 水域↔陆地跨界时河流/海岸/movementCost 按 §8 双向重置正确；4) 连续按住无事务嵌套异常、无锁残留；5) 仅编辑器/开发构建可用（Release 不可见） |

---

## 二、现状盘点（全部可复用，零新增依赖）

| 组件 | 位置 | 用途 |
|---|---|---|
| `MapMutationService`（事务协议） | `Assets/Scripts/Core/Services/MapMutation/MapMutationService.cs` | `BeginTransaction → Apply(HexCellPatch.HeightPatch) → Commit`，最小编程面见使用报告 §3.1 |
| `HexCellPatch.HeightPatch(h)` | `Assets/Scripts/Core/Models/HexCellPatch.cs:41` | 只改高度字段 |
| `MapTransitionOptions` | `Assets/Scripts/Core/Models/MapChangedEvent.cs:77-93` | `Duration=0` 同步提交（推荐，见 §三-3） |
| `IInputService`（GetKey/GetKeyDown/IsPointerOverUI） | `Assets/Scripts/Core/Interfaces/IInputService.cs` | R/F 键盘 + UI 阻挡检测 |
| 指针取格现成范式 | `PlayerInputHandler.cs:87-89` | `RaycastFromScreen(MousePosition, LayerMask "Map")` + `GetCellByWorldPosition(hit.point)` |
| `HexHighlightRenderer` | `Assets/Scripts/Managers/HexHighlightRenderer.cs` | 5 通道单格高亮，`Selection` 通道（索引 3）当前无占用，直接复用 |
| 测试控制器注册范式 | `GameInstaller.cs:151-155` | `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` + `BindInterfacesAndSelfTo<T>().AsSingle()`（ITickable 自动每帧驱动） |
| 诊断开关 | `MapMutationDiagnostics` | `EnableCommitLogging` / `EnableDirtyChunkHighlight` 观察每次提交 |
| 水陆双向重置 | `MapMutationService.cs:472-500` | `ApplyPatch` 内高度跨界自动处理（水→陆重置河流/海岸/movementCost=1；陆→水 movementCost=∞） |

**键位冲突检查**：全代码库无 `KeyCode.R/F` 被占用（`grep KeyCode.(R|F)` 无命中，波浪测试 V 键不受影响），R/F 可安全使用。

---

## 三、设计讨论（关键决策点）

### 3.1 指针取格：直接复用卡牌拖拽的射线范式
`PlayerInputHandler.HighlightGridOnMouseHover`（:85-109）已经回答了"鼠标指到哪个格"：屏幕射线打在 `Map` Layer 上，再 `GetCellByWorldPosition(hit.point)`。本测试照抄该两行，无新机制。
- **高亮节流（重要）**：`HexHighlightRenderer.SetHighlightedCells` 每次调用都会 `RebuildChannel` 重建 mesh（HexHighlightRenderer.cs:99-140）。虽然单格重建很便宜，仍应**只在指针格变化时**调用，避免每帧无谓重建。实现：缓存 `_hoveredCell` 引用比较（`GetCellByWorldPosition` 每次返回同一 `HexCellData` 实例，引用相等成立），格不变则跳过；"屏幕坐标未变则跳过射线"属于射线节流（§七-3 可选项），与高亮节流无关。

### 3.2 触发方式：单按立即 + 按住 10Hz 连发
"测极端情况"需要快速连续堆高，只做 `GetKeyDown` 单次 +1 太慢。方案：
```
按住 R/F 且（按键瞬间 或 距上次提交 ≥ 0.1s）→ 提交一次
```
- 按下瞬间 `GetKeyDown` 为真 → 立即响应（手感）；
- 之后 `GetKey` 持续为真 → 每 0.1s 提交一次（10Hz，60 帧/秒×0.1s=6 帧一次，避免每秒 60 次提交）；
- 两个键同按取 R（`holdingR ? +Step : -Step`），避免同帧双提交；F 在 R 松开前被忽略。
- 事务无嵌套风险：每次提交是完整 `Begin→Apply→Commit`，间隔 0.1s ≫ 同步提交耗时。

### 3.3 动画时长：默认 `Duration=0` 同步提交（可配置切 0.3s 动画）
| 方案 | 优点 | 缺点 |
|---|---|---|
| `Duration=0` 同步（**推荐默认**） | 连发稳定无排队；无锁残留期；极端堆高立即生效 | 无升起动画，视觉跳跃 |
| `Duration≈0.3s` 短动画 | 高度变化直观 | 同格连发时每次 Commit 触发 `ForceCompleteConflicting` 强制完成上个动画（MapMutationService.cs:149-153），开销随连发叠加；极端测试体验反而卡顿 |

结论：`Duration=0` 做极端测试，需要观感时改常量切 0.3s。这是**调用方一处常量**（使用报告 §五），不动管线。

### 3.4 水位跨界：不跳过水域格（与波浪测试相反）
波浪测试跳过水域是因为全图 +Δ 会把湖海误判成陆地；本测试是**单格 ±1 逐级走**，跨界正是测试目标：
- 水域格 R 升过 `WaterLevel=1` → `ApplyPatch` 水→陆双向重置：`HexType=NoRiver`、清河流四字段、`isCoast=false`、`movementCost=1`（MapMutationService.cs:480-493）；
- 陆地格 F 降过水位线 → 陆→水：`movementCost=∞`（:494-499），随后 Commit 的 `EjectUnitsFromImpassableCells` 会把格上单位弹射出去——**这是最值得测的极端联动**（把单位脚下格按成水，观察弹射与落点选择）。
- 高度不设上下限：持续 -1 可到负高度（水面判定恒真、mesh 沉入水下）；持续 +1 可测高空分桶（`WaterLevelConfig.ClassifyHeight` 高地桶）与雾面跟随。

### 3.5 高亮通道与颜色
- 用 `HexHighlightChannel.Selection`（现有 5 通道之一，当前无调用方占用）；
- 青色（`Color.cyan`）与卡牌黄（CardPlacement）、可达/攻击范围区分开；
- 指针移出地图 / 停在 UI 上 → `ClearChannel`。

### 3.6 UI 阻挡
`IInputService.IsPointerOverUI()` 为真时不取格、不高亮、不响应 R/F（与卡牌拖拽同口径，避免隔着 UI 面板改地形）。

### 3.7 与波浪测试（V 键）并行
两者不冲突：波浪是纯视觉脉冲、逻辑数据已恢复原值（MapWaveTestController.cs:173-183），R/F 改的是逻辑数据；若同格动画期间提交，管线会自动 `ForceCompleteConflicting` 并广播 `Cancelled` 阶段（使用报告 §六-8），行为可观察但无害。

### 3.8 恢复路径：不做（这就是测试的目的）
"永久 +1/-1"即验收要求。破坏性测试靠**重开对局**复位；如需中途归零可后续追加 R 键初始高度快照（本方案不实现，保持最小面）。

---

## 四、实现方案

### 4.1 新文件：`Assets/Scripts/Managers/MapHeightEditTestController.cs`

```csharp
using UnityEngine;
using Zenject;

//****************************************
// 【动态地图-能力测试】鼠标指格地形高度微调测试控制器（MapHeightEditTestController）
// 鼠标悬停地图地块（Selection 通道青色高亮指示）；按住 R 高度永久 +1、按住 F 永久 -1：
//   - 单按立即提交一次；按住后每 CommitIntervalSeconds（0.1s）连发一次，10Hz；
//   - Duration=0 同步提交（连发稳定、无动画排队）；如需观感改 AnimationDuration=0.3f；
//   - 不跳过水域格：±1 跨过 WaterLevel=1 阈值时经 MapMutationService.ApplyPatch
//     触发水陆双向重置（河流/海岸/movementCost），陆地→水域还会弹射格上单位——
//     这些联动即本测试的极端情况考察点；
//   - 高亮仅在指针格变化时重建（HexHighlightRenderer.RebuildChannel 每帧重建成本）；
//   - 指针在 UI 上时不取格、不响应（与卡牌拖拽同口径）。
// 仅编辑器/开发构建绑定（GameInstaller #if UNITY_EDITOR || DEVELOPMENT_BUILD）。
//****************************************

public class MapHeightEditTestController : ITickable
{
    /// <summary>按住连发的提交间隔（秒）→ 10Hz。</summary>
    public const float CommitIntervalSeconds = 0.1f;

    /// <summary>单次高度变化量（地形海拔级数）。</summary>
    public const float Step = 1f;

    /// <summary>0=同步提交（推荐，连发稳定）；>0 时同格连发会强制完成相交旧动画。</summary>
    public const float AnimationDuration = 0f;

    private readonly IInputService _input;
    private readonly IMapDataService _mapDataService;
    private readonly MapMutationService _mutationService;
    private readonly HexHighlightRenderer _highlightRenderer;

    private HexCellData _hoveredCell;
    private float _nextCommitTime;

    public MapHeightEditTestController(
        IInputService input,
        IMapDataService mapDataService,
        MapMutationService mutationService,
        [Zenject.InjectOptional] HexHighlightRenderer highlightRenderer = null)
    {
        _input = input;
        _mapDataService = mapDataService;
        _mutationService = mutationService;
        _highlightRenderer = highlightRenderer;
    }

    public void Tick()
    {
        // UI 阻挡：指针在 UI 上时既不取格也不响应（清高亮，防隔着面板改地形）
        if (_input.IsPointerOverUI())
        {
            RefreshHover(null);
            return;
        }

        RefreshHover(ResolveHoveredCell());
        if (_hoveredCell == null) return;

        bool holdingR = _input.GetKey(KeyCode.R);
        bool holdingF = _input.GetKey(KeyCode.F);
        if (!holdingR && !holdingF) return;

        // 按下瞬间立即提交；随后按 CommitIntervalSeconds 连发
        bool trigger = _input.GetKeyDown(KeyCode.R) || _input.GetKeyDown(KeyCode.F) ||
                       Time.realtimeSinceStartup >= _nextCommitTime;
        if (!trigger) return;

        ApplyHeight(_hoveredCell, holdingR ? +Step : -Step);
        _nextCommitTime = Time.realtimeSinceStartup + CommitIntervalSeconds;
    }

    /// <summary>屏幕射线取指针格（与 PlayerInputHandler.HighlightGridOnMouseHover 同范式）。</summary>
    private HexCellData ResolveHoveredCell()
    {
        if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit hit, 100f, LayerMask.GetMask("Map")))
            return _mapDataService.GetCellByWorldPosition(hit.point);
        return null;
    }

    /// <summary>高亮仅在指针格变化时重建（RebuildChannel 每次调用都会重建 mesh）。</summary>
    private void RefreshHover(HexCellData cell)
    {
        if (cell == _hoveredCell) return;
        _hoveredCell = cell;
        if (_highlightRenderer == null) return;
        if (cell == null)
            _highlightRenderer.ClearChannel(HexHighlightChannel.Selection);
        else
            _highlightRenderer.SetHighlightedCells(HexHighlightChannel.Selection, new[] { cell }, Color.cyan);
    }

    /// <summary>单格高度 ±Step 的完整事务（读的是 cell 最新 Height，连发天然累加）。
    /// try/catch 兜底（审计 2026-08-05）：Commit 自身有 finally 清事务（MapMutationService.cs:272-280），
    /// 但 BeginTransaction→Apply 之间若抛异常会残留 _inTransaction=true 毒化整个会话
    /// （10Hz 连发下必须防御）——异常时调用幂等 Rollback 后 rethrow，下帧可继续。</summary>
    private void ApplyHeight(HexCellData cell, float delta)
    {
        try
        {
            _mutationService.BeginTransaction();
            _mutationService.Apply(cell, HexCellPatch.HeightPatch(cell.Height + delta));
            MapCommitResult result = _mutationService.Commit(new MapTransitionOptions
            {
                Duration = AnimationDuration,
                Stagger = MapTransitionStagger.Simultaneous,
                LockAffectedCells = true
            });
            if (result != null)
                Debug.Log($"[MapHeightEdit] 格 {cell.GenerateOrder} 高度={cell.Height}（" +
                          $"脏 Chunk {result.AffectedChunks?.Count ?? 0}；水域跨界联动由 ApplyPatch 自动处理）");
        }
        catch
        {
            _mutationService.Rollback();
            throw;
        }
    }
}
```

### 4.2 注册：`Assets/Scripts/Infrastructure/Installers/GameInstaller.cs:151-155` 的 `#if` 块内追加

```csharp
// 【动态地图-能力测试】鼠标指格地形高度微调测试（R/F 键单格 ±1 永久修改，2026-08-05）。
// 仅编辑器/开发构建绑定——Release 构建中 R/F 键不得修改地形（与波浪测试同评审口径）。
Container.BindInterfacesAndSelfTo<MapHeightEditTestController>().AsSingle();
```

无需改 `ChunkMapRenderer` / `MapMutationService` / 任何管线代码。

---

## 五、极端情况测试清单（本测试的存在意义）

| # | 操作 | 预期观察（联动点） |
|---|---|---|
| 1 | 陆地格连按 R 至 10+ 级 | 3 子 Mesh 分桶切换（`ClassifyHeight`）、雾面/网格线跟随高度、无破面 |
| 2 | 连按 F 至负高度 | 水面判定恒真（`IsWater(负)=true`）自动转水域；mesh 沉入水下 |
| 3 | 水域格 R 跨过水位线（1→2） | 水→陆双向重置：河流四字段清空、`isCoast=false`、`movementCost=1`（MapMutationService.cs:480-493） |
| 4 | 陆地格 F 跨过水位线（2→1） | 陆→水：`movementCost=∞`；若格上有单位 → `EjectUnitsFromImpassableCells` 弹射（含落点选取、找不到落点释放占用 + 警告） |
| 5 | 河流格抬升出水面 | 河流清除联动；对相邻格海岸/连接面无残留 |
| 6 | 带地貌/资源/金矿的格升降 | `LandFormMarkerManager` 浮标刷新、位置跟随 |
| 7 | 竞技场预留区（IsUnexplorable）格升降 | 预留区格参与事务无异常，动画/提交正常 |
| 8 | 按住 R 不放 30 秒 | 300 次提交无事务嵌套/锁残留；脏 Chunk 持续重建稳定；开 `EnableCommitLogging` 观察每笔日志 |
| 9 | 指针快速扫过地图 | 高亮跟随且无每帧重建（节流生效）；移出地图/移到 UI 上高亮消失 |
| 10 | 波浪测试（V 键）进行中按 R/F | 同格动画冲突走 `ForceCompleteConflicting` + `Cancelled` 阶段广播（使用报告 §六-8），无死锁 |
| 11 | 迷雾中未探索格 | 射线是否命中（Map Layer 物理层不受迷雾遮挡）→ 高亮显示但可照常修改（测试可探索性联动） |

---

## 六、验证步骤

1. 编辑器打开主场景 → Play；
2. 指针悬停陆地格 → 青色高亮出现；按一下 R → 格升高一级（日志 `[MapHeightEdit] 格 N 高度=X`）；按住 R → 10Hz 连续升高；
3. 按 F 回落；把格按回水面以下 → 观察变水域、格上单位（若有）弹射；
4. 水域格按 R 升出水面 → 观察河流/海岸重置；
5. 开 `MapMutationDiagnostics.EnableCommitLogging = true` 观察每笔提交耗时与脏 Chunk 数；
6. 连续按住 R/F 各 30 秒 → 无异常、无卡顿、`MapInteractionGate` 无锁残留（`IsLocked` 查询）；
7. 退出 Play 重进 → 地形回到初始（永久修改只存在于本次对局）。

---

## 七、风险与注意事项

1. **永久不可逆**：本测试没有恢复路径（与波浪测试自动回落相反），破坏后只能重开对局；后续如需要可追加 R 键快照恢复，本方案保持最小面。
2. **Selection 通道占用**：当前 `HexHighlightChannel.Selection`（索引 3）无调用方；若未来正式功能占用需换通道（如新增 Debug 通道）或测试期间互斥。
3. **每帧射线开销**：Tick 每帧一条物理射线（约 0.02ms 级），有 PlayerInputHandler 拖拽先例；若在意可加"屏幕坐标未变则跳过射线"缓存（本方案高亮已节流，射线未节流——需要时同法处理）。
4. **与交互锁的关系**：`Duration=0` 同步提交的锁在 Finalize 立即释放，连发无累积；测试期间其他系统（卡牌放置等）对该格的查询会看到锁瞬时存在，属正常现象。
5. **Release 构建安全**：绑定在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内，与波浪测试同口径，评审 2026-08-05 已认可此模式。
6. **暂停态未禁用**：卡牌拖拽在 `GameLoop.IsPaused` 时被禁用（PlayerInputHandler.cs:64-68），本测试未注入 GameLoop 查暂停，暂停中按住 R/F 仍会提交。测试工具场景影响可接受；如需与正式交互同口径，注入 GameLoop 加 `IsPaused` 门控即可。
