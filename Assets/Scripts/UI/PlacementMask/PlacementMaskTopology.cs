using System.Collections.Generic;
using UnityEngine;

namespace UI.PlacementMask
{
    //****************************************
    // 不可放置区域遮罩 · 拓扑层（纯几何，无场景依赖）。
    //
    // 【角点身份用立方坐标三元组，不用浮点坐标】
    // 六边形网格中每个角点恰由 3 个互为邻居的格共享。把这 3 个立方坐标排序后作为角点键，
    // 「是否同一角点」就是整数相等判定，与世界坐标的噪声扰动彻底无关。
    //
    // 这是前两版轮廓追踪失败的根因修复。旧实现（PlacementMaskGeometry 旧 Quantize）
    // 按 0.01 精度量化世界坐标来焊接角点，但 RealCenterWorldCoordinate 被
    // HexMetrics.Perturb 逐格独立推开 ±0.2（HexMetrics.cs:42-43），相邻格的公共角点
    // 实际相差 0.1~0.4（六边形半径才 3）→ 永远焊不到一起 → 内部边一条也剔不掉
    // → 邻接表退化成 N 个孤立六环，追踪只能得到单格小六边形。
    // 同一个扰动也让逐格填充之间出现缝隙，那正是「马赛克拼接」网格纹的来源
    //（Stencil 只能消重叠，对缝隙无能为力）。
    //
    // 【度数不变量：不存在 T 型交叉】
    // 任一角点处 3 格贡献 AB/BC/CA 三条边，按「在集合内的格数」统计边界边度数：
    //   0 格 → 0 ； 1 格 → 2 ； 2 格 → 2 ； 3 格 → 0
    // 恒为 0 或 2，故边界必然是若干条简单闭环。追踪只需「走向非来路的那个邻居」，
    // 不需要左手法则或转角比较——旧版脆弱性的来源被整体消除。
    //
    // 产物同时供两用：Loops 出边界闭环（拟合后**填充与描边共用**），
    // CellCorners + CellCenterWorld 仍保留供诊断/测试用。
    // 角点按立方坐标身份去重，相邻格共用同一顶点 → 环上不会出现本该重合却错开的点
    //（那正是旧版马赛克缝隙与轮廓追踪失败的根源）。
    //****************************************
    public static class PlacementMaskTopology
    {
        // 立方坐标方向偏移（索引 0..5 = NE,E,SE,SW,W,NW），镜像 HexMapService.cs:162-167。
        // 必须用算术偏移而非 IMapDataService.GetNeighbor：图外邻居同样参与角点身份，
        // 而 GetNeighbor 对图外返回 null，拿不到坐标。
        private static readonly int[,] DirCube =
        {
            {  0, -1,  1 }, // NE
            {  1, -1,  0 }, // E
            {  1,  0, -1 }, // SE
            {  0,  1, -1 }, // SW
            { -1,  1,  0 }, // W
            { -1,  0,  1 }, // NW
        };

        // 角点相对格心的方位角（度）：+Z 为 0°，顺时针。与「方向 d 的公共边 = 角点 d 与 d+1」
        // 配套，等价于 MeshDataGenerator.ExtractSphereOfInfluenceBoundary 的
        // NE→(1,2) / E→(2,3) / … 1-based 映射（MeshDataGenerator.cs:4881-4886）。
        private static readonly float[] CornerAnglesDeg = { 0f, 60f, 120f, 180f, 240f, 300f };

        /// <summary>
        /// 一次构建的遮罩拓扑。索引口径：
        ///   CellCenterWorld[i]           = 第 i 个有效格的格心（世界，未扰动）
        ///   CellCorners[i*6 + k]         = 第 i 个格的角点 k 在 CornerWorld 中的下标
        ///   Loops[j]                     = 第 j 条边界闭环（CornerWorld 下标序列）
        /// Loops 含外环与洞环（洞 = 被不可放置格包围的可放置格孤岛），二者都值得描边。
        /// </summary>
        public sealed class Topology
        {
            public readonly List<Vector3> CornerWorld = new List<Vector3>();
            public readonly List<Vector3> CellCenterWorld = new List<Vector3>();
            public readonly List<int> CellCorners = new List<int>();
            public readonly List<List<int>> Loops = new List<List<int>>();

            public int CellCount => CellCenterWorld.Count;
        }

        /// <summary>
        /// 由「在集合内的格」构建拓扑。outerRadius = MapGenerationConfigSO.OuterRadius，
        /// elevationStep = MapGenerationConfigSO.elevationStep（Height→世界Y 换算）。
        /// </summary>
        public static Topology Build(IReadOnlyList<HexCellData> cells, float outerRadius, float elevationStep)
        {
            var topo = new Topology();
            if (cells == null || cells.Count == 0) return topo;

            var inSet = new HashSet<(int, int)>();
            foreach (HexCellData c in cells)
                if (c != null) inSet.Add(Axial(c));

            var cornerIndex = new Dictionary<(int, int, int, int, int, int), int>();
            var cornerSum = new List<Vector3>();
            var cornerHits = new List<int>();
            // 边界边邻接：靠上面的度数不变量，每个角点最多 2 个邻居，两个平行数组足够。
            var adjA = new List<int>();
            var adjB = new List<int>();

            foreach (HexCellData cell in cells)
            {
                if (cell == null) continue;

                (int cx, int cz) = Axial(cell);
                Vector3 center = UnperturbedCenter(cell, elevationStep);
                topo.CellCenterWorld.Add(center);

                int baseCorner = topo.CellCorners.Count;
                for (int k = 0; k < 6; k++)
                {
                    var key = CornerKey(cx, cz, k);
                    Vector3 pos = center + CornerOffset(k, outerRadius);
                    if (!cornerIndex.TryGetValue(key, out int idx))
                    {
                        idx = cornerSum.Count;
                        cornerIndex[key] = idx;
                        cornerSum.Add(pos);
                        cornerHits.Add(1);
                        adjA.Add(-1);
                        adjB.Add(-1);
                    }
                    else
                    {
                        // 同一角点由不同格算出的 XZ 只差浮点误差；Y 因各格 Height 不同而不同，
                        // 取均值让高差处的角点高度连续（避免描边在陡坡处跳台阶）。
                        cornerSum[idx] += pos;
                        cornerHits[idx]++;
                    }
                    topo.CellCorners.Add(idx);
                }

                for (int d = 0; d < 6; d++)
                {
                    if (inSet.Contains((cx + DirCube[d, 0], cz + DirCube[d, 2]))) continue;
                    int ia = topo.CellCorners[baseCorner + d];
                    int ib = topo.CellCorners[baseCorner + (d + 1) % 6];
                    Link(adjA, adjB, ia, ib);
                    Link(adjA, adjB, ib, ia);
                }
            }

            for (int i = 0; i < cornerSum.Count; i++)
                topo.CornerWorld.Add(cornerSum[i] / cornerHits[i]);

            TraceLoops(adjA, adjB, topo.Loops);
            return topo;
        }
        // ---------------- 角点身份 ----------------

        /// <summary>
        /// 角点 k 的身份 = 共享它的 3 个格的立方坐标（本格 + 方向 k 邻居 + 方向 k+5 邻居），
        /// 按字典序排序后拼成元组。排序保证 3 个格算出同一个键。
        /// 图外邻居照常参与（用算术偏移而非 GetNeighbor），保证图边缘格的角点身份也一致。
        /// </summary>
        private static (int, int, int, int, int, int) CornerKey(int cx, int cz, int k)
        {
            int d1 = k;
            int d2 = (k + 5) % 6;
            int ax = cx, az = cz;
            int bx = cx + DirCube[d1, 0], bz = cz + DirCube[d1, 2];
            int ccx = cx + DirCube[d2, 0], ccz = cz + DirCube[d2, 2];

            // 三对 (x,z) 排序：手写三元排序，避免每个角点分配临时数组（全图 598 格 × 6 角点）。
            if (Before(bx, bz, ax, az)) { Swap(ref ax, ref az, ref bx, ref bz); }
            if (Before(ccx, ccz, bx, bz)) { Swap(ref bx, ref bz, ref ccx, ref ccz); }
            if (Before(bx, bz, ax, az)) { Swap(ref ax, ref az, ref bx, ref bz); }

            return (ax, az, bx, bz, ccx, ccz);
        }

        private static bool Before(int x1, int z1, int x2, int z2) =>
            x1 != x2 ? x1 < x2 : z1 < z2;

        private static void Swap(ref int x1, ref int z1, ref int x2, ref int z2)
        {
            int tx = x1, tz = z1;
            x1 = x2; z1 = z2;
            x2 = tx; z2 = tz;
        }

        /// <summary>立方坐标的 (x,z) 二元投影：y = -x-z 冗余，(x,z) 已唯一标识一格。</summary>
        private static (int, int) Axial(HexCellData cell) =>
            (Mathf.RoundToInt(cell.HexCoordinate.x), Mathf.RoundToInt(cell.HexCoordinate.z));

        /// <summary>
        /// 未扰动格心：CenterWorldCoordinate + Height * elevationStep，与
        /// MeshGeneratorService.SolidAreaCenterWithoutPerturb（MeshDataGenerator.cs:60-66）同式。
        ///
        /// ⚠️ 刻意不用 RealCenterWorldCoordinate：那是 Perturb 之后的值，逐格独立偏移 ±0.2，
        /// 会让本该重合的角点错开（正是马赛克缝隙与旧轮廓追踪失败的根源）。用未扰动中心
        /// 换来严格密铺；代价是遮罩边界与地形 silhouette 最多差 0.2 世界单位，半透明层上不可见。
        /// </summary>
        private static Vector3 UnperturbedCenter(HexCellData cell, float elevationStep)
        {
            Vector3 c = cell.CenterWorldCoordinate;
            return new Vector3(c.x, c.y + cell.Height * elevationStep, c.z);
        }

        private static Vector3 CornerOffset(int cornerIndex, float outerRadius)
        {
            float rad = CornerAnglesDeg[cornerIndex] * Mathf.Deg2Rad;
            return new Vector3(outerRadius * Mathf.Sin(rad), 0f, outerRadius * Mathf.Cos(rad));
        }

        // ---------------- 边界追踪 ----------------

        private static void Link(List<int> adjA, List<int> adjB, int from, int to)
        {
            if (adjA[from] == -1) { adjA[from] = to; return; }
            if (adjA[from] == to) return;
            if (adjB[from] == -1) { adjB[from] = to; return; }
            // 度数不变量（见类注释）保证走不到这里；真到了说明输入集合有重复格。
        }

        /// <summary>
        /// 把边界边串成闭环。每个角点度数恒为 0 或 2，所以「取非来路的那个邻居」即可确定性前进，
        /// 无需左手法则 / 转角比较（旧版 PickNextByLeftHand 的脆弱性正在于此）。
        /// 产出所有环（外环 + 洞环），不做面积筛选——洞同样需要描边。
        /// </summary>
        private static void TraceLoops(List<int> adjA, List<int> adjB, List<List<int>> loops)
        {
            int n = adjA.Count;
            var visited = new bool[n];

            for (int start = 0; start < n; start++)
            {
                if (visited[start] || adjA[start] == -1) continue;

                var loop = new List<int>();
                int prev = -1;
                int cur = start;

                while (cur != -1 && !visited[cur])
                {
                    visited[cur] = true;
                    loop.Add(cur);
                    int a = adjA[cur], b = adjB[cur];
                    int next = a != prev ? a : b;
                    prev = cur;
                    cur = next;
                }

                if (loop.Count >= 3) loops.Add(loop);
            }
        }

        // ---------------- 填充三角化 ----------------
        //
        // ⚠️ 这里曾有 BuildFillTriangles：用原始六边形角点逐格扇形三角化。已删除。
        // 描边走的是「简化 + 圆角」之后的路径，两者是两套不同几何 —— 凹口处描边被切到填充之外
        // → 线内侧露白；凸角处圆角切角 → 填充溢出线外。偏移量可达大半个格，调粗线盖不住。
        // 填充现由 PlacementMaskFill 对**描边同一批处理后闭环**做扫描线偶奇填充，两层逐点重合。
        // 别再加回按格填充的捷径：那等于把这个 bug 重新引入。
    }
}
