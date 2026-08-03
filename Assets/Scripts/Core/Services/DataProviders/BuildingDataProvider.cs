using System;
using UnityEngine;

public interface IBuildingDataProvider
{
    GameObject GetCityModel();
    GameObject GetBuildingPrefab(int buildingId);
    float GetBuildingBaseHP(int buildingId);

    Sprite GetBuildingCards(int buildingId);

    int GetBuildingCardsCount();

    /// <summary>按显式 ID 查找建筑配置；不存在时抛带上下文的异常（不依赖列表索引）。</summary>
    BuildingConfigSO GetBuildingConfig(int buildingId);

    /// <summary>按显式 ID 查找建筑配置；不存在返回 false。</summary>
    bool TryGetBuildingConfig(int buildingId, out BuildingConfigSO config);
}

public class BuildingDataProvider : IBuildingDataProvider
{
    private BuildingDatabaseSO _buildingDatabase;

    public BuildingDataProvider(BuildingDatabaseSO buildingDatabase)
    {
        _buildingDatabase = buildingDatabase;
    }

    private BuildingConfigSO FindConfig(int buildingId)
    {
        if (_buildingDatabase.buildings == null) return null;
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

    public GameObject GetCityModel() => _buildingDatabase.cityModel != null
        ? _buildingDatabase.cityModel
        : _buildingDatabase.CityModel[0];

    // 旧 int 查询 API：优先走 config，config 缺失时回退旧平行列表（过渡期兼容）。
    public GameObject GetBuildingPrefab(int buildingId)
    {
        BuildingConfigSO config = FindConfig(buildingId);
        return config != null ? config.buildingModel : _buildingDatabase.buildingModels[buildingId];
    }

    public float GetBuildingBaseHP(int buildingId)
    {
        BuildingConfigSO config = FindConfig(buildingId);
        return config != null ? config.baseHP : _buildingDatabase.buildingBaseHP[buildingId];
    }

    public Sprite GetBuildingCards(int buildingId)
    {
        BuildingConfigSO config = FindConfig(buildingId);
        return config != null ? config.cardSprite : _buildingDatabase.buildingCards[buildingId];
    }

    public int GetBuildingCardsCount() => _buildingDatabase.buildingCards.Count;
}
