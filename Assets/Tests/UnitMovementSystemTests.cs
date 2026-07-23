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
    private GameObject _unitObject;

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        _mockMapData = Substitute.For<IMapDataService>();
        _mockMapEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();

        _container.Bind<IMapDataService>().FromInstance(_mockMapData);
        _container.Bind<MapVisualEventSO>().FromInstance(_mockMapEvent);
        _container.Bind<UnitMovementSystem>().AsSingle();

        _system = _container.Resolve<UnitMovementSystem>();
        SetupMockMap();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mockMapEvent);
        if (_unitObject != null)
        {
            Object.DestroyImmediate(_unitObject);
        }
    }

    private void SetupMockMap()
    {
        // �����򵥵ĵ�ͼ����
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
        _unitObject = new GameObject();
        unit.gameObject.Returns(_unitObject);

        var targetHex = new Vector3(2, -3, 1); // NE ����

        bool result = _system.RequestMove(unit, targetHex, Enums.MovementPurpose.MoveToDestination);

        Assert.IsTrue(result);
        // ��֤��λ���ڸ��ӱ���գ��� IUnitMovement ������Ӧ�ɵ����ߴ�����
        // ��֤�ƶ��б����иõ�λ
        // �˴�ͨ���������ڲ��б��ϸ��ӣ��ɸ�Ϊ��֤ UnitMovementSystem ����Ϊ������ Tick ��λλ��Ӧ�仯
    }

    [Test]
    public void CancelMove_RemovesPendingMovementWithoutFinishingCallback()
    {
        var unit = Substitute.For<IUnitMovement>();
        unit.RemainingMovement.Returns(5f);
        unit.CurrentHexCoordinate.Returns(new Vector3(2, -2, 0));
        _unitObject = new GameObject();
        unit.gameObject.Returns(_unitObject);

        bool requested = _system.RequestMove(
            unit,
            new Vector3(2, -3, 1),
            Enums.MovementPurpose.MoveToDestination);

        _system.CancelMove(unit);
        _system.Tick();

        Assert.IsTrue(requested);
        unit.DidNotReceive().OnMoveFinished();
    }

    [Test]
    public void CancelMove_RestoresStartOccupancyAndMovementPoints()
    {
        var unit = Substitute.For<IUnitMovement>();
        unit.RemainingMovement.Returns(5f);
        Vector3 start = new Vector3(2, -2, 0);
        unit.CurrentHexCoordinate.Returns(start);
        _unitObject = new GameObject();
        unit.gameObject.Returns(_unitObject);
        HexCellData startCell = _mockMapData.GetCell(start);
        startCell.SetHaveUnit(true, _unitObject);

        Assert.IsTrue(_system.RequestMove(unit, new Vector3(2, -3, 1), Enums.MovementPurpose.MoveToDestination));
        _system.CancelMove(unit);

        Assert.IsTrue(startCell.IsHaveUnit());
        Assert.AreSame(_unitObject, startCell.GetUnit());
        unit.Received().RemainingMovement = 5f;
    }

    [Test]
    public void RequestMove_OccupiedDestination_ReturnsFalse()
    {
        var unit = Substitute.For<IUnitMovement>();
        unit.RemainingMovement.Returns(5f);
        unit.CurrentHexCoordinate.Returns(new Vector3(2, -2, 0));
        _unitObject = new GameObject();
        unit.gameObject.Returns(_unitObject);
        GameObject occupant = new GameObject("Occupant");
        Vector3 target = new Vector3(2, -3, 1);
        _mockMapData.GetCell(target).SetHaveUnit(true, occupant);

        try
        {
            Assert.IsFalse(_system.RequestMove(unit, target, Enums.MovementPurpose.MoveToDestination));
        }
        finally
        {
            Object.DestroyImmediate(occupant);
        }
    }

    [Test]
    public void Pathfinding_MaxValueCell_IsImpassableForNormalMovement()
    {
        Vector3 start = new Vector3(2, -2, 0);
        Vector3 target = new Vector3(2, -3, 1);
        _mockMapData.GetCell(target).movementCost = float.MaxValue;

        bool success = _system.CalculateMinMovementCostBetweenTwoHexes(
            new List<Vector3>(_mockMapData.GetAllHexCoordinates()),
            start,
            target,
            Enums.MovementPurpose.MoveToDestination,
            out _,
            out _);

        Assert.IsFalse(success);
    }

    [Test]
    public void CalculateMinMovementCostBetweenTwoHexes_ReturnsCorrectCost()
    {
        var allPoints = new List<Vector3>(_mockMapData.GetAllHexCoordinates());
        var start = new Vector3(0, 0, 0);
        var end = new Vector3(4, -4, 0); // �Խ��߾���4��

        bool success = _system.CalculateMinMovementCostBetweenTwoHexes(
            allPoints, start, end, Enums.MovementPurpose.MoveToDestination,
            out float cost, out List<Vector3> path);

        Assert.IsTrue(success);
        Assert.AreEqual(4f, cost, 0.01f);
        Assert.IsNotNull(path);
        Assert.AreEqual(4, path.Count); // ÿһ��һ������
    }

    [Test]
    public void GetAllReachableHexesFromStartHex_ReturnsWithinMovementRange()
    {
        var allPoints = new List<Vector3>(_mockMapData.GetAllHexCoordinates());
        var start = new Vector3(2, -2, 0);
        float movement = 2.5f; // ����2����ÿ��1�ѣ�

        var reachable = _system.GetAllReachableHexesFromStartHex(allPoints, start, movement);

        // Ӧ������㱾���;����2�ĸ���
        Assert.Contains(start, reachable);
        // ������Ϊ2�ĸ��ӣ����� (2, -3, 1)
        Assert.Contains(new Vector3(2, -3, 1), reachable);
        Assert.IsFalse(reachable.Contains(new Vector3(2, -5, 3))); // ����3
    }
}
