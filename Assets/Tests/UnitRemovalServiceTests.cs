using NSubstitute;
using NUnit.Framework;
using UnityEngine;

public class UnitRemovalServiceTests
{
    private IMapDataService _mapDataService;
    private UnitRepository _unitRepository;
    private UnitMovementSystem _movementSystem;
    private UnitRemovalService _service;
    private MapVisualEventSO _mapVisualEvent;
    private GameObject _unit;
    private HexCellData _cell;

    [SetUp]
    public void SetUp()
    {
        _mapDataService = Substitute.For<IMapDataService>();
        _unitRepository = new UnitRepository();
        _mapVisualEvent = ScriptableObject.CreateInstance<MapVisualEventSO>();
        _movementSystem = new UnitMovementSystem(_mapDataService, _mapVisualEvent);
        _service = new UnitRemovalService(_mapDataService, _unitRepository, _movementSystem);

        _unit = new GameObject("Unit");
        _cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);
        _cell.SetHaveUnit(true, _unit);
        _mapDataService.GetCellByWorldPosition(_unit.transform.position).Returns(_cell);
        _mapDataService.GetAllCells().Returns(new System.Collections.Generic.List<HexCellData> { _cell });
    }

    [TearDown]
    public void TearDown()
    {
        if (_unit != null)
        {
            Object.DestroyImmediate(_unit);
        }

        Object.DestroyImmediate(_mapVisualEvent);
    }

    [Test]
    public void RemoveUnit_PlayerUnit_ClearsCellRepositoryAndSceneObjectOnce()
    {
        CharacterData data = CreateCharacterData();
        int removedCount = 0;
        _unitRepository.OnPlayerUnitRemoved += _ => removedCount++;
        _unitRepository.AddPlayerUnit(_unit, data);

        bool firstRemoval = _service.RemoveUnit(_unit);
        bool secondRemoval = _service.RemoveUnit(_unit);

        Assert.IsTrue(firstRemoval);
        Assert.IsFalse(secondRemoval);
        Assert.IsFalse(_cell.IsHaveUnit());
        Assert.IsNull(_cell.GetUnit());
        Assert.IsFalse(_unitRepository.AllPlayerUnits.ContainsKey(_unit));
        Assert.IsFalse(_unit.activeSelf);
        Assert.AreEqual(1, removedCount);
    }

    [Test]
    public void RemoveUnit_EnemyUnit_ClearsCellAndEnemyRepository()
    {
        CharacterData data = CreateCharacterData();
        _unitRepository.AddEnemyUnit(2, _unit, data);

        _service.RemoveUnit(_unit);

        Assert.IsFalse(_cell.IsHaveUnit());
        Assert.IsFalse(_unitRepository.TryGetEnemyUnit(_unit, out _));
        Assert.IsFalse(_unit.activeSelf);
    }

    [Test]
    public void RemoveUnit_CellOccupiedByAnotherUnit_DoesNotClearCell()
    {
        GameObject otherUnit = new GameObject("OtherUnit");
        _cell.SetHaveUnit(true, otherUnit);

        try
        {
            _service.RemoveUnit(_unit);

            Assert.IsTrue(_cell.IsHaveUnit());
            Assert.AreSame(otherUnit, _cell.GetUnit());
        }
        finally
        {
            Object.DestroyImmediate(otherUnit);
        }
    }

    [Test]
    public void RemoveUnit_UnitAwayFromOccupiedCell_FindsCellByOccupantReference()
    {
        HexCellData transientCell = new HexCellData(
            Enums.HexType.NoRiver,
            1,
            Vector3.one,
            Vector3.one,
            1f);
        _mapDataService.GetCellByWorldPosition(_unit.transform.position).Returns(transientCell);

        _service.RemoveUnit(_unit);

        Assert.IsFalse(_cell.IsHaveUnit());
        Assert.IsNull(_cell.GetUnit());
    }

    private CharacterData CreateCharacterData()
    {
        var unitData = new UnitData(0, "Test Unit", 5f, 100, 1, 10, 3f, 2f);
        return new CharacterData(0, _unit, null, unitData);
    }
}
