using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

//****************************************
// 【动态地图-阶段五】通用化与后续增强测试
// 覆盖：归属 Patch 接入（§二十-12）、诊断扩展（AffectedChunks/日志开关）、
// 不相交 Chunk 并行动画（§二十-6）、分帧提交、管线通用性（新事件接入同一管线）。
//****************************************

public class MapMutationStage5Tests
{
    private IMapDataService _mapData;
    private IMapRenderBackend _backend;
    private UnitMovementSystem _movementSystem;
    private GameLoop _gameLoop;
    private MapVisualEventSO _mapVisualEvent;
    private LandFormMarkerManager _markerManager;
    private MapInteractionGate _gate;
    private ILogisticsService _logistics;
    private MapVisualTransitionService _visualTransition;
    private MapMutationService _service;

    private HexCellData _cellA;
    private HexCellData _cellB;

    [SetUp]
    public void SetUp()
    {
        _mapData = Substitute.For<IMapDataService>();
        _backend = Substitute.For<IMapRenderBackend>();
        _backend.SupportsAnimatedTransition.Returns(true);

        _cellA = new HexCellData(Enums.HexType.NoRiver, 0, new Vector3(0, 0, 0), Vector3.zero, 2f)
        {
            movementCost = 1f
        };
        _cellB = new HexCellData(Enums.HexType.NoRiver, 1, new Vector3(1, -1, 0), Vector3.zero, 2f)
        {
            movementCost = 1f
        };
        _mapData.GetAllCells().Returns(new List<HexCellData> { _cellA, _cellB });

        _mapVisualEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();
        _gameLoop = new GameLoop(new GlobalTimerService());
        _movementSystem = new UnitMovementSystem(_mapData, _mapVisualEvent, _gameLoop);
        _markerManager = new LandFormMarkerManager(_mapData);
        _gate = new MapInteractionGate();
        _logistics = Substitute.For<ILogisticsService>();
        _visualTransition = new MapVisualTransitionService(_backend, _gate, _movementSystem);

        _service = new MapMutationService(
            _backend, _movementSystem, _gameLoop, _mapVisualEvent, _markerManager, _gate,
            _visualTransition, _logistics);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mapVisualEvent);
    }

    // ── 阶段五-1：归属 Patch 接入（§二十-12）──────────────────

    [Test]
    public void Commit_OwnerPatch_AppliesViaLogisticsDomainEntry()
    {
        _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(new PreparedChunkGeometry());

        _service.BeginTransaction();
        _service.Apply(_cellA, new HexCellPatch { Owner = 1 });
        var result = _service.Commit();

        _logistics.Received(1).SetOwner(_cellA, 1);
        Assert.IsTrue((result.DirtyFlags & MapDirtyFlags.Territory) != 0, "归属变化应标记 Territory 脏位");
        Assert.IsTrue((result.DirtyFlags & MapDirtyFlags.Logistics) != 0, "归属变化应标记 Logistics 脏位");
        Assert.IsTrue((result.DirtyFlags & MapDirtyFlags.Fog) != 0, "归属变化应标记 Fog 脏位");
    }

    [Test]
    public void Commit_OwnerPatchNegative_ClearsOwner()
    {
        _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(new PreparedChunkGeometry());

        _service.BeginTransaction();
        _service.Apply(_cellA, new HexCellPatch { Owner = -1 });
        _service.Commit();

        _logistics.Received(1).ClearOwner(_cellA);
    }

    [Test]
    public void Apply_OwnerPatch_WithoutLogistics_StillThrows()
    {
        var serviceNoLogistics = new MapMutationService(
            _backend, _movementSystem, _gameLoop, _mapVisualEvent, _markerManager, _gate);

        serviceNoLogistics.BeginTransaction();
        Assert.Throws<System.NotSupportedException>(() =>
            serviceNoLogistics.Apply(_cellA, new HexCellPatch { Owner = 0 }));
    }

    [Test]
    public void ComputeDirtyFlags_OwnerPatch_IncludesTerritoryLogisticsFog()
    {
        // 与 HeightPatch 组合：脏位应同时含几何位与归属位
        _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(new PreparedChunkGeometry());

        _service.BeginTransaction();
        _service.Apply(_cellA, new HexCellPatch { Owner = 1, HasHeight = true, Height = 5f });
        var result = _service.Commit();

        Assert.IsTrue((result.DirtyFlags & MapDirtyFlags.Territory) != 0);
        Assert.IsTrue((result.DirtyFlags & MapDirtyFlags.Terrain) != 0);
    }

    // ── 阶段五-2：诊断扩展（AffectedChunks / 日志开关）────────

    [Test]
    public void Commit_MapChangedEvent_CarriesAffectedChunks()
    {
        var staging = new PreparedChunkGeometry();
        staging.Chunks.Add(new ChunkStagingGeometry { Index = new ChunkIndex(1, 1) });
        staging.Chunks.Add(new ChunkStagingGeometry { Index = new ChunkIndex(1, 2) });
        _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>()).Returns(staging);

        MapChangedEvent captured = null;
        _service.MapChanged += ev => captured = ev;

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(5f));
        _service.Commit();

        Assert.NotNull(captured);
        Assert.AreEqual(2, captured.AffectedChunks.Count);
        Assert.Contains(new ChunkIndex(1, 1), captured.AffectedChunks.ToList());
    }

    [Test]
    public void Commit_DiagnosticsLoggingEnabled_DoesNotThrow()
    {
        bool prev = MapMutationDiagnostics.EnableCommitLogging;
        MapMutationDiagnostics.EnableCommitLogging = true;
        try
        {
            _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
                .Returns(new PreparedChunkGeometry());
            _service.BeginTransaction();
            _service.Apply(_cellA, HexCellPatch.HeightPatch(5f));
            Assert.DoesNotThrow(() => _service.Commit());
        }
        finally
        {
            MapMutationDiagnostics.EnableCommitLogging = prev;
        }
    }

    [Test]
    public void Commit_DirtyChunkHighlightDisabled_DoesNotThrowWithRenderer()
    {
        // 高亮开关默认关：即使注入 renderer 也不调用（防止误高亮刷屏）
        bool prev = MapMutationDiagnostics.EnableDirtyChunkHighlight;
        MapMutationDiagnostics.EnableDirtyChunkHighlight = false;
        try
        {
            var go = new GameObject("HighlightProbe");
            try
            {
                HexHighlightRenderer renderer = go.AddComponent<HexHighlightRenderer>();
                var service = new MapMutationService(
                    _backend, _movementSystem, _gameLoop, _mapVisualEvent, _markerManager, _gate,
                    null, null, renderer);
                _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
                    .Returns(new PreparedChunkGeometry());

                service.BeginTransaction();
                service.Apply(_cellA, HexCellPatch.HeightPatch(5f));
                Assert.DoesNotThrow(() => service.Commit());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
        finally
        {
            MapMutationDiagnostics.EnableDirtyChunkHighlight = prev;
        }
    }

    // ── 阶段五-4：不相交 Chunk 并行动画（§二十-6）────────────

    [Test]
    public void BeginTransition_DisjointChunks_RunInParallel()
    {
        var phases = new List<MapChangedPhase>();

        _visualTransition.BeginTransition(
            1, MapDirtyFlags.Terrain, new List<HexCellData> { _cellA },
            new MapTransitionOptions { Duration = 2f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => phases.Add(ev.Phase));

        _visualTransition.BeginTransition(
            2, MapDirtyFlags.Terrain, new List<HexCellData> { _cellB },
            new MapTransitionOptions { Duration = 2f },
            new List<ChunkIndex> { new ChunkIndex(3, 3) },
            new Dictionary<int, float> { { 1, 0f } },
            new Dictionary<int, float> { { 1, 0f } },
            new RemovedVisualHandle(),
            ev => phases.Add(ev.Phase));

        // 两个动画应同时活动（不相交 Chunk 并行，§阶段五-并行动画）
        Assert.AreEqual(2, phases.Count(p => p == MapChangedPhase.TransitionStarted),
            "不相交 Chunk 的两个动画都应发布 TransitionStarted");

        // 推进到动画 1 完成（1s 后 → 未完成；2s 后 → 完成）
        _visualTransition.Tick(2.0f);
        Assert.AreEqual(2, phases.Count(p => p == MapChangedPhase.Finalized),
            "两个动画都应各自 Finalized（互不干扰）");
        Assert.IsFalse(_visualTransition.IsAnimating);
    }

    [Test]
    public void BeginTransition_OverlappingChunks_CompletesConflictingOldAnimation()
    {
        var phases = new List<MapChangedPhase>();

        _visualTransition.BeginTransition(
            1, MapDirtyFlags.Terrain, new List<HexCellData> { _cellA },
            new MapTransitionOptions { Duration = 5f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => phases.Add(ev.Phase));

        // 与 Chunk(0,0) 相交的新动画 → 旧动画应被取消，新动画正常开始
        _visualTransition.BeginTransition(
            2, MapDirtyFlags.Terrain, new List<HexCellData> { _cellB },
            new MapTransitionOptions { Duration = 5f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 1, 0f } },
            new Dictionary<int, float> { { 1, 0f } },
            new RemovedVisualHandle(),
            ev => phases.Add(ev.Phase));

        Assert.IsTrue(phases.Contains(MapChangedPhase.Cancelled), "相交的旧动画应被取消");
        Assert.AreEqual(1, phases.Count(p => p == MapChangedPhase.TransitionStarted),
            "只有新动画保留 TransitionStarted");
        Assert.IsTrue(_visualTransition.IsAnimating, "新动画应继续活动");
    }

    [Test]
    public void ForceCompleteConflicting_OnlyCompletesConflictingOnes()
    {
        _visualTransition.BeginTransition(
            1, MapDirtyFlags.Terrain, new List<HexCellData> { _cellA },
            new MapTransitionOptions { Duration = 5f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => { });
        _visualTransition.BeginTransition(
            2, MapDirtyFlags.Terrain, new List<HexCellData> { _cellB },
            new MapTransitionOptions { Duration = 5f },
            new List<ChunkIndex> { new ChunkIndex(4, 4) },
            new Dictionary<int, float> { { 1, 0f } },
            new Dictionary<int, float> { { 1, 0f } },
            new RemovedVisualHandle(),
            ev => { });

        // 只完成与 Chunk(0,0) 相交的动画 1；动画 2（Chunk(4,4)）保持并行
        _visualTransition.ForceCompleteConflicting(new List<ChunkIndex> { new ChunkIndex(0, 0) });

        Assert.IsTrue(_visualTransition.IsAnimating, "不相交的动画 2 应保持活动");
        _visualTransition.ForceComplete();
        Assert.IsFalse(_visualTransition.IsAnimating);
    }

    [Test]
    public void Complete_ParallelAnimation_OnlyUnlocksOwnCells()
    {
        _gate.LockCells(new List<HexCellData> { _cellA, _cellB });

        _visualTransition.BeginTransition(
            1, MapDirtyFlags.Terrain, new List<HexCellData> { _cellA },
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => { });
        _visualTransition.BeginTransition(
            2, MapDirtyFlags.Terrain, new List<HexCellData> { _cellB },
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(5, 5) },
            new Dictionary<int, float> { { 1, 0f } },
            new Dictionary<int, float> { { 1, 0f } },
            new RemovedVisualHandle(),
            ev => { });

        // 动画 1 完成 → 只解锁 _cellA；_cellB 仍锁定
        _visualTransition.Tick(1.0f);
        Assert.IsFalse(_gate.IsLocked(_cellA, MapInteractionType.Move), "完成的动画应解锁自己的格");
        Assert.IsTrue(_gate.IsLocked(_cellB, MapInteractionType.Move), "并行动画的格应保持锁定");

        _visualTransition.Tick(1.0f);
        Assert.IsFalse(_gate.IsLocked(_cellB, MapInteractionType.Move), "动画 2 完成后再解锁自己的格");
    }

    // ── 阶段五-5：分帧提交 ─────────────────────────────────

    [Test]
    public void CommitSliced_BuildsChunksAcrossTicks_ThenCommitsOnce()
    {
        // 3 个脏 Chunk，每帧最多 1 个 → 需要 3 次 TickSlicedCommit
        var dirtyChunks = new List<ChunkIndex>
        {
            new ChunkIndex(0, 0), new ChunkIndex(0, 1), new ChunkIndex(1, 0)
        };
        _backend.ComputeDirtyChunkIndices(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(dirtyChunks);
        _backend.PrepareChunkGeometrySlice(Arg.Any<IReadOnlyList<ChunkIndex>>())
            .Returns(callInfo =>
            {
                var staging = new PreparedChunkGeometry();
                var indices = callInfo.Arg<IReadOnlyList<ChunkIndex>>();
                foreach (ChunkIndex index in indices)
                    staging.Chunks.Add(new ChunkStagingGeometry { Index = index });
                return staging;
            });

        int committedCount = 0;
        _service.MapChanged += ev =>
        {
            if (ev.Phase == MapChangedPhase.Committed) committedCount++;
        };

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(5f));
        var result = _service.CommitSliced(new MapTransitionOptions(), maxChunksPerFrame: 1);

        Assert.NotNull(result);
        Assert.IsTrue(_service.HasSlicedCommitPending, "首帧后应仍有分帧提交进行中（3 个 Chunk 每帧 1 个）");
        Assert.IsTrue(_gate.HasLocks, "分帧提交期间应保持交互锁（§20-5 语义）");
        Assert.AreEqual(0, committedCount, "分帧提交未完成前不应广播 Committed");

        // 第 2 帧：构建第 2 个 Chunk
        _service.TickSlicedCommit();
        Assert.IsTrue(_service.HasSlicedCommitPending);
        Assert.AreEqual(0, committedCount);

        // 第 3 帧：构建第 3 个 Chunk 并完成提交
        _service.TickSlicedCommit();
        Assert.IsFalse(_service.HasSlicedCommitPending, "全部 Chunk 构建完成后分帧提交应结束");
        Assert.AreEqual(1, committedCount, "分帧提交完成只广播一次 Committed");
        Assert.IsFalse(_gate.HasLocks, "分帧提交完成后应释放锁");

        _backend.Received(3).PrepareChunkGeometrySlice(Arg.Any<IReadOnlyList<ChunkIndex>>());
        _backend.Received(1).CommitChunkGeometry(Arg.Any<PreparedChunkGeometry>());
    }

    [Test]
    public void ForceCompleteSliced_CompletesRemainingChunksImmediately()
    {
        var dirtyChunks = new List<ChunkIndex>
        {
            new ChunkIndex(0, 0), new ChunkIndex(0, 1), new ChunkIndex(1, 0), new ChunkIndex(1, 1)
        };
        _backend.ComputeDirtyChunkIndices(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(dirtyChunks);
        _backend.PrepareChunkGeometrySlice(Arg.Any<IReadOnlyList<ChunkIndex>>())
            .Returns(callInfo =>
            {
                var staging = new PreparedChunkGeometry();
                foreach (ChunkIndex index in callInfo.Arg<IReadOnlyList<ChunkIndex>>())
                    staging.Chunks.Add(new ChunkStagingGeometry { Index = index });
                return staging;
            });

        int committedCount = 0;
        _service.MapChanged += ev =>
        {
            if (ev.Phase == MapChangedPhase.Committed) committedCount++;
        };

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(5f));
        _service.CommitSliced(new MapTransitionOptions(), maxChunksPerFrame: 1);

        Assert.IsTrue(_service.HasSlicedCommitPending);

        _service.ForceCompleteSliced();
        Assert.IsFalse(_service.HasSlicedCommitPending, "强制完成后不应有进行中的分帧提交");
        Assert.AreEqual(1, committedCount);
        Assert.IsFalse(_gate.HasLocks, "强制完成后应释放锁");
    }

    [Test]
    public void CommitSliced_WithoutTransaction_ReturnsNull()
    {
        Assert.IsNull(_service.CommitSliced());
    }

    [Test]
    public void BeginTransaction_WhileSlicedPending_Throws()
    {
        _backend.ComputeDirtyChunkIndices(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(new List<ChunkIndex> { new ChunkIndex(0, 0), new ChunkIndex(0, 1) });
        _backend.PrepareChunkGeometrySlice(Arg.Any<IReadOnlyList<ChunkIndex>>())
            .Returns(new PreparedChunkGeometry());

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(5f));
        _service.CommitSliced(new MapTransitionOptions(), maxChunksPerFrame: 1);

        Assert.Throws<System.InvalidOperationException>(() => _service.BeginTransaction(),
            "分帧提交进行中禁止开启新事务（§20-6 几何事务串行）");

        _service.ForceCompleteSliced();
    }

    // ── 阶段五-6：管线通用性验证（新事件接入同一管线）────────

    [Test]
    public void NewEvent_FloodDemoStyle_WorksThroughSamePipeline()
    {
        // 模拟"洪水"新事件：非竞技场调用方，同样经 BeginTransaction → Apply → Commit
        _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(new PreparedChunkGeometry());

        MapChangedEvent captured = null;
        _service.MapChanged += ev => captured = ev;

        _service.BeginTransaction();
        _service.Apply(_cellA, new HexCellPatch
        {
            HasHeight = true,
            Height = 0f, // ≤ seaLevel → 变水
            ClearRiver = true,
            ClearLandForm = true,
            ClearResource = true
        });
        _service.Apply(_cellB, new HexCellPatch
        {
            HasHeight = true,
            Height = 6f, // 陆地抬升
            HasMovementCost = true,
            MovementCost = 1f
        });
        var result = _service.Commit(new MapTransitionOptions { Duration = 0f });

        Assert.NotNull(result);
        Assert.AreEqual(0f, _cellA.Height);
        Assert.AreEqual(float.MaxValue, _cellA.movementCost, "变水格 movementCost 应自动置为 MaxValue（§8 水陆双向重置）");
        Assert.AreEqual(6f, _cellB.Height);
        Assert.AreEqual(2, captured.ChangedCells.Count);
        Assert.AreEqual(MapChangedPhase.Committed, captured.Phase);
        Assert.IsFalse(_service.HasActiveTransaction);
        Assert.IsFalse(_gate.HasLocks);
    }

    [Test]
    public void NewEvent_AnimatedFlood_ReusesAnimationPipeline()
    {
        // 新事件同样可带动画（Duration>0）：验证 TransitionStarted → Finalized 生命周期
        _backend.PrepareAnimatedChunkGeometry(
                Arg.Any<IReadOnlyCollection<HexCellData>>(),
                Arg.Any<IReadOnlyDictionary<int, float>>(),
                Arg.Any<IReadOnlyDictionary<int, float>>())
            .Returns(new PreparedChunkGeometry());

        var phases = new List<MapChangedPhase>();
        _service.MapChanged += ev => phases.Add(ev.Phase);

        _service.BeginTransaction();
        _service.Apply(_cellA, new HexCellPatch { HasHeight = true, Height = 6f });
        _service.Commit(new MapTransitionOptions { Duration = 1.2f });

        Assert.AreEqual(MapChangedPhase.Committed, phases[0]);
        Assert.AreEqual(MapChangedPhase.TransitionStarted, phases[1]);
        _visualTransition.Tick(1.2f);
        Assert.AreEqual(MapChangedPhase.Finalized, phases[2]);
        Assert.IsFalse(_gate.HasLocks, "动画完成后应解锁");
    }
}
