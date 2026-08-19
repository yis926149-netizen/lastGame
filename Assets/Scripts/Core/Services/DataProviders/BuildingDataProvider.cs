using System;
using UnityEngine;
using GameConfig;

public interface IBuildingDataProvider
{
    GameObject GetCityModel();
    GameObject GetEnemyCityModel();
    GameObject GetBuildingPrefab(int buildingId);

    /// <summary>建筑基础血量：仅取 Excel 平衡库（阶段6 唯一主源）。</summary>
    float GetBuildingBaseHP(int buildingId);

    Sprite GetBuildingCards(int buildingId);

    /// <summary>按显式 ID 查找建筑配置；不存在时抛带上下文的异常（不依赖列表索引）。</summary>
    BuildingConfigSO GetBuildingConfig(int buildingId);

    /// <summary>按显式 ID 查找建筑配置；不存在返回 false。</summary>
    bool TryGetBuildingConfig(int buildingId, out BuildingConfigSO config);

    /// <summary>建筑类型：仅取 Excel 数值。</summary>
    Enums.BulidingType GetBuildingType(int buildingId);

    /// <summary>是否阻挡移动：仅取 Excel 数值。</summary>
    bool GetBuildingBlocksMovement(int buildingId);

    /// <summary>建筑卡费：仅取 Excel 数值。</summary>
    int GetBuildingCardCost(int buildingId);

    /// <summary>金矿每秒收入：仅取 Excel 数值。</summary>
    float GetBuildingGoldIncomePerSecond(int buildingId);
}

public class BuildingDataProvider : IBuildingDataProvider
{
    private BuildingDatabaseSO _buildingDatabase;          // 资源 SO（手工维护资源字段）
    private BuildingBalanceDatabaseSO _balance;            // 数值 SO（Excel 生成，只读）

    public BuildingDataProvider(BuildingDatabaseSO buildingDatabase, BuildingBalanceDatabaseSO balance = null)
    {
        _buildingDatabase = buildingDatabase;
        _balance = balance;
    }

    private BuildingConfigSO FindConfig(int buildingId)
    {
        if (_buildingDatabase == null || _buildingDatabase.buildings == null) return null;
        foreach (BuildingConfigSO config in _buildingDatabase.buildings)
        {
            if (config != null && config.buildingId == buildingId) return config;
        }
        return null;
    }

    public BuildingConfigSO GetBuildingConfig(int buildingId)
    {
        BuildingConfigSO config = FindConfig(buildingId);
        if (config == null)
            throw new InvalidOperationException($"[BuildingDataProvider] 未找到建筑 ID {buildingId} 的 BuildingConfig（BuildingDatabase.buildings）。");
        return config;
    }

    public bool TryGetBuildingConfig(int buildingId, out BuildingConfigSO config)
    {
        config = FindConfig(buildingId);
        return config != null;
    }

    // —— 数值：仅 Excel 平衡库（阶段6 唯一主源）——

    private BuildingBalanceDatabaseSO RequireBalance()
    {
        if (_balance == null)
            throw new InvalidOperationException(
                "[BuildingDataProvider] Excel 建筑平衡库未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 BuildingBalanceDatabaseSO。");
        return _balance;
    }

    private BuildingBalanceData RequireBalanceData(int buildingId)
    {
        if (!RequireBalance().TryGetByLegacyId(buildingId, out var b))
            throw new InvalidOperationException(
                $"[BuildingDataProvider] 建筑 ID {buildingId} 未在 Excel 建筑平衡库命中，无法读取数值。");
        return b;
    }

    public float GetBuildingBaseHP(int buildingId) => RequireBalanceData(buildingId).hp;

    public Enums.BulidingType GetBuildingType(int buildingId) =>
        ParseBuildingType(RequireBalanceData(buildingId).buildingType);

    public bool GetBuildingBlocksMovement(int buildingId) => RequireBalanceData(buildingId).blocksMovement;

    public int GetBuildingCardCost(int buildingId) => RequireBalanceData(buildingId).cardCost;

    public float GetBuildingGoldIncomePerSecond(int buildingId) => RequireBalanceData(buildingId).goldIncomePerSecond;

    private static Enums.BulidingType ParseBuildingType(string s)
    {
        return s switch
        {
            "AttackStatue" => Enums.BulidingType.AttackStatue,
            "DefenseStatue" => Enums.BulidingType.DefenseStatue,
            "Altar" => Enums.BulidingType.Altar,
            "TechnologyAndCultural" => Enums.BulidingType.TechnologyAndCultural,
            "Barracks" => Enums.BulidingType.Barracks,
            "ArrowTower" => Enums.BulidingType.ArrowTower,
            "GoldMine" => Enums.BulidingType.GoldMine,
            _ => Enums.BulidingType.NoBuilding,
        };
    }

    // —— 资源：仍从 BuildingDatabaseSO 读 ——

    public GameObject GetCityModel() => _buildingDatabase.cityModel;

    public GameObject GetEnemyCityModel() =>
        _buildingDatabase.enemyCityModel != null ? _buildingDatabase.enemyCityModel : _buildingDatabase.cityModel;

    public GameObject GetBuildingPrefab(int buildingId) => GetBuildingConfig(buildingId).buildingModel;
    public Sprite GetBuildingCards(int buildingId) => GetBuildingConfig(buildingId).cardSprite;
}
