using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

//****************************************
// 【动态地图-阶段四】MapVisualTransitionService 单元测试
// 覆盖：错峰延迟计算（Simultaneous/CenterToOuter）、BeginTransition 生命周期、
// Tick 进度驱动、完成/取消事件、幂等、单位视觉跟随、视觉跟随物、模型溶解。
//****************************************

public class MapVisualTransitionServiceTests
{
    private IMapRenderBackend _backend;
    private MapInteractionGate _gate;
    private UnitMovementSystem _movementSystem;
    private MapVisualEventSO _mapVisualEvent;
    private GameLoop _gameLoop;
    private MapVisualTransitionService _service;

    private HexCellData _center;
    private HexCellData _ringA;
    private HexCellData _ringB;

    [SetUp]
    public void SetUp()
    {
        _backend = Substitute.For<IMapRenderBackend>();
        _backend.SupportsAnimatedTransition.Returns(true);

        _gate = new MapInteractionGate();

        var mapData = Substitute.For<IMapDataService>();
        _center = new HexCellData(Enums.HexType.NoRiver, 0, new Vector3(0, 0, 0), new Vector3(0, 0, 0), 0f)
        {
            movementCost = 1f,
            Height = 3f
        };
        _ringA = new HexCellData(Enums.HexType.NoRiver, 1, new Vector3(1, -1, 0), new Vector3(0, 0, 0), 0f)
        {
            movementCost = 1f,
            Height = 3f
        };
        _ringB = new HexCellData(Enums.HexType.NoRiver, 2, new Vector3(2, -2, 0), new Vector3(0, 0, 0), 0f)
        {
            movementCost = 1f,
            Height = 3f
        };
        mapData.GetAllCells().Returns(new List<HexCellData> { _center, _ringA, _ringB });

        _mapVisualEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();
        _gameLoop = new GameLoop(new GlobalTimerService());
        _movementSystem = new UnitMovementSystem(mapData, _mapVisualEvent, _gameLoop);

        _service = new MapVisualTransitionService(_backend, _gate, _movementSystem);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mapVisualEvent);
    }

    // ── 错峰延迟计算（§13.7）────────────────────────────────

    [Test]
    public void ComputeStaggerDelays_Simultaneous_AllZero()
    {
        var cells = new List<HexCellData> { _center, _ringA, _ringB };
        var delays = _service.ComputeStaggerDelays(cells, new MapTransitionOptions
        {
            Stagger = MapTransitionStagger.Simultaneous
        });

        Assert.AreEqual(0f, delays[_center.GenerateOrder]);
        Assert.AreEqual(0f, delays[_ringA.GenerateOrder]);
        Assert.AreEqual(0f, delays[_ringB.GenerateOrder]);
    }

    [Test]
    public void ComputeStaggerDelays_CenterToOuter_CenterZeroOuterLarger()
    {
        var cells = new List<HexCellData> { _center, _ringA, _ringB };
        var delays = _service.ComputeStaggerDelays(cells, new MapTransitionOptions
        {
            Stagger = MapTransitionStagger.CenterToOuter,
            StaggerCenter = _center
        });

        Assert.AreEqual(0f, delays[_center.GenerateOrder], "中心格应无延迟");
        Assert.Greater(delays[_ringB.GenerateOrder], delays[_ringA.GenerateOrder], "外环延迟应大于内环");
        Assert.LessOrEqual(delays[_ringB.GenerateOrder], 0.6f, "错峰上限 60%（§13.2）");
    }

    [Test]
    public void ComputeStaggerDelays_UnsupportedMode_FallsBackToZero()
    {
        var cells = new List<HexCellData> { _center, _ringA };
        var delays = _service.ComputeStaggerDelays(cells, new MapTransitionOptions
        {
            Stagger = MapTransitionStagger.Wave,
            StaggerCenter = _center
        });

        Assert.AreEqual(0f, delays[_center.GenerateOrder]);
        Assert.AreEqual(0f, delays[_ringA.GenerateOrder]);
    }

    // ── Duration=0：同步完成（§12.3）────────────────────────

    [Test]
    public void BeginTransition_DurationZero_CompletesSynchronously()
    {
        var phases = new List<MapChangedPhase>();
        var cells = new List<HexCellData> { _center, _ringA };
        var removed = new RemovedVisualHandle();

        _service.BeginTransition(
            1, MapDirtyFlags.Terrain, cells,
            new MapTransitionOptions { Duration = 0f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f }, { 1, 0f } },
            new Dictionary<int, float> { { 0, 0f }, { 1, 0f } },
            removed,
            ev => phases.Add(ev.Phase));

        Assert.IsFalse(_service.IsAnimating, "Duration=0 不应保留活动动画");
        Assert.AreEqual(1, phases.Count, "同步路径只应发布一次阶段事件");
        Assert.AreEqual(MapChangedPhase.Finalized, phases[0], "Duration=0 应直接 Finalized");
        Assert.IsFalse(_gate.HasLocks, "同步完成应释放交互锁");
        _backend.Received(1).FinalizeChunkAnimation(new ChunkIndex(0, 0));
    }

    // ── Duration>0：生命周期 ─────────────────────────────────

    [Test]
    public void BeginTransition_DurationPositive_PublishesStarted()
    {
        var phases = new List<MapChangedPhase>();
        var cells = new List<HexCellData> { _center };

        _service.BeginTransition(
            1, MapDirtyFlags.Terrain, cells,
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => phases.Add(ev.Phase));

        Assert.IsTrue(_service.IsAnimating);
        Assert.AreEqual(1, phases.Count);
        Assert.AreEqual(MapChangedPhase.TransitionStarted, phases[0]);
    }

    [Test]
    public void Tick_AdvancesProgress_AndDrivesChunkMpb()
    {
        var cells = new List<HexCellData> { _center };
        _service.BeginTransition(
            1, MapDirtyFlags.Terrain, cells,
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => { });

        _service.Tick(0.25f);
        _backend.Received(1).SetChunkAnimationProgress(new ChunkIndex(0, 0), Arg.Any<float>());
    }

    [Test]
    public void Tick_UntilDone_PublishesFinalized_Unlocks_AndFinalizesChunk()
    {
        var phases = new List<MapChangedPhase>();
        var cells = new List<HexCellData> { _center };
        _gate.LockCells(cells);
        var removed = new RemovedVisualHandle();

        _service.BeginTransition(
            1, MapDirtyFlags.Terrain, cells,
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            removed,
            ev => phases.Add(ev.Phase));

        _service.Tick(1.0f);

        Assert.IsFalse(_service.IsAnimating, "动画完成后不应再处于动画中");
        Assert.AreEqual(2, phases.Count);
        Assert.AreEqual(MapChangedPhase.TransitionStarted, phases[0]);
        Assert.AreEqual(MapChangedPhase.Finalized, phases[1]);
        Assert.IsFalse(_gate.HasLocks, "Finalize 应解锁");
        _backend.Received(1).FinalizeChunkAnimation(new ChunkIndex(0, 0));
    }

    [Test]
    public void Tick_AfterComplete_IsIdempotent()
    {
        var phases = new List<MapChangedPhase>();
        var cells = new List<HexCellData> { _center };

        _service.BeginTransition(
            1, MapDirtyFlags.Terrain, cells,
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => phases.Add(ev.Phase));

        _service.Tick(2.0f);
        int finalizedCount = phases.FindAll(p => p == MapChangedPhase.Finalized).Count;
        _service.Tick(1.0f); // 完成后再次 Tick 不应重复 Finalize

        Assert.AreEqual(1, finalizedCount, "同一 CommitId 只 Finalize 一次（§12.3 幂等）");
    }

    // ── ForceComplete / 取消（§13.8 对局结束）────────────────

    [Test]
    public void ForceComplete_CompletesAnimation()
    {
        var phases = new List<MapChangedPhase>();
        var cells = new List<HexCellData> { _center };
        _gate.LockCells(cells);

        _service.BeginTransition(
            1, MapDirtyFlags.Terrain, cells,
            new MapTransitionOptions { Duration = 5f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => phases.Add(ev.Phase));

        _service.ForceComplete();

        Assert.IsFalse(_service.IsAnimating);
        Assert.IsTrue(phases.Contains(MapChangedPhase.Cancelled), "对局结束强制完成应发布 Cancelled");
        Assert.IsFalse(_gate.HasLocks);
        _backend.Received(1).FinalizeChunkAnimation(new ChunkIndex(0, 0));
    }

    [Test]
    public void ForceComplete_WhenIdle_IsNoOp()
    {
        Assert.DoesNotThrow(() => _service.ForceComplete());
        _backend.DidNotReceive().FinalizeChunkAnimation(Arg.Any<ChunkIndex>());
    }

    // ── 单位视觉跟随（§13.6）────────────────────────────────

    [Test]
    public void Tick_StandingUnitFollowsAnimatedHeight()
    {
        // 构造一个站在中心格上的单位
        GameObject unit = new GameObject("TestUnit");
        _center.SetHaveUnit(true, unit);
        _center.SetOccupant(unit);
        unit.transform.position = new Vector3(0f, 9f, 0f); // 最终高度 RealCenterWorldCoordinate.y=9（快照旧高度 0 → 新 9）

        // 注意：RealCenterWorldCoordinate 由几何构建写入；这里手动设置以模拟新高度
        _center.RealCenterWorldCoordinate = new Vector3(0f, 9f, 0f);

        var cells = new List<HexCellData> { _center };
        _service.BeginTransition(
            1, MapDirtyFlags.Terrain, cells,
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },   // 旧中心世界 Y=0
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => { });

        // 进度 50%：旧 0 → 新 9 的 smoothstep(0.5)=0.5 → y≈4.5
        _service.Tick(0.5f);
        Assert.Greater(unit.transform.position.y, 3f);
        Assert.Less(unit.transform.position.y, 6f);

        // 动画结束：吸附回最终高度
        _service.Tick(0.5f);
        Assert.AreEqual(9f, unit.transform.position.y, 0.001f);

        Object.DestroyImmediate(unit);
    }

    // ── 视觉跟随物（§13.2 宝箱升起）──────────────────────────

    [Test]
    public void RegisterVisualFollower_FollowsCellAnimation()
    {
        GameObject go = new GameObject("Chest");
        go.transform.position = new Vector3(0f, 9f, 0f);
        _center.RealCenterWorldCoordinate = new Vector3(0f, 9f, 0f);

        var cells = new List<HexCellData> { _center };
        _service.BeginTransition(
            1, MapDirtyFlags.Terrain, cells,
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            new RemovedVisualHandle(),
            ev => { });

        _service.RegisterVisualFollower(go.transform, _center);
        Assert.AreEqual(0f, go.transform.position.y, 0.001f, "注册时应立即定位到动画起点（旧高度）");

        _service.Tick(1.0f);
        Assert.AreEqual(9f, go.transform.position.y, 0.001f, "动画结束跟随物应停在最终高度");

        Object.DestroyImmediate(go);
    }

    // ── 模型溶解（§13.4）────────────────────────────────────

    [Test]
    public void Tick_RemovedModelsSinkAndScaleDown()
    {
        GameObject model = new GameObject("OldModel");
        var removed = new RemovedVisualHandle();
        removed.Add(model);

        var cells = new List<HexCellData> { _center };
        _service.BeginTransition(
            1, MapDirtyFlags.Objects, cells,
            new MapTransitionOptions { Duration = 1f },
            new List<ChunkIndex> { new ChunkIndex(0, 0) },
            new Dictionary<int, float> { { 0, 0f } },
            new Dictionary<int, float> { { 0, 0f } },
            removed,
            ev => { });

        _service.Tick(0.5f);
        Assert.Less(model.transform.position.y, 0f, "模型应下沉");
        Assert.Less(model.transform.localScale.x, 1f, "模型应缩小");

        _service.Tick(0.5f);
        Assert.IsTrue(removed.IsEmpty, "Finalize 后句柄应清空（对象已销毁）");

        Object.DestroyImmediate(model);
    }
}
