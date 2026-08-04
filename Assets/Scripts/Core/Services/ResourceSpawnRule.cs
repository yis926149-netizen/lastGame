using System;

/// <summary>
/// 地图资源生成规则（纯函数）。
/// 【地图资源配置化】替代 MapGenerator.MapRandomResourceRoll 写死的 Next(0,18)/roll<5：
/// 按「空白权重 + 各资源正权重」掷点，命中空白返回 null。
/// </summary>
public static class ResourceSpawnRule
{
    /// <summary>总权重 = 空白权重 + 所有正权重资源之和。</summary>
    public static int TotalWeight(MapResourceDatabaseSO database)
    {
        if (database == null) return 0;

        int total = Math.Max(0, database.emptySpawnWeight);
        if (database.resources != null)
        {
            foreach (var r in database.resources)
            {
                if (r != null && r.spawnWeight > 0)
                    total += r.spawnWeight;
            }
        }
        return total;
    }

    /// <summary>
    /// 按权重表掷点选择资源。
    /// </summary>
    /// <param name="database">资源数据库</param>
    /// <param name="random">随机源</param>
    /// <returns>选中的资源；掷中空白或数据库为空返回 null。</returns>
    public static MapResourceSO RollResource(MapResourceDatabaseSO database, Random random)
    {
        int total = TotalWeight(database);
        if (total <= 0 || random == null) return null;

        return RollResource(database, random.Next(0, total));
    }

    /// <summary>
    /// 按权重表掷点选择资源（确定性重载，便于测试）。
    /// </summary>
    /// <param name="database">资源数据库</param>
    /// <param name="rollValue">掷点值，必须 ∈ [0, TotalWeight)</param>
    /// <returns>选中的资源；掷中空白或数据库为空返回 null。</returns>
    public static MapResourceSO RollResource(MapResourceDatabaseSO database, int rollValue)
    {
        if (database == null || rollValue < 0) return null;

        if (rollValue < Math.Max(0, database.emptySpawnWeight))
            return null;

        int remaining = rollValue - Math.Max(0, database.emptySpawnWeight);
        if (database.resources != null)
        {
            foreach (var r in database.resources)
            {
                if (r == null || r.spawnWeight <= 0) continue;
                if (remaining < r.spawnWeight) return r;
                remaining -= r.spawnWeight;
            }
        }
        return null;
    }
}
