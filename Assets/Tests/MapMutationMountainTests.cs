using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using UnityEngine;
//****************************************
// 【程序化山脉】MapMutationService 山脉规则联动测试（决策 ①/⑦/㉕）。
// 覆盖：水→陆山格不可通行规则重新派生、ClearLandForm 永久清除山体、Terrain 脏位补齐。
//****************************************

public class MapMutationMountainTests
{
    private IMapDataService _mapData;
    private IMapRenderBackend _backend;
    private UnitMovementSystem _movementSystem;
    private GameLoop _gameLoop;
    private MapVisualEventSO _mapVisualEvent;
    private LandFormMarkerManager _markerManager;
    private MapInteractionGate _gate;
    private MapMutationService _service;

    private MapLandFormSO _mountainForm;
    private HexCellData _mountainCell;

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;

        _mapData = Substitute.For<IMapDataService>();
        _backend = Substitute.For<IMapRenderBackend>();
        _backend.PrepareChunkGeometry(Arg.Any<IReadOnlyCollection<HexCellData>>())
            .Returns(new PreparedChunkGeometry());

        _mountainForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        _mountainForm.mountainForm = true;
        _mountainForm.blockBuildingSpawn = true;

        _mountainCell = new HexCellData(Enums.HexType.NoRiver, 0, new Vector3(0, 0, 0), Vector3.zero, 2f);
        _mountainCell.landForm = _mountainForm;
        _mountainCell.movementCost = float.MaxValue;
        _mountainCell.mountainRidge = new MountainRidgeData { ridgeId = 1, hMax = 2f };
        _mountainCell.mountainRidgeStatus = Enums.MountainRidgeStatus.RidgeCell;

        _mapData.GetAllCells().Returns(new List<HexCellData> { _mountainCell });

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
        Object.DestroyImmediate(_mountainForm);
    }

    [Test]
    public void Commit_WaterToLand_MountainCell_RestoresImpassable()
    {
        // 决策 ⑦：山格被水淹后 movementCost=MaxValue（水域）；水→陆恢复时必须重新派生为 MaxValue，
        // 不能像普通格一样重置为 1（MapMutationService.cs 原 491-492 的默认重置）
        _service.BeginTransaction();
        _service.Apply(_mountainCell, HexCellPatch.HeightPatch(0.5f)); // 陆 → 水
        _service.Commit();
        Assert.AreEqual(float.MaxValue, _mountainCell.movementCost, "水淹山格仍不可通行");

        _service.BeginTransaction();
        _service.Apply(_mountainCell, HexCellPatch.HeightPatch(2f)); // 水 → 陆
        _service.Commit();

        Assert.IsTrue(MountainCellRule.IsMountainCell(_mountainCell), "landForm 残留，山体标记仍在");
        Assert.AreEqual(float.MaxValue, _mountainCell.movementCost, "水→陆后山格必须恢复不可通行（决策 ①/⑦）");
    }

    [Test]
    public void Commit_ClearLandForm_Mountain_PermanentRemoval()
    {
        _service.BeginTransaction();
        _service.Apply(_mountainCell, new HexCellPatch { ClearLandForm = true });
        MapCommitResult result = _service.Commit();

        Assert.IsNull(_mountainCell.landForm, "清除地貌");
        Assert.IsTrue(_mountainCell.mountainCleared, "决策 ㉕：永久清除标记");
        Assert.IsNull(_mountainCell.mountainRidge, "固化参数快照清除");
        Assert.AreEqual(Enums.MountainRidgeStatus.None, _mountainCell.mountainRidgeStatus);
        Assert.AreEqual(1f, _mountainCell.movementCost, "清除后恢复可通行");
        Assert.IsFalse(MountainCellRule.IsMountainCell(_mountainCell), "清除后不再是山格");
    }

    [Test]
    public void Commit_ClearLandForm_Mountain_MarksTerrainDirtyFlag()
    {
        _service.BeginTransaction();
        _service.Apply(_mountainCell, new HexCellPatch { ClearLandForm = true });
        MapCommitResult result = _service.Commit();

        Assert.IsTrue(result.DirtyFlags.HasFlag(MapDirtyFlags.Objects), "地貌对象脏位");
        Assert.IsTrue(result.DirtyFlags.HasFlag(MapDirtyFlags.Terrain), "山体几何清除必须补 Terrain 脏位（源码审计修正 A-5）");
    }

    [Test]
    public void Commit_ClearLandForm_NonMountain_NoTerrainDirtyFlag()
    {
        _mountainCell.landForm = null;
        _mountainCell.movementCost = 1f;

        _service.BeginTransaction();
        _service.Apply(_mountainCell, new HexCellPatch { ClearLandForm = true });
        MapCommitResult result = _service.Commit();

        Assert.IsTrue(result.DirtyFlags.HasFlag(MapDirtyFlags.Objects));
        Assert.IsFalse(result.DirtyFlags.HasFlag(MapDirtyFlags.Terrain), "普通格清除地貌不应补 Terrain 脏位");
    }
}
