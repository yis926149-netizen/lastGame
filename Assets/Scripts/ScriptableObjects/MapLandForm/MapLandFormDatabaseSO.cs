using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地图地貌数据库容器。
/// 【地图地貌配置化】对齐 MapResourceDatabaseSO 模式：所有地貌定义 + 全局生成权重。
/// </summary>
[CreateAssetMenu(fileName = "MapLandFormDatabase", menuName = "Game Data/Map Land Forms/Map Land Form Database")]
public class MapLandFormDatabaseSO : ScriptableObject
{
    [Tooltip("所有地图地貌定义")]
    public List<MapLandFormSO> landForms = new();

    [Tooltip("不生成地貌的权重；为保持现状配置 10")]
    [Min(0)]
    public int emptySpawnWeight = 10;
}
