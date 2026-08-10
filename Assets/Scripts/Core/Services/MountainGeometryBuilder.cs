using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 山体几何的纯函数入口。阶段 3.1 提供确定性高度场；阶段 3.3~3.5 提供
/// solid 顶面 / rect 过渡面 / tri 封口的 Low-poly 构造（决策 ④⑤⑯⑲㉘㉙）。
/// 全部输入 = 格数据 + seed，任意 Chunk 重建生成相同几何（决策 ㉓）。
/// 山体槽 UV0 契约（阶段 4.1，权威定义见 MountainMaterialContract）：
/// UV0.x = ridgeKey01（ridgeId 散列，shader 只做极轻色相偏移，禁止作纹理平移量）；
/// UV0.y = faceTier 编码，(tier + 0.5)/3，tier ∈ {0,1,2}。普通地形 UV 逻辑禁止重解释山体槽 UV。
/// </summary>
public static class MountainGeometryBuilder
{
    private const int RidgeNoiseDomain = 0x52494447; // "RIDG"
    private const int CellNoiseDomain = 0x43454c4c;  // "CELL"

    /// <summary>山-非山公共边环点衰减比例（v1 常量 0.5，决策 ⑲；后续可入配置）。</summary>
    private const float BoundaryEdgeRatio = 0.4f;

    /// <summary>连续脊边向相邻 tri 延展的肩部高度比例（相对两脊线格平均山高）。</summary>
    private const float RidgeEdgeShoulderHeightRatio = 0.42f;

    /// <summary>脊边肩部在 tri 内沿普通格方向的深度比例；只占 tri 靠脊边的一条窄带。</summary>
    private const float RidgeEdgeShoulderDepthRatio = 0.38f;

    /// <summary>单格山体顶面（solid 扇）flat 拆分后的顶点预算 = 18 面 × 3 = 54（决策 ㉛/阶段 7.8 预算断言）。</summary>
    public const int SolidMountainFanVertexCount = 54;

    // 环扇顺序：solid 索引 0 中心 + 1..6 角点 + 7..18 边点中 18 个环点的扇序。
    private static readonly int[] RingFanOrder = { 1, 7, 8, 2, 9, 10, 3, 11, 12, 4, 13, 14, 5, 15, 16, 6, 17, 18 };

    // 每条边（7..18 边点分 6 组）：起始角点、结束角点、该边的邻居方向。
    private static readonly (int cornerA, int cornerB, Enums.HexDirection dir)[] EdgeInfo =
    {
        (1, 2, Enums.HexDirection.NE),
        (2, 3, Enums.HexDirection.E),
        (3, 4, Enums.HexDirection.SE),
        (4, 5, Enums.HexDirection.SW),
        (5, 6, Enums.HexDirection.W),
        (6, 1, Enums.HexDirection.NW),
    };

    // 角点 1..6 交汇的 3 格方向（本格 + 两个邻居方向）。
    private static readonly (Enums.HexDirection a, Enums.HexDirection b)[] CornerDirs =
    {
        (Enums.HexDirection.NE, Enums.HexDirection.NW),
        (Enums.HexDirection.NE, Enums.HexDirection.E),
        (Enums.HexDirection.E, Enums.HexDirection.SE),
        (Enums.HexDirection.SE, Enums.HexDirection.SW),
        (Enums.HexDirection.SW, Enums.HexDirection.W),
        (Enums.HexDirection.W, Enums.HexDirection.NW),
    };

    public static float ComputeMountainHeight(HexCellData cell)
    {
        if (!MountainCellRule.IsEffectiveMountainCell(cell) || cell.mountainRidge == null)
            return 0f;

        MountainRidgeData ridge = cell.mountainRidge;
        if (ridge.widthRadius <= 0f || ridge.hMax <= 0f)
            return 0f;

        float t = Mathf.Clamp01(1f - Mathf.Max(0f, cell.mountainDistToRidge) / ridge.widthRadius);
        if (t <= 0f)
            return 0f;

        float ridgeNoise = SampleRidgeNoise(ridge, cell.mountainPosAlongRidge);
        float ridgeHeight = ridge.hMax * (0.6f + Mathf.Clamp01(ridge.ridgeNoiseAmplitude) * ridgeNoise);
        float attenuation = Mathf.Pow(t, Mathf.Max(0.0001f, ridge.gamma));

        Vector3 coordinate = cell.HexCoordinate;
        float cellNoise = MountainHash.HashSigned(
            ridge.seed,
            CellNoiseDomain,
            ridge.ridgeId,
            Mathf.RoundToInt(coordinate.x),
            Mathf.RoundToInt(coordinate.y),
            Mathf.RoundToInt(coordinate.z));
        float noiseHeight = cellNoise * Mathf.Max(0f, ridge.cellNoiseScale) * ridge.hMax * t;

        return Mathf.Clamp(ridgeHeight * attenuation + noiseHeight, 0f, ridge.hMax);
    }

    public static bool HasVisibleMountain(HexCellData cell)
    {
        return MountainCellRule.IsEffectiveMountainCell(cell)
            && cell.mountainRidge != null
            && ComputeMountainHeight(cell) >= Mathf.Max(0f, cell.mountainRidge.minVisibleHeight);
    }

    public static float SampleRidgeNoise(MountainRidgeData ridge, float position)
    {
        if (ridge == null)
            return 0f;

        float clampedPosition = Mathf.Max(0f, position);
        int latticeA = Mathf.FloorToInt(clampedPosition);
        int latticeB = latticeA + 1;
        float fraction = clampedPosition - latticeA;
        float smoothFraction = fraction * fraction * (3f - 2f * fraction);
        float a = MountainHash.Hash01(ridge.seed, RidgeNoiseDomain, ridge.ridgeId, latticeA);
        float b = MountainHash.Hash01(ridge.seed, RidgeNoiseDomain, ridge.ridgeId, latticeB);
        return Mathf.Lerp(a, b, smoothFraction);
    }

    // ── 3.3 规范化边界高度（决策 ④/⑤ 细化）──────────────────────

    /// <summary>
    /// 角点规则（2026-08-06 脊线连续修订）：3 格全为有效山 ⇒
    /// 若 3 格中存在"脊线相邻对"（同一脊线的两个脊线格且沿路径相邻，<see cref="IsRidgeConsecutive"/>），
    /// 角点 = 各相邻对两格山高均值的最大值（脊脊线过点，成连续山脊）；
    /// 否则 = 三格山高均值（坡面，成坡）；否则（任一非有效山）恒 0。
    /// 【修订记录】max → 均值（防平顶台地，见续17）→ 脊线连续（防锯齿独立尖峰，见续20）：
    /// 纯均值把脊线格之间的公共角点压到"含坡面格的三格均值"，相邻脊线格主峰之间形成深谷，
    /// 整条山脉呈"每格一个尖峰"的锯齿截面（场景截图验收发现）。脊线相邻对的公共角点位于
    /// 脊脊线上，取相邻对均值后，脊线格公共边/rect 保持脊线高度 ⇒ 各主峰由折线状连续
    /// 山脊连接，仍保留峰/垭口起伏（决策 ⑱/㉔：相邻对均值 ≤ 对中较高峰，最高峰依然突出）。
    /// 规则对 3 格对称、确定性、跨 Chunk 一致（决策 ㉓）；仍要求 3 格全有效山才隆起，
    /// 含普通格交汇角点恒 0（决策 ④ 固定锚点，防裂缝约束不变）。
    /// 【2026-08-10 封闭墙鞍部修订】唯一例外：恰好两格为封闭墙脊线（closedWallCols）
    /// 连续脊线对、第三格非有效山时，角点 = 两格山高均值（鞍部）。单格宽封闭墙不存在
    /// 3 山格交汇，角点恒 0 会把墙面撕成锯齿尖牙、角点镂空见背景；抬升由
    /// BuildWallColTriangle 的鞍部三角封口（普通格表面不动，决策 ④ 锚点保持）。
    /// </summary>
    public static float CornerHeight(HexCellData a, HexCellData b, HexCellData c)
    {
        if (a == null || b == null || c == null) return 0f;
        bool visibleA = HasVisibleMountain(a);
        bool visibleB = HasVisibleMountain(b);
        bool visibleC = HasVisibleMountain(c);
        if (!visibleA || !visibleB || !visibleC)
        {
            if (!visibleA && IsWallColPair(b, c)) return WallColHeight(b, c);
            if (!visibleB && IsWallColPair(a, c)) return WallColHeight(a, c);
            if (!visibleC && IsWallColPair(a, b)) return WallColHeight(a, b);
            return 0f;
        }
        float hA = ComputeMountainHeight(a);
        float hB = ComputeMountainHeight(b);
        float hC = ComputeMountainHeight(c);
        float crest = -1f;
        if (IsRidgeConsecutive(a, b)) crest = Mathf.Max(crest, (hA + hB) * 0.5f);
        if (IsRidgeConsecutive(a, c)) crest = Mathf.Max(crest, (hA + hC) * 0.5f);
        if (IsRidgeConsecutive(b, c)) crest = Mathf.Max(crest, (hB + hC) * 0.5f);
        if (crest >= 0f) return crest;
        return (hA + hB + hC) / 3f;
    }

    /// <summary>封闭墙鞍部对：两格为同一封闭墙脊线（closedWallCols）的连续脊线格且均可见。
    /// 可见性纳入判定：任一格低于 minVisibleHeight 时角点不抬升、鞍部三角不生成，二者同源防裂缝。</summary>
    public static bool IsWallColPair(HexCellData x, HexCellData y)
    {
        return HasVisibleMountain(x) && HasVisibleMountain(y)
            && IsRidgeConsecutive(x, y) && x.mountainRidge.closedWallCols;
    }

    /// <summary>封闭墙鞍部高度 = 连续脊线对两格山高均值（与脊线边带/rect 同源）。</summary>
    public static float WallColHeight(HexCellData x, HexCellData y)
    {
        return (ComputeMountainHeight(x) + ComputeMountainHeight(y)) * 0.5f;
    }

    /// <summary>
    /// 脊线相邻对（规范化判定，与遍历顺序/Chunk 无关）：两格均为同一脊线的脊线格
    /// （ridgeId 相同）且沿脊线路径相邻。脊线格的 mountainPosAlongRidge = 路径整数索引
    /// （RidgeGenerator 写入，相邻脊线格间距 = 1）；路径禁回访且不分岔（决策 ⑱）⇒
    /// |Δs| = 1 当且仅当两格在路径上前后相邻。急转弯自触（|Δs| ≥ 2 的空间相邻）不视为
    /// 脊线相邻，不在非相邻段之间架脊桥；坡面格/不同脊线/缺失快照一律 false。
    /// </summary>
    public static bool IsRidgeConsecutive(HexCellData x, HexCellData y)
    {
        if (x == null || y == null) return false;
        if (x.mountainRidgeStatus != Enums.MountainRidgeStatus.RidgeCell
            || y.mountainRidgeStatus != Enums.MountainRidgeStatus.RidgeCell) return false;
        if (x.mountainRidge == null || y.mountainRidge == null) return false;
        if (x.mountainRidge.ridgeId != y.mountainRidge.ridgeId) return false;
        return Mathf.Abs(Mathf.Abs(x.mountainPosAlongRidge - y.mountainPosAlongRidge) - 1f) < 1e-4f;
    }

    /// <summary>角点 k（1..6）的山体隆起高度（规范化，两侧计算一致）。</summary>
    public static float CornerLift(HexCellData cell, int cornerIndex,
        Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf)
    {
        (Enums.HexDirection a, Enums.HexDirection b) = CornerDirs[cornerIndex - 1];
        return CornerHeight(cell, neighborOf(cell, a), neighborOf(cell, b));
    }

    /// <summary>
    /// 边点（solid 索引 7..18）的山体隆起高度：连续脊线对 = 两格山高均值（两个边点同高，
    /// 即使两侧第三格为普通格也在公共边中央形成窄脊桥）；其他山-山边 = lerp(两端角点高度, u∈{1/3,2/3})；
    /// 山-非山边 = hCell × 0.5（本格可见时）；否则 0。
    /// </summary>
    public static float EdgePointLift(HexCellData cell, int solidIndex,
        Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf)
    {
        int edge = (solidIndex - 7) / 2;
        (int cornerA, int cornerB, Enums.HexDirection dir) = EdgeInfo[edge];
        HexCellData neighbor = neighborOf(cell, dir);
        bool bothVisible = HasVisibleMountain(cell) && HasVisibleMountain(neighbor);
        if (bothVisible)
        {
            // 直脊线调试模式不宽度化时，公共边两端角点都因第三格为普通格而恒 0。
            // 若继续从角点插值，整条 mountain-mountain rect 会贴地，峰体彼此断开。
            // 连续脊线对的两个内部边点直接取两格高度均值：中央高、两端角点仍 0，
            // 形成不侵入侧邻普通格的窄脊桥；规则对两格对称，跨 Chunk 一致。
            if (IsRidgeConsecutive(cell, neighbor))
                return (ComputeMountainHeight(cell) + ComputeMountainHeight(neighbor)) * 0.5f;

            int uIndex = (solidIndex - 7) % 2;
            float u = uIndex == 0 ? 1f / 3f : 2f / 3f;
            float hA = CornerLift(cell, cornerA, neighborOf);
            float hB = CornerLift(cell, cornerB, neighborOf);
            return Mathf.Lerp(hA, hB, u);
        }
        return HasVisibleMountain(cell) ? ComputeMountainHeight(cell) * BoundaryEdgeRatio : 0f;
    }

    /// <summary>rect/tri 端点统一高度：角点走角点规则、边点走边点规则。</summary>
    public static float RectEndpointLift(HexCellData cell, int solidIndex,
        Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf)
    {
        return solidIndex <= 6 ? CornerLift(cell, solidIndex, neighborOf) : EdgePointLift(cell, solidIndex, neighborOf);
    }

    // ── 3.3 solid 山体（Low-poly 顶面）─────────────────────────

    /// <summary>
    /// 单格山体顶面：18 环点 + 1 主峰三角扇 → flat 拆分 54 顶点。
    /// 环点 XZ 恒取原 solid 点位，仅 Y = terrainY + 边高（决策 ④：外边界由角点规则回落 0）。
    /// 每面同 faceTier：tier = clamp(floor(面均高 / hMax × 3), 0, 2)（决策 ㉘ 色阶 3 段起步）。
    /// </summary>
    public static CellGeometry BuildSolidMountain(HexCellData cell, Vector3[] solid,
        Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf)
    {
        if (cell == null || solid == null || solid.Length < 19 || cell.mountainRidge == null)
            throw new ArgumentException("BuildSolidMountain 需要完整 solid（≥19 点）与山脉快照");

        MountainRidgeData ridge = cell.mountainRidge;
        float peakLift = ComputeMountainHeight(cell);

        var ring = new Vector3[18];
        var ringLifts = new float[18];
        var ringWeights = new HexCellData[18][];
        for (int i = 0; i < 18; i++)
        {
            int solidIndex = RingFanOrder[i];
            Vector3 basePoint = solid[solidIndex];
            float lift;
            HexCellData[] weights;
            if (solidIndex <= 6)
            {
                (Enums.HexDirection a, Enums.HexDirection b) = CornerDirs[solidIndex - 1];
                HexCellData na = neighborOf(cell, a);
                HexCellData nb = neighborOf(cell, b);
                lift = CornerHeight(cell, na, nb);
                weights = new[] { cell, na, nb };
            }
            else
            {
                (int cornerA, int cornerB, Enums.HexDirection dir) = EdgeInfo[(solidIndex - 7) / 2];
                HexCellData neighbor = neighborOf(cell, dir);
                lift = EdgePointLift(cell, solidIndex, neighborOf);
                weights = new[] { cell, neighbor };
            }
            ring[i] = new Vector3(basePoint.x, basePoint.y + lift, basePoint.z);
            ringLifts[i] = lift;
            ringWeights[i] = weights;
        }

        // 主峰（偏心，决策 ㉘）：XZ 偏心幅度 hash ∈ [peakEccentricMin, peakEccentricMax]；
        // 脊线格偏心方向沿 RidgeDirectionA/B 轴 ±hash，坡面格按 hash 选方向。
        Vector3 center = solid[0];
        Vector2 eccentric = PeakEccentricOffset(cell, neighborOf, ridge);
        Vector3 peak = new Vector3(center.x + eccentric.x, center.y + peakLift, center.z + eccentric.y);

        // 扇面 18：peak → ring[i] → ring[i+1]（向上绕序）
        var triangleIndices = new List<int>(SolidMountainFanVertexCount);
        for (int i = 0; i < 18; i++)
        {
            triangleIndices.Add(0);
            triangleIndices.Add(i + 1);
            triangleIndices.Add(i + 1 < 18 ? i + 2 : 1);
        }

        // flat 拆分 + faceTier
        var vertices = new List<Vector3>(SolidMountainFanVertexCount);
        var uvs = new List<Vector2>(SolidMountainFanVertexCount);
        var weightList = new List<HexCellData[]>(SolidMountainFanVertexCount);
        // 【阶段 5.2】动画来源：主峰/环点 = 本格权重 1（基点 = 本格 solid 顶点 Y，
        // 隆起量与 Height 无关；与 plain 地形 solid 环同模型，决策 ㉙）。
        var animSourceList = new List<MountainVertexAnimSource[]>(SolidMountainFanVertexCount);
        float ridgeKey01 = MountainMaterialContract.RidgeKey01(ridge);
        float hMax = Mathf.Max(1e-4f, ridge.hMax);
        for (int t = 0; t < triangleIndices.Count; t += 3)
        {
            int i1 = triangleIndices[t + 1], i2 = triangleIndices[t + 2];
            float avg = (peakLift + ringLifts[i1 - 1] + ringLifts[i2 - 1]) / 3f;
            int tier = Mathf.Clamp(Mathf.FloorToInt(avg / hMax * MountainMaterialContract.FaceTierCount), 0, MountainMaterialContract.FaceTierCount - 1);
            float uvY = MountainMaterialContract.EncodeFaceTier(tier);
            vertices.Add(peak); uvs.Add(new Vector2(ridgeKey01, uvY)); weightList.Add(new[] { cell });
            animSourceList.Add(MountainVertexAnimSource.Unit(cell));
            vertices.Add(ring[i1 - 1]); uvs.Add(new Vector2(ridgeKey01, uvY)); weightList.Add(ringWeights[i1 - 1]);
            animSourceList.Add(MountainVertexAnimSource.Unit(cell));
            vertices.Add(ring[i2 - 1]); uvs.Add(new Vector2(ridgeKey01, uvY)); weightList.Add(ringWeights[i2 - 1]);
            animSourceList.Add(MountainVertexAnimSource.Unit(cell));
        }

        return new CellGeometry
        {
            Vertices = vertices.ToArray(),
            UVs = uvs.ToArray(),
            Indices = LinearIndices(vertices.Count),
            Weights = weightList,
            AnimSources = animSourceList,
        };
    }

    private static Vector2 PeakEccentricOffset(HexCellData cell,
        Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf, MountainRidgeData ridge)
    {
        float magnitude = ridge.peakEccentricMin
            + MountainHash.Hash01(ridge.seed, 0x5045414b, ridge.ridgeId, cell.GenerateOrder)
            * Mathf.Max(0f, ridge.peakEccentricMax - ridge.peakEccentricMin);

        Vector2 direction;
        if (cell.mountainRidgeStatus == Enums.MountainRidgeStatus.RidgeCell)
        {
            Vector2 dirA = cell.RidgeDirectionA != Enums.HexDirection.None
                ? WorldDirection(cell, cell.RidgeDirectionA, neighborOf) : Vector2.zero;
            Vector2 dirB = cell.RidgeDirectionB != Enums.HexDirection.None
                ? WorldDirection(cell, cell.RidgeDirectionB, neighborOf) : Vector2.zero;
            Vector2 axis = dirA + dirB;
            if (axis.sqrMagnitude < 1e-6f) axis = dirA.sqrMagnitude >= dirB.sqrMagnitude ? dirA : dirB;
            if (axis.sqrMagnitude < 1e-6f) axis = Vector2.up;
            axis.Normalize();
            float sign = MountainHash.Hash01(ridge.seed, 0x5349474e, ridge.ridgeId, cell.GenerateOrder) < 0.5f ? 1f : -1f;
            direction = axis * sign;
        }
        else
        {
            int dirIndex = Mathf.FloorToInt(MountainHash.Hash01(ridge.seed, 0x44495245, ridge.ridgeId, cell.GenerateOrder) * 6f);
            if (dirIndex > 5) dirIndex = 5;
            Vector2 world = WorldDirection(cell, (Enums.HexDirection)dirIndex, neighborOf);
            direction = world.sqrMagnitude > 1e-6f ? world.normalized : Vector2.up;
        }
        return direction * magnitude;
    }

    private static Vector2 WorldDirection(HexCellData cell, Enums.HexDirection dir,
        Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf)
    {
        HexCellData neighbor = neighborOf(cell, dir);
        if (neighbor == null) return Vector2.zero;
        return new Vector2(
            neighbor.CenterWorldCoordinate.x - cell.CenterWorldCoordinate.x,
            neighbor.CenterWorldCoordinate.z - cell.CenterWorldCoordinate.z);
    }

    // ── 3.4 mountain-aware rect ───────────────────────────────

    /// <summary>
    /// 山体 rect：山-山 = Slope 直纹面（profile 长度 2，8 顶点 6 三角）；
    /// 山-普通 = **山侧半边**（profile 长度 2：山格环点 → 格界锚点；边 profile 起点 E=hA×0.5、
    /// 格界恒 0，决策 ⑲/④）+ <see cref="MountainRectBuild.PlainRect"/>（普通半边，恒 0 隆起，
    /// 由 ChunkMapRenderer 回地形槽渲染——2026-08-07 格界劈半，决策 ④ 细化）。
    /// 输出同时携带每顶点隆起（faceTier 用）；tri 复用 Rect.Profiles（审计修正 B-3；
    /// 3 山格 tri 只用山-山 rect，其 Profiles 保持 [start,end] 直 profile 不变）。
    /// </summary>
    public static MountainRectBuild BuildMountainRectData(
        HexCellData owner, HexCellData neighbor,
        Vector3[] ownerSolid, Vector3[] neighborSolid,
        Enums.HexDirection direction,
        Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf)
    {
        int[] startIndices;
        int[] endIndices;
        switch (direction)
        {
            case Enums.HexDirection.NE:
                startIndices = new[] { 1, 7, 8, 2 };
                endIndices = new[] { 5, 14, 13, 4 };
                break;
            case Enums.HexDirection.E:
                startIndices = new[] { 2, 9, 10, 3 };
                endIndices = new[] { 6, 16, 15, 5 };
                break;
            case Enums.HexDirection.SE:
                startIndices = new[] { 3, 11, 12, 4 };
                endIndices = new[] { 1, 18, 17, 6 };
                break;
            default:
                throw new ArgumentException("Unsupported mountain rect direction.");
        }

        var starts = new List<Vector3>(4);
        var ends = new List<Vector3>(4);
        var startLifts = new float[4];
        var endLifts = new float[4];
        bool ownerVisible = HasVisibleMountain(owner);
        bool neighborVisible = HasVisibleMountain(neighbor);
        for (int p = 0; p < 4; p++)
        {
            Vector3 s = ownerSolid[startIndices[p]];
            Vector3 e = neighborSolid[endIndices[p]];
            // 【2026-08-10 封闭墙鞍部】非山侧端点隆起强制 0：鞍部角点值是山体表面特征，
            // 普通侧表面（plain 半 rect / 普通格顶面）保持原始高度，由墙鞍三角在格界处闭合。
            startLifts[p] = ownerVisible ? RectEndpointLift(owner, startIndices[p], neighborOf) : 0f;
            endLifts[p] = neighborVisible ? RectEndpointLift(neighbor, endIndices[p], neighborOf) : 0f;
            starts.Add(new Vector3(s.x, s.y + startLifts[p], s.z));
            ends.Add(new Vector3(e.x, e.y + endLifts[p], e.z));
        }

        if (ownerVisible && neighborVisible)
        {
            if (IsRidgeConsecutive(owner, neighbor))
            {
                // 连续脊线 rect：四条 profile 统一增加中点，保证两端相邻 tri 可以复用同一肩点。
                // 边 profile 中点保持主脊高度；角 profile 若两端均为 0（第三格普通/越界），
                // 中点抬到 pairMean×shoulderRatio，形成中央高、侧向渐落的脊边肩部。
                float pairHeight = (ComputeMountainHeight(owner) + ComputeMountainHeight(neighbor)) * 0.5f;
                var profiles = new List<TransitionEdgeProfile>(4);
                var ridgeLifts = new float[12];
                for (int p = 0; p < 4; p++)
                {
                    Vector3 s = starts[p];
                    Vector3 e = ends[p];
                    float midLift = (startLifts[p] + endLifts[p]) * 0.5f;
                    if ((p == 0 || p == 3) && midLift < 1e-4f)
                        midLift = pairHeight * RidgeEdgeShoulderHeightRatio;
                    float baseY = (ownerSolid[startIndices[p]].y + neighborSolid[endIndices[p]].y) * 0.5f;
                    Vector3 mid = new Vector3((s.x + e.x) * 0.5f, baseY + midLift, (s.z + e.z) * 0.5f);
                    profiles.Add(new TransitionEdgeProfile(
                        Enums.TransitionEdgeType.Slope, new List<Vector3> { s, mid, e }));
                    ridgeLifts[p * 3] = startLifts[p];
                    ridgeLifts[p * 3 + 1] = midLift;
                    ridgeLifts[p * 3 + 2] = endLifts[p];
                }
                return new MountainRectBuild
                {
                    Rect = BuildRectFromProfiles(profiles),
                    VertexLifts = ridgeLifts,
                };
            }

            // 山-山：Slope 直纹面（profile 长度 2）
            RectangleTransitionMeshData rect = RectangleTransitionMesh.Build(
                starts, ends, Enums.TransitionEdgeType.Slope, 0, false);
            var lifts = new float[8];
            for (int p = 0; p < 4; p++)
            {
                lifts[p * 2] = startLifts[p];
                lifts[p * 2 + 1] = endLifts[p];
            }
            return new MountainRectBuild { Rect = rect, VertexLifts = lifts };
        }
        else
        {
            // 山-普通：格界（profile 中点 u=0.5）恒为原始高度（决策 ④ 固定锚点）。
            // 【2026-08-07 格界劈半，决策 ④ 细化】rect 在格界处劈成两件：
            // 山侧半边（山格环点 → 格界，带坡度折面）= 山体件（进山体槽）；
            // 普通半边（格界 → 普通格环点，恒 0 隆起）= PlainRect（回地形槽，地形材质/格线）。
            // 山体视觉边界收回到格界线；几何上两件在格界点严格闭合（同一 boundary 点位）。
            // 注意山格可能是 owner 也可能是 neighbor（rect 归属方向 {NE,E,SE}），山体件取山侧那半。
            var ownerHalfProfiles = new List<TransitionEdgeProfile>(4);
            var neighborHalfProfiles = new List<TransitionEdgeProfile>(4);
            for (int p = 0; p < 4; p++)
            {
                Vector3 s = starts[p];
                Vector3 e = ends[p];
                Vector3 boundary = new Vector3(
                    (s.x + e.x) * 0.5f,
                    (ownerSolid[startIndices[p]].y + neighborSolid[endIndices[p]].y) * 0.5f,
                    (s.z + e.z) * 0.5f);
                ownerHalfProfiles.Add(new TransitionEdgeProfile(
                    Enums.TransitionEdgeType.Slope, new List<Vector3> { s, boundary }));
                neighborHalfProfiles.Add(new TransitionEdgeProfile(
                    Enums.TransitionEdgeType.Slope, new List<Vector3> { boundary, e }));
            }

            RectangleTransitionMeshData ownerHalf = BuildRectFromProfiles(ownerHalfProfiles, 0f, 0.5f);
            RectangleTransitionMeshData neighborHalf = BuildRectFromProfiles(neighborHalfProfiles, 0.5f, 1f);
            if (ownerVisible)
            {
                // 山侧 = owner 半边（v ∈ [0, 0.5]）；VertexLifts 与 Rect.Vertices 平行
                var lifts = new float[8];
                for (int p = 0; p < 4; p++)
                {
                    lifts[p * 2] = startLifts[p];
                    lifts[p * 2 + 1] = 0f;
                }
                return new MountainRectBuild { Rect = ownerHalf, VertexLifts = lifts, PlainRect = neighborHalf };
            }
            else
            {
                // 山侧 = neighbor 半边（v ∈ [0.5, 1]）
                var lifts = new float[8];
                for (int p = 0; p < 4; p++)
                {
                    lifts[p * 2] = 0f;
                    lifts[p * 2 + 1] = endLifts[p];
                }
                return new MountainRectBuild { Rect = neighborHalf, VertexLifts = lifts, PlainRect = ownerHalf };
            }
        }
    }

    /// <summary>
    /// 连续脊边相邻 tri 的视觉肩部：仅当三格中恰有两个是同脊线连续 RidgeCell、第三格非有效山时生成。
    /// 原 terrain tri 保留；返回的山体 wedge 覆盖 tri 靠脊边的一条窄带，不写第三格数据、不改变占地与 collision。
    /// wedge 的脊边为 [ridgeCorner0, raisedMid, ridgeCorner1]，raisedMid 与连续脊线 rect 的角 profile
    /// 中点使用同一公式，静态/动画期间严格闭合；普通格方向在 depthRatio 处回落到原地形。
    /// </summary>
    public static CellGeometry BuildRidgeEdgeTriangleShoulder(
        HexCellData owner, HexCellData neighborA, HexCellData neighborB,
        TriangleTransitionMeshData plainTriangle)
    {
        if (owner == null || neighborA == null || neighborB == null || plainTriangle == null
            || plainTriangle.Vertices == null || plainTriangle.UVs == null)
            return null;

        HexCellData ridge0 = null, ridge1 = null, third = null;
        Vector2 uv0 = default, uv1 = default, uvThird = default;
        if (IsRidgeConsecutive(owner, neighborA) && !HasVisibleMountain(neighborB))
        {
            ridge0 = owner; ridge1 = neighborA; third = neighborB;
            uv0 = Vector2.zero; uv1 = new Vector2(1f, 0f); uvThird = new Vector2(0f, 1f);
        }
        else if (IsRidgeConsecutive(owner, neighborB) && !HasVisibleMountain(neighborA))
        {
            ridge0 = owner; ridge1 = neighborB; third = neighborA;
            uv0 = Vector2.zero; uv1 = new Vector2(0f, 1f); uvThird = new Vector2(1f, 0f);
        }
        else if (IsRidgeConsecutive(neighborA, neighborB) && !HasVisibleMountain(owner))
        {
            ridge0 = neighborA; ridge1 = neighborB; third = owner;
            uv0 = new Vector2(1f, 0f); uv1 = new Vector2(0f, 1f); uvThird = Vector2.zero;
        }
        if (ridge0 == null || !HasVisibleMountain(ridge0) || !HasVisibleMountain(ridge1)) return null;

        Vector3 p0 = FindTriangleCorner(plainTriangle, uv0);
        Vector3 p1 = FindTriangleCorner(plainTriangle, uv1);
        Vector3 pThird = FindTriangleCorner(plainTriangle, uvThird);
        float pairHeight = (ComputeMountainHeight(ridge0) + ComputeMountainHeight(ridge1)) * 0.5f;
        float shoulderLift = pairHeight * RidgeEdgeShoulderHeightRatio;
        Vector3 crest = (p0 + p1) * 0.5f;
        crest.y += shoulderLift;
        Vector3 q0 = Vector3.Lerp(p0, pThird, RidgeEdgeShoulderDepthRatio);
        Vector3 q1 = Vector3.Lerp(p1, pThird, RidgeEdgeShoulderDepthRatio);

        var sourceByPoint = new Dictionary<int, MountainVertexAnimSource[]>
        {
            [0] = MountainVertexAnimSource.Uniform(new[] { ridge0 }),
            [1] = MountainVertexAnimSource.Lerp(ridge0, ridge1, 0.5f),
            [2] = MountainVertexAnimSource.Uniform(new[] { ridge1 }),
            [3] = MountainVertexAnimSource.Lerp(ridge0, third, RidgeEdgeShoulderDepthRatio),
            [4] = MountainVertexAnimSource.Lerp(ridge1, third, RidgeEdgeShoulderDepthRatio),
        };
        var points = new List<Vector3> { p0, crest, p1, q0, q1 };
        int[] triangles =
        {
            0, 1, 3,
            1, 4, 3,
            1, 2, 4,
        };

        MountainRidgeData ridge = ridge0.mountainRidge ?? ridge1.mountainRidge;
        float hMax = ridge != null ? Mathf.Max(1e-4f, ridge.hMax) : 1f;
        int tier = Mathf.Clamp(Mathf.FloorToInt(
            shoulderLift / hMax * MountainMaterialContract.FaceTierCount), 0,
            MountainMaterialContract.FaceTierCount - 1);
        Vector2 materialUV = new Vector2(
            MountainMaterialContract.RidgeKey01(ridge), MountainMaterialContract.EncodeFaceTier(tier));

        var vertices = new List<Vector3>(9);
        var uvs = new List<Vector2>(9);
        var weights = new List<HexCellData[]>(9);
        var animSources = new List<MountainVertexAnimSource[]>(9);
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            var localIndices = new List<int>(3);
            AddUpwardTriangle(localIndices, points, a, b, c);
            for (int i = 0; i < 3; i++)
            {
                int pointIndex = localIndices[i];
                vertices.Add(points[pointIndex]);
                uvs.Add(materialUV);
                weights.Add(new[] { ridge0, ridge1, third });
                animSources.Add(sourceByPoint[pointIndex]);
            }
        }
        return new CellGeometry
        {
            Vertices = vertices.ToArray(),
            UVs = uvs.ToArray(),
            Indices = LinearIndices(vertices.Count),
            Weights = weights,
            AnimSources = animSources,
        };
    }

    private static Vector3 FindTriangleCorner(TriangleTransitionMeshData triangle, Vector2 targetUV)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < triangle.Vertices.Count && i < triangle.UVs.Count; i++)
        {
            float distance = (triangle.UVs[i] - targetUV).sqrMagnitude;
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return triangle.Vertices[best];
    }

    /// <summary>
    /// 【2026-08-10 封闭墙鞍部三角】两封闭墙脊线格（closedWallCols 连续对）+ 一格普通格的
    /// 三格交汇：原 terrain tri 只进 collision，本件进山体渲染槽。
    /// 6 顶点环 = [山角0+col, 脊边格界中点+col, 山角1+col, 脊1侧格界点, 普通角点, 脊0侧格界点]，
    /// 与山-山 rect 角 profile（[aV+col, abMid+col, bV+col]）和山-普通 rect 角 profile
    ///（[aV+col, 格界 0] + [格界 0, pV 0]）逐段共点闭合；普通格表面不动（决策 ④ 锚点保持）。
    /// col = 两格山高均值（与脊线边带同值），墙面成连续山脊、无镂空；rules 与遍历顺序/Chunk 无关。
    /// 动画来源与所接 rect 角 profile 端点逐点一致（决策 ㉙ 共享边同规则）。
    /// </summary>
    public static CellGeometry BuildWallColTriangle(
        HexCellData owner, HexCellData neighborA, HexCellData neighborB,
        TriangleTransitionMeshData plainTriangle)
    {
        if (owner == null || neighborA == null || neighborB == null || plainTriangle == null
            || plainTriangle.Vertices == null || plainTriangle.UVs == null)
            return null;

        HexCellData ridge0 = null, ridge1 = null, third = null;
        Vector2 uv0 = default, uv1 = default, uvThird = default;
        if (IsWallColPair(owner, neighborA) && !HasVisibleMountain(neighborB))
        {
            ridge0 = owner; ridge1 = neighborA; third = neighborB;
            uv0 = Vector2.zero; uv1 = new Vector2(1f, 0f); uvThird = new Vector2(0f, 1f);
        }
        else if (IsWallColPair(owner, neighborB) && !HasVisibleMountain(neighborA))
        {
            ridge0 = owner; ridge1 = neighborB; third = neighborA;
            uv0 = Vector2.zero; uv1 = new Vector2(0f, 1f); uvThird = new Vector2(1f, 0f);
        }
        else if (IsWallColPair(neighborA, neighborB) && !HasVisibleMountain(owner))
        {
            ridge0 = neighborA; ridge1 = neighborB; third = owner;
            uv0 = new Vector2(1f, 0f); uv1 = new Vector2(0f, 1f); uvThird = Vector2.zero;
        }
        if (ridge0 == null || !HasVisibleMountain(ridge0) || !HasVisibleMountain(ridge1)) return null;

        Vector3 p0 = FindTriangleCorner(plainTriangle, uv0);
        Vector3 p1 = FindTriangleCorner(plainTriangle, uv1);
        Vector3 pThird = FindTriangleCorner(plainTriangle, uvThird);
        float col = WallColHeight(ridge0, ridge1);

        // 6 顶点环：与三条相接 rect 的角 profile 逐段共点（闭合契约会）。
        Vector3 aV = new Vector3(p0.x, p0.y + col, p0.z);
        Vector3 bV = new Vector3(p1.x, p1.y + col, p1.z);
        Vector3 abMid = new Vector3((p0.x + p1.x) * 0.5f, (p0.y + p1.y) * 0.5f + col, (p0.z + p1.z) * 0.5f);
        Vector3 apBnd = new Vector3((p0.x + pThird.x) * 0.5f, (p0.y + pThird.y) * 0.5f, (p0.z + pThird.z) * 0.5f);
        Vector3 bpBnd = new Vector3((p1.x + pThird.x) * 0.5f, (p1.y + pThird.y) * 0.5f, (p1.z + pThird.z) * 0.5f);
        Vector3 pV = pThird;

        var points = new List<Vector3> { aV, abMid, bV, bpBnd, pV, apBnd };
        var pointLifts = new float[] { col, col, col, 0f, 0f, 0f };
        Vector3 centroid = (aV + abMid + bV + bpBnd + pV + apBnd) / 6f;
        float centroidLift = col * 0.5f;
        points.Add(centroid);

        var pointSources = new[]
        {
            MountainVertexAnimSource.Uniform(new[] { ridge0 }),
            MountainVertexAnimSource.Lerp(ridge0, ridge1, 0.5f),
            MountainVertexAnimSource.Uniform(new[] { ridge1 }),
            MountainVertexAnimSource.Lerp(ridge1, third, 0.5f),
            MountainVertexAnimSource.Uniform(new[] { third }),
            MountainVertexAnimSource.Lerp(ridge0, third, 0.5f),
            MountainVertexAnimSource.Uniform(new[] { ridge0, ridge1, third }),
        };

        MountainRidgeData ridge = ridge0.mountainRidge ?? ridge1.mountainRidge;
        float hMax = ridge != null ? Mathf.Max(1e-4f, ridge.hMax) : 1f;
        float ridgeKey01 = MountainMaterialContract.RidgeKey01(ridge);

        var vertices = new List<Vector3>(18);
        var uvs = new List<Vector2>(18);
        var weights = new List<HexCellData[]>(18);
        var animSources = new List<MountainVertexAnimSource[]>(18);
        var local = new List<int>(3);
        for (int i = 0; i < 6; i++)
        {
            local.Clear();
            AddUpwardTriangle(local, points, 6, i, (i + 1) % 6);
            float avgLift = (centroidLift + pointLifts[i] + pointLifts[(i + 1) % 6]) / 3f;
            int tier = Mathf.Clamp(Mathf.FloorToInt(avgLift / hMax * MountainMaterialContract.FaceTierCount), 0,
                MountainMaterialContract.FaceTierCount - 1);
            Vector2 materialUV = new Vector2(ridgeKey01, MountainMaterialContract.EncodeFaceTier(tier));
            for (int v = 0; v < 3; v++)
            {
                int pointIndex = local[v];
                vertices.Add(points[pointIndex]);
                uvs.Add(materialUV);
                weights.Add(new[] { ridge0, ridge1, third });
                animSources.Add(pointSources[pointIndex]);
            }
        }
        return new CellGeometry
        {
            Vertices = vertices.ToArray(),
            UVs = uvs.ToArray(),
            Indices = LinearIndices(vertices.Count),
            Weights = weights,
            AnimSources = animSources,
        };
    }


    /// <summary>
    /// 由预构建 profiles 构建 rect（与 RectangleTransitionMesh.Build 同绕序；UV.u = k/3）。
    /// vStart/vEnd：UV.y（profile 进度）区间——整面 rect 用 [0,1]；格界劈半后山侧半边 [0,0.5]、
    /// 普通半边 [0.5,1]，保证动画权重/材质混合坐标与整面 rect 的对应半段逐点一致。
    /// </summary>
    private static RectangleTransitionMeshData BuildRectFromProfiles(List<TransitionEdgeProfile> profiles,
        float vStart = 0f, float vEnd = 1f)
    {
        int profileLength = profiles[0].Points.Count;
        var vertices = new List<Vector3>(4 * profileLength);
        var uvs = new List<Vector2>(4 * profileLength);
        for (int k = 0; k < 4; k++)
        {
            IReadOnlyList<Vector3> points = profiles[k].Points;
            for (int j = 0; j < points.Count; j++) vertices.Add(points[j]);
            float u = k / 3f;
            for (int j = 0; j < profileLength; j++)
                uvs.Add(new Vector2(u, Mathf.Lerp(vStart, vEnd, j / (profileLength - 1f))));
        }

        var indices = new List<int>();
        for (int strip = 0; strip < 3; strip++)
        {
            int first = strip * profileLength;
            int second = (strip + 1) * profileLength;
            for (int i = 0; i < profileLength - 1; i++)
            {
                AddUpwardTriangle(indices, vertices, first + i, second + i + 1, second + i);
                AddUpwardTriangle(indices, vertices, first + i, first + i + 1, second + i + 1);
            }
        }
        return new RectangleTransitionMeshData(vertices, uvs, indices, profiles);
    }

    /// <summary>山体 rect → 渲染几何（flat 拆分 + faceTier UV + Weights）。</summary>
    public static CellGeometry RectToRender(MountainRectBuild build, HexCellData owner, HexCellData neighbor)
    {
        if (build == null || build.Rect == null)
            throw new ArgumentException("RectToRender 需要构建产物");

        RectangleTransitionMeshData raw = build.Rect;
        MountainRidgeData ridge = owner != null && owner.mountainRidge != null
            ? owner.mountainRidge
            : neighbor != null ? neighbor.mountainRidge : null;
        float ridgeKey01 = MountainMaterialContract.RidgeKey01(ridge);
        float hMax = ridge != null ? Mathf.Max(1e-4f, ridge.hMax) : 1f;

        var vertices = new List<Vector3>(raw.Indices.Count);
        var uvs = new List<Vector2>(raw.Indices.Count);
        var weights = new List<HexCellData[]>(raw.Indices.Count);
        // 【阶段 5.2】动画来源：每个顶点按 raw UV.v（profile 进度 u = j/(len-1)）写 owner/neighbor
        // 数值权重 [owner:1-u, neighbor:u]（与 plain rect 的 AppendRectAnimUV 同模型）。
        // 山-普通劈半（续22）：山侧半边 UV.y 区间 [0,0.5]（owner 为山）或 [0.5,1]（neighbor 为山），
        // 格界锚点恒 v=0.5 得 [0.5,0.5]（普通侧基础地形旧高，隆起仍 0）——与整面 rect 半段逐点一致。
        var animSourceList = new List<MountainVertexAnimSource[]>(raw.Indices.Count);
        for (int t = 0; t < raw.Indices.Count; t += 3)
        {
            int i0 = raw.Indices[t], i1 = raw.Indices[t + 1], i2 = raw.Indices[t + 2];
            float avg = (build.VertexLifts[i0] + build.VertexLifts[i1] + build.VertexLifts[i2]) / 3f;
            int tier = Mathf.Clamp(Mathf.FloorToInt(avg / hMax * MountainMaterialContract.FaceTierCount), 0, MountainMaterialContract.FaceTierCount - 1);
            float uvY = MountainMaterialContract.EncodeFaceTier(tier);
            vertices.Add(raw.Vertices[i0]); uvs.Add(new Vector2(ridgeKey01, uvY)); weights.Add(new[] { owner, neighbor });
            animSourceList.Add(MountainVertexAnimSource.Lerp(owner, neighbor, raw.UVs[i0].y));
            vertices.Add(raw.Vertices[i1]); uvs.Add(new Vector2(ridgeKey01, uvY)); weights.Add(new[] { owner, neighbor });
            animSourceList.Add(MountainVertexAnimSource.Lerp(owner, neighbor, raw.UVs[i1].y));
            vertices.Add(raw.Vertices[i2]); uvs.Add(new Vector2(ridgeKey01, uvY)); weights.Add(new[] { owner, neighbor });
            animSourceList.Add(MountainVertexAnimSource.Lerp(owner, neighbor, raw.UVs[i2].y));
        }
        return new CellGeometry
        {
            Vertices = vertices.ToArray(),
            UVs = uvs.ToArray(),
            Indices = LinearIndices(vertices.Count),
            Weights = weights,
            AnimSources = animSourceList,
        };
    }

    /// <summary>
    /// 山-普通 rect 山侧半边的材质融合几何。几何/UV0/动画来源与 RectToRender 相同，额外输出
    /// BlendData（UV4）：xy=原 rect UV；z=岩石权重。山侧环点为 1，格界为 0。
    /// </summary>
    public static CellGeometry RectToTerrainBlendRender(MountainRectBuild build,
        HexCellData owner, HexCellData neighbor)
    {
        CellGeometry geometry = RectToRender(build, owner, neighbor);
        RectangleTransitionMeshData raw = build?.Rect;
        if (geometry == null || raw == null || raw.Indices == null || raw.UVs == null)
            return geometry;

        bool mountainAtStart = HasVisibleMountain(owner) && !HasVisibleMountain(neighbor);
        var blendData = new Vector4[raw.Indices.Count];
        for (int t = 0; t < raw.Indices.Count; t++)
        {
            Vector2 rectUV = raw.UVs[raw.Indices[t]];
            float blend = mountainAtStart ? 1f - rectUV.y * 2f : rectUV.y * 2f - 1f;
            blendData[t] = new Vector4(rectUV.x, rectUV.y, Mathf.Clamp01(blend), 0f);
        }
        geometry.BlendData = blendData;
        return geometry;
    }

    // ── 3.5 tri 封口（仅 3 山格进山体槽）────────────────────────

    /// <summary>
    /// 3 山格 tri：三条角 profile 均隆起（决策 ⑤），复用 RectangleDrivenTriangleMesh +
    /// flat 拆分；3 角点高度一致（= 该交汇角点规则值：脊线相邻对均值 / 三格均值，
    /// 2026-08-06 脊线连续修订，见 CornerHeight），整个 tri 平坦，faceTier 按 H/hMax。
    /// 动画来源（阶段 5.2）：顶点从三条 mountain rect 角 profile 按位置继承来源，
    /// 不在 tri 构建末尾另算一套权重（决策 ㉙；共享端点来源集合、权重与 profile 完全一致）。
    /// </summary>
    public static CellGeometry BuildTriangleMountain(
        HexCellData owner,
        Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf,
        Func<HexCellData, Enums.HexDirection, RectangleTransitionMeshData> mountainRectOf,
        Enums.HexDirection directionA, Enums.HexDirection directionB)
    {
        HexCellData neighborA;
        HexCellData neighborB;
        TriangleTransitionMeshData raw;
        RectangleTransitionMeshData rectOwnerA;
        RectangleTransitionMeshData rectNeighbor;
        RectangleTransitionMeshData rectOwnerB;
        if (directionA == Enums.HexDirection.NE && directionB == Enums.HexDirection.E)
        {
            neighborA = neighborOf(owner, Enums.HexDirection.NE);
            neighborB = neighborOf(owner, Enums.HexDirection.E);
            rectOwnerA = mountainRectOf(owner, Enums.HexDirection.NE);
            rectNeighbor = mountainRectOf(neighborA, Enums.HexDirection.SE);
            rectOwnerB = mountainRectOf(owner, Enums.HexDirection.E);
            raw = RectangleDrivenTriangleMesh.BuildNEE(rectOwnerA, rectNeighbor, rectOwnerB);
        }
        else if (directionA == Enums.HexDirection.E && directionB == Enums.HexDirection.SE)
        {
            neighborA = neighborOf(owner, Enums.HexDirection.SE);
            neighborB = neighborOf(owner, Enums.HexDirection.E);
            rectOwnerA = mountainRectOf(owner, Enums.HexDirection.E);
            rectNeighbor = mountainRectOf(neighborA, Enums.HexDirection.NE);
            rectOwnerB = mountainRectOf(owner, Enums.HexDirection.SE);
            raw = RectangleDrivenTriangleMesh.BuildESE(rectOwnerA, rectNeighbor, rectOwnerB);
        }
        else
        {
            throw new ArgumentException("Unsupported triangle directions.");
        }

        // 【阶段 5.2】与 BuildNEE/BuildESE 相同的 3 条角 profile 来源参数化：
        // (profile, owner, neighbor, reversed)；reversed 时 u 反向（1-u），
        // 保证来源与 RectangleDrivenTriangleMesh 内部 GetProfile 的翻转一致。
        var profileSources = new List<(TransitionEdgeProfile profile, HexCellData a, HexCellData b, bool reversed)>
        {
            (rectOwnerA.Profiles[3], owner, neighborA, false),
            (rectNeighbor.Profiles[3], neighborA, neighborB, false),
            (rectOwnerB.Profiles[0], owner, neighborB, true),
        };

        float cornerH = CornerHeight(owner, neighborA, neighborB);
        float hMax = 1e-4f;
        if (owner != null && owner.mountainRidge != null) hMax = Mathf.Max(hMax, owner.mountainRidge.hMax);
        if (neighborA != null && neighborA.mountainRidge != null) hMax = Mathf.Max(hMax, neighborA.mountainRidge.hMax);
        if (neighborB != null && neighborB.mountainRidge != null) hMax = Mathf.Max(hMax, neighborB.mountainRidge.hMax);
        MountainRidgeData ridge = owner != null ? owner.mountainRidge : null;
        float ridgeKey01 = MountainMaterialContract.RidgeKey01(ridge);
        int tier = Mathf.Clamp(Mathf.FloorToInt(cornerH / hMax * MountainMaterialContract.FaceTierCount), 0, MountainMaterialContract.FaceTierCount - 1);
        float uvY = MountainMaterialContract.EncodeFaceTier(tier);

        var vertices = new List<Vector3>(raw.Indices.Count);
        var uvs = new List<Vector2>(raw.Indices.Count);
        var weights = new List<HexCellData[]>(raw.Indices.Count);
        var animSourceList = new List<MountainVertexAnimSource[]>(raw.Indices.Count);
        for (int t = 0; t < raw.Indices.Count; t++)
        {
            Vector3 vertex = raw.Vertices[raw.Indices[t]];
            vertices.Add(vertex);
            uvs.Add(new Vector2(ridgeKey01, uvY));
            weights.Add(new[] { owner, neighborA, neighborB });
            animSourceList.Add(ResolveTriangleVertexSource(vertex, profileSources, owner, neighborA, neighborB));
        }
        return new CellGeometry
        {
            Vertices = vertices.ToArray(),
            UVs = uvs.ToArray(),
            Indices = LinearIndices(vertices.Count),
            Weights = weights,
            AnimSources = animSourceList,
        };
    }

    /// <summary>
    /// 【阶段 5.2】tri 顶点 → 角 profile 来源：按位置匹配（1e-4 容差）到三条角 profile 的某个点，
    /// 返回该点的 u 值来源；匹配失败时防御性回落三格等权（正常路径不会发生）。
    /// </summary>
    private static MountainVertexAnimSource[] ResolveTriangleVertexSource(
        Vector3 vertex,
        IReadOnlyList<(TransitionEdgeProfile profile, HexCellData a, HexCellData b, bool reversed)> profileSources,
        HexCellData owner, HexCellData neighborA, HexCellData neighborB)
    {
        foreach ((TransitionEdgeProfile profile, HexCellData a, HexCellData b, bool reversed) in profileSources)
        {
            IReadOnlyList<Vector3> points = profile.Points;
            for (int j = 0; j < points.Count; j++)
            {
                if ((points[j] - vertex).sqrMagnitude < 1e-8f)
                {
                    float u = j / (float)(points.Count - 1);
                    if (reversed) u = 1f - u;
                    return MountainVertexAnimSource.Lerp(a, b, u);
                }
            }
        }
        return MountainVertexAnimSource.Uniform(new[] { owner, neighborA, neighborB });
    }

    // ── 3.8 诊断工具（决策 ㉛）─────────────────────────────────

    /// <summary>
    /// 【阶段 5.3】山体顶点动画通道（决策 ㉙）：由 CellGeometry 的逐顶点来源计算
    /// UV2 = (startY, targetY) 与 UV3 = (delayStart, delayEnd)。
    /// targetY = 最终顶点 Y；startY = targetY − Σ(weight × ΔY(cell))——
    /// 山体隆起量本身不变，只插值其所附着基础地形的高度差。
    /// delayStart/delayEnd 用同一来源集合按权重确定性混合，共享边重复顶点窗口完全一致。
    /// 纯函数：delta/delay 以委托注入（渲染端包 AnimatedChunkBuildData），测试可受控验证。
    /// </summary>
    public static void AppendMountainAnimUV(
        CellGeometry geometry,
        Func<HexCellData, float> deltaYOf,
        Func<HexCellData, float> delayOf,
        Func<HexCellData, float> delayEndOf,
        List<Vector2> uv2List, List<Vector2> uv3List)
    {
        if (geometry == null || geometry.Vertices == null || uv2List == null || uv3List == null) return;
        List<MountainVertexAnimSource[]> sources = geometry.AnimSources;
        bool hasSources = sources != null && sources.Count == geometry.Vertices.Length;
        for (int i = 0; i < geometry.Vertices.Length; i++)
        {
            float targetY = geometry.Vertices[i].y;
            MountainVertexAnimSource[] vertexSources = hasSources ? sources[i] : null;
            if (vertexSources == null || vertexSources.Length == 0)
            {
                uv2List.Add(new Vector2(targetY, targetY));
                uv3List.Add(new Vector2(0f, 1f));
                continue;
            }
            float delta = 0f, delay = 0f, delayEnd = 0f;
            foreach (MountainVertexAnimSource s in vertexSources)
            {
                if (s.Cell == null) continue;
                delta += s.Weight * deltaYOf(s.Cell);
                delay += s.Weight * delayOf(s.Cell);
                delayEnd += s.Weight * delayEndOf(s.Cell);
            }
            uv2List.Add(new Vector2(targetY - delta, targetY));
            uv3List.Add(new Vector2(delay, delayEnd));
        }
    }

    /// <summary>
    /// 【阶段 5.7】动画保守 bounds 纯函数（决策 ㉛）：覆盖所有顶点 XZ 与 UV2.x(start)/UV2.y(target)
    /// 的包络 + Y 安全余量（keep-below clip +0.02 与数值误差；默认 0.05）。
    /// CPU 动画逐帧写 mesh.vertices 不更新 bounds（源码审计修正 B-6），因此提交时预扩到动画全程：
    /// progress ∈ [0,1] 的插值顶点恒落在 [min(start,target), max(start,target)] 内 ⇒ 全部包含，
    /// 峰顶/阴影/上升全过程不会被视锥或阴影剔除。普通无动画构建不调用（RecalculateBounds 行为零变化）。
    /// </summary>
    public static Bounds ComputeConservativeAnimBounds(
        IReadOnlyList<Vector3> vertices, IReadOnlyList<Vector2> uv2s, float yMargin = 0.05f)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int count = Mathf.Min(vertices != null ? vertices.Count : 0, uv2s != null ? uv2s.Count : 0);
        for (int i = 0; i < count; i++)
        {
            Vector3 v = vertices[i];
            Vector2 channel = uv2s[i];
            min = Vector3.Min(min, new Vector3(v.x, Mathf.Min(channel.x, channel.y), v.z));
            max = Vector3.Max(max, new Vector3(v.x, Mathf.Max(channel.x, channel.y), v.z));
        }
        if (count == 0) return new Bounds(Vector3.zero, Vector3.zero);

        float margin = Mathf.Max(0f, yMargin);
        min.y -= margin;
        max.y += margin;
        return new Bounds((min + max) * 0.5f, max - min);
    }

    /// <summary>退化三角计数（XZ 投影面积 &lt; minArea）。</summary>
    public static int CountDegenerateTriangles(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, float minArea = 1e-6f)
    {
        int count = 0;
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
            float area = Mathf.Abs(((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) * 0.5f);
            if (area < minArea) count++;
        }
        return count;
    }

    /// <summary>非流形边：被超过 2 个三角面引用的边（规范化端点序）。</summary>
    public static List<(int a, int b, int usage)> FindNonManifoldEdges(IReadOnlyList<int> indices)
    {
        var usage = new Dictionary<(int, int), int>();
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            AddEdgeUsage(usage, a, b);
            AddEdgeUsage(usage, b, c);
            AddEdgeUsage(usage, c, a);
        }
        var result = new List<(int, int, int)>();
        foreach (KeyValuePair<(int, int), int> kv in usage)
        {
            if (kv.Value > 2) result.Add((kv.Key.Item1, kv.Key.Item2, kv.Value));
        }
        return result;
    }

    /// <summary>几何确定性 hash（固定 seed 重建 hash 一致，决策 ㉛）。</summary>
    public static int GeometryHash(Vector3[] vertices, int[] indices)
    {
        unchecked
        {
            int hash = 17;
            if (vertices != null)
            {
                foreach (Vector3 v in vertices)
                {
                    hash = hash * 31 + v.x.GetHashCode();
                    hash = hash * 31 + v.y.GetHashCode();
                    hash = hash * 31 + v.z.GetHashCode();
                }
            }
            if (indices != null)
            {
                foreach (int i in indices) hash = hash * 31 + i;
            }
            return hash;
        }
    }

    // ── 工具 ──────────────────────────────────────────────────

    private static int[] LinearIndices(int count)
    {
        var indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;
        return indices;
    }

    private static void AddEdgeUsage(Dictionary<(int, int), int> usage, int a, int b)
    {
        (int, int) key = a < b ? (a, b) : (b, a);
        usage.TryGetValue(key, out int count);
        usage[key] = count + 1;
    }

    /// <summary>与 RectangleTransitionMesh.AddUpwardTriangle 同绕序（面朝 +y 为正面）。</summary>
    private static void AddUpwardTriangle(List<int> indices, List<Vector3> vertices, int a, int b, int c)
    {
        if (a == b || b == c || a == c) return;
        Vector3 va = vertices[a], vb = vertices[b], vc = vertices[c];
        float signedArea = (vb.x - va.x) * (vc.z - va.z) - (vc.x - va.x) * (vb.z - va.z);
        indices.Add(a);
        if (signedArea < 0f)
        {
            indices.Add(b);
            indices.Add(c);
        }
        else
        {
            indices.Add(c);
            indices.Add(b);
        }
    }
}

/// <summary>山体几何纯函数输出：顶点/UV/索引 + 每顶点所属格权重（阶段 5 动画插值用）。</summary>
public sealed class CellGeometry
{
    public Vector3[] Vertices;
    public Vector2[] UVs;
    public int[] Indices;
    public List<HexCellData[]> Weights;
    /// <summary>
    /// 【阶段 5.1】每顶点动画来源（决策 ㉙）：与 Weights 平行、逐顶点一致；
    /// 等权归一化基线（权重总和恒 1），5.2 起按规范化共享边规则细化权重。
    /// 供阶段 5.3 计算 start/target Y 与 UV3 错峰窗口（共享边重复顶点必须来源一致）。
    /// </summary>
    public List<MountainVertexAnimSource[]> AnimSources;
    /// <summary>可选 UV4/TEXCOORD3 数据；山脚融合几何中 xy=terrain UV、z=mountain blend。</summary>
    public Vector4[] BlendData;
}

/// <summary>
/// 【阶段 5.1】山体顶点的动画来源（一个顶点可关联多格，各带权重，决策 ㉙）。
/// 一个顶点最终只取一个来源集合：权重决定其 start/target Y 与 delay window
/// 从哪些格混合而来。共享边重复顶点必须由同一规范化来源集合计算，
/// 否则动画期间出现裂缝（源码审计修正 B-4）。
/// </summary>
public readonly struct MountainVertexAnimSource
{
    public readonly HexCellData Cell;
    public readonly float Weight;

    public MountainVertexAnimSource(HexCellData cell, float weight)
    {
        Cell = cell;
        Weight = weight;
    }

    /// <summary>单格来源（权重 1）：主峰、solid 环点（基点 = 本格 solid 顶点 Y）。</summary>
    public static MountainVertexAnimSource[] Unit(HexCellData cell)
    {
        return new[] { new MountainVertexAnimSource(cell, 1f) };
    }

    /// <summary>两格线性插值来源（阶段 5.2 起 rect profile 用）：t=0 → [a,1]，t=1 → [b,1]。
    /// 与 plain rect 的 AppendRectAnimUV 同模型：profile 上的点按其进度 u 混合两端格高度差。</summary>
    public static MountainVertexAnimSource[] Lerp(HexCellData a, HexCellData b, float t)
    {
        t = Mathf.Clamp01(t);
        return new[] { new MountainVertexAnimSource(a, 1f - t), new MountainVertexAnimSource(b, t) };
    }

    /// <summary>
    /// 把格数组转为等权来源（5.1 基线；权重总和恒 1；null → null，空数组 → 空数组）。
    /// 5.2 起仅保留为防御性回退；solid/rect/tri 的正式来源见 Unit/Lerp 与 tri 继承规则。
    /// </summary>
    public static MountainVertexAnimSource[] Uniform(HexCellData[] cells)
    {
        if (cells == null) return null;
        if (cells.Length == 0) return System.Array.Empty<MountainVertexAnimSource>();
        var sources = new MountainVertexAnimSource[cells.Length];
        float weight = 1f / cells.Length;
        for (int i = 0; i < cells.Length; i++)
            sources[i] = new MountainVertexAnimSource(cells[i], weight);
        return sources;
    }

    /// <summary>诊断（决策 ㉛）：所有顶点来源非空、Cell 非 null、权重 ∈ [0,1]、总和 = 1（1e-3 容差）。</summary>
    public static bool IsValid(IReadOnlyList<MountainVertexAnimSource[]> sources)
    {
        if (sources == null) return true;
        foreach (MountainVertexAnimSource[] vertex in sources)
        {
            if (vertex == null || vertex.Length == 0) return false;
            float sum = 0f;
            foreach (MountainVertexAnimSource s in vertex)
            {
                if (s.Cell == null) return false;
                if (s.Weight < -1e-4f || s.Weight > 1f + 1e-4f) return false;
                sum += s.Weight;
            }
            if (Mathf.Abs(sum - 1f) > 1e-3f) return false;
        }
        return true;
    }
}

/// <summary>山体 rect 构建产物：原始 rect（tri 复用 profiles）+ 每顶点隆起（faceTier 用）。</summary>
public sealed class MountainRectBuild
{
    public RectangleTransitionMeshData Rect;
    public float[] VertexLifts;

    /// <summary>
    /// 山-普通边的普通半边（格界 → 普通侧环点，恒 0 隆起）：由 ChunkMapRenderer 回地形槽渲染
    /// （地形材质/格线/动画通道按普通 rect 规则），山体视觉边界收回到格界线；
    /// 山-山边为 null（整面山体）。UV.y ∈ [0.5, 1] 或 [0, 0.5]（普通侧那半的原始 profile 进度）。
    /// （2026-08-07 格界劈半，决策 ④ 细化——此前整条 rect 连面带料进山体槽，普通半边虽高度
    /// 不变但披山体材质，视觉上每个山格向外多吃一圈 rect。）
    /// </summary>
    public RectangleTransitionMeshData PlainRect;
}
