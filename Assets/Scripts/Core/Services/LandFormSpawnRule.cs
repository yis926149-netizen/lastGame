using System;

/// <summary>
/// 地图地貌生成规则（纯函数）。
/// 【地图地貌配置化】替代 MapGenerator.LandFormDataGeneration 写死的 Next(0,14)+Clamp。
/// 掷点顺序必须保持旧算法映射：列表地貌在前（0~3 依次为森林/石头/大骨阵/农田），
/// 空白权重在后（4~13 返回 null），否则同一种子下地貌位置会改变。
/// </summary>
public static class LandFormSpawnRule
{
    /// <summary>总权重 = 各正权重地貌之和 + 空白权重。</summary>
    public static int TotalWeight(MapLandFormDatabaseSO database)
    {
        if (database == null) return 0;

        int total = 0;
        if (database.landForms != null)
        {
            foreach (var f in database.landForms)
            {
                if (f != null && f.spawnWeight > 0)
                    total += f.spawnWeight;
            }
        }
        total += Math.Max(0, database.emptySpawnWeight);
        return total;
    }

    /// <summary>
    /// 按权重表掷点选择地貌。
    /// </summary>
    /// <param name="database">地貌数据库</param>
    /// <param name="random">随机源</param>
    /// <returns>选中的地貌；掷中空白或数据库为空返回 null。</returns>
    public static MapLandFormSO RollLandForm(MapLandFormDatabaseSO database, Random random)
    {
        int total = TotalWeight(database);
        if (total <= 0 || random == null) return null;

        return RollLandForm(database, random.Next(0, total));
    }

    /// <summary>
    /// 按权重表掷点选择地貌（确定性重载，便于测试）。
    /// </summary>
    /// <param name="database">地貌数据库</param>
    /// <param name="rollValue">掷点值，必须 ∈ [0, TotalWeight)</param>
    /// <returns>选中的地貌；掷中空白或数据库为空返回 null。</returns>
    public static MapLandFormSO RollLandForm(MapLandFormDatabaseSO database, int rollValue)
    {
        if (database == null || rollValue < 0) return null;

        // 地貌在前：先遍历正权重地貌
        int remaining = rollValue;
        if (database.landForms != null)
        {
            foreach (var f in database.landForms)
            {
                if (f == null || f.spawnWeight <= 0) continue;
                if (remaining < f.spawnWeight) return f;
                remaining -= f.spawnWeight;
            }
        }

        // 空白在后：剩余区间落在空白权重内则返回 null
        if (remaining < Math.Max(0, database.emptySpawnWeight))
            return null;

        return null;
    }
}
