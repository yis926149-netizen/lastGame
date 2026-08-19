using System;
using UnityEngine;
using GameConfig;

public interface IUnitDataProvider
{
    GameObject GetUnitPrefab(int unitId);

    /// <summary>敌方（AI）单位预制体；enemyUnitModel 留空时回退 unitModel。</summary>
    GameObject GetEnemyUnitPrefab(int unitId);

    /// <summary>单位数值：仅取 Excel 平衡库（阶段6 唯一主源）。</summary>
    UnitData GetUnitData(int unitId);

    Sprite GetUnitIcon(int unitId);
    Sprite GetSkillIcon(int unitId);
    Sprite GetCard(int unitId);

    /// <summary>按显式 ID 查找单位配置；不存在时抛带上下文的异常（不依赖列表索引）。</summary>
    UnitConfigSO GetUnitConfig(int unitId);

    /// <summary>按显式 ID 查找单位配置；不存在返回 false。</summary>
    bool TryGetUnitConfig(int unitId, out UnitConfigSO config);

    /// <summary>单位策略类型：仅取 Excel 数值。</summary>
    UnitStrategyType GetUnitStrategyType(int unitId);

    /// <summary>单位卡费：仅取 Excel 数值。</summary>
    int GetUnitCardCost(int unitId);
}

public class UnitDataProvider : IUnitDataProvider
{
    private readonly UnitDatabaseSO _unitDatabase;              // 资源 SO（手工维护资源字段）
    private readonly UnitBalanceDatabaseSO _balance;            // 数值 SO（Excel 生成，只读）

    public UnitDataProvider(UnitDatabaseSO unitDatabase, UnitBalanceDatabaseSO balance = null)
    {
        _unitDatabase = unitDatabase;
        _balance = balance;
    }

    private UnitConfigSO FindConfig(int unitId)
    {
        if (_unitDatabase == null || _unitDatabase.units == null) return null;
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

    // —— 数值：仅 Excel 平衡库（阶段6 唯一主源）——

    private UnitBalanceDatabaseSO RequireBalance()
    {
        if (_balance == null)
            throw new InvalidOperationException(
                "[UnitDataProvider] Excel 单位平衡库未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 UnitBalanceDatabaseSO。");
        return _balance;
    }

    private UnitBalanceData RequireBalanceData(int unitId)
    {
        if (!RequireBalance().TryGetByLegacyId(unitId, out var b))
            throw new InvalidOperationException(
                $"[UnitDataProvider] 单位 ID {unitId} 未在 Excel 单位平衡库命中，无法读取数值。");
        return b;
    }

    public UnitData GetUnitData(int unitId) => BuildUnitData(RequireBalanceData(unitId));

    public UnitStrategyType GetUnitStrategyType(int unitId) =>
        ParseStrategyType(RequireBalanceData(unitId).strategyType);

    public int GetUnitCardCost(int unitId) => RequireBalanceData(unitId).cardCost;

    private static UnitData BuildUnitData(UnitBalanceData b)
    {
        return new UnitData(
            b.legacyId,
            b.displayName,
            b.movementPoints,
            (int)b.hp,
            b.attackRange,
            (int)b.attack,
            b.viewPoints,
            b.defense)
        {
            AttackInterval = b.attackIntervalSeconds,
        };
    }

    private static UnitStrategyType ParseStrategyType(string s)
    {
        if (s == "Settler") return UnitStrategyType.Settler;
        if (s == "Ranged") return UnitStrategyType.Ranged;
        return UnitStrategyType.Melee;
    }

    // —— 资源：仍从 UnitDatabaseSO 读 ——

    public GameObject GetUnitPrefab(int unitId) => GetUnitConfig(unitId).unitModel;

    public GameObject GetEnemyUnitPrefab(int unitId)
    {
        UnitConfigSO config = GetUnitConfig(unitId);
        return config.enemyUnitModel != null ? config.enemyUnitModel : config.unitModel;
    }

    public Sprite GetUnitIcon(int unitId) => GetUnitConfig(unitId).unitIcon;
    public Sprite GetSkillIcon(int unitId) => GetUnitConfig(unitId).skillIcon;
    public Sprite GetCard(int unitId) => GetUnitConfig(unitId).cardSprite;
}
