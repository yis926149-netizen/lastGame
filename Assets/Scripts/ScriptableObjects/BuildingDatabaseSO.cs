using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildingDatabase", menuName = "Game/BuildingDatabase")]
public class BuildingDatabaseSO : ScriptableObject
{
    [Header("建筑配置对象（对象化改造后唯一数据源）")]
    public List<BuildingConfigSO> buildings = new();

    [Header("主城模型")]
    public GameObject cityModel;
}
