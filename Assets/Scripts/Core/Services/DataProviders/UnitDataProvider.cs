using System;
using UnityEngine;

public interface IUnitDataProvider
{
    GameObject GetUnitPrefab(int unitId);
    UnitData GetUnitData(int unitId);
    Sprite GetUnitIcon(int unitId);
    float GetUnitIconCount();
    Sprite GetSkillIcon(int unitId);

    Sprite GetCard(int unitId);

    /// <summary>按显式 ID 查找单位配置；不存在时抛带上下文的异常（不依赖列表索引）。</summary>
    UnitConfigSO GetUnitConfig(int unitId);

    /// <summary>按显式 ID 查找单位配置；不存在返回 false。</summary>
    bool TryGetUnitConfig(int unitId, out UnitConfigSO config);
}

public class UnitDataProvider : IUnitDataProvider
{
    private readonly UnitDatabaseSO _unitDatabase;

    public UnitDataProvider(UnitDatabaseSO unitDatabase)
    {
        _unitDatabase = unitDatabase;
    }

    private UnitConfigSO FindConfig(int unitId)
    {
        if (_unitDatabase.units == null) return null;
        foreach (UnitConfigSO config in _unitDatabase.units)
        {
            if (config != null && config.Id == unitId) return config;
        }
        return null;
    }

    public UnitConfigSO GetUnitConfig(int unitId)
    {
        UnitConfigSO config = FindConfig(unitId);
        if (config == null)
            throw new InvalidOperationException($"[UnitDataProvider] 未找到单位 ID {unitId} 的 UnitConfig（UnitDatabase.units）。");
        return config;
    }

    public bool TryGetUnitConfig(int unitId, out UnitConfigSO config)
    {
        config = FindConfig(unitId);
        return config != null;
    }

    // 旧 int 查询 API：优先走 config，config 缺失时回退旧平行列表（过渡期兼容）。
    public GameObject GetUnitPrefab(int unitId)
    {
        UnitConfigSO config = FindConfig(unitId);
        return config != null ? config.unitModel : _unitDatabase.unitModels[unitId];
    }

    public UnitData GetUnitData(int unitId)
    {
        UnitConfigSO config = FindConfig(unitId);
        return config != null ? config.unitData : _unitDatabase.unitDatas[unitId];
    }

    public Sprite GetUnitIcon(int unitId)
    {
        UnitConfigSO config = FindConfig(unitId);
        return config != null ? config.unitIcon : _unitDatabase.unitIcons[unitId];
    }

    public float GetUnitIconCount() => _unitDatabase.unitIcons.Count;

    public Sprite GetSkillIcon(int unitId)
    {
        UnitConfigSO config = FindConfig(unitId);
        return config != null ? config.skillIcon : _unitDatabase.skillIcons[unitId];
    }

    public Sprite GetCard(int unitId)
    {
        UnitConfigSO config = FindConfig(unitId);
        return config != null ? config.cardSprite : _unitDatabase.Cards[unitId];
    }
}
