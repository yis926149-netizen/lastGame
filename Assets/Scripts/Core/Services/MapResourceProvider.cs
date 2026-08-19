using System;
using GameConfig;

//****************************************
//功能说明：地图资源提供者（阶段6：Excel 唯一主源）。
//         生成权重/拾取效果/探索收割数值仅由 Excel（MapResourceBalanceDatabaseSO +
//         ResourceGlobalConfigDatabaseSO）读取；resourceId → 手工资源 SO（MapResourceSO）
//         仅用于解析模型/特效/音效等资源对象。Excel 未生成/未命中时抛异常，暴露配置缺失。
//****************************************
public class MapResourceProvider
{
    private readonly MapResourceDatabaseSO _database;              // 手工资源库（模型/特效/音效）
    private readonly MapResourceBalanceDatabaseSO _balance;        // Excel 数值
    private readonly ResourceGlobalConfigDatabaseSO _global;       // Excel 全局参数

    public MapResourceProvider(
        MapResourceDatabaseSO database,
        MapResourceBalanceDatabaseSO balance = null,
        ResourceGlobalConfigDatabaseSO global = null)
    {
        _database = database;
        _balance = balance;
        _global = global;
    }

    private ResourceGlobalConfigData RequireGlobal()
    {
        if (_global?.Config == null)
            throw new System.InvalidOperationException(
                "[MapResource] Excel 资源全局配置未加载：请先运行 工具/游戏配置/导入并校验，并绑定 ResourceGlobalConfigDatabaseSO。");
        return _global.Config;
    }

    private MapResourceBalanceDatabaseSO RequireBalanceDb()
    {
        if (_balance == null)
            throw new System.InvalidOperationException(
                "[MapResource] Excel 资源数值库未加载：请先运行 工具/游戏配置/导入并校验，并绑定 MapResourceBalanceDatabaseSO。");
        return _balance;
    }

    /// <summary>不生成资源的权重（Excel 唯一主源）。</summary>
    public int EmptySpawnWeight => RequireGlobal().emptySpawnWeight;

    /// <summary>探索任意地块的基础金币奖励（Excel 唯一主源）。</summary>
    public int BaseExplorationGold => RequireGlobal().baseExplorationGold;

    /// <summary>按权重表掷点选择资源；掷中空白或数据库为空返回 null。</summary>
    public MapResourceSO RollResource(System.Random random)
    {
        int total = ComputeTotalWeight();
        if (total <= 0 || random == null) return null;
        return RollResource(random.Next(0, total));
    }

    /// <summary>按权重表掷点选择资源（确定性重载，便于测试）。</summary>
    public MapResourceSO RollResource(int rollValue)
    {
        if (rollValue < 0) return null;

        int remaining = rollValue;
        if (remaining < EmptySpawnWeight) return null;
        remaining -= EmptySpawnWeight;

        foreach (var b in RequireBalanceDb().EnabledResources)
        {
            if (b.spawnWeight <= 0) continue;
            if (remaining < b.spawnWeight) return FindResource(b.resourceId);
            remaining -= b.spawnWeight;
        }

        return null;
    }

    /// <summary>按 resourceId 查找手工资源 SO（模型/特效/音效）。</summary>
    public MapResourceSO FindResource(string resourceId)
    {
        if (_database == null || _database.resources == null) return null;
        foreach (var r in _database.resources)
            if (r != null && r.resourceId == resourceId) return r;
        return null;
    }

    /// <summary>按资源 SO 查 Excel 数值；未命中返回 null。</summary>
    public MapResourceBalanceData GetBalance(MapResourceSO resource)
    {
        if (resource == null) return null;
        return RequireBalanceDb().TryGetResource(resource.resourceId, out var b) ? b : null;
    }

    /// <summary>拾取效果类型（Excel 唯一主源；无资源返回 None，未命中抛异常）。</summary>
    public ResourcePickupEffectType GetPickupEffectType(MapResourceSO resource)
    {
        if (resource == null) return ResourcePickupEffectType.None;
        return ParseEffectType(RequireBalance(resource).pickupEffectType);
    }

    /// <summary>拾取效果参数（Excel 唯一主源；无资源返回 default，未命中抛异常）。</summary>
    public ResourcePickupEffect GetPickupEffect(MapResourceSO resource)
    {
        if (resource == null) return default;
        var balance = RequireBalance(resource);
        return new ResourcePickupEffect
        {
            attackBonus = balance.attackBonus,
            healRatio = balance.healRatio,
            defenseBonus = balance.defenseBonus,
            goldAmount = balance.goldAmount,
        };
    }

    /// <summary>探索收割金币 = 基础奖励 + 资源加成（Excel 唯一主源；无资源仅基础奖励）。</summary>
    public int ComputeExplorationReward(MapResourceSO resource)
    {
        if (resource == null) return BaseExplorationGold;
        return BaseExplorationGold + RequireBalance(resource).explorationGoldBonus;
    }

    private MapResourceBalanceData RequireBalance(MapResourceSO resource)
    {
        var balance = GetBalance(resource);
        if (balance == null)
            throw new System.InvalidOperationException(
                $"[MapResource] 资源 {resource?.resourceId ?? "(null)"} 未在 Excel 资源数值库命中，无法读取数值。");
        return balance;
    }

    private int ComputeTotalWeight()
    {
        int total = EmptySpawnWeight;
        foreach (var b in RequireBalanceDb().EnabledResources)
            if (b.spawnWeight > 0) total += b.spawnWeight;
        return total;
    }

    private static ResourcePickupEffectType ParseEffectType(string s)
    {
        switch (s)
        {
            case "AttackBoost": return ResourcePickupEffectType.AttackBoost;
            case "Heal": return ResourcePickupEffectType.Heal;
            case "DefenseBoost": return ResourcePickupEffectType.DefenseBoost;
            case "Gold": return ResourcePickupEffectType.Gold;
            default: return ResourcePickupEffectType.None;
        }
    }
}
