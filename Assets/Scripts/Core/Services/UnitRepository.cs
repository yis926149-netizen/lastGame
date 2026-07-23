using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UnitRepository : IUnitRepository
{
    // ��ҵ�λ
    private readonly Dictionary<GameObject, CharacterData> _playerUnits = new();
    public IReadOnlyDictionary<GameObject, CharacterData> AllPlayerUnits => _playerUnits;

    // �з���λ����AI���飩
    private readonly List<Dictionary<GameObject, CharacterData>> _enemyUnitGroups = new();
    public IReadOnlyList<IReadOnlyDictionary<GameObject, CharacterData>> AllEnemyUnitGroups =>
        _enemyUnitGroups.Select(g => (IReadOnlyDictionary<GameObject, CharacterData>)g).ToList();

    // �¼�ʵ��
    public event Action<GameObject, CharacterData> OnPlayerUnitAdded;
    public event Action<GameObject> OnPlayerUnitRemoved;
    public event Action<int, GameObject, CharacterData> OnEnemyUnitAdded;
    public event Action<GameObject> OnEnemyUnitRemoved;

    // ��ҵ�λ����
    public CharacterData GetPlayerUnit(GameObject unitObject) =>
        _playerUnits.TryGetValue(unitObject, out var data) ? data : null;

    public bool TryGetPlayerUnit(GameObject unitObject, out CharacterData data) =>
        _playerUnits.TryGetValue(unitObject, out data);

    public void AddPlayerUnit(GameObject unitObject, CharacterData data)
    {
        if (unitObject == null || data == null) return;
        RemoveEnemyUnit(unitObject);
        if (_playerUnits.TryGetValue(unitObject, out var existing) && existing == data) return;
        _playerUnits[unitObject] = data;
        OnPlayerUnitAdded?.Invoke(unitObject, data);
    }

    public void RemovePlayerUnit(GameObject unitObject)
    {
        if (_playerUnits.Remove(unitObject))
            OnPlayerUnitRemoved?.Invoke(unitObject);
    }

    // �з���λ����
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
        if (aiIndex < 0 || unitObject == null || data == null) return;
        EnsureGroupCapacity(aiIndex + 1);
        if (_enemyUnitGroups[aiIndex].TryGetValue(unitObject, out var existing) && existing == data && !_playerUnits.ContainsKey(unitObject)) return;
        RemovePlayerUnit(unitObject);
        RemoveEnemyUnit(unitObject);
        _enemyUnitGroups[aiIndex][unitObject] = data;
        OnEnemyUnitAdded?.Invoke(aiIndex, unitObject, data);
    }

    public void RemoveEnemyUnit(GameObject unitObject)
    {
        bool removed = false;
        for (int i = 0; i < _enemyUnitGroups.Count; i++)
        {
            if (_enemyUnitGroups[i].Remove(unitObject))
            {
                removed = true;
            }
        }
        if (removed) OnEnemyUnitRemoved?.Invoke(unitObject);
    }

    private void EnsureGroupCapacity(int requiredCount)
    {
        while (_enemyUnitGroups.Count < requiredCount)
            _enemyUnitGroups.Add(new Dictionary<GameObject, CharacterData>());
    }
}
