using System;
using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：地图地貌提供者（对象化 + Excel 数值化）。
//         散落/簇生成的权重与参数优先由 Excel（MapLandFormBalanceDatabaseSO +
//         LandFormGlobalConfigDatabaseSO）决定，通过 landFormId → 手工资源 SO（MapLandFormSO）
//         解析出模型/浮标等资源对象。效果数值由 LandFormEffectRule 统一读取（同样 Excel 优先）。
//         Excel 未生成时回退 Legacy 手工 SO（双轨迁移期）。
//****************************************
public class MapLandFormProvider
{
    private readonly MapLandFormDatabaseSO _database;             // Legacy 地貌库（模型/浮标）
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

    /// <summary>不生成地貌的权重（Excel 优先，缺失回退 Legacy）。</summary>
    public int EmptySpawnWeight =>
        _global?.Config?.emptySpawnWeight ?? _database?.emptySpawnWeight ?? 0;

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

        if (_balance != null && _balance.EnabledLandForms.Count > 0)
        {
            foreach (var b in _balance.EnabledLandForms)
            {
                if (b.spawnWeight <= 0) continue;
                if (remaining < b.spawnWeight) return FindResource(b.landFormId);
                remaining -= b.spawnWeight;
            }
        }
        else if (_database != null && _database.landForms != null)
        {
            foreach (var f in _database.landForms)
            {
                if (f == null || f.spawnWeight <= 0) continue;
                if (remaining < f.spawnWeight) return f;
                remaining -= f.spawnWeight;
            }
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

    /// <summary>启用散落地貌列表（资源对象），Excel 优先，缺失回退 Legacy。</summary>
    public IReadOnlyList<MapLandFormSO> GetEnabledForms()
    {
        var result = new List<MapLandFormSO>();
        if (_balance != null && _balance.EnabledLandForms.Count > 0)
        {
            foreach (var b in _balance.EnabledLandForms)
            {
                var form = FindResource(b.landFormId);
                if (form != null) result.Add(form);
            }
        }
        else if (_database != null && _database.landForms != null)
        {
            foreach (var f in _database.landForms)
                if (f != null) result.Add(f);
        }
        return result;
    }

    /// <summary>按地貌 SO 查 Excel 数值；未命中返回 null。</summary>
    public MapLandFormBalanceData GetBalance(MapLandFormSO form)
    {
        if (form == null || _balance == null) return null;
        return _balance.TryGetLandForm(form.landFormId, out var b) ? b : null;
    }

    /// <summary>是否为簇生成地貌（Excel 优先，缺失回退 Legacy）。</summary>
    public bool IsClusterSpawn(MapLandFormSO form)
    {
        var b = GetBalance(form);
        if (b != null) return b.clusterSpawn;
        return form != null && form.clusterSpawn;
    }

    public int GetClusterCount(MapLandFormSO form)
    {
        var b = GetBalance(form);
        if (b != null) return b.clusterCount;
        return form != null ? form.clusterCount : 1;
    }

    public int GetClusterTargetSize(MapLandFormSO form)
    {
        var b = GetBalance(form);
        if (b != null) return b.clusterTargetSize;
        return form != null ? form.clusterTargetSize : 8;
    }

    public float GetClusterFillProbability(MapLandFormSO form)
    {
        var b = GetBalance(form);
        if (b != null) return b.clusterFillProbability;
        return form != null ? form.clusterFillProbability : 0.8f;
    }

    public int GetClusterMinSpacing(MapLandFormSO form)
    {
        var b = GetBalance(form);
        if (b != null) return b.clusterMinSpacing;
        return form != null ? form.clusterMinSpacing : 4;
    }

    public int GetClusterMaxRadius(MapLandFormSO form)
    {
        var b = GetBalance(form);
        if (b != null) return b.clusterMaxRadius;
        return form != null ? form.clusterMaxRadius : 4;
    }

    private int ComputeTotalWeight()
    {
        int total = EmptySpawnWeight;
        if (_balance != null && _balance.EnabledLandForms.Count > 0)
        {
            foreach (var b in _balance.EnabledLandForms)
                if (b.spawnWeight > 0) total += b.spawnWeight;
        }
        else if (_database != null && _database.landForms != null)
        {
            foreach (var f in _database.landForms)
                if (f != null && f.spawnWeight > 0) total += f.spawnWeight;
        }
        return total;
    }
}
