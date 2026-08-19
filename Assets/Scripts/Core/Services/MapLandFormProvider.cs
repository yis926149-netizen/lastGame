using System;
using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：地图地貌提供者（阶段6：Excel 唯一主源）。
//         散落/簇生成的权重与参数仅由 Excel（MapLandFormBalanceDatabaseSO +
//         LandFormGlobalConfigDatabaseSO）读取，通过 landFormId → 手工资源 SO（MapLandFormSO）
//         仅用于解析模型/浮标等资源对象。效果数值由 LandFormEffectRule 统一读取（同样 Excel 唯一）。
//         Excel 未生成/未命中时抛异常，暴露配置缺失。
//****************************************
public class MapLandFormProvider
{
    private readonly MapLandFormDatabaseSO _database;             // 手工地貌库（模型/浮标）
    private readonly MapLandFormBalanceDatabaseSO _balance;       // Excel 数值
    private readonly LandFormGlobalConfigDatabaseSO _global;      // Excel 全局参数

    public MapLandFormProvider(
        MapLandFormDatabaseSO database,
        MapLandFormBalanceDatabaseSO balance = null,
        LandFormGlobalConfigDatabaseSO global = null)
    {
        _database = database;
        _balance = balance;
        _global = global;
    }

    private LandFormGlobalConfigData RequireGlobal()
    {
        if (_global?.Config == null)
            throw new System.InvalidOperationException(
                "[MapLandForm] Excel 地貌全局配置未加载：请先运行 工具/游戏配置/导入并校验，并绑定 LandFormGlobalConfigDatabaseSO。");
        return _global.Config;
    }

    private MapLandFormBalanceDatabaseSO RequireBalanceDb()
    {
        if (_balance == null)
            throw new System.InvalidOperationException(
                "[MapLandForm] Excel 地貌数值库未加载：请先运行 工具/游戏配置/导入并校验，并绑定 MapLandFormBalanceDatabaseSO。");
        return _balance;
    }

    /// <summary>不生成地貌的权重（Excel 唯一主源）。</summary>
    public int EmptySpawnWeight => RequireGlobal().emptySpawnWeight;

    /// <summary>按权重表掷点选择散落地貌；掷中空白或数据库为空返回 null。</summary>
    public MapLandFormSO RollLandForm(System.Random random)
    {
        int total = ComputeTotalWeight();
        if (total <= 0 || random == null) return null;
        return RollLandForm(random.Next(0, total));
    }

    /// <summary>按权重表掷点选择散落地貌（确定性重载，便于测试）。</summary>
    public MapLandFormSO RollLandForm(int rollValue)
    {
        if (rollValue < 0) return null;

        int remaining = rollValue;
        if (remaining < EmptySpawnWeight) return null;
        remaining -= EmptySpawnWeight;

        foreach (var b in RequireBalanceDb().EnabledLandForms)
        {
            if (b.spawnWeight <= 0) continue;
            if (remaining < b.spawnWeight) return FindResource(b.landFormId);
            remaining -= b.spawnWeight;
        }

        return null;
    }

    /// <summary>按 landFormId 查找手工地貌 SO（模型/浮标）。</summary>
    public MapLandFormSO FindResource(string landFormId)
    {
        if (_database == null || _database.landForms == null) return null;
        foreach (var f in _database.landForms)
            if (f != null && f.landFormId == landFormId) return f;
        return null;
    }

    /// <summary>启用散落地貌列表（资源对象），Excel 唯一主源。</summary>
    public IReadOnlyList<MapLandFormSO> GetEnabledForms()
    {
        var result = new List<MapLandFormSO>();
        foreach (var b in RequireBalanceDb().EnabledLandForms)
        {
            var form = FindResource(b.landFormId);
            if (form != null) result.Add(form);
        }
        return result;
    }

    /// <summary>按地貌 SO 查 Excel 数值；未命中返回 null。</summary>
    public MapLandFormBalanceData GetBalance(MapLandFormSO form)
    {
        if (form == null) return null;
        return RequireBalanceDb().TryGetLandForm(form.landFormId, out var b) ? b : null;
    }

    /// <summary>是否为簇生成地貌（Excel 唯一主源；无地貌返回 false，未命中抛异常）。</summary>
    public bool IsClusterSpawn(MapLandFormSO form)
    {
        if (form == null) return false;
        return RequireBalance(form).clusterSpawn;
    }

    public int GetClusterCount(MapLandFormSO form)
    {
        if (form == null) return 1;
        return RequireBalance(form).clusterCount;
    }

    public int GetClusterTargetSize(MapLandFormSO form)
    {
        if (form == null) return 8;
        return RequireBalance(form).clusterTargetSize;
    }

    public float GetClusterFillProbability(MapLandFormSO form)
    {
        if (form == null) return 0.8f;
        return RequireBalance(form).clusterFillProbability;
    }

    public int GetClusterMinSpacing(MapLandFormSO form)
    {
        if (form == null) return 4;
        return RequireBalance(form).clusterMinSpacing;
    }

    public int GetClusterMaxRadius(MapLandFormSO form)
    {
        if (form == null) return 4;
        return RequireBalance(form).clusterMaxRadius;
    }

    private MapLandFormBalanceData RequireBalance(MapLandFormSO form)
    {
        var balance = GetBalance(form);
        if (balance == null)
            throw new System.InvalidOperationException(
                $"[MapLandForm] 地貌 {form?.landFormId ?? "(null)"} 未在 Excel 地貌数值库命中，无法读取数值。");
        return balance;
    }

    private int ComputeTotalWeight()
    {
        int total = EmptySpawnWeight;
        foreach (var b in RequireBalanceDb().EnabledLandForms)
            if (b.spawnWeight > 0) total += b.spawnWeight;
        return total;
    }
}
