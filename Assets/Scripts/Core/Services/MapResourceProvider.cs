using System;
using GameConfig;

//****************************************
//功能说明：地图资源提供者（对象化 + Excel 数值化）。
//         生成权重/拾取效果/探索收割数值优先由 Excel（MapResourceBalanceDatabaseSO +
//         ResourceGlobalConfigDatabaseSO）决定，通过 resourceId → 手工资源 SO（MapResourceSO）
//         解析出模型/特效/音效等资源对象。Excel 未生成时回退 Legacy 手工 SO（双轨迁移期）。
//****************************************
public class MapResourceProvider
{
    private readonly MapResourceDatabaseSO _database;              // Legacy 资源库（模型/特效/音效）
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

    /// <summary>不生成资源的权重（Excel 优先，缺失回退 Legacy）。</summary>
    public int EmptySpawnWeight =>
        _global?.Config?.emptySpawnWeight ?? _database?.emptySpawnWeight ?? 0;

    /// <summary>探索任意地块的基础金币奖励（Excel 优先，缺失回退 Legacy）。</summary>
    public int BaseExplorationGold =>
        _global?.Config?.baseExplorationGold ?? _database?.baseExplorationGold ?? 0;

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

        // Excel 数值优先
        if (_balance != null && _balance.EnabledResources.Count > 0)
        {
            foreach (var b in _balance.EnabledResources)
            {
                if (b.spawnWeight <= 0) continue;
                if (remaining < b.spawnWeight) return FindResource(b.resourceId);
                remaining -= b.spawnWeight;
            }
        }
        else if (_database != null && _database.resources != null)
        {
            foreach (var r in _database.resources)
            {
                if (r == null || r.spawnWeight <= 0) continue;
                if (remaining < r.spawnWeight) return r;
                remaining -= r.spawnWeight;
            }
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
        if (resource == null || _balance == null) return null;
        return _balance.TryGetResource(resource.resourceId, out var b) ? b : null;
    }

    /// <summary>拾取效果类型：Excel 优先，缺失回退 Legacy SO。</summary>
    public ResourcePickupEffectType GetPickupEffectType(MapResourceSO resource)
    {
        var balance = GetBalance(resource);
        if (balance != null) return ParseEffectType(balance.pickupEffectType);
        return resource != null ? resource.pickupEffectType : ResourcePickupEffectType.None;
    }

    /// <summary>拾取效果参数：Excel 优先，缺失回退 Legacy SO。</summary>
    public ResourcePickupEffect GetPickupEffect(MapResourceSO resource)
    {
        var balance = GetBalance(resource);
        if (balance != null)
        {
            return new ResourcePickupEffect
            {
                attackBonus = balance.attackBonus,
                healRatio = balance.healRatio,
                defenseBonus = balance.defenseBonus,
                goldAmount = balance.goldAmount,
            };
        }
        return resource != null ? resource.pickupEffect : default;
    }

    /// <summary>探索收割金币 = 基础奖励 + 资源加成（Excel 优先）。</summary>
    public int ComputeExplorationReward(MapResourceSO resource)
    {
        var balance = GetBalance(resource);
        int bonus = balance != null
            ? balance.explorationGoldBonus
            : (resource != null ? resource.explorationGoldBonus : 0);
        return BaseExplorationGold + bonus;
    }

    private int ComputeTotalWeight()
    {
        int total = EmptySpawnWeight;
        if (_balance != null && _balance.EnabledResources.Count > 0)
        {
            foreach (var b in _balance.EnabledResources)
                if (b.spawnWeight > 0) total += b.spawnWeight;
        }
        else if (_database != null && _database.resources != null)
        {
            foreach (var r in _database.resources)
                if (r != null && r.spawnWeight > 0) total += r.spawnWeight;
        }
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
