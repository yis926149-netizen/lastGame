using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildingDatabase", menuName = "Game/BuildingDatabase")]
public class BuildingDatabaseSO : ScriptableObject
{
    public List<GameObject> CityModel;           // 城市模型
    public List<GameObject> buildingModels;      // 建筑模型
    public List<float> buildingBaseHP;            // 基础血量（若所有建筑相同）
    public List<Sprite> buildingCards;            // 建筑卡面

    // 建筑数据类(目前来说建筑类和建筑基础数据类不是同一个东西)
    //public List<BuildingData> BuildingDatas = new List<BuildingData>(); 
}