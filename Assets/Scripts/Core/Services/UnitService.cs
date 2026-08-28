// DataProvider/UnitService.cs

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

public class UnitService : IUnitService
{
    [Inject] private IUnitRepository _unitRepository;  // ע��ֿ�
    [Inject] private EnemyModelManager _enemyModelManager;

    public List<CharacterData> GetAllPlayerUnits()
    {
        return _unitRepository.AllPlayerUnits.Values.ToList();
    }

    public List<CharacterData> GetAllEnemyUnits()
    {
        var result = new List<CharacterData>();
        foreach (var group in _unitRepository.AllEnemyUnitGroups)
        {
            result.AddRange(group.Values);
        }
        return result;
    }

    public void AddEnemyUnit(int aiIndex, GameObject unit, CharacterData data)
    {
        _unitRepository.AddEnemyUnit(aiIndex, unit, data);
    }

    public void RemoveEnemyUnit(GameObject unit)
    {
        _unitRepository.RemoveEnemyUnit(unit);
    }

    // ���·�����з�������Χ�����м�����أ���ʹ�� EnemyModelManager
    public int GetAICityCount(int aiIndex) =>
        _enemyModelManager.CityCount.ContainsKey(aiIndex) ? _enemyModelManager.CityCount[aiIndex] : 0;

    public void IncrementAICityCount(int aiIndex)
    {
        if (!_enemyModelManager.CityCount.ContainsKey(aiIndex))
            _enemyModelManager.CityCount[aiIndex] = 0;
        _enemyModelManager.CityCount[aiIndex]++;
    }

    public Dictionary<Vector3, HexCellData> GetAISphereOfInfluence(int aiIndex)
    {
        if (!_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.ContainsKey(aiIndex))
            _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[aiIndex] = new Dictionary<Vector3, HexCellData>();
        return _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[aiIndex];
    }

    public void AddToAISphereOfInfluence(int aiIndex, Vector3 hexCoord, HexCellData cell)
    {
        if (!_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.ContainsKey(aiIndex))
            _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[aiIndex] = new Dictionary<Vector3, HexCellData>();
        var dict = _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[aiIndex];
        if (!dict.ContainsKey(hexCoord))
            dict[hexCoord] = cell;
    }
}

public class UnitRemovalService
{
    private readonly IMapDataService _mapDataService;
    private readonly IUnitRepository _unitRepository;
    private readonly UnitMovementSystem _movementSystem;
    private readonly MapVisualEventSO _mapVisualEvent;
    private readonly HashSet<GameObject> _removedUnits = new HashSet<GameObject>();

    public UnitRemovalService(
        IMapDataService mapDataService,
        IUnitRepository unitRepository,
        UnitMovementSystem movementSystem,
        MapVisualEventSO mapVisualEvent = null)
    {
        _mapDataService = mapDataService;
        _unitRepository = unitRepository;
        _movementSystem = movementSystem;
        _mapVisualEvent = mapVisualEvent;
    }

    public bool RemoveUnit(GameObject unit)
    {
        if (!DeactivateUnit(unit)) return false;

        DestroyDeactivatedUnit(unit);

        // 【单位擦除层-方案A】单位销毁后刷新雾化遮罩的渲染器列表，
        // 避免 CommandBuffer 残留已销毁单位（与建筑销毁路径一致，UnitRemovalService 原无 Raise）。
        _mapVisualEvent?.Raise();

        return true;
    }

    public bool DeactivateUnit(GameObject unit)
    {
        if (unit == null || !_removedUnits.Add(unit)) return false;

        UnitMovementController controller = unit.GetComponent<UnitMovementController>();
        if (controller != null)
        {
            _movementSystem.ReleaseReservationByUnit(unit);
        }

        // 【多单位落点】按站位槽枚举查找并释放单位的站位（含旧字段同步）。
        HexCellData occupiedCell = _mapDataService.GetCellByWorldPosition(unit.transform.position);
        if (occupiedCell == null || !occupiedCell.GetStandingUnits().Contains(unit))
        {
            occupiedCell = _mapDataService.GetAllCells()?.FirstOrDefault(cell => cell.GetStandingUnits().Contains(unit));
        }

        occupiedCell?.ReleaseStandingUnit(unit);

        if (controller != null)
        {
            controller.PrepareForRemoval();
        }

        if (_unitRepository.TryGetPlayerUnit(unit, out _))
        {
            _unitRepository.RemovePlayerUnit(unit);
        }
        if (_unitRepository.TryGetEnemyUnit(unit, out _))
        {
            _unitRepository.RemoveEnemyUnit(unit);
        }

        return true;
    }

    public void DestroyDeactivatedUnit(GameObject unit)
    {
        if (unit == null) return;
        unit.SetActive(false);

        if (Application.isPlaying)
            Object.Destroy(unit);
        else
            Object.DestroyImmediate(unit);
    }
}
