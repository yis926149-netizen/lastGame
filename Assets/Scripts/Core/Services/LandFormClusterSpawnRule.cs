using System;
using System.Collections.Generic;

/// <summary>
/// 地图地貌簇生成规则（纯函数）。
/// 【金矿扎堆 + Excel 数值化】仅 clusterSpawn=true 的地貌（金矿）使用：固定 n 堆、
/// 目标格数预算 + 概率生长的不规则扎堆；其余地貌保持原有散落生成。
/// 簇参数优先由 Excel（MapLandFormProvider）读取，缺失回退 Legacy MapLandFormSO 字段。
/// 设计要点：
///  - 散落池保持不变以锁定随机流，同种子下其他地貌位置逐格不变；
///  - 簇生成使用独立随机流（SeedService "LandFormCluster"），不消耗散落流的随机数；
///  - 掷中簇地貌但不在堆内的格由 RemoveScatteredForm 拦截改写为空白。
/// 【程序化山脉-决策 ⑮】多簇遍历：按数据库顺序处理全部 clusterSpawn 地貌，共享占用集合互斥；
/// 山脉地貌（mountainForm=true）不入本规则（RidgeGenerator 专属 pass），山格不参与簇生长。
/// </summary>
public static class LandFormClusterSpawnRule
{
    /// <summary>格是否可作为簇成员：非水域、非河流、非山脉地块。</summary>
    public static bool IsEligible(HexCellData cell)
    {
        return cell != null && !cell.hasRiver && !WaterLevelConfig.IsWater(cell) && !MountainCellRule.IsMountainCell(cell);
    }

    /// <summary>从数据库中找到开启簇生成的地貌；没有则返回 null。</summary>
    public static MapLandFormSO FindClusterForm(MapLandFormProvider provider)
    {
        if (provider == null) return null;

        foreach (MapLandFormSO form in provider.GetEnabledForms())
        {
            if (form != null && provider.IsClusterSpawn(form) && !form.mountainForm)
                return form;
        }
        return null;
    }

    /// <summary>
    /// 多簇遍历（决策 ⑮）：按数据库顺序生成所有 clusterSpawn 地貌。
    /// 共享占用集合（reserved = 已有山格 + 已生成的其他簇格）保证互斥。
    /// </summary>
    public static void PlaceAllClusters(
        MapLandFormProvider provider, List<HexCellData> cells,
        Func<HexCellData, List<HexCellData>> neighborsOf, Random random)
    {
        if (provider == null || cells == null || cells.Count == 0
            || neighborsOf == null || random == null)
            return;

        // 共享占用集合：先纳入全部山脉地块（山格不参与簇生长，决策 ⑫）
        var reserved = new HashSet<HexCellData>();
        foreach (HexCellData cell in cells)
        {
            if (cell != null && MountainCellRule.IsMountainCell(cell))
                reserved.Add(cell);
        }

        foreach (MapLandFormSO form in provider.GetEnabledForms())
        {
            if (form == null || !provider.IsClusterSpawn(form) || form.mountainForm) continue;

            HashSet<HexCellData> claimed = PlaceClusters(provider, form, cells, neighborsOf, random, reserved);
            RemoveScatteredForm(form, cells, claimed);
            foreach (HexCellData cell in claimed)
                reserved.Add(cell);
        }
    }

    /// <summary>
    /// 固定成功数选取堆心：掷 clusterCount 个合格且互相间距 >= clusterMinSpacing 的格。
    /// 掷点失败自动重试（上限 clusterCount * 100 + 500 次，防止死循环）；
    /// 地图过小无法满足时降级返回已选中的堆心。
    /// </summary>
    public static List<HexCellData> SelectCenters(
        MapLandFormProvider provider, MapLandFormSO form, List<HexCellData> cells,
        Func<HexCellData, List<HexCellData>> neighborsOf, Random random,
        HashSet<HexCellData> reserved = null)
    {
        var centers = new List<HexCellData>();
        if (form == null || provider == null || !provider.IsClusterSpawn(form) || provider.GetClusterCount(form) <= 0
            || random == null || cells == null || cells.Count == 0 || neighborsOf == null)
            return centers;

        // 已占用区域：已选堆心及其 clusterMinSpacing-1 圈内，避免两堆粘连
        var blocked = new HashSet<HexCellData>();
        int maxAttempts = provider.GetClusterCount(form) * 100 + 500;
        for (int attempt = 0; attempt < maxAttempts && centers.Count < provider.GetClusterCount(form); attempt++)
        {
            HexCellData candidate = cells[random.Next(cells.Count)];
            if (!IsEligible(candidate) || blocked.Contains(candidate)) continue;
            if (reserved != null && reserved.Contains(candidate)) continue;

            centers.Add(candidate);
            MarkBlocked(blocked, candidate, Math.Max(0, provider.GetClusterMinSpacing(form) - 1), neighborsOf);
        }
        return centers;
    }

    /// <summary>
    /// 以堆心为起点按概率生长一团不规则簇：中心必填，其余格以 clusterFillProbability
    /// 填充；未填充或不合格（水域/河流/山脉地块）的格成为死路不再扩展；达到目标格数预算或
    /// 最大半径即停止。返回簇内格集合（含堆心）。
    /// </summary>
    public static HashSet<HexCellData> GrowCluster(
        MapLandFormProvider provider, MapLandFormSO form, HexCellData center,
        Func<HexCellData, List<HexCellData>> neighborsOf, Random random,
        HashSet<HexCellData> reserved = null)
    {
        var cluster = new HashSet<HexCellData>();
        if (form == null || provider == null || !provider.IsClusterSpawn(form) || center == null
            || neighborsOf == null || random == null)
            return cluster;

        cluster.Add(center);
        int budget = Math.Max(1, provider.GetClusterTargetSize(form));
        int maxRadius = Math.Max(1, provider.GetClusterMaxRadius(form));
        var frontier = new Queue<HexCellData>();
        var distances = new Dictionary<HexCellData, int> { [center] = 0 };
        frontier.Enqueue(center);

        while (frontier.Count > 0 && cluster.Count < budget)
        {
            HexCellData current = frontier.Dequeue();
            int dist = distances[current];
            if (dist >= maxRadius) continue;

            List<HexCellData> neighbors = neighborsOf(current);
            Shuffle(neighbors, random);
            foreach (HexCellData neighbor in neighbors)
            {
                if (cluster.Contains(neighbor)) continue;
                if (!IsEligible(neighbor)) continue;                              // 水域/河流/山格：死路
                if (reserved != null && reserved.Contains(neighbor)) continue;   // 共享占用：其他簇格
                if (random.NextDouble() >= provider.GetClusterFillProbability(form)) continue; // 概率未命中：死路

                cluster.Add(neighbor);
                distances[neighbor] = dist + 1;
                frontier.Enqueue(neighbor);
                if (cluster.Count >= budget) break;
            }
        }
        return cluster;
    }

    /// <summary>
    /// 执行簇生成并写回地块：选堆心 → 逐堆生长 → 将簇内格覆盖为 form。
    /// 返回被占用的格集合，供调用方做散落拦截（RemoveScatteredForm）。
    /// </summary>
    public static HashSet<HexCellData> PlaceClusters(
        MapLandFormProvider provider, MapLandFormSO form, List<HexCellData> cells,
        Func<HexCellData, List<HexCellData>> neighborsOf, Random random,
        HashSet<HexCellData> reserved = null)
    {
        var claimed = new HashSet<HexCellData>();
        if (form == null || provider == null || !provider.IsClusterSpawn(form) || provider.GetClusterCount(form) <= 0
            || random == null || cells == null || cells.Count == 0 || neighborsOf == null)
            return claimed;

        foreach (HexCellData center in SelectCenters(provider, form, cells, neighborsOf, random, reserved))
        {
            foreach (HexCellData cell in GrowCluster(provider, form, center, neighborsOf, random, reserved))
                claimed.Add(cell);
        }

        foreach (HexCellData cell in claimed)
            cell.landForm = form;

        return claimed;
    }

    /// <summary>
    /// 拦截散落结果：把散落掷中 form 但不在任何堆内的格改写为空白。
    /// </summary>
    public static void RemoveScatteredForm(MapLandFormSO form, List<HexCellData> cells, HashSet<HexCellData> claimed)
    {
        if (form == null || cells == null || claimed == null) return;

        foreach (HexCellData cell in cells)
        {
            if (claimed.Contains(cell)) continue;
            if (cell != null && cell.landForm == form)
                cell.landForm = null;
        }
    }

    private static void MarkBlocked(HashSet<HexCellData> blocked, HexCellData center, int radius,
        Func<HexCellData, List<HexCellData>> neighborsOf)
    {
        var frontier = new Queue<HexCellData>();
        var distances = new Dictionary<HexCellData, int>();
        blocked.Add(center);
        frontier.Enqueue(center);
        distances[center] = 0;

        while (frontier.Count > 0)
        {
            HexCellData current = frontier.Dequeue();
            int dist = distances[current];
            if (dist >= radius) continue;

            foreach (HexCellData neighbor in neighborsOf(current))
            {
                if (distances.ContainsKey(neighbor)) continue;
                distances[neighbor] = dist + 1;
                blocked.Add(neighbor);
                frontier.Enqueue(neighbor);
            }
        }
    }

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
