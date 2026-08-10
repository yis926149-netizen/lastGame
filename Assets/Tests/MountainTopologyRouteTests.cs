using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using UnityEngine;
//****************************************
// 【程序化山脉-阶段 5.5·修订】山体拓扑路由测试（决策 ⑨/㉕/㉛）。
// 移除方向（清除/水淹/阈值跌落 = 可见→不可见）即使请求 Duration>0 也整笔事务降级同步提交
// （旧山体 Ghost 保留能力首版不具备）；新增方向（恢复/新增 = 不可见→可见）允许动画——
// 新山体几何由 keep-below clip 在 progress=0 整体隐藏、随进度顶出（竞技场突起合并事务依赖此路径）；
// 纯 Height 修改保持动画路径；降级后无锁/无动画残留。
//****************************************

public class MountainTopologyRouteTests
{
    private IMapDataService _mapData;
    private IMapRenderBackend _backend;
    private UnitMovementSystem _movementSystem;
    private GameLoop _gameLoop;
    private MapVisualEventSO _mapVisualEvent;
    private LandFormMarkerManager _markerManager;
    private MapInteractionGate _gate;
    private MapVisualTransitionService _visualTransition;
    private MapMutationService _service;

    private MapLandFormSO _mountainForm;
    private HexCellData _cell;

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;
        WaterLevelConfig.MaxHeight = 5f;

        _mapData = Substitute.For<IMapDataService>();
        _backend = Substitute.For<IMapRenderBackend>();
        _backend.SupportsAnimatedTransition.Returns(true);
        _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(new PreparedChunkGeometry());
        _backend.PrepareAnimatedChunkGeometry(
                Arg.Any<IReadOnlyCollection<HexCellData>>(),
                Arg.Any<IReadOnlyDictionary<int, float>>(),
                Arg.Any<IReadOnlyDictionary<int, float>>())
            .Returns(new PreparedChunkGeometry());

        _mountainForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        _mountainForm.mountainForm = true;
        _mountainForm.blockBuildingSpawn = true;

        _cell = new HexCellData(Enums.HexType.NoRiver, 0, new Vector3(0, 0, 0), Vector3.zero, 2f);
        _cell.landForm = _mountainForm;
        _cell.mountainRidge = new MountainRidgeData
        {
            ridgeId = 1,
            seed = 1,
            length = 8,
            widthRadius = 1.5f,
            gamma = 1.2f,
            hMax = 2f,
            minVisibleHeight = 0.15f,
        };
        _cell.mountainRidgeStatus = Enums.MountainRidgeStatus.RidgeCell;
        _cell.mountainDistToRidge = 0f;
        _cell.mountainPosAlongRidge = 1f;

        _mapData.GetAllCells().Returns(new List<HexCellData> { _cell });

        _mapVisualEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();
        _gameLoop = new GameLoop(new GlobalTimerService());
        _movementSystem = new UnitMovementSystem(_mapData, _mapVisualEvent, _gameLoop);
        _markerManager = new LandFormMarkerManager(_mapData);
        _gate = new MapInteractionGate();
        _visualTransition = new MapVisualTransitionService(_backend, _gate, _movementSystem);
        _service = new MapMutationService(_backend, _movementSystem, _gameLoop, _mapVisualEvent, _markerManager, _gate, _visualTransition);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mapVisualEvent);
        Object.DestroyImmediate(_mountainForm);
    }

    private static MapTransitionOptions AnimatedOptions(float duration = 1.2f)
    {
        return new MapTransitionOptions { Duration = duration, Stagger = MapTransitionStagger.CenterToOuter };
    }

    [Test]
    public void Commit_HeightOnlyOnMountain_KeepsAnimationPath()
    {
        var phases = new List<MapChangedPhase>();
        _service.MapChanged += ev => phases.Add(ev.Phase);

        _service.BeginTransaction();
        _service.Apply(_cell, HexCellPatch.HeightPatch(4f)); // 纯 Height，山体拓扑不变
        _service.Commit(AnimatedOptions());

        _backend.Received(1).PrepareAnimatedChunkGeometry(
            Arg.Any<IReadOnlyCollection<HexCellData>>(),
            Arg.Any<IReadOnlyDictionary<int, float>>(),
            Arg.Any<IReadOnlyDictionary<int, float>>());
        _backend.Received(1).CommitAnimatedChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        _backend.Received(0).CommitChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        Assert.IsTrue(_visualTransition.IsAnimating, "纯 Height 修改仍走动画路径（决策 ⑨）");
        Assert.IsTrue(_gate.HasLocks, "动画期间保持交互锁");

        _visualTransition.Tick(1.2f);
        Assert.IsFalse(_visualTransition.IsAnimating);
        Assert.IsFalse(_gate.HasLocks, "Finalize 解锁");
    }

    [Test]
    public void Commit_ClearMountain_WithDuration_DowngradesToSync()
    {
        var phases = new List<MapChangedPhase>();
        _service.MapChanged += ev => phases.Add(ev.Phase);

        _service.BeginTransaction();
        _service.Apply(_cell, new HexCellPatch { ClearLandForm = true });
        _service.Commit(AnimatedOptions());

        _backend.Received(0).PrepareAnimatedChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>(),
            Arg.Any<IReadOnlyDictionary<int, float>>(), Arg.Any<IReadOnlyDictionary<int, float>>());
        _backend.Received(0).CommitAnimatedChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        _backend.Received(1).PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>());
        _backend.Received(1).CommitChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        _backend.Received(1).RefreshCellObjects(
            Arg.Any<IReadOnlyCollection<HexCellData>>(), Arg.Any<RemovedVisualHandle>(), true);

        Assert.IsFalse(_visualTransition.IsAnimating, "拓扑变化不得启动视觉过渡");
        Assert.IsFalse(_gate.HasLocks, "同步降级不得残留 Deferred lock");
        CollectionAssert.DoesNotContain(phases, MapChangedPhase.TransitionStarted, "不得发布 TransitionStarted");
        Assert.IsTrue(phases.Contains(MapChangedPhase.Committed), "仍发布 Committed");
        Assert.IsNull(_cell.landForm, "数据已写入（清除生效）");
    }

    [Test]
    public void Commit_WaterFlood_WithDuration_DowngradesToSync()
    {
        _service.BeginTransaction();
        _service.Apply(_cell, HexCellPatch.HeightPatch(0.5f)); // 陆 → 水（决策 ⑦ 水淹）
        _service.Commit(AnimatedOptions());

        _backend.Received(0).CommitAnimatedChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        _backend.Received(1).CommitChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        Assert.IsFalse(_visualTransition.IsAnimating);
        Assert.IsFalse(_gate.HasLocks);
        Assert.AreEqual(float.MaxValue, _cell.movementCost, "水淹山格不可通行");
    }

    [Test]
    public void Commit_WaterToLandRestore_WithDuration_Animates()
    {
        // 预置水淹（同步提交），随后水 → 陆恢复山体（决策 ⑦：恢复可见 = 新增方向 ⇒
        // 阶段 5.5 修订后走动画路径——新山体由 keep-below clip 隐藏后随进度顶出，无需 Ghost）
        _service.BeginTransaction();
        _service.Apply(_cell, HexCellPatch.HeightPatch(0.5f));
        _service.Commit();
        _backend.ClearReceivedCalls();

        var phases = new List<MapChangedPhase>();
        _service.MapChanged += ev => phases.Add(ev.Phase);

        _service.BeginTransaction();
        _service.Apply(_cell, HexCellPatch.HeightPatch(2f));
        _service.Commit(AnimatedOptions());

        _backend.Received(1).CommitAnimatedChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        _backend.Received(0).CommitChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        Assert.IsTrue(_visualTransition.IsAnimating, "新增方向拓扑变化走动画路径（阶段 5.5 修订）");
        Assert.IsTrue(_gate.HasLocks, "动画期间保持交互锁");
        Assert.IsTrue(phases.Contains(MapChangedPhase.TransitionStarted));
        Assert.AreEqual(float.MaxValue, _cell.movementCost, "水→陆恢复后不可通行规则立即生效（决策 ①/⑦）");

        _visualTransition.Tick(1.2f);
        Assert.IsFalse(_visualTransition.IsAnimating);
        Assert.IsFalse(_gate.HasLocks, "Finalize 解锁");
    }

    [Test]
    public void Commit_MountainAddition_WithDuration_Animates()
    {
        // 平地格直接写入山体数据（竞技场突起合并事务同款：高度 + 山体一笔动画事务）：
        // 不可见→可见 = 新增方向，不降级，走动画路径。
        var plainCell = new HexCellData(Enums.HexType.NoRiver, 1, new Vector3(1, 0, -1), Vector3.zero, 2f);
        _mapData.GetAllCells().Returns(new List<HexCellData> { _cell, plainCell });

        _service.BeginTransaction();
        _service.Apply(plainCell, new HexCellPatch
        {
            HasHeight = true,
            Height = 3f,
            HasMountain = true,
            MountainLandForm = _mountainForm,
            MountainRidge = new MountainRidgeData
            {
                ridgeId = 2,
                seed = 2,
                length = 5,
                widthRadius = 1.5f,
                gamma = 1.2f,
                hMax = 2f,
                minVisibleHeight = 0.15f,
            },
            MountainRidgeStatus = Enums.MountainRidgeStatus.RidgeCell,
            MountainDistToRidge = 0f,
            MountainPosAlongRidge = 0f,
        });
        _service.Commit(AnimatedOptions());

        Assert.IsTrue(MountainGeometryBuilder.HasVisibleMountain(plainCell), "山体数据已写入且可见");
        _backend.Received(1).PrepareAnimatedChunkGeometry(
            Arg.Any<IReadOnlyCollection<HexCellData>>(),
            Arg.Any<IReadOnlyDictionary<int, float>>(),
            Arg.Any<IReadOnlyDictionary<int, float>>());
        _backend.Received(1).CommitAnimatedChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        _backend.Received(0).CommitChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        Assert.IsTrue(_visualTransition.IsAnimating, "纯山体新增走动画路径（竞技场突起合并事务依赖）");
        Assert.IsTrue(_gate.HasLocks);

        _visualTransition.Tick(1.2f);
        Assert.IsFalse(_visualTransition.IsAnimating);
        Assert.IsFalse(_gate.HasLocks, "Finalize 解锁");
    }

    [Test]
    public void Commit_MountainAdditionMixedWithRemoval_WithDuration_DowngradesToSync()
    {
        // 混合事务：同笔既有新增（plainCell）又有移除（_cell 清除）⇒ 移除方向存在，整笔降级。
        var plainCell = new HexCellData(Enums.HexType.NoRiver, 1, new Vector3(1, 0, -1), Vector3.zero, 2f);
        _mapData.GetAllCells().Returns(new List<HexCellData> { _cell, plainCell });

        _service.BeginTransaction();
        _service.Apply(plainCell, new HexCellPatch
        {
            HasMountain = true,
            MountainLandForm = _mountainForm,
            MountainRidge = new MountainRidgeData
            {
                ridgeId = 2,
                seed = 2,
                length = 5,
                widthRadius = 1.5f,
                gamma = 1.2f,
                hMax = 2f,
                minVisibleHeight = 0.15f,
            },
            MountainRidgeStatus = Enums.MountainRidgeStatus.RidgeCell,
            MountainDistToRidge = 0f,
            MountainPosAlongRidge = 0f,
        });
        _service.Apply(_cell, new HexCellPatch { ClearLandForm = true });
        _service.Commit(AnimatedOptions());

        _backend.Received(0).CommitAnimatedChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        _backend.Received(1).CommitChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        Assert.IsFalse(_visualTransition.IsAnimating, "含移除方向的混合事务仍整笔降级");
        Assert.IsFalse(_gate.HasLocks);
        Assert.IsNull(_cell.landForm, "移除已生效");
        Assert.IsTrue(MountainGeometryBuilder.HasVisibleMountain(plainCell), "新增已生效（同步提交）");
    }

    [Test]
    public void Commit_AnimatedHeightOnMountain_ThenClear_ForceCompletesAndSyncs()
    {
        // 阶段 7.5：动画进行中再次清除山格——旧动画被 ForceCompleteConflicting 收尾，
        // 拓扑变化整笔事务降级同步提交；无双山重叠窗口、无锁残留、无 TransitionStarted 残留。
        var phases = new List<MapChangedPhase>();
        _service.MapChanged += ev => phases.Add(ev.Phase);

        _service.BeginTransaction();
        _service.Apply(_cell, HexCellPatch.HeightPatch(4f)); // 纯 Height，山体拓扑不变 ⇒ 动画
        _service.Commit(AnimatedOptions());
        Assert.IsTrue(_visualTransition.IsAnimating, "纯 Height 动画进行中");
        Assert.IsTrue(_gate.HasLocks, "动画期间交互锁生效");
        _backend.ClearReceivedCalls();
        phases.Clear(); // 第一次提交的合法 TransitionStarted 不计入断言

        _service.BeginTransaction();
        _service.Apply(_cell, new HexCellPatch { ClearLandForm = true }); // 动画中清除山格
        _service.Commit(AnimatedOptions());

        Assert.IsFalse(_visualTransition.IsAnimating, "旧动画已被 ForceCompleteConflicting 收尾（无 Ghost 突消窗口）");
        Assert.IsFalse(_gate.HasLocks, "动画 Finalize 与同步提交均不残留锁");
        _backend.Received(0).CommitAnimatedChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        _backend.Received(1).CommitChunkGeometry(Arg.Any<PreparedChunkGeometry>());
        Assert.IsNull(_cell.landForm, "清除数据已写入");
        Assert.AreEqual(1f, _cell.movementCost, "清除后 movementCost 回落 1（决策 ㉕）");
        CollectionAssert.DoesNotContain(phases, MapChangedPhase.TransitionStarted, "拓扑变化事务不得发布 TransitionStarted");
    }
}
