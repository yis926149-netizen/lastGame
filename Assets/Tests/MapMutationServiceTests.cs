using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using UnityEngine;
//****************************************
// 【动态地图-阶段二】MapMutationService 单元测试
// 覆盖：事务写数据/水陆双向重置/脏位标记/事件单次广播/归属 Patch 拒绝/提交后无活动事务。
//****************************************

public class MapMutationServiceTests
{
    private IMapDataService _mapData;
    private IMapRenderBackend _backend;
    private UnitMovementSystem _movementSystem;
    private GameLoop _gameLoop;
    private MapVisualEventSO _mapVisualEvent;
    private LandFormMarkerManager _markerManager;
    private MapInteractionGate _gate;
    private MapMutationService _service;

    private HexCellData _cellA;
    private HexCellData _cellB;

    [SetUp]
    public void SetUp()
    {
        _mapData = Substitute.For<IMapDataService>();
        _backend = Substitute.For<IMapRenderBackend>();
        _backend.PrepareWholeMapGeometry().Returns(new PreparedWholeMapGeometry());

        _cellA = new HexCellData(Enums.HexType.NoRiver, 0, new Vector3(0, 0, 0), Vector3.zero, 2f)
        {
            movementCost = 1f
        };
        _cellB = new HexCellData(Enums.HexType.LakeOrSea, 1, new Vector3(1, -1, 0), Vector3.zero, 1f)
        {
            movementCost = float.MaxValue
        };
        _mapData.GetAllCells().Returns(new List<HexCellData> { _cellA, _cellB });

        _mapVisualEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();
        _gameLoop = new GameLoop(new GlobalTimerService());
        _movementSystem = new UnitMovementSystem(_mapData, _mapVisualEvent, _gameLoop);
        _markerManager = new LandFormMarkerManager(_mapData);
        _gate = new MapInteractionGate();

        _service = new MapMutationService(_backend, _movementSystem, _gameLoop, _mapVisualEvent, _markerManager, _gate);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mapVisualEvent);
    }

    [Test]
    public void Commit_AppliesPatchToCellData()
    {
        _service.BeginTransaction();
        _service.Apply(_cellA, new HexCellPatch
        {
            HasHeight = true, Height = 5f,
            HasMovementCost = true, MovementCost = 2f,
            HasIsUnexplorable = true, IsUnexplorable = true,
            ClearRiver = true
        });
        var result = _service.Commit();

        Assert.NotNull(result);
        Assert.AreEqual(5f, _cellA.Height);
        Assert.AreEqual(2f, _cellA.movementCost);
        Assert.IsTrue(_cellA.IsUnexplorable);
    }

    [Test]
    public void Commit_WaterToLand_ResetsWaterStateAndMovementCost()
    {
        // _cellB 是水（Height=1 ≤ seaLevel=1，movementCost=MaxValue，HexType=LakeOrSea）
        _service.BeginTransaction();
        _service.Apply(_cellB, HexCellPatch.HeightPatch(3f)); // 抬至 seaLevel 以上，无显式 movementCost
        _service.Commit();

        Assert.AreEqual(3f, _cellB.Height);
        Assert.AreEqual(Enums.HexType.NoRiver, _cellB.HexType);
        Assert.IsFalse(_cellB.isCoast);
        Assert.AreEqual(0f, _cellB.waterLevel);
        Assert.AreEqual(1f, _cellB.movementCost, "水→陆地后 movementCost 应自动重置为 1");
    }

    [Test]
    public void Commit_LandToWater_SetsMovementCostImpassable()
    {
        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(0f)); // 降至 seaLevel 以下
        _service.Commit();

        Assert.AreEqual(0f, _cellA.Height);
        Assert.AreEqual(float.MaxValue, _cellA.movementCost, "陆地→水后 movementCost 应自动置为 MaxValue");
    }

    [Test]
    public void Commit_ClearRiver_ResetsRiverFieldsAndHexType()
    {
        var riverCell = new HexCellData(Enums.HexType.RiverMidstream, 2, new Vector3(0, -1, 1), Vector3.zero, 2f)
        {
            hasRiver = true,
            hasRiverIncoming = true,
            hasRiverOutgoing = true,
            RiverIncomingDirection = Enums.HexDirection.NE,
            RiverOutgoingDirection = Enums.HexDirection.E
        };

        _service.BeginTransaction();
        _service.Apply(riverCell, new HexCellPatch { ClearRiver = true });
        _service.Commit();

        Assert.IsFalse(riverCell.hasRiver);
        Assert.IsFalse(riverCell.hasRiverIncoming);
        Assert.IsFalse(riverCell.hasRiverOutgoing);
        Assert.AreEqual(Enums.HexDirection.None, riverCell.RiverIncomingDirection);
        Assert.AreEqual(Enums.HexDirection.None, riverCell.RiverOutgoingDirection);
        Assert.AreEqual(Enums.HexType.NoRiver, riverCell.HexType);
    }

    [Test]
    public void Commit_ComputesDirtyFlagsByPatch()
    {
        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(5f));
        var heightResult = _service.Commit();

        Assert.AreEqual(
            MapDirtyFlags.Terrain | MapDirtyFlags.Water | MapDirtyFlags.River |
            MapDirtyFlags.Grid | MapDirtyFlags.Objects | MapDirtyFlags.Navigation,
            heightResult.DirtyFlags);

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.MovementCostPatch(3f));
        var costResult = _service.Commit();
        Assert.AreEqual(MapDirtyFlags.Navigation, costResult.DirtyFlags);

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.UnexplorablePatch(true));
        var labelResult = _service.Commit();
        Assert.AreEqual(MapDirtyFlags.Labels, labelResult.DirtyFlags);
    }

    [Test]
    public void Commit_RaisesMapChangedOnceWithCommittedPhase()
    {
        int eventCount = 0;
        MapChangedEvent captured = null;
        _service.MapChanged += e => { eventCount++; captured = e; };

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(4f));
        _service.Apply(_cellB, HexCellPatch.MovementCostPatch(1f));
        var result = _service.Commit();

        Assert.AreEqual(1, eventCount, "一次事务只应广播一次 MapChanged");
        Assert.AreEqual(MapChangedPhase.Committed, captured.Phase);
        Assert.AreEqual(result.CommitId, captured.CommitId);
        Assert.AreEqual(2, captured.ChangedCells.Count);
        Assert.IsTrue(System.Linq.Enumerable.Contains(captured.ChangedCells, _cellA));
        Assert.IsTrue(System.Linq.Enumerable.Contains(captured.ChangedCells, _cellB));
    }

    [Test]
    public void Commit_BatchPatches_RebuildBackendExactlyOnce()
    {
        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(4f));
        _service.Apply(_cellB, HexCellPatch.HeightPatch(5f));
        _service.Commit();

        _backend.Received(1).PrepareWholeMapGeometry();
        _backend.Received(1).CommitWholeMapGeometry(Arg.Any<PreparedWholeMapGeometry>());
        _backend.Received(1).RefreshCellObjects(Arg.Any<IReadOnlyCollection<HexCellData>>(), Arg.Any<RemovedVisualHandle>());
    }

    [Test]
    public void Commit_AfterReturn_NoActiveTransactionAndNoLockedCells()
    {
        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(4f));
        _service.Commit();

        Assert.IsFalse(_service.HasActiveTransaction);
        Assert.IsFalse(_gate.IsLocked(_cellA, MapInteractionType.Move));
        Assert.IsFalse(_gate.IsLocked(_cellA, MapInteractionType.Explore));
        Assert.IsFalse(_gate.IsLocked(_cellA, MapInteractionType.Deploy));
        Assert.IsFalse(_gate.HasLocks);
    }

    [Test]
    public void Apply_OwnerPatch_ThrowsNotSupported()
    {
        _service.BeginTransaction();
        Assert.Throws<System.NotSupportedException>(() =>
            _service.Apply(_cellA, new HexCellPatch { Owner = 0 }));
    }

    [Test]
    public void Commit_WithoutTransaction_ReturnsNull()
    {
        var result = _service.Commit();
        Assert.IsNull(result);
    }

    [Test]
    public void BeginTransaction_WhileActive_Throws()
    {
        _service.BeginTransaction();
        Assert.Throws<System.InvalidOperationException>(() => _service.BeginTransaction());
    }

    [Test]
    public void Rollback_ClearsTransactionWithoutSideEffects()
    {
        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(9f));
        _service.Rollback();

        Assert.IsFalse(_service.HasActiveTransaction);
        Assert.AreEqual(2f, _cellA.Height, "Rollback 后不应写入任何数据");
        _backend.DidNotReceive().PrepareWholeMapGeometry();
    }

    [Test]
    public void Commit_DurationGreaterThanZero_StillCommitsSynchronously()
    {
        // 阶段二强制 Duration=0：传入 >0 应警告并按同步提交处理，不抛异常
        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(4f));
        var result = _service.Commit(new MapTransitionOptions { Duration = 1.2f });

        Assert.NotNull(result);
        Assert.AreEqual(4f, _cellA.Height);
        Assert.IsFalse(_service.HasActiveTransaction);
    }

    // ── 阶段四：动画路径（Duration>0 且后端支持动画）──────────

    [Test]
    public void Commit_AnimatedPath_PreparesAnimatedGeometryAndKeepsLockUntilFinalize()
    {
        _backend.SupportsAnimatedTransition.Returns(true);
        _backend.SupportsChunkedRebuild.Returns(true);
        _backend.PrepareAnimatedChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>(),
                Arg.Any<IReadOnlyDictionary<int, float>>(),
                Arg.Any<IReadOnlyDictionary<int, float>>())
            .Returns(new PreparedChunkGeometry());
        var visualTransition = new MapVisualTransitionService(_backend, _gate, _movementSystem);
        _service = new MapMutationService(_backend, _movementSystem, _gameLoop, _mapVisualEvent, _markerManager, _gate, visualTransition);

        var phases = new List<MapChangedPhase>();
        _service.MapChanged += ev => phases.Add(ev.Phase);

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(4f));
        var result = _service.Commit(new MapTransitionOptions
        {
            Duration = 1.2f,
            Stagger = MapTransitionStagger.CenterToOuter,
            StaggerCenter = _cellA
        });

        Assert.NotNull(result);
        _backend.Received(1).PrepareAnimatedChunkGeometry(
            Arg.Any<IReadOnlyCollection<HexCellData>>(),
            Arg.Any<IReadOnlyDictionary<int, float>>(),
            Arg.Any<IReadOnlyDictionary<int, float>>());
        _backend.Received(1).CommitAnimatedChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        Assert.AreEqual(MapChangedPhase.Committed, phases[0]);
        Assert.AreEqual(MapChangedPhase.TransitionStarted, phases[1]);
        Assert.IsTrue(visualTransition.IsAnimating, "动画应处于活动状态");
        Assert.IsTrue(_gate.HasLocks, "动画期间应保持交互锁（§20-5）");

        // 动画完成 → Finalized + 解锁
        visualTransition.Tick(1.2f);
        Assert.AreEqual(MapChangedPhase.Finalized, phases[2]);
        Assert.IsFalse(_gate.HasLocks, "动画完成应解锁");
        Assert.IsFalse(visualTransition.IsAnimating);
    }

    [Test]
    public void Commit_AnimatedPath_ForceCompleteReleasesLock()
    {
        _backend.SupportsAnimatedTransition.Returns(true);
        _backend.SupportsChunkedRebuild.Returns(true);
        _backend.PrepareAnimatedChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>(),
                Arg.Any<IReadOnlyDictionary<int, float>>(),
                Arg.Any<IReadOnlyDictionary<int, float>>())
            .Returns(new PreparedChunkGeometry());
        var visualTransition = new MapVisualTransitionService(_backend, _gate, _movementSystem);
        _service = new MapMutationService(_backend, _movementSystem, _gameLoop, _mapVisualEvent, _markerManager, _gate, visualTransition);

        var phases = new List<MapChangedPhase>();
        _service.MapChanged += ev => phases.Add(ev.Phase);

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(4f));
        _service.Commit(new MapTransitionOptions { Duration = 5f });

        Assert.IsTrue(_gate.HasLocks);
        visualTransition.ForceComplete();
        Assert.IsFalse(_gate.HasLocks, "对局结束强制完成后应解锁");
        Assert.IsTrue(phases.Contains(MapChangedPhase.Cancelled), "强制完成应发布 Cancelled");
    }

    [Test]
    public void Commit_AnimatedPath_SupportsFalse_FallsBackToSync()
    {
        // 后端不支持动画（Substitute 默认 false）→ Duration>0 降级同步，提交后无锁
        _backend.SupportsAnimatedTransition.Returns(false);
        _backend.SupportsChunkedRebuild.Returns(true);
        _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(new PreparedChunkGeometry());
        var visualTransition = new MapVisualTransitionService(_backend, _gate, _movementSystem);
        _service = new MapMutationService(_backend, _movementSystem, _gameLoop, _mapVisualEvent, _markerManager, _gate, visualTransition);

        var phases = new List<MapChangedPhase>();
        _service.MapChanged += ev => phases.Add(ev.Phase);

        _service.BeginTransaction();
        _service.Apply(_cellA, HexCellPatch.HeightPatch(4f));
        var result = _service.Commit(new MapTransitionOptions { Duration = 1.2f });

        Assert.NotNull(result);
        Assert.IsFalse(_gate.HasLocks, "不支持动画时提交返回后无锁定格");
        Assert.IsFalse(phases.Contains(MapChangedPhase.TransitionStarted), "降级路径不应发布 TransitionStarted");
        Assert.AreEqual(MapChangedPhase.Committed, phases[phases.Count - 1]);
    }
}
