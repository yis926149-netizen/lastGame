using UnityEngine;

//****************************************
//功能说明：建筑卡配置对象。建筑类型直接存储枚举，消除 (buildingId + 1) 映射魔法。
//****************************************
[CreateAssetMenu(fileName = "BuildingConfig", menuName = "Game Data/Normal Cards/Building Config")]
public class BuildingConfigSO : NormalCardConfigSO
{
    [Tooltip("运行时建筑 ID（旧数据库索引，必须唯一、非负）")]
    public int buildingId;

    [Tooltip("建筑类型（直接存储枚举，不再由 ID 推导）")]
    public Enums.BulidingType buildingType;

    [Tooltip("建筑模型预制体（玩家侧）")]
    public GameObject buildingModel;

    [Tooltip("敌方（AI）专用建筑模型预制体；留空则回退使用 buildingModel")]
    public GameObject enemyBuildingModel;

    [Tooltip("基础血量")]
    public float baseHP;

    [Tooltip("不可通行（替代 buildingId == 0 || == 1 的魔法判定）")]
    public bool blocksMovement;

    [Tooltip("兵营产出单位（兵营专用，动态 AddComponent 的 BarracksSpawner 由此初始化）")]
    public UnitConfigSO producedUnit;
}
