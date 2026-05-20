using System;
using System.Collections.Generic;
using UnityEngine;

public interface IUnitRepository
{
    // ===== 玩家单位 =====
    IReadOnlyDictionary<GameObject, CharacterData> AllPlayerUnits { get; }
    CharacterData GetPlayerUnit(GameObject unitObject);
    bool TryGetPlayerUnit(GameObject unitObject, out CharacterData data);
    void AddPlayerUnit(GameObject unitObject, CharacterData data);
    void RemovePlayerUnit(GameObject unitObject);

    // ===== 敌方单位（按AI索引分组） =====
    IReadOnlyList<IReadOnlyDictionary<GameObject, CharacterData>> AllEnemyUnitGroups { get; }
    IReadOnlyDictionary<GameObject, CharacterData> GetEnemyUnitGroup(int aiIndex);
    CharacterData GetEnemyUnit(GameObject unitObject);
    bool TryGetEnemyUnit(GameObject unitObject, out CharacterData data);
    void AddEnemyUnit(int aiIndex, GameObject unitObject, CharacterData data);
    void RemoveEnemyUnit(GameObject unitObject);

    // 事件（可选，便于UI等系统响应数据变化）
    event Action<GameObject, CharacterData> OnPlayerUnitAdded;
    event Action<GameObject> OnPlayerUnitRemoved;
    event Action<int, GameObject, CharacterData> OnEnemyUnitAdded;
    event Action<GameObject> OnEnemyUnitRemoved;
}