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

    public BuildingData(Enums.BulidingType type, IBuildingDataProvider provider)
    {
        this.type = type;
        this.buildingDataProvider = provider; // 赋值依赖

        if (this.buildingDataProvider == null)
        {
            Debug.LogError("BuildingDataProvider is null!");
            return;
        }

        hp = buildingDataProvider.GetBuildingBaseHP((int)type) + extraHP;
        currentHp = hp;
    }
}