using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地图资源数据库容器。
/// 【地图资源配置化】对齐 TacticalCardDatabaseSO 模式：所有资源定义 + 全局生成/奖励参数。
/// </summary>
[CreateAssetMenu(fileName = "MapResourceDatabase", menuName = "Game Data/Map Resources/Map Resource Database")]
public class MapResourceDatabaseSO : ScriptableObject
{
    [Tooltip("所有地图资源定义")]
    public List<MapResourceSO> resources = new();

    [Tooltip("不生成资源的权重；现状应配置为 14")]
    [Min(0)]
    public int emptySpawnWeight = 14;

    [Tooltip("探索任意地块的基础金币奖励")]
    [Min(0)]
    public int baseExplorationGold = 5;
}
