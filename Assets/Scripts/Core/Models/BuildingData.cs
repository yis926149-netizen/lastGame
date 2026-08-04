using UnityEngine;

[System.Serializable]
public class BuildingData
{
    private IBuildingDataProvider buildingDataProvider;

    //对应的控制类
    public BuildingController controller;
    //建筑血量
    public float hp;
    //当前建筑血量
    public float currentHp;
    //建筑类型
    public Enums.BulidingType type;
    //回血阵的数值
    public float AltarValue = 0.4f;
    //额外血量
    public float extraHP = 0f;
    //【批次 C】回血触发间隔（秒），未配置时 UnitBrainBase 用兜底常量 5f
    public float HealInterval = 5f;
    
    // 【公共建筑系统】两阶段血量：captureHp 用于中立夺取，defenseHp 用于归属后防守
    public float captureHp = 0f;   // 首次夺取所需血量
    public float defenseHp = 0f;   // 归属后防守血量

    public BuildingData(Enums.BulidingType type, IBuildingDataProvider provider, int? buildingDatabaseId = null)
    {
        this.type = type;
        this.buildingDataProvider = provider;

        if (this.buildingDataProvider == null)
        {
            // 公共建筑等无 provider 路径：HP 由 Initialize() 等外部调用方设置
            return;
        }

        hp = buildingDataProvider.GetBuildingBaseHP(buildingDatabaseId ?? (int)type) + extraHP;
        currentHp = hp;
    }
}
