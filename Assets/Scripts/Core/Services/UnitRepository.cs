using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UnitRepository : IUnitRepository
{
    // 玩家单位
    private readonly Dictionary<GameObject, CharacterData> _playerUnits = new();
    public IReadOnlyDictionary<GameObject, CharacterData> AllPlayerUnits => _playerUnits;

    // 敌方单位（按AI分组）
    private readonly List<Dictionary<GameObject, CharacterData>> _enemyUnitGroups = new();
    public IReadOnlyList<IReadOnlyDictionary<GameObject, CharacterData>> AllEnemyUnitGroups =>
        _enemyUnitGroups.Select(g => (IReadOnlyDictionary<GameObject, CharacterData>)g).ToList();

    // 事件实现
    public event Action<GameObject, CharacterData> OnPlayerUnitAdded;
    public event Action<GameObject> OnPlayerUnitRemoved;
    public event Action<int, GameObject, CharacterData> OnEnemyUnitAdded;
    public event Action<GameObject> OnEnemyUnitRemoved;

    // 玩家单位操作
    public CharacterData GetPlayerUnit(GameObject unitObject) =>
        _playerUnits.TryGetValue(unitObject, out var data) ? data : null;

    public bool TryGetPlayerUnit(GameObject unitObject, out CharacterData data) =>
        _playerUnits.TryGetValue(unitObject, out data);

    public void AddPlayerUnit(GameObject unitObject, CharacterData data)
    {
        _playerUnits[unitObject] = data;
        OnPlayerUnitAdded?.Invoke(unitObject, data);
    }

    public void RemovePlayerUnit(GameObject unitObject)
    {
        if (_playerUnits.Remove(unitObject))
            OnPlayerUnitRemoved?.Invoke(unitObject);
    }

    // 敌方单位操作
    public IReadOnlyDictionary<GameObject, CharacterData> GetEnemyUnitGroup(int aiIndex)
    {
        EnsureGroupCapacity(aiIndex + 1);
        return _enemyUnitGroups[aiIndex];
    }

    public CharacterData GetEnemyUnit(GameObject unitObject)
    {
        foreach (var group in _enemyUnitGroups)
            if (group.TryGetValue(unitObject, out var data))
                return data;
        return null;
    }

    public bool TryGetEnemyUnit(GameObject unitObject, out CharacterData data)
    {
        data = null;
        foreach (var group in _enemyUnitGroups)
            if (group.TryGetValue(unitObject, out data))
                return true;
        return false;
    }

    public void AddEnemyUnit(int aiIndex, GameObject unitObject, CharacterData data)
    {
        EnsureGroupCapacity(aiIndex + 1);
        _enemyUnitGroups[aiIndex][unitObject] = data;
        OnEnemyUnitAdded?.Invoke(aiIndex, unitObject, data);
    }

    public void RemoveEnemyUnit(GameObject unitObject)
    {
        for (int i = 0; i < _enemyUnitGroups.Count; i++)
        {
            if (_enemyUnitGroups[i].Remove(unitObject))
            {
                OnEnemyUnitRemoved?.Invoke(unitObject);
                return;
            }
        }
    }

    private void EnsureGroupCapacity(int requiredCount)
    {
        while (_enemyUnitGroups.Count < requiredCount)
            _enemyUnitGroups.Add(new Dictionary<GameObject, CharacterData>());
    }
}