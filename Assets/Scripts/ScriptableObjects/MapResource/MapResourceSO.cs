using UnityEngine;

/// <summary>
/// 地图资源（道具）单条配置。
/// 【地图资源配置化】对齐 TacticalCardSO 模式：一条资源一条 SO，显示/表现/效果/金币/权重全部在此配置。
/// </summary>
[CreateAssetMenu(fileName = "MapResource", menuName = "Game Data/Map Resources/Map Resource")]
public class MapResourceSO : ScriptableObject
{
    [Header("显示")]
    [Tooltip("稳定唯一 ID；仅用于日志、编辑器校验和未来存档，不作为本局运行时查找键")]
    public string resourceId;

    [Tooltip("名称")]
    public string resourceName;

    [Tooltip("描述")]
    [TextArea(2, 4)]
    public string description;

    [Header("地图表现")]
    [Tooltip("地图模型预制体；留空 = 不生成模型")]
    public GameObject modelPrefab;

    [Tooltip("拾取特效预制体；留空 = 无特效")]
    public GameObject reapEffectPrefab;

    [Tooltip("拾取音效名；留空 = 无音效")]
    public string pickupSfxName;

    [Header("拾取效果（单位踩格）")]
    [Tooltip("拾取效果类型")]
    public ResourcePickupEffectType pickupEffectType;

    [Tooltip("拾取效果参数（按 pickupEffectType 取用）")]
    public ResourcePickupEffect pickupEffect;

    [Header("探索收割")]
    [Tooltip("探索收割金币加成（最终 = 数据库基础值 + 本值）")]
    public int explorationGoldBonus;

    [Header("生成")]
    [Tooltip("生成权重；0 = 本资源不生成")]
    public int spawnWeight = 1;
}
