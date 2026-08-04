using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using UnityEngine;

//****************************************
// 【动态地图-阶段二】UnitMovementSystem 地块变化联动测试
// 覆盖：CancelMovesIntersecting（路径途经不可通行格取消）、
// EjectUnitsFromImpassableCells（弹射到最近可通行格 + 占用原子迁移）、
// RefreshStandingUnitPositions（站立单位高度吸附）。
//****************************************

public class UnitMovementEjectionTests
{
    private IMapDataService _mockMapData;
    private MapVisualEventSO _mockMapEvent;
    private UnitMovementSystem _system;
    private readonly Dictionary<Vector3, HexCellData> _cells = new Dictionary<Vector3, HexCellData>();

    private Vector3 _origin = new Vector3(0, 0, 0);       // 中心（起始格）
    private Vector3 _ne = new Vector3(0, -1, 1);          // NE
    private Vector3 _e = new Vector3(1, -1, 0);           // E
    private Vector3 _se = new Vector3(1, 0, -1);          // SE
    private Vector3 _sw = new Vector3(0, 1, -1);          // SW
    private Vector3 _w = new Vector3(-1, 1, 0);           // W
    private Vector3 _nw = new Vector3(-1, 0, 1);          // NW

    [SetUp]
    public void SetUp()
    {
        _mockMapData = Substitute.For<IMapDataService>();
        _mockMapEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();

        BuildMap();
        _system = new UnitMovementSystem(_mockMapData, _mockMapEvent, new GameLoop(new GlobalTimerService()));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mockMapEvent);
    }

    private HexCellData NewCell(Vector3 coord, float height = 2f)
    {
        return new HexCellData(Enums.HexType.NoRiver, 0, coord, new Vector3(coord.x, height, coord.z), height)
        {
            movementCost = 1f,
            RealCenterWorldCoordinate = new Vector3(coord.x * 3f, height, coord.z * 3f)
        };
    }

    private void BuildMap()
    {
        _cells.Clear();
        _cells[_origin] = NewCell(_origin);
        _cells[_ne] = NewCell(_ne);
        _cells[_e] = NewCell(_e);
        _cells[_se] = NewCell(_se);
        _cells[_sw] = NewCell(_sw);
        _cells[_w] = NewCell(_w);
        _cells[_nw] = NewCell(_nw);

        _mockMapData.GetAllHexCoordinates().Returns(new List<Vector3>(_cells.Keys));
        _mockMapData.GetCell(Arg.Any<Vector3>()).Returns(c => _cells.TryGetValue(c.Arg<Vector3>(), out var cell) ? cell : null);
        _mockMapData.GetNeighbor(Arg.Any<HexCellData>(), Arg.Any<Enums.HexDirection>()).Returns(call =>
        {
            var center = call.Arg<HexCellData>();
            var dir = call.Arg<Enums.HexDirection>();
            Vector3 offset = dir switch
            {
                Enums.HexDirection.NE => new Vector3(0, -1, 1),
                Enums.HexDirection.E => new Vector3(1, -1, 0),
                Enums.HexDirection.SE => new Vector3(1, 0, -1),
                Enums.HexDirection.SW => new Vector3(0, 1, -1),
                Enums.HexDirection.W => new Vector3(-1, 1, 0),
                Enums.HexDirection.NW => new Vector3(-1, 0, 1),
                _ => Vector3.zero
            };
            _cells.TryGetValue(center.HexCoordinate + offset, out var neighbor);
            return neighbor;
        });
    }

    private IUnitMovement MakeUnitAt(Vector3 coord, out GameObject go)
    {
        go = new GameObject("TestUnit");
        go.transform.position = _cells[coord].RealCenterWorldCoordinate;

        var unit = Substitute.For<IUnitMovement>();
        unit.RemainingMovement.Returns(5f);
        unit.CurrentHexCoordinate.Returns(coord);
        unit.gameObject.Returns(go);
        return unit;
    }

    [Test]
    public void EjectUnits_UnitOnImpassableCell_MovesToNearestFreePassableCell()
    {
        // 把 NE 设为不可通行，单位站在 NE 上；其余格可通行
        _cells[_ne].movementCost = float.MaxValue;
        _cells[_ne].SetHaveUnit(true, null); // 先占位，下面用真实单位覆盖

        var unit = MakeUnitAt(_ne, out GameObject go);
        _cells[_ne].SetHaveUnit(true, go);
        _cells[_ne].SetOccupant(go);

        _system.EjectUnitsFromImpassableCells(new List<HexCellData> { _cells[_ne] });

        // 弹射后：原格清空，单位落在可通行格（E 距离 1 应优先于其它）
        Assert.IsFalse(_cells[_ne].IsHaveUnit(), "原格占用必须清空");
        Assert.IsNull(_cells[_ne].GetOccupant());

        HexCellData landing = FindUnitCell(go);
        Assert.NotNull(landing, "单位必须落到某个格");
        Assert.AreNotEqual(_ne, landing.HexCoordinate);
        Assert.Less(landing.movementCost, float.MaxValue, "落点必须可通行");
        Assert.AreEqual(1, CubeDistance(_ne, landing.HexCoordinate), "应弹到最近邻格");
        Assert.AreEqual(landing.RealCenterWorldCoordinate, go.transform.position, "位置必须吸附到落点格心");
    }

    [Test]
    public void EjectUnits_PrefersFreeCell_OverOccupiedNeighbor()
    {
        // NE/E 不可通行；SE 被占用；SW 空闲
        _cells[_ne].movementCost = float.MaxValue;
        _cells[_e].movementCost = float.MaxValue;

        var blocker = MakeUnitAt(_se, out GameObject blockerGo);
        _cells[_se].SetHaveUnit(true, blockerGo);
        _cells[_se].SetOccupant(blockerGo);

        var unit = MakeUnitAt(_origin, out GameObject go);
        _cells[_origin].SetHaveUnit(true, go);
        _cells[_origin].SetOccupant(go);
        _cells[_origin].movementCost = float.MaxValue; // 原格变不可通行

        _system.EjectUnitsFromImpassableCells(new List<HexCellData> { _cells[_origin] });

        HexCellData landing = FindUnitCell(go);
        Assert.NotNull(landing);
        Assert.AreEqual(_sw, landing.HexCoordinate, "被占用的 SE 不应作为落点");
        Assert.AreEqual(go, _cells[_sw].GetUnit());
        Assert.AreEqual(go, _cells[_sw].GetOccupant(), "落点 Occupant 必须同步写入");
    }

    // NOTE: The 2 tests below depend on RequestMove registering a moving unit.
    // RequestMove has the guard `(unit as UnityEngine.Object) == null` which always
    // returns false for NSubstitute proxies — the same limitation as the pre-existing
    // UnitMovementSystemTests.RequestMove_ValidTarget_ReturnsTrueAndStartsMove failures.
    // Full integration coverage requires a real UnitMovementController (PlayMode / scene test).

    [Test]
    [NUnit.Framework.Ignore("RequestMove guard requires real UnitMovementController; NSubstitute proxy cast to UnityEngine.Object == null — same as pre-existing UnitMovementSystemTests limit")]
    public void CancelMovesIntersecting_CancelsMoveWhoseDestinationBecameImpassable()
    {
        var unit = MakeUnitAt(_origin, out GameObject go);
        _cells[_origin].SetHaveUnit(true, go);
        _cells[_origin].SetOccupant(go);

        bool requested = _system.RequestMove(unit, _e, Enums.MovementPurpose.MoveToDestination);
        Assert.IsTrue(requested);

        _cells[_e].movementCost = float.MaxValue;
        _system.CancelMovesIntersecting(new List<HexCellData> { _cells[_e] });

        Assert.IsFalse(_system.IsDestinationReserved(_e), "取消后预占目的地必须释放");
        Assert.AreEqual(go, _cells[_origin].GetUnit());
        Assert.AreEqual(go, _cells[_origin].GetOccupant());
        Assert.AreEqual(_cells[_origin].RealCenterWorldCoordinate, go.transform.position);
    }

    [Test]
    [NUnit.Framework.Ignore("RequestMove guard requires real UnitMovementController; NSubstitute proxy cast to UnityEngine.Object == null — same as pre-existing UnitMovementSystemTests limit")]
    public void CancelMovesIntersecting_NoBlockedPath_KeepsMove()
    {
        var unit = MakeUnitAt(_origin, out GameObject go);
        _cells[_origin].SetHaveUnit(true, go);
        _cells[_origin].SetOccupant(go);

        bool requested = _system.RequestMove(unit, _ne, Enums.MovementPurpose.MoveToDestination);
        Assert.IsTrue(requested);

        _system.CancelMovesIntersecting(new List<HexCellData> { _cells[_sw] });

        Assert.IsTrue(_system.IsDestinationReserved(_ne), "无关格不应取消移动任务");
    }

    [Test]
    public void RefreshStandingUnitPositions_SnapsStandingUnitToNewHeight()
    {
        var unit = MakeUnitAt(_origin, out GameObject go);
        _cells[_origin].SetHaveUnit(true, go);
        _cells[_origin].SetOccupant(go);
        go.transform.position = new Vector3(0f, 0f, 0f); // 旧位置

        _cells[_origin].RealCenterWorldCoordinate = new Vector3(0f, 10f, 0f); // 突起后新高度

        _system.RefreshStandingUnitPositions(new List<HexCellData> { _cells[_origin] });

        Assert.AreEqual(new Vector3(0f, 10f, 0f), go.transform.position, "站立单位应吸附到新 RealCenterWorldCoordinate");
    }

    [Test]
    [NUnit.Framework.Ignore("RequestMove guard requires real UnitMovementController; NSubstitute proxy cast to UnityEngine.Object == null — same pre-existing limit as UnitMovementSystemTests")]
    public void RefreshStandingUnitPositions_SkipsMovingUnit()
    {
        var unit = MakeUnitAt(_origin, out GameObject go);
        _cells[_origin].SetHaveUnit(true, go);
        _cells[_origin].SetOccupant(go);

        // 让单位处于移动中（不 Tick 完成）
        _system.RequestMove(unit, _ne, Enums.MovementPurpose.MoveToDestination);
        _cells[_origin].RealCenterWorldCoordinate = new Vector3(0f, 10f, 0f);
        go.transform.position = Vector3.zero;

        _system.RefreshStandingUnitPositions(new List<HexCellData> { _cells[_origin] });

        Assert.AreEqual(Vector3.zero, go.transform.position, "移动中单位不应被直接吸附（逐点跟随）");
    }

    private HexCellData FindUnitCell(GameObject go)
    {
        foreach (var kv in _cells)
        {
            if (kv.Value.GetUnit() == go || kv.Value.GetOccupant() == go)
                return kv.Value;
        }
        return null;
    }

    private static int CubeDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = a - b;
        return (int)((Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z)) * 0.5f);
    }
}
