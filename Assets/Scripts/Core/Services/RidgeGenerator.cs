using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 山脉脊线生成器（决策 ⑯/⑰/⑱/⑳/㉑/㉔；复刻 RiverGenerator 行走框架，高度条件反写为综合评分）。
/// 生成单位 = 一条山脉：脊线（无向有向路径） + 宽度化坡面格（决策 ⑯，不做单格山）。
///
/// 确定性：所有随机量来自调用方传入的独立随机流（SeedService "Mountain"）；生成结果固化写入
/// 格级数据（mountainRidge 参数快照 + d/s 派生输入，决策 ②），不依赖后续遍历顺序（决策 ㉓）。
///
/// 互斥：脊线行走与宽度化共享 occupied 集合（决策 ⑮：山与地貌/簇互斥）；河流生成时排除山格（决策 ③）。
/// </summary>
public static class RidgeGenerator
{
    private static readonly Vector3[] DirectionDeltas =
    {
        new Vector3(0, -1, 1), new Vector3(1, -1, 0), new Vector3(1, 0, -1),
        new Vector3(0, 1, -1), new Vector3(-1, 1, 0), new Vector3(-1, 0, 1)
    };

    /// <summary>
    /// 生成所有山脉并写回格级数据。
    /// </summary>
    /// <param name="config">山脉配置；mountainLandForm 为空时无操作。</param>
    /// <param name="cells">全部地块。</param>
    /// <param name="neighborsOf">邻居查询（六边形）。</param>
    /// <param name="random">山脉专属确定性随机流。</param>
    /// <param name="heightScale">高度全局缩放（2026-08-06 地图设置 SO 暴露）：乘在 H_max 上，
    /// 只影响视觉隆起，不消耗随机流、不改变脊线路径与宽度化（决策 ㉓ 确定性保持）。默认 1。</param>
    /// <returns>生成成功的脊线列表（含固化参数快照）。</returns>
    public static List<MountainRidgeData> Generate(
        MountainConfigSO config,
        IReadOnlyList<HexCellData> cells,
        Func<HexCellData, List<HexCellData>> neighborsOf,
        System.Random random,
        float heightScale = 1f)
    {
        if (config == null || config.mountainLandForm == null || cells == null || cells.Count == 0
            || neighborsOf == null || random == null)
            return new List<MountainRidgeData>();

        // 【调试对照】勾选 debugSingleCellAndStraightRidge 后绕过正常生成规律
        // （ridgeCount/起点禁区/评分行走均不生效），正常生成代码路径保留不删。
        if (config.debugSingleCellAndStraightRidge)
            return GenerateDebugComparison(config, cells, neighborsOf, random, heightScale);

        if (config.ridgeCount <= 0)
            return new List<MountainRidgeData>();

        float cellDist = ComputeCellDistance(cells, neighborsOf);
        if (cellDist <= 0f) cellDist = 1f;

        // occupied：已生成的山脉地块（脊线格 + 宽度化格），跨脊线互斥
        var occupied = new HashSet<HexCellData>();
        // blocked：起点禁区（已生成山脉周边 ridgeMinSpacing 圈），防山脉粘连
        var blocked = new HashSet<HexCellData>();
        var results = new List<MountainRidgeData>();

        int maxAttempts = config.ridgeCount * 200 + 500;
        int nextRidgeId = 1;
        for (int attempt = 0; attempt < maxAttempts && results.Count < config.ridgeCount; attempt++)
        {
            HexCellData start = PickStart(config, cells, random, blocked, occupied);
            if (start == null) continue;

            List<HexCellData> path = WalkRidge(config, start, neighborsOf, random, occupied);
            if (path == null) continue; // 长度不足：放弃，未写任何数据

            MountainRidgeData ridge = WriteRidgeData(config, path, nextRidgeId++, cellDist, random, heightScale);
            foreach (HexCellData cell in path)
                occupied.Add(cell);
            WidenMountain(config, ridge, path, cells, neighborsOf, occupied, cellDist);

            int blockRadius = Mathf.Max(0, config.ridgeMinSpacing - 1);
            foreach (HexCellData cell in occupied)
                MarkBlocked(blocked, cell, blockRadius, neighborsOf);

            results.Add(ridge);
        }
        return results;
    }

    // ── 起点选择 ─────────────────────────────────────────────

    /// <summary>随机选取起点：非水域、非山脉、未被永久清除、不在已有山脉的禁区圈内。</summary>
    private static HexCellData PickStart(MountainConfigSO config, IReadOnlyList<HexCellData> cells,
        System.Random random, HashSet<HexCellData> blocked, HashSet<HexCellData> occupied)
    {
        int maxAttempts = Mathf.Max(1, cells.Count * 2);
        for (int i = 0; i < maxAttempts; i++)
        {
            HexCellData candidate = cells[random.Next(cells.Count)];
            if (candidate == null) continue;
            if (blocked.Contains(candidate) || occupied.Contains(candidate)) continue;
            if (WaterLevelConfig.IsWater(candidate)) continue;
            if (MountainCellRule.IsMountainCell(candidate) && candidate.mountainCleared) continue;
            return candidate;
        }
        return null;
    }

    // ── 脊线行走（决策 ⑰/⑱）──────────────────────────────────

    /// <summary>
    /// 从起点沿综合评分走一条脊线：长度 = [min, max] 随机；禁回访；不分岔（每次只选一个方向）；
    /// 候选为平地（高度差不足 flatHeightThreshold）时噪声随机游走（转向惩罚加权，优先直行——
    /// 2026-08-06 修订，防平地脊线蜷缩成圆形酱饼）。长度不足 minRidgeLength 返回 null。
    /// </summary>
    private static List<HexCellData> WalkRidge(MountainConfigSO config, HexCellData start,
        Func<HexCellData, List<HexCellData>> neighborsOf, System.Random random, HashSet<HexCellData> occupied)
    {
        int targetLength = random.Next(config.minRidgeLength, config.maxRidgeLength + 1);
        var path = new List<HexCellData> { start };
        var visited = new HashSet<HexCellData> { start };

        HexCellData current = start;
        Enums.HexDirection prevDir = Enums.HexDirection.None;
        while (path.Count < targetLength)
        {
            List<RidgeCandidate> candidates = CollectCandidates(current, neighborsOf, visited, occupied);
            if (candidates.Count == 0) break;

            HexCellData next = PickNext(config, current, candidates, prevDir, random, neighborsOf);
            if (next == null) break;

            path.Add(next);
            visited.Add(next);
            prevDir = DirectionFromTo(current.HexCoordinate, next.HexCoordinate);
            current = next;
        }

        return path.Count >= config.minRidgeLength ? path : null;
    }

    /// <summary>收集可延伸方向：禁回访（决策 ⑰）、不与其他山脉交叉、不涉水、跳过永久清除格。</summary>
    private static List<RidgeCandidate> CollectCandidates(HexCellData current,
        Func<HexCellData, List<HexCellData>> neighborsOf, HashSet<HexCellData> visited, HashSet<HexCellData> occupied)
    {
        var result = new List<RidgeCandidate>();
        List<HexCellData> neighbors = neighborsOf(current);
        if (neighbors == null) return result;

        foreach (HexCellData neighbor in neighbors)
        {
            if (neighbor == null) continue;
            Enums.HexDirection dir = DirectionFromTo(current.HexCoordinate, neighbor.HexCoordinate);
            if (dir == Enums.HexDirection.None) continue;
            if (visited.Contains(neighbor)) continue;
            if (occupied.Contains(neighbor)) continue;
            if (WaterLevelConfig.IsWater(neighbor)) continue;
            if (MountainCellRule.IsMountainCell(neighbor) && neighbor.mountainCleared) continue;
            result.Add(new RidgeCandidate(neighbor, dir));
        }
        return result;
    }

    /// <summary>
    /// 按综合评分加权随机选下一步（决策 ⑰：w1·高度排名 + w2·两侧落差 − w3·转向惩罚，全部确定性）。
    /// 平坦区（候选高度差 &lt; flatHeightThreshold，决策 ⑱ 陡坡阈值 = 1 级）退化为噪声随机游走；
    /// 【2026-08-06 修订】平坦区游走同样按转向惩罚加权（优先直行/缓弯）——均匀随机会让脊线
    /// 在平地蜷缩成团，再经宽度化膨胀成圆形"酱饼"，丧失山脉的长条形态。
    /// </summary>
    private static HexCellData PickNext(MountainConfigSO config, HexCellData current,
        List<RidgeCandidate> candidates, Enums.HexDirection prevDir, System.Random random,
        Func<HexCellData, List<HexCellData>> neighborsOf)
    {
        // 平坦区判定：候选间最大高度差不足 → 噪声随机游走（转向惩罚加权）
        float minH = float.MaxValue, maxH = float.MinValue;
        foreach (RidgeCandidate c in candidates)
        {
            minH = Mathf.Min(minH, c.Cell.Height);
            maxH = Mathf.Max(maxH, c.Cell.Height);
        }
        if (maxH - minH < config.flatHeightThreshold)
        {
            float flatTotal = 0f;
            foreach (RidgeCandidate c in candidates)
            {
                int turn = prevDir == Enums.HexDirection.None ? 0 : TurnAmount(prevDir, c.Direction);
                c.Weight = ComputeFlatWalkWeight(config.scoreTurnPenalty, turn);
                flatTotal += c.Weight;
            }
            double rf = random.NextDouble() * flatTotal;
            foreach (RidgeCandidate c in candidates)
            {
                rf -= c.Weight;
                if (rf <= 0d) return c.Cell;
            }
            return candidates[candidates.Count - 1].Cell;
        }

        float heightSpan = Mathf.Max(1e-4f, maxH - minH);
        foreach (RidgeCandidate c in candidates)
        {
            // 高度排名（决策 ⑰）：候选内归一化高度
            float rankNorm = (c.Cell.Height - minH) / heightSpan;

            // 两侧落差：候选方向走廊两侧（current 的 dir±1 邻居）相对候选的低矮程度（山脊性）
            float crest = CrestScore(current, c, neighborsOf);

            // 转向惩罚：转弯越大惩罚越大（180° 已被禁回访过滤）
            int turn = prevDir == Enums.HexDirection.None ? 0 : TurnAmount(prevDir, c.Direction);
            float turnNorm = turn / 3f;

            float raw = config.scoreHeightWeight * rankNorm
                      + config.scoreDropWeight * crest
                      - config.scoreTurnPenalty * turnNorm;
            c.Weight = Mathf.Exp(raw);
        }

        float total = 0f;
        foreach (RidgeCandidate c in candidates) total += c.Weight;
        double r = random.NextDouble() * total;
        foreach (RidgeCandidate c in candidates)
        {
            r -= c.Weight;
            if (r <= 0d) return c.Cell;
        }
        return candidates[candidates.Count - 1].Cell;
    }

    /// <summary>
    /// 山脊性评分：候选 N 相对走廊两侧（current 的 dir±1 邻居）的高度差之和，归一化到 [0,1]。
    /// 两侧越高越像山脊 → 得分低；候选越高于两侧 → 得分高。
    /// </summary>
    private static float CrestScore(HexCellData current, RidgeCandidate candidate,
        Func<HexCellData, List<HexCellData>> neighborsOf)
    {
        float score = 0f;
        int sides = 0;
        int dir = (int)candidate.Direction;
        foreach (int sideOffset in new[] { -1, 1 })
        {
            Vector3 sideHex = current.HexCoordinate + DirectionDeltas[(dir + sideOffset + 6) % 6];
            HexCellData side = FindNeighborByHex(current, sideHex, neighborsOf);
            if (side == null) continue;
            score += candidate.Cell.Height - side.Height;
            sides++;
        }
        if (sides == 0) return 0f;
        return Mathf.Clamp01(score / Mathf.Max(1f, sides * 2f));
    }

    private static HexCellData FindNeighborByHex(HexCellData current, Vector3 hex,
        Func<HexCellData, List<HexCellData>> neighborsOf)
    {
        List<HexCellData> neighbors = neighborsOf(current);
        if (neighbors == null) return null;
        foreach (HexCellData n in neighbors)
        {
            if (n != null && n.HexCoordinate == hex) return n;
        }
        return null;
    }

    // ── 数据固化（决策 ②）────────────────────────────────────

    private static MountainRidgeData WriteRidgeData(MountainConfigSO config, List<HexCellData> path,
        int ridgeId, float cellDist, System.Random random, float heightScale)
    {
        int length = path.Count;
        // 决策 ㉔：H_max = clamp(baseH + k·(len − minLen), minH, maxH) × heightScale
        // （heightScale = 地图设置 SO 的全局高度缩放，2026-08-06；clamp 后缩放，允许突破 maxHeight）
        float hMax = Mathf.Clamp(
            config.baseHeight + config.heightPerLength * (length - config.minRidgeLength),
            config.minHeight, config.maxHeight) * Mathf.Max(0.01f, heightScale);
        float innerRadius = cellDist * 0.5f;

        var ridge = new MountainRidgeData
        {
            ridgeId = ridgeId,
            seed = random.Next(),
            length = length,
            widthRadius = config.widthRadius,
            gamma = config.gamma,
            hMax = hMax,
            ridgeNoiseAmplitude = config.ridgeNoiseAmplitude,
            cellNoiseScale = config.cellNoiseScale,
            minVisibleHeight = config.minVisibleHeight,
            maxSlope = config.maxSlopeRatio * cellDist,
            xzPerturb = config.xzPerturbRatio * innerRadius,
            peakEccentricMin = config.peakEccentricMinRatio * innerRadius,
            peakEccentricMax = config.peakEccentricMaxRatio * innerRadius,
        };

        for (int i = 0; i < path.Count; i++)
        {
            HexCellData cell = path[i];
            cell.landForm = config.mountainLandForm;
            cell.mountainRidge = ridge;
            cell.mountainRidgeStatus = Enums.MountainRidgeStatus.RidgeCell;
            cell.mountainDistToRidge = 0f;
            cell.mountainPosAlongRidge = i; // 相邻脊线格间距 = 1 格距
            cell.RidgeDirectionA = i > 0
                ? DirectionFromTo(path[i - 1].HexCoordinate, path[i].HexCoordinate)
                : Enums.HexDirection.None;
            cell.RidgeDirectionB = i < path.Count - 1
                ? DirectionFromTo(path[i].HexCoordinate, path[i + 1].HexCoordinate)
                : Enums.HexDirection.None;
            cell.mountainCleared = false;
            cell.movementCost = MountainCellRule.DeriveMovementCost(cell);
            ridge.ridgeHexes.Add(cell.HexCoordinate);
        }
        ridge.mountainCellCount = path.Count;
        return ridge;
    }

    // ── 宽度化（决策 ⑯/⑱/㉑）──────────────────────────────────

    /// <summary>
    /// 脊线两侧宽度化：到脊线折线 XZ 距离 ≤ widthRadius 的合格格成为低矮坡面格
    /// （决策 ㉑：禁用含 Y 的三维距离）。与脊线格共享同一固化参数快照。
    /// </summary>
    private static void WidenMountain(MountainConfigSO config, MountainRidgeData ridge,
        List<HexCellData> path, IReadOnlyList<HexCellData> cells,
        Func<HexCellData, List<HexCellData>> neighborsOf, HashSet<HexCellData> occupied, float cellDist)
    {
        if (config.widthRadius <= 0f || path.Count < 2) return;

        var poly = new Vector2[path.Count];
        for (int i = 0; i < path.Count; i++)
            poly[i] = new Vector2(path[i].CenterWorldCoordinate.x, path[i].CenterWorldCoordinate.z);

        foreach (HexCellData cell in cells)
        {
            if (cell == null) continue;
            if (occupied.Contains(cell)) continue;
            if (cell.mountainCleared) continue;
            if (WaterLevelConfig.IsWater(cell)) continue;

            float d = PointToPolylineDistance(
                new Vector2(cell.CenterWorldCoordinate.x, cell.CenterWorldCoordinate.z), poly, out float arcLength);
            float dCells = d / cellDist;
            if (dCells > config.widthRadius + 1e-4f) continue;

            cell.landForm = config.mountainLandForm;
            cell.mountainRidge = ridge;
            cell.mountainRidgeStatus = Enums.MountainRidgeStatus.SlopeCell;
            cell.mountainDistToRidge = dCells;
            cell.mountainPosAlongRidge = arcLength / cellDist;
            cell.RidgeDirectionA = Enums.HexDirection.None;
            cell.RidgeDirectionB = Enums.HexDirection.None;
            cell.mountainCleared = false;
            cell.movementCost = MountainCellRule.DeriveMovementCost(cell);
            occupied.Add(cell);
            ridge.mountainCellCount++;
        }
    }

    // ── 工具 ─────────────────────────────────────────────────

    /// <summary>点 P 到折线的最小 XZ 距离，并输出投影点处的累计弧长（沿折线）。</summary>
    public static float PointToPolylineDistance(Vector2 p, Vector2[] poly, out float arcLength)
    {
        float best = float.MaxValue;
        arcLength = 0f;
        float acc = 0f;
        for (int i = 0; i < poly.Length - 1; i++)
        {
            Vector2 a = poly[i], b = poly[i + 1];
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            Vector2 proj = a + t * ab;
            float d = Vector2.Distance(p, proj);
            if (d < best)
            {
                best = d;
                arcLength = acc + t * Mathf.Sqrt(len2);
            }
            acc += Mathf.Sqrt(len2);
        }
        return best;
    }

    /// <summary>相邻格中心 XZ 距离（世界单位），作为格距单位。取第一对有距离的相邻格。</summary>
    private static float ComputeCellDistance(IReadOnlyList<HexCellData> cells,
        Func<HexCellData, List<HexCellData>> neighborsOf)
    {
        foreach (HexCellData cell in cells)
        {
            if (cell == null) continue;
            List<HexCellData> neighbors = neighborsOf(cell);
            if (neighbors == null) continue;
            foreach (HexCellData n in neighbors)
            {
                if (n == null) continue;
                float dist = Vector2.Distance(
                    new Vector2(cell.CenterWorldCoordinate.x, cell.CenterWorldCoordinate.z),
                    new Vector2(n.CenterWorldCoordinate.x, n.CenterWorldCoordinate.z));
                if (dist > 0f) return dist;
            }
        }
        return 0f;
    }

    /// <summary>把起点周边 radius 圈（六边形 BFS）加入禁区，防止山脉粘连。</summary>
    private static void MarkBlocked(HashSet<HexCellData> blocked, HexCellData center, int radius,
        Func<HexCellData, List<HexCellData>> neighborsOf)
    {
        if (radius <= 0) return;
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

            List<HexCellData> neighbors = neighborsOf(current);
            if (neighbors == null) continue;
            foreach (HexCellData neighbor in neighbors)
            {
                if (neighbor == null || distances.ContainsKey(neighbor)) continue;
                distances[neighbor] = dist + 1;
                blocked.Add(neighbor);
                frontier.Enqueue(neighbor);
            }
        }
    }

    /// <summary>两个相邻六边形坐标之间的方向；不相邻返回 None。</summary>
    public static Enums.HexDirection DirectionFromTo(Vector3 fromHex, Vector3 toHex)
    {
        Vector3 delta = toHex - fromHex;
        for (int i = 0; i < DirectionDeltas.Length; i++)
        {
            if (delta == DirectionDeltas[i]) return (Enums.HexDirection)i;
        }
        return Enums.HexDirection.None;
    }

    /// <summary>两个方向的夹角（0=直行，1=60°，2=120°，3=180°）。</summary>
    private static int TurnAmount(Enums.HexDirection from, Enums.HexDirection to)
    {
        int d = Mathf.Abs((int)from - (int)to) % 6;
        return Mathf.Min(d, 6 - d);
    }

    /// <summary>
    /// 平坦区候选权重（2026-08-06 决策 ⑰ 修订）：exp(-转向惩罚 × 转向量)，
    /// 转向量 0=直行 / 1=60° / 2=120°（180° 已被禁回访过滤）。纯函数，供单测锁定直行偏好。
    /// </summary>
    public static float ComputeFlatWalkWeight(float scoreTurnPenalty, int turn)
    {
        return Mathf.Exp(-Mathf.Max(0f, scoreTurnPenalty) * Mathf.Max(0, turn));
    }

    private sealed class RidgeCandidate
    {
        public readonly HexCellData Cell;
        public readonly Enums.HexDirection Direction;
        public float Weight;

        public RidgeCandidate(HexCellData cell, Enums.HexDirection direction)
        {
            Cell = cell;
            Direction = direction;
        }
    }

    // ── 调试对照（绕过正常生成规律；正常代码保留不删）──────────────

    /// <summary>
    /// 【调试对照】同图生成两座对照山体，供视觉对比"单格山 vs 连续山脊"：
    /// A = 单个山脉地块（1 格脊线；WidenMountain 对 path.Count &lt; 2 直接返回 ⇒ 天然无坡面格）；
    /// B = 一条直的山脊（固定方向直线行走 debugStraightRidgeLength 格，不做宽度化，
    /// 因而 Debug Straight Ridge Length = n 时严格只占 n 个山格）。
    /// B 起点距 A ≥ length+3 格（六边形距离），保证两座山体同图分离、互不遮挡。
    /// 确定性：锚点与方向扫描均来自传入随机流，同 seed 结果一致（决策 ㉓）；
    /// 数据固化/几何/玩法规则与正常生成共用 WriteRidgeData；仅跳过 WidenMountain，便于精确比较
    /// “单格山”与“n 个脊线格首尾连接”的几何效果。正常生成路径仍保留宽度化。
    /// </summary>
    private static List<MountainRidgeData> GenerateDebugComparison(
        MountainConfigSO config, IReadOnlyList<HexCellData> cells,
        Func<HexCellData, List<HexCellData>> neighborsOf, System.Random random, float heightScale)
    {
        float cellDist = ComputeCellDistance(cells, neighborsOf);
        if (cellDist <= 0f) cellDist = 1f;
        var occupied = new HashSet<HexCellData>();
        var results = new List<MountainRidgeData>();
        int nextRidgeId = 1;

        // A：单个山脉地块（1 格脊线，无坡面格）
        HexCellData single = PickDebugAnchor(cells, random, occupied, null, 0);
        if (single != null)
        {
            MountainRidgeData ridge = WriteRidgeData(config,
                new List<HexCellData> { single }, nextRidgeId++, cellDist, random, heightScale);
            occupied.Add(single);
            results.Add(ridge);
        }

        // B：一条严格占 length 格的直脊线（不宽度化；与 A 保持距离，便于同图对照）
        int length = Mathf.Max(2, config.debugStraightRidgeLength);
        for (int attempt = 0; attempt < 200 && results.Count < 2; attempt++)
        {
            HexCellData start = PickDebugAnchor(cells, random, occupied, single, length + 3);
            if (start == null) break;
            int dirOffset = random.Next(6);
            for (int k = 0; k < 6; k++)
            {
                Enums.HexDirection dir = (Enums.HexDirection)((dirOffset + k) % 6);
                List<HexCellData> path = TryBuildStraightPath(start, dir, length, neighborsOf, occupied);
                if (path == null) continue;
                MountainRidgeData ridge = WriteRidgeData(config, path, nextRidgeId++, cellDist, random, heightScale);
                foreach (HexCellData cell in path) occupied.Add(cell);
                results.Add(ridge);
                break;
            }
        }
        return results;
    }

    /// <summary>调试锚点：非水域、非占用、非永久清除；reference 非空时要求其六边形距离 ≥ minHexDistance。</summary>
    private static HexCellData PickDebugAnchor(IReadOnlyList<HexCellData> cells, System.Random random,
        HashSet<HexCellData> occupied, HexCellData reference, int minHexDistance)
    {
        int maxAttempts = Mathf.Max(1, cells.Count * 2);
        for (int i = 0; i < maxAttempts; i++)
        {
            HexCellData candidate = cells[random.Next(cells.Count)];
            if (candidate == null || occupied.Contains(candidate)) continue;
            if (WaterLevelConfig.IsWater(candidate)) continue;
            if (MountainCellRule.IsMountainCell(candidate) && candidate.mountainCleared) continue;
            if (reference != null && HexDistance(candidate.HexCoordinate, reference.HexCoordinate) < minHexDistance)
                continue;
            return candidate;
        }
        return null;
    }

    /// <summary>从 start 沿固定方向直线取 length 格；任一格非法（出界/水/占用/清除）返回 null。</summary>
    private static List<HexCellData> TryBuildStraightPath(HexCellData start, Enums.HexDirection dir, int length,
        Func<HexCellData, List<HexCellData>> neighborsOf, HashSet<HexCellData> occupied)
    {
        var path = new List<HexCellData> { start };
        HexCellData current = start;
        while (path.Count < length)
        {
            HexCellData next = null;
            List<HexCellData> neighbors = neighborsOf(current);
            if (neighbors != null)
            {
                foreach (HexCellData n in neighbors)
                {
                    if (n != null && DirectionFromTo(current.HexCoordinate, n.HexCoordinate) == dir)
                    {
                        next = n;
                        break;
                    }
                }
            }
            if (next == null) return null;
            if (occupied.Contains(next)) return null;
            if (WaterLevelConfig.IsWater(next)) return null;
            if (MountainCellRule.IsMountainCell(next) && next.mountainCleared) return null;
            path.Add(next);
            current = next;
        }
        return path;
    }

    /// <summary>六边形（cube 坐标）距离。</summary>
    private static int HexDistance(Vector3 a, Vector3 b)
    {
        return Mathf.RoundToInt(
            (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f);
    }
}
