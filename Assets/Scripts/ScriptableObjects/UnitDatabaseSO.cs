using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitDatabase", menuName = "Game/UnitDatabase")]
public class UnitDatabaseSO : ScriptableObject
{
    [Header("单位配置对象（对象化改造后唯一数据源）")]
    public List<UnitConfigSO> units = new();
}
