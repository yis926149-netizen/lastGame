using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Zenject;
using Debug = UnityEngine.Debug;

//****************************************
// 【动态地图-阶段二/四/五】通用地块变化服务（MapMutationService）
// 竞技场/技能/天灾/地形编辑器的统一入口（§二/§四/§五/§十二）。
// 协议：BeginTransaction → Apply(HexCellPatch) → Commit(MapTransitionOptions)，
// Commit 内同步执行：写数据 → 单位处理（取消/弹射/吸附）→ 渲染重建 →
// 对象/浮标/标签 → 路径失效 → 兼容广播 → 强类型 MapChangedEvent → 同步 Finalize。
// 阶段四：Duration>0 且后端支持动画 → 动画路径（TransitionStarted → Finalized/Cancelled）。
// 阶段五：
//  1. 归属 Patch 接入（§二十-12）：Apply(Owner) 收集，Commit 经 ILogisticsService 领域入口应用；
//  2. 诊断扩展：AffectedChunks 事件字段 + 批量提交日志 + 脏 Chunk 高亮（MapMutationDiagnostics）；
//  3. 并行动画：Commit 只完成与新动画 Chunk 相交的旧动画，不相交动画并行（§阶段五-并行动画）；
//  4. 分帧提交：CommitSliced 把脏 Chunk 几何构建拆到多帧（§阶段五-分帧提交）。
// 同时实现 IMapInteractionGate 语义：事务/动画期间锁定受影响格。
//
// 【动画管线设计约束-2026-08-05（波浪测试反哺，详见 动态地图/动态地图变化与分块重建方案.md 末章）】
// ① 动画事务的视觉刷新必须按阶段感知，禁止 Commit 帧吸附到终态：
//    动画路径不调用 RefreshStandingUnitPositions（提前吸附 = 全图第二层跳变）；
//    RefreshCellObjects 传 snapToFinalPosition=false，地貌/资源模型经
//    MapVisualTransitionService.RegisterCellVisualFollowers 逐格跟随。
// ② 纯视觉脉冲事务模式：动画 Commit 只用于生成 oldY/targetY 缓存，调用方应在 Commit 返回后
//    立即恢复逻辑 Height/RealCenterWorldCoordinate——动画期间逻辑数据保持原值，
//    只有 Chunk 顶点缓存驱动视觉；任何系统读 Cell 数据都不应看到临时目标高度。
// ③ 旧"同步提交假设"的视觉刷新（浮标重建/MapVisualEventSO.Raise）只适用于 Duration=0 提交；
//    带 Stagger 的动画提交需逐个评估是否跳过（Wave 测试已跳过两者）。
//****************************************

public class MapMutationService
{
    private readonly IMapRenderBackend _renderBackend;
    private readonly UnitMovementSystem _unitMovementSystem;
    private readonly GameLoop _gameLoop;
    private readonly MapVisualEventSO _mapVisualEvent;
    private readonly LandFormMarkerManager _landFormMarkerManager;
    private readonly MapInteractionGate _interactionGate;
    private readonly MapVisualTransitionService _visualTransition;
    private readonly ILogisticsService _logisticsService;
    private readonly HexHighlightRenderer _highlightRenderer;

    /// <summary>强类型地图变化事件（阶段二仅广播 Committed；阶段四扩展 TransitionStarted/Finalized/Cancelled）。</summary>
    public event Action<MapChangedEvent> MapChanged;

    private readonly List<(HexCellData Cell, HexCellPatch Patch)> _pending = new List<(HexCellData, HexCellPatch)>();
    private readonly List<(HexCellData Cell, int FactionId)> _pendingOwners = new List<(HexCellData, int)>();
    private bool _inTransaction;
    private int _commitSequence;

    // 【阶段五-分帧提交】进行中的分帧状态（null = 无分帧提交）
    private SlicedCommitState _slicedState;

    public MapMutationService(
        IMapRenderBackend renderBackend,
        UnitMovementSystem unitMovementSystem,
        GameLoop gameLoop,
        MapVisualEventSO mapVisualEvent,
        LandFormMarkerManager landFormMarkerManager,
        MapInteractionGate interactionGate,
        [Zenject.InjectOptional] MapVisualTransitionService visualTransition = null,
        [Zenject.InjectOptional] ILogisticsService logisticsService = null,
        [Zenject.InjectOptional] HexHighlightRenderer highlightRenderer = null)
    {
        _renderBackend = renderBackend;
        _unitMovementSystem = unitMovementSystem;
        _gameLoop = gameLoop;
        _mapVisualEvent = mapVisualEvent;
        _landFormMarkerManager = landFormMarkerManager;
        _interactionGate = interactionGate;
        _visualTransition = visualTransition;
        _logisticsService = logisticsService;
        _highlightRenderer = highlightRenderer;
    }

    // ── 事务协议 ──────────────────────────────────────────────

    /// <summary>开启新事务。同一时刻只允许一个事务（几何事务全局串行，§20-6）。分帧提交进行中禁止新事务。</summary>
    public void BeginTransaction()
    {
        if (_inTransaction)
            throw new InvalidOperationException("[MapMutationService] 已有未提交事务，禁止嵌套 BeginTransaction。");
        if (_slicedState != null)
            throw new InvalidOperationException("[MapMutationService] 分帧提交进行中，禁止开启新事务（§20-6 几何事务串行）。");

        _inTransaction = true;
        _pending.Clear();
        _pendingOwners.Clear();
    }

    /// <summary>
    /// 向当前事务追加地块补丁。
    /// 阶段五：归属 Patch（Owner）不再直接抛异常——收集后在 Commit 经 ILogisticsService 领域入口应用
    /// （§二十-12）；未注入 ILogisticsService 时保持抛 NotSupportedException。
    /// </summary>
    public void Apply(HexCellData cell, HexCellPatch patch)
    {
        if (!_inTransaction)
            throw new InvalidOperationException("[MapMutationService] Apply 必须在 BeginTransaction 与 Commit 之间调用。");
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        if (patch == null) throw new ArgumentNullException(nameof(patch));

        if (patch.Owner.HasValue)
        {
            if (_logisticsService == null)
                throw new NotSupportedException(
                    "[MapMutationService] 未注入 ILogisticsService，无法应用归属 Patch，请经 TerritoryService/LogisticsService 领域入口（§二十-12）。");
            _pendingOwners.Add((cell, patch.Owner.Value));
        }

        _pending.Add((cell, patch));
    }

    /// <summary>
    /// 提交事务。Duration&gt;0 且后端支持动画时走动画路径——Commit 原子提交逻辑，动画由
    /// MapVisualTransitionService 驱动（TransitionStarted → Finalized/Cancelled），
    /// 交互锁保持到动画结束（§20-5），RemovedVisualHandle 在 Finalize 统一销毁。
    /// 阶段五：提交前只强制完成与新动画 Chunk 相交的旧动画，不相交动画并行（§阶段五-并行动画）。
    /// 返回提交结果；任何异常都会在 finally 中释放锁与事务状态。
    /// </summary>
    public MapCommitResult Commit(MapTransitionOptions options = null)
    {
        if (!_inTransaction)
        {
            Debug.LogWarning("[MapMutationService] Commit 被调用但没有活动事务，忽略。");
            return null;
        }

        options = options ?? new MapTransitionOptions();
        bool animated = options.Duration > 0f &&
                        _visualTransition != null &&
                        _renderBackend.SupportsAnimatedTransition;
        if (options.Duration > 0f && !animated)
            Debug.LogWarning("[MapMutationService] 当前后端/服务不支持动画过渡（阶段四），已按 Duration=0 同步提交。");

        int commitId = ++_commitSequence;
        var changedSet = new HashSet<HexCellData>();
        foreach (var (cell, _) in _pending)
            changedSet.Add(cell);

        MapDirtyFlags dirtyFlags = ComputeDirtyFlags(_pending);

        // 【阶段五-并行动画】交互锁：提交期间同步锁定受影响格（阶段四动画期间扩展为持续锁定，Finalize 解锁）。
        // 若已有活动动画且与新动画 Chunk 相交：先强制完成冲突的旧动画并解锁，再锁新格，
        // 避免旧动画 Finalize 的 UnlockCells 清掉本次新锁（§20-6）。不相交动画保持并行。
        if (animated && _visualTransition != null && _visualTransition.IsAnimating)
        {
            IReadOnlyList<ChunkIndex> incomingChunks = TryComputeDirtyChunkIndices(changedSet);
            _visualTransition.ForceCompleteConflicting(incomingChunks);
        }

        if (options.LockAffectedCells)
            _interactionGate.LockCells(changedSet);

        // 阶段四：写数据前快照旧数据（动画起点，§12.1）——
        // oldHeights（Height 级差，供几何 startVertexY 计算）与 oldCenterWorldY（世界 Y，供单位视觉跟随）
        Dictionary<int, float> oldHeights = null;
        Dictionary<int, float> oldCenterWorldY = null;
        Dictionary<int, float> staggerDelays = null;
        if (animated)
        {
            oldHeights = new Dictionary<int, float>();
            oldCenterWorldY = new Dictionary<int, float>();
            foreach (HexCellData cell in changedSet)
            {
                if (cell == null) continue;
                oldHeights[cell.GenerateOrder] = cell.Height;
                oldCenterWorldY[cell.GenerateOrder] = cell.RealCenterWorldCoordinate.y;
            }
            staggerDelays = _visualTransition.ComputeStaggerDelays(changedSet, options);
        }

        RemovedVisualHandle removed = new RemovedVisualHandle();
        bool lockDeferredToAnimation = false;
        List<ChunkIndex> affectedChunks = null;
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            // 1. 写入正式数据（归属类变化经领域入口，见 ApplyOwners）
            foreach (var (cell, patch) in _pending)
                ApplyPatch(cell, patch);
            ApplyOwners(ref dirtyFlags);

            // 2. 单位处理：先取消途经不可通行格的移动任务（可能回落到同样不可通行的起点），再兜底弹射
            _unitMovementSystem.CancelMovesIntersecting(changedSet);
            _unitMovementSystem.EjectUnitsFromImpassableCells(changedSet);

            // 3. 渲染重建：动画路径构建带 UV2/UV3 的 staging；否则构建普通 Chunk staging
            PreparedChunkGeometry chunkStaging = null;
            if (animated)
            {
                chunkStaging = _renderBackend.PrepareAnimatedChunkGeometry(changedSet, oldHeights, staggerDelays);
                _renderBackend.CommitAnimatedChunkGeometry(chunkStaging);
            }
            else
            {
                chunkStaging = _renderBackend.PrepareChunkGeometry(changedSet);
                _renderBackend.CommitChunkGeometry(chunkStaging);
            }

            // 4. 站立单位高度吸附（依赖 RealCenterWorldCoordinate 已同步）
            // 动画期间由 MapVisualTransitionService 按每格进度跟随；立即吸附到终点会造成
            // 单位随全图一起瞬移，尤其在 Wave 测试的全图 Height 提升中明显。
            if (!animated)
                _unitMovementSystem.RefreshStandingUnitPositions(changedSet);

            // 5. 变化格对象刷新（地貌/资源模型移除或归位 + 网格线重建）
            _renderBackend.RefreshCellObjects(changedSet, removed, snapToFinalPosition: !animated);

            // 6. 地貌浮标：Wave 测试最终会恢复原高度；动画中按目标高度重建会制造整批浮标瞬移。
            // 其他模式维持原有全量重建行为。
            if (!animated || options.Stagger != MapTransitionStagger.Wave)
                _landFormMarkerManager.CreateAllMarkers();

            // 7. 路径失效（公共建筑 Reveal 同款）：Brain 按新地形重决策
            _gameLoop.InvalidateAllBrainPaths();

            // 8. 兼容广播（仅一次）：驱动迷雾目标/费用标签/势力范围等旧订阅者。
            // Wave 是往返能力测试，逻辑高度会恢复；此处广播会让势力范围等覆盖层按最终
            // RealCenterWorldCoordinate 整体瞬移，形成与地形波浪并存的“全图突变”。
            if (_mapVisualEvent != null && (!animated || options.Stagger != MapTransitionStagger.Wave))
                _mapVisualEvent.Raise();

            // 9. 强类型事件（Committed + AffectedChunks 诊断）
            affectedChunks = ExtractAffectedChunks(chunkStaging);
            MapChanged?.Invoke(new MapChangedEvent(commitId, new List<HexCellData>(changedSet), dirtyFlags, MapChangedPhase.Committed, affectedChunks));

            if (animated)
            {
                // 10. 启动动画：TransitionStarted 由服务发布；锁/句柄/单位吸附由动画 Finalize 收尾。
                //     先调用后置位 flag：BeginTransition 抛异常时 finally 仍会解锁（§12.1 失败路径）。
                var chunks = new List<ChunkIndex>();
                if (chunkStaging != null)
                {
                    foreach (var s in chunkStaging.Chunks)
                        chunks.Add(s.Index);
                }
                _visualTransition.BeginTransition(
                    commitId,
                    dirtyFlags,
                    changedSet,
                    options,
                    chunks,
                    oldCenterWorldY,
                    staggerDelays,
                    removed,
                    ev => MapChanged?.Invoke(ev));
                lockDeferredToAnimation = true;
                _visualTransition.RegisterCellVisualFollowers(changedSet);
            }
            else
            {
                // 11. 同步 Finalize：销毁旧视觉句柄（Duration=0 或后端不支持动画）
                removed.DestroyAll();
            }

            // 【阶段五-诊断】批量提交日志 + 脏格高亮
            sw.Stop();
            LogCommitSummary(commitId, _pending.Count, changedSet.Count, affectedChunks?.Count ?? 0, dirtyFlags, sw.ElapsedMilliseconds);
            HighlightChangedCells(changedSet);
        }
        catch
        {
            // 失败路径：清理已收集的待移除对象（§12.1 任一步失败 → 模型引用不残留）；
            // 锁由 finally 释放（lockDeferredToAnimation 仅在动画成功启动后置位）。
            removed.DestroyAll();
            throw;
        }
        finally
        {
            // 【阶段五-并行动画】只解锁本次事务的格（不 UnlockAll——避免清掉并行动画的锁）
            if (!lockDeferredToAnimation)
                _interactionGate.UnlockCells(changedSet);
            _inTransaction = false;
            _pending.Clear();
            _pendingOwners.Clear();
        }

        return new MapCommitResult(commitId, new List<HexCellData>(changedSet), dirtyFlags, affectedChunks);
    }

    /// <summary>取消当前未提交事务（清空补丁并只释放本事务的锁）。幂等。</summary>
    public void Rollback()
    {
        var cells = new List<HexCellData>();
        foreach (var (cell, _) in _pending)
        {
            if (cell != null) cells.Add(cell);
        }
        _pending.Clear();
        _pendingOwners.Clear();
        _interactionGate.UnlockCells(cells);
        _inTransaction = false;
    }

    public bool HasActiveTransaction => _inTransaction;

    // ── 分帧提交（阶段五，§阶段五-分帧提交）──────────────────

    /// <summary>是否有进行中的分帧提交。</summary>
    public bool HasSlicedCommitPending => _slicedState != null;

    /// <summary>
    /// 分帧提交：数据写入/单位处理同步原子完成，脏 Chunk 几何构建拆到多帧（每帧 maxChunksPerFrame 个），
    /// 全部构建完成后统一 Commit + 收尾 + 事件。返回提交结果；事件在最后一帧发布。
    /// 不支持动画组合（第一版）。
    /// 交互锁从开始保持到全部完成（等价动画锁语义，§20-5）。
    /// </summary>
    public MapCommitResult CommitSliced(MapTransitionOptions options = null, int maxChunksPerFrame = 2)
    {
        if (!_inTransaction)
        {
            Debug.LogWarning("[MapMutationService] CommitSliced 被调用但没有活动事务，忽略。");
            return null;
        }
        if (options != null && options.Duration > 0f)
        {
            Debug.LogWarning("[MapMutationService] 分帧提交第一版不支持动画过渡（Duration>0），已按 Duration=0 分帧提交。");
            options.Duration = 0f;
        }

        int commitId = ++_commitSequence;
        var changedSet = new HashSet<HexCellData>();
        foreach (var (cell, _) in _pending)
            changedSet.Add(cell);

        MapDirtyFlags dirtyFlags = ComputeDirtyFlags(_pending);

        if (options == null || options.LockAffectedCells)
            _interactionGate.LockCells(changedSet);

        var removed = new RemovedVisualHandle();
        try
        {
            // 数据写入 + 归属 + 单位处理（同步原子）
            foreach (var (cell, patch) in _pending)
                ApplyPatch(cell, patch);
            ApplyOwners(ref dirtyFlags);
            _unitMovementSystem.CancelMovesIntersecting(changedSet);
            _unitMovementSystem.EjectUnitsFromImpassableCells(changedSet);

            // 脏 Chunk 列表入队（分帧构建）
            IReadOnlyList<ChunkIndex> dirtyIndices = _renderBackend.ComputeDirtyChunkIndices(changedSet);
            var pendingChunks = new List<ChunkIndex>(dirtyIndices ?? Array.Empty<ChunkIndex>());
            _slicedState = new SlicedCommitState
            {
                CommitId = commitId,
                DirtyFlags = dirtyFlags,
                ChangedSet = changedSet,
                Removed = removed,
                PendingChunks = pendingChunks,
                Staging = new PreparedChunkGeometry(),
                MaxChunksPerFrame = Mathf.Max(1, maxChunksPerFrame)
            };

            // 首帧立即推进一批（避免空等一帧）
            TickSlicedCommit();
            return new MapCommitResult(commitId, new List<HexCellData>(changedSet), dirtyFlags, null);
        }
        catch
        {
            removed.DestroyAll();
            _interactionGate.UnlockCells(changedSet);
            _slicedState = null;
            throw;
        }
        finally
        {
            _inTransaction = false;
            _pending.Clear();
            _pendingOwners.Clear();
        }
    }

    /// <summary>分帧提交推进（由 MapSlicedCommitExecutor ITickable 每帧调用）。幂等。</summary>
    public void TickSlicedCommit()
    {
        SlicedCommitState s = _slicedState;
        if (s == null) return;

        try
        {
            int built = 0;
            while (s.PendingChunks.Count > 0 && built < s.MaxChunksPerFrame)
            {
                var slice = new List<ChunkIndex> { s.PendingChunks[0] };
                s.PendingChunks.RemoveAt(0);
                PreparedChunkGeometry part = _renderBackend.PrepareChunkGeometrySlice(slice);
                foreach (ChunkStagingGeometry st in part.Chunks)
                    s.Staging.Chunks.Add(st);
                built++;
            }

            if (s.PendingChunks.Count > 0)
                return; // 还有剩余 Chunk，下帧继续

            // 全部构建完成：统一提交 + 收尾
            Stopwatch sw = Stopwatch.StartNew();
            _renderBackend.CommitChunkGeometry(s.Staging);
            _unitMovementSystem.RefreshStandingUnitPositions(s.ChangedSet);
            _renderBackend.RefreshCellObjects(s.ChangedSet, s.Removed);
            _landFormMarkerManager.CreateAllMarkers();
            _gameLoop.InvalidateAllBrainPaths();
            if (_mapVisualEvent != null)
                _mapVisualEvent.Raise();

            var affectedChunks = new List<ChunkIndex>();
            foreach (ChunkStagingGeometry st in s.Staging.Chunks)
                affectedChunks.Add(st.Index);

            MapChanged?.Invoke(new MapChangedEvent(s.CommitId, new List<HexCellData>(s.ChangedSet), s.DirtyFlags, MapChangedPhase.Committed, affectedChunks));
            s.Removed.DestroyAll();
            _interactionGate.UnlockCells(s.ChangedSet);

            sw.Stop();
            LogCommitSummary(s.CommitId, s.ChangedSet.Count, s.ChangedSet.Count, affectedChunks.Count, s.DirtyFlags, sw.ElapsedMilliseconds);
            HighlightChangedCells(s.ChangedSet);
            _slicedState = null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[MapMutationService] 分帧提交失败（CommitId={s.CommitId}）：{e}");
            s.Removed.DestroyAll();
            _interactionGate.UnlockCells(s.ChangedSet);
            _slicedState = null;
        }
    }

    /// <summary>对局结束/取消路径：立即完成剩余分帧（幂等；无分帧时无操作）。</summary>
    public void ForceCompleteSliced()
    {
        if (_slicedState == null) return;
        _slicedState.MaxChunksPerFrame = int.MaxValue;
        TickSlicedCommit();
    }

    /// <summary>分帧提交状态（内部）。</summary>
    private sealed class SlicedCommitState
    {
        public int CommitId;
        public MapDirtyFlags DirtyFlags;
        public HashSet<HexCellData> ChangedSet;
        public RemovedVisualHandle Removed;
        public List<ChunkIndex> PendingChunks;
        public PreparedChunkGeometry Staging;
        public int MaxChunksPerFrame;
    }

    // ── 补丁应用与脏位 ───────────────────────────────────────

    /// <summary>
    /// 归属 Patch 经 ILogisticsService 领域入口应用（§二十-12：探索位 + 后勤重算 + 领地字典重建 + 事件）。
    /// FactionId &lt; 0 = 清除归属（ClearOwner）；&gt;= 0 = 设置归属（SetOwner）。
    /// </summary>
    private void ApplyOwners(ref MapDirtyFlags dirtyFlags)
    {
        if (_pendingOwners.Count == 0 || _logisticsService == null) return;
        foreach ((HexCellData cell, int factionId) in _pendingOwners)
        {
            if (cell == null) continue;
            if (factionId < 0)
                _logisticsService.ClearOwner(cell);
            else
                _logisticsService.SetOwner(cell, factionId);
        }
        dirtyFlags |= MapDirtyFlags.Territory | MapDirtyFlags.Logistics | MapDirtyFlags.Fog;
    }

    private static void ApplyPatch(HexCellData cell, HexCellPatch patch)
    {
        if (patch.HasHeight)
        {
            bool wasWater = cell.HexType == Enums.HexType.LakeOrSea || WaterLevelConfig.IsWater(cell);
            cell.Height = patch.Height;
            bool nowWater = WaterLevelConfig.IsWater(cell);

            if (wasWater && !nowWater)
            {
                // 水 → 陆地：重置水域状态（§8 双向重置），避免湖海连接面残留
                cell.HexType = Enums.HexType.NoRiver;
                cell.isCoast = false;
                cell.waterLevel = 0f;
                cell.hasRiver = false;
                cell.hasRiverIncoming = false;
                cell.hasRiverOutgoing = false;
                cell.RiverIncomingDirection = Enums.HexDirection.None;
                cell.RiverOutgoingDirection = Enums.HexDirection.None;
                if (!patch.HasMovementCost)
                    cell.movementCost = 1f;
            }
            else if (!wasWater && nowWater)
            {
                // 陆地 → 水：反向重置（第一版仅支持水域判断字段）
                if (!patch.HasMovementCost)
                    cell.movementCost = float.MaxValue;
            }
        }

        if (patch.HasHexType)
            cell.HexType = patch.HexType;

        if (patch.HasMovementCost)
            cell.movementCost = patch.MovementCost;

        if (patch.HasIsUnexplorable)
            cell.IsUnexplorable = patch.IsUnexplorable;

        if (patch.ClearLandForm)
            cell.landForm = null;

        if (patch.ClearResource)
            cell.resource = null;

        if (patch.ClearRiver)
        {
            cell.hasRiver = false;
            cell.hasRiverIncoming = false;
            cell.hasRiverOutgoing = false;
            cell.RiverIncomingDirection = Enums.HexDirection.None;
            cell.RiverOutgoingDirection = Enums.HexDirection.None;
            if (cell.HexType == Enums.HexType.RiverSource ||
                cell.HexType == Enums.HexType.RiverMidstream ||
                cell.HexType == Enums.HexType.RiverEnd)
            {
                cell.HexType = Enums.HexType.NoRiver;
            }
        }
    }

    private static MapDirtyFlags ComputeDirtyFlags(IReadOnlyList<(HexCellData Cell, HexCellPatch Patch)> patches)
    {
        MapDirtyFlags flags = MapDirtyFlags.None;
        foreach (var (_, patch) in patches)
        {
            if (patch.HasHeight)
                flags |= MapDirtyFlags.Terrain | MapDirtyFlags.Water | MapDirtyFlags.River |
                         MapDirtyFlags.Grid | MapDirtyFlags.Objects | MapDirtyFlags.Navigation;
            if (patch.HasHexType)
                flags |= MapDirtyFlags.Terrain | MapDirtyFlags.Water | MapDirtyFlags.River | MapDirtyFlags.Navigation;
            if (patch.ClearRiver)
                flags |= MapDirtyFlags.Terrain | MapDirtyFlags.River;
            if (patch.ClearLandForm || patch.ClearResource)
                flags |= MapDirtyFlags.Objects;
            if (patch.HasMovementCost)
                flags |= MapDirtyFlags.Navigation;
            if (patch.HasIsUnexplorable)
                flags |= MapDirtyFlags.Labels;
            if (patch.Owner.HasValue)
                flags |= MapDirtyFlags.Territory | MapDirtyFlags.Logistics | MapDirtyFlags.Fog;
        }
        return flags;
    }

    // ── 诊断（阶段五，§阶段五-诊断扩展）──────────────────────

    private IReadOnlyList<ChunkIndex> TryComputeDirtyChunkIndices(IReadOnlyCollection<HexCellData> changedSet)
    {
        try
        {
            return _renderBackend.ComputeDirtyChunkIndices(changedSet);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static List<ChunkIndex> ExtractAffectedChunks(PreparedChunkGeometry chunkStaging)
    {
        var list = new List<ChunkIndex>();
        if (chunkStaging == null) return list;
        foreach (ChunkStagingGeometry st in chunkStaging.Chunks)
            list.Add(st.Index);
        return list;
    }

    private void LogCommitSummary(int commitId, int patchCount, int cellCount, int chunkCount, MapDirtyFlags dirtyFlags, long elapsedMs)
    {
        if (!MapMutationDiagnostics.EnableCommitLogging) return;
        Debug.Log($"[MapMutationService] Commit#{commitId} 完成：补丁 {patchCount}、脏格 {cellCount}、脏 Chunk {chunkCount}、" +
                  $"脏位 {MapMutationDiagnostics.FormatDirtyFlags(dirtyFlags)}、耗时 {elapsedMs}ms。");
    }

    /// <summary>脏格高亮（DebugDirtyChunk 通道）；由下一次提交覆盖或 ClearDirtyChunkHighlight 清除。</summary>
    private void HighlightChangedCells(IReadOnlyCollection<HexCellData> changedCells)
    {
        if (!MapMutationDiagnostics.EnableDirtyChunkHighlight || _highlightRenderer == null) return;
        _highlightRenderer.SetHighlightedCells(
            HexHighlightChannel.DebugDirtyChunk,
            changedCells,
            MapMutationDiagnostics.DirtyChunkHighlightColor);
    }

    /// <summary>手动清除脏 Chunk 高亮（诊断辅助）。幂等。</summary>
    public void ClearDirtyChunkHighlight()
    {
        if (_highlightRenderer == null) return;
        _highlightRenderer.ClearChannel(HexHighlightChannel.DebugDirtyChunk);
    }
}
