using System.Collections.Generic;
using UnityEngine;

public interface IUnitService
{
    // 获取所有玩家单位数据
    List<CharacterData> GetAllPlayerUnits();

    // 获取所有敌方单位数据（按AI编号分组或扁平化列表）
    List<CharacterData> GetAllEnemyUnits();

    // 添加一个敌方单位到指定AI
    void AddEnemyUnit(int aiIndex, GameObject unit, CharacterData data);

    // 移除一个敌方单位（当单位死亡时）
    void RemoveEnemyUnit(GameObject unit);

    // 获取指定AI的城市数量
    int GetAICityCount(int aiIndex);

    // 增加AI的城市数量（当AI建立城市时）
    void IncrementAICityCount(int aiIndex);

    // 获取AI的势力范围字典（如果需要）
    Dictionary<Vector3, HexCellData> GetAISphereOfInfluence(int aiIndex);

    // 向指定AI的势力范围添加地块
    void AddToAISphereOfInfluence(int aiIndex, Vector3 hexCoord, HexCellData cell);
}