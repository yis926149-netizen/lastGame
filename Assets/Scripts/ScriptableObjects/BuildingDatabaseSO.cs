using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildingDatabase", menuName = "Game/BuildingDatabase")]
public class BuildingDatabaseSO : ScriptableObject
{
    [Header("建筑配置对象（对象化改造后唯一数据源）")]
    public List<BuildingConfigSO> buildings = new();

    [Header("主城模型")]
    public GameObject cityModel;

    [Header("敌方（AI）主城模型；留空则回退使用 cityModel")]
    public GameObject enemyCityModel;
}
