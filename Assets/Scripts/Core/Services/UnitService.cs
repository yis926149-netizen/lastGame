// DataProvider/UnitService.cs

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

public class UnitService : IUnitService
{
    [Inject] private IUnitRepository _unitRepository;  // 注入仓库
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

    // 以下方法与敌方势力范围、城市计数相关，仍使用 EnemyModelManager
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