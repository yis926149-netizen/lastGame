using UnityEngine;

//****************************************
// 【公共建筑系统-决策#35】公共建筑数据提供者接口
//****************************************

public interface IPublicBuildingDataProvider
{
    GameObject GetPrefab(int buildingId);
    float GetCaptureHp(int buildingId);
    float GetDefenseHp(int buildingId);
    Enums.HexDirection[] GetSubHexDirections(int buildingId);
    int GetBuildingCount();
}

public class PublicBuildingDataProvider : IPublicBuildingDataProvider
{
    private readonly PublicBuildingSO _database;

    public PublicBuildingDataProvider(PublicBuildingSO database)
    {
        _database = database;
    }

    public GameObject GetPrefab(int buildingId)
    {
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return null;
        return _database.buildings[buildingId].prefab;
    }

    public float GetCaptureHp(int buildingId)
    {
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return 100f;
        return _database.buildings[buildingId].captureHp;
    }

    public float GetDefenseHp(int buildingId)
    {
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return 150f;
        return _database.buildings[buildingId].defenseHp;
    }

    public Enums.HexDirection[] GetSubHexDirections(int buildingId)
    {
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return new Enums.HexDirection[] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
        return _database.buildings[buildingId].subHexDirections;
    }

    public int GetBuildingCount()
    {
        return _database.buildings?.Length ?? 0;
    }
}
