using UnityEngine;
using GameConfig;

//****************************************
// 【公共建筑系统-决策#35】公共建筑数据提供者接口 + 实现
// 【Excel 数值化】captureHp/defenseHp/subHexDirections（占格关系）优先由 Excel 数值库读取，
// prefab/markerIcon 等资源引用保留在手工资源 SO（PublicBuildingSO）。
// Excel 未生成时回退 Legacy PublicBuildingSO 字段（双轨迁移期）。
//****************************************

public interface IPublicBuildingDataProvider
{
    GameObject GetPrefab(int buildingId);
    GameObject GetMarkerPrefab();
    Sprite GetMarkerIcon(int buildingId);
    float GetCaptureHp(int buildingId);
    float GetDefenseHp(int buildingId);
    Enums.HexDirection[] GetSubHexDirections(int buildingId);
    int GetBuildingCount();
}

public class PublicBuildingDataProvider : IPublicBuildingDataProvider
{
    private readonly PublicBuildingSO _database;                     // Legacy 资源（prefab/markerIcon）
    private readonly PublicBuildingBalanceDatabaseSO _balance;       // Excel 数值

    public PublicBuildingDataProvider(PublicBuildingSO database, PublicBuildingBalanceDatabaseSO balance = null)
    {
        _database = database;
        _balance = balance;
    }

    // —— 资源：仍从 PublicBuildingSO 读 ——

    public GameObject GetPrefab(int buildingId)
    {
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return null;
        return _database.buildings[buildingId].prefab;
    }

    public GameObject GetMarkerPrefab()
    {
        return _database.markerPrefab;
    }

    public Sprite GetMarkerIcon(int buildingId)
    {
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return null;
        return _database.buildings[buildingId].markerIcon;
    }

    // —— 数值：优先 Excel，缺失回退 Legacy ——

    public float GetCaptureHp(int buildingId)
    {
        if (_balance != null && _balance.TryGetByLegacyId(buildingId, out var b))
            return b.captureHp;
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return 100f;
        return _database.buildings[buildingId].captureHp;
    }

    public float GetDefenseHp(int buildingId)
    {
        if (_balance != null && _balance.TryGetByLegacyId(buildingId, out var b))
            return b.defenseHp;
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return 150f;
        return _database.buildings[buildingId].defenseHp;
    }

    public Enums.HexDirection[] GetSubHexDirections(int buildingId)
    {
        if (_balance != null && _balance.TryGetByLegacyId(buildingId, out var b))
            return ParseDirections(b.subHexDirections);
        if (_database.buildings == null || buildingId < 0 || buildingId >= _database.buildings.Length)
            return new Enums.HexDirection[] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
        return _database.buildings[buildingId].subHexDirections;
    }

    public int GetBuildingCount()
    {
        if (_balance != null && _balance.EnabledBuildings.Count > 0)
            return _balance.EnabledBuildings.Count;
        return _database.buildings?.Length ?? 0;
    }

    private static Enums.HexDirection[] ParseDirections(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return new Enums.HexDirection[] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };

        var parts = csv.Split(',');
        var result = new System.Collections.Generic.List<Enums.HexDirection>(parts.Length);
        foreach (var p in parts)
        {
            if (System.Enum.TryParse<Enums.HexDirection>(p.Trim(), out var dir))
                result.Add(dir);
        }
        return result.Count > 0
            ? result.ToArray()
            : new Enums.HexDirection[] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
    }
}
