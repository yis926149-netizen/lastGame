using UnityEngine;
using GameConfig;

//****************************************
// 【公共建筑系统-决策#35】公共建筑数据提供者接口 + 实现
// 【Excel 数值化 + 阶段6 唯一主源】captureHp/defenseHp/subHexDirections（占格关系）仅由 Excel 数值库读取，
// prefab/markerIcon 等资源引用保留在手工资源 SO（PublicBuildingSO）。
// Excel 未生成/未命中时抛异常，暴露配置缺失。
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
    private readonly PublicBuildingSO _database;                     // 资源（prefab/markerIcon）
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

    // —— 数值：仅 Excel（阶段6 唯一主源）——

    private PublicBuildingBalanceDatabaseSO RequireBalance()
    {
        if (_balance == null)
            throw new System.InvalidOperationException(
                "[PublicBuilding] Excel 公共建筑平衡库未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 PublicBuildingBalanceDatabaseSO。");
        return _balance;
    }

    private PublicBuildingBalanceData RequireBalanceData(int buildingId)
    {
        if (!RequireBalance().TryGetByLegacyId(buildingId, out var b))
            throw new System.InvalidOperationException(
                $"[PublicBuilding] 公共建筑 ID {buildingId} 未在 Excel 公共建筑平衡库命中，无法读取数值。");
        return b;
    }

    public float GetCaptureHp(int buildingId) => RequireBalanceData(buildingId).captureHp;

    public float GetDefenseHp(int buildingId) => RequireBalanceData(buildingId).defenseHp;

    public Enums.HexDirection[] GetSubHexDirections(int buildingId) =>
        ParseDirections(RequireBalanceData(buildingId).subHexDirections);

    public int GetBuildingCount() => RequireBalance().EnabledBuildings.Count;

    private static readonly Enums.HexDirection[] DefaultSubHexDirections =
        { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };

    // 解析结果会被 PublicBuildingBase.SubHexDirections 长期持有，默认形状必须发副本，
    // 避免某个建筑就地改写数组影响到其他建筑。
    private static Enums.HexDirection[] NewDefaultDirections() =>
        (Enums.HexDirection[])DefaultSubHexDirections.Clone();

    /// <summary>
    /// 解析 Excel 的 subHexDirections 列。
    /// 空值 = 沿用历史默认的三子格形状（NE,E,SE）；"None" = 显式声明单格建筑（无子格）。
    /// 注意不能用空值表达单格：现有多格建筑（Fort 等）都依赖空值取默认形状。
    /// </summary>
    private static Enums.HexDirection[] ParseDirections(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return NewDefaultDirections();

        // 显式单格标记：根格之外不占任何子格
        if (csv.Trim().Equals("None", System.StringComparison.OrdinalIgnoreCase))
            return System.Array.Empty<Enums.HexDirection>();

        var parts = csv.Split(',');
        var result = new System.Collections.Generic.List<Enums.HexDirection>(parts.Length);
        foreach (var p in parts)
        {
            if (System.Enum.TryParse<Enums.HexDirection>(p.Trim(), out var dir))
                result.Add(dir);
        }

        if (result.Count == 0)
        {
            // 静默退回默认形状会让配置笔误变成“莫名其妙的 4 格建筑”，这里必须暴露出来。
            Debug.LogError(
                $"[PublicBuilding] subHexDirections=\"{csv}\" 无法解析出任何合法方向，已退回默认形状 NE,E,SE。" +
                "请检查 Excel 拼写；若本意是单格建筑，应填 \"None\"。");
            return NewDefaultDirections();
        }

        return result.ToArray();
    }
}
