using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class UnitMovementSystemTests
{
    private DiContainer _container;
    private IMapDataService _mockMapData;
    private MapVisualEventSO _mockMapEvent;
    private UnitMovementSystem _system;

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        _mockMapData = Substitute.For<IMapDataService>();
        _mockMapEvent = Substitute.For<MapVisualEventSO>(); // ScriptableObject 可用 CreateInstance

        _container.Bind<IMapDataService>().FromInstance(_mockMapData);
        _container.Bind<MapVisualEventSO>().FromInstance(_mockMapEvent);
        _container.Bind<UnitMovementSystem>().AsSingle();

        _system = _container.Resolve<UnitMovementSystem>();
        SetupMockMap();
    }

    private void SetupMockMap()
    {
        // 创建简单的地图格子
        var cells = new Dictionary<Vector3, HexCellData>();
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                var coord = new Vector3(x, -x - y, y);
                var cell = new HexCellData(Enums.HexType.NoRiver, 0, coord, Vector3.zero, 1f)
                {
                    movementCost = 1f
                };
                cells[coord] = cell;
            }
        }
        _mockMapData.GetAllHexCoordinates().Returns(new List<Vector3>(cells.Keys));
        _mockMapData.GetCell(Arg.Any<Vector3>()).Returns(c => cells.TryGetValue(c.Arg<Vector3>(), out var cell) ? cell : null);
        _mockMapData.GetNeighbor(Arg.Any<HexCellData>(), Arg.Any<Enums.HexDirection>()).Returns(call =>
        {
            var center = call.Arg<HexCellData>();
            var dir = call.Arg<Enums.HexDirection>();
            Vector3 neighborCoord = center.HexCoordinate + (dir switch
            {
                Enums.HexDirection.NE => new Vector3(0, -1, 1),
                Enums.HexDirection.E => new Vector3(1, -1, 0),
                Enums.HexDirection.SE => new Vector3(1, 0, -1),
                Enums.HexDirection.SW => new Vector3(0, 1, -1),
                Enums.HexDirection.W => new Vector3(-1, 1, 0),
                Enums.HexDirection.NW => new Vector3(-1, 0, 1),
                _ => Vector3.zero
            });
            cells.TryGetValue(neighborCoord, out var neighbor);
            return neighbor;
        });
    }

    [Test]
    public void RequestMove_ValidTarget_ReturnsTrueAndStartsMove()
    {
        var unit = Substitute.For<IUnitMovement>();
        unit.RemainingMovement.Returns(5f);
        unit.CurrentHexCoordinate.Returns(new Vector3(2, -2, 0));
        unit.gameObject.Returns(new GameObject()); // 需要 GameObject，但不会实际使用

        var targetHex = new Vector3(2, -3, 1); // NE 方向

        bool result = _system.RequestMove(unit, targetHex, Enums.MovementPurpose.MoveToDestination);

        Assert.IsTrue(result);
        // 验证单位所在格子被清空（但 IUnitMovement 不负责，应由调用者处理）
        // 验证移动列表中有该单位
        // 此处通过反射检查内部列表较复杂，可改为验证 UnitMovementSystem 的行为：调用 Tick 后单位位置应变化
    }

    [Test]
    public void CalculateMinMovementCostBetweenTwoHexes_ReturnsCorrectCost()
    {
        var allPoints = new List<Vector3>(_mockMapData.GetAllHexCoordinates());
        var start = new Vector3(0, 0, 0);
        var end = new Vector3(4, -4, 0); // 对角线距离4步

        bool success = _system.CalculateMinMovementCostBetweenTwoHexes(
            allPoints, start, end, Enums.MovementPurpose.MoveToDestination,
            out float cost, out List<Vector3> path);

        Assert.IsTrue(success);
        Assert.AreEqual(4f, cost, 0.01f);
        Assert.IsNotNull(path);
        Assert.AreEqual(4, path.Count); // 每一步一个格子
    }

    [Test]
    public void GetAllReachableHexesFromStartHex_ReturnsWithinMovementRange()
    {
        var allPoints = new List<Vector3>(_mockMapData.GetAllHexCoordinates());
        var start = new Vector3(2, -2, 0);
        float movement = 2.5f; // 能走2步（每步1费）

        var reachable = _system.GetAllReachableHexesFromStartHex(allPoints, start, movement);

        // 应包括起点本身和距离≤2的格子
        Assert.Contains(start, reachable);
        // 检查距离为2的格子，例如 (2, -3, 1)
        Assert.Contains(new Vector3(2, -3, 1), reachable);
        Assert.IsFalse(reachable.Contains(new Vector3(2, -4, 2))); // 距离3
    }
}