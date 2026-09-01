using System.Collections.Generic;
using UnityEngine;

namespace UI.PlacementMask
{
    //****************************************
    // 不可放置区域红色遮罩 · 几何算法（纯函数，无 Unity 场景依赖）。
    //
    // 职责（对应实现方案 §3.2 / §3.5 / §3.6 / §3.7）：
    //   1) 把不可放置格集合按六方向邻接关系分成多个连通区域；
    //   2) 对每个区域，用「六边形邻接边界」求外轮廓多边形（世界空间 XZ）——
    //      通过收集边界边（邻居不在区域内的那条外圈边）并首尾相接成环，
    //      天然覆盖地块之间的矩形/三角过渡区，并正确识别凹形与洞；
    //   3) Catmull-Rom 平滑 + 去退化点；
    //   4) 简易自交检测 + Ear Clipping 三角化。
    //
    // 说明：本类不依赖相机与 Canvas，只产出「世界空间 XZ 轮廓环 / 三角化索引」，
    // 屏幕空间投影与 UI mesh 由 PlacementRangeMaskUI / PlacementRangeMaskGraphic 负责。
    // 方向索引严格 0..5（NE,E,SE,SW,W,NW），不含 None（Enums.cs:13-16）。
    //****************************************
    public static class PlacementMaskGeometry
    {
        // 六边形 6 个外圈角点相对格心的方位角（度）。Unity 六边形为「尖顶」布局：
        // 角点在 0/60/.../300 度（世界 XZ 平面，+Z 为 0 度，顺时针）。
        // 与 MeshDataGenerator 的 solid 外圈次序无关——此处仅用于生成覆盖用的外轮廓角环。
        private static readonly float[] CornerAnglesDeg = { 0f, 60f, 120f, 180f, 240f, 300f };

        // 六方向（索引 0..5）对应的两个相邻角点索引：方向 d 的公共边由角点 (d, d+1) 组成。
        // 用于判定「某方向邻居不在区域内 → 该侧 (角点d, 角点d+1) 是边界边」。
        // 角点次序与 CornerAnglesDeg 一致，方向次序与 Enums.HexDirection(NE,E,SE,SW,W,NW) 一致。
        // NE 在 +Z 偏东 → 角点 0(0°) 与 1(60°)；依此顺时针。
        private static readonly int[,] DirEdgeCorners =
        {
            { 0, 1 }, // NE
            { 1, 2 }, // E
            { 2, 3 }, // SE
            { 3, 4 }, // SW
            { 4, 5 }, // W
            { 5, 0 }, // NW
        };

        public sealed class Region
        {
            public readonly List<HexCellData> Cells = new List<HexCellData>();
            // 一个区域可能产出多个轮廓环（外环 + 洞环）；初版只取外环，洞在上层按需处理。
            public readonly List<List<Vector3>> OutlinesWorld = new List<List<Vector3>>();
        }

        /// <summary>
        /// 按六方向邻接把不可放置格分成连通区域（洪泛填充）。
        /// neighborOf: (cell, dir) → 邻居格；仅当邻居也在 unplaceable 集合内才视为连通。
        /// </summary>
        public static List<Region> GroupIntoRegions(
            IReadOnlyList<HexCellData> unplaceable,
            System.Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf)
        {
            var regions = new List<Region>();
            if (unplaceable == null || unplaceable.Count == 0 || neighborOf == null)
                return regions;

            var inSet = new HashSet<HexCellData>();
            foreach (var c in unplaceable)
                if (c != null) inSet.Add(c);

            var visited = new HashSet<HexCellData>();
            var stack = new Stack<HexCellData>();

            foreach (var start in unplaceable)
            {
                if (start == null || visited.Contains(start)) continue;

                var region = new Region();
                stack.Clear();
                stack.Push(start);
                visited.Add(start);

                while (stack.Count > 0)
                {
                    HexCellData cell = stack.Pop();
                    region.Cells.Add(cell);

                    for (int d = 0; d < 6; d++)
                    {
                        HexCellData nb = neighborOf(cell, (Enums.HexDirection)d);
                        if (nb != null && inSet.Contains(nb) && !visited.Contains(nb))
                        {
                            visited.Add(nb);
                            stack.Push(nb);
                        }
                    }
                }

                regions.Add(region);
            }

            return regions;
        }

        /// <summary>
        /// 对单个区域用「六边形邻接边界」求外轮廓环（世界空间 XZ，Y 取格心高度）。
        /// outerRadius = 六边形外接圆半径（世界单位，MapGenerationConfigSO.OuterRadius）。
        /// 返回 0..N 个闭合环（外环 + 可能的洞环）；每个环为有序顶点列表。
        /// </summary>
        public static void BuildRegionOutlines(
            Region region,
            System.Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf,
            float outerRadius)
        {
            region.OutlinesWorld.Clear();
            if (region.Cells.Count == 0) return;

            var inSet = new HashSet<HexCellData>(region.Cells);

            // 1) 收集所有边界边（有向段），用「量化后的角点坐标」作为顶点键，保证共享角点被合并。
            //    边方向统一：沿区域外侧逆时针（保持环闭合的一致缠绕）。
            var edges = new List<(long a, long b)>();
            var keyToPoint = new Dictionary<long, Vector3>();

            foreach (HexCellData cell in region.Cells)
            {
                Vector3 center = cell.RealCenterWorldCoordinate;
                for (int d = 0; d < 6; d++)
                {
                    HexCellData nb = neighborOf(cell, (Enums.HexDirection)d);
                    bool neighborInside = nb != null && inSet.Contains(nb);
                    if (neighborInside) continue; // 内部边，跳过

                    int cA = DirEdgeCorners[d, 0];
                    int cB = DirEdgeCorners[d, 1];
                    Vector3 pA = Corner(center, cA, outerRadius);
                    Vector3 pB = Corner(center, cB, outerRadius);

                    long ka = Quantize(pA, keyToPoint);
                    long kb = Quantize(pB, keyToPoint);
                    if (ka == kb) continue;
                    edges.Add((ka, kb));
                }
            }

            if (edges.Count == 0) return;

            // 2) 把有向边按起点串成环（每个起点键映射到其后继）。边集来自闭合多边形并集，
            //    正常情况下每个顶点入度=出度；据此走出一个或多个闭合环。
            var next = new Dictionary<long, List<long>>();
            foreach (var (a, b) in edges)
            {
                if (!next.TryGetValue(a, out var list))
                {
                    list = new List<long>();
                    next[a] = list;
                }
                list.Add(b);
            }

            var used = new HashSet<(long, long)>(); // 已消费的有向边（起点键, 终点键），精确无碰撞
            foreach (var (startA, _) in edges)
            {
                if (!next.TryGetValue(startA, out var outs) || outs.Count == 0) continue;

                // 从任意尚未走过的出边起环
                long? firstNext = null;
                foreach (long candidate in outs)
                {
                    if (!used.Contains((startA, candidate))) { firstNext = candidate; break; }
                }
                if (firstNext == null) continue;

                var loopKeys = new List<long>();
                long cur = startA;
                long nxt = firstNext.Value;
                int guard = 0;
                int guardMax = edges.Count + 4;

                while (guard++ < guardMax)
                {
                    used.Add((cur, nxt));
                    loopKeys.Add(cur);

                    if (nxt == startA) break; // 闭合

                    cur = nxt;
                    if (!next.TryGetValue(cur, out var curOuts)) break;

                    long? pick = null;
                    foreach (long candidate in curOuts)
                    {
                        if (!used.Contains((cur, candidate))) { pick = candidate; break; }
                    }
                    if (pick == null) break;
                    nxt = pick.Value;
                }

                if (loopKeys.Count >= 3)
                {
                    var loop = new List<Vector3>(loopKeys.Count);
                    foreach (long k in loopKeys)
                        loop.Add(keyToPoint[k]);
                    region.OutlinesWorld.Add(loop);
                }
            }
        }

        /// <summary>
        /// Catmull-Rom 平滑：对闭合环重采样，每段插 subdivisions 个点。
        /// 先做相邻重复点去除，避免样条退化尖刺。
        /// </summary>
        public static List<Vector3> SmoothClosedLoop(List<Vector3> loop, int subdivisions, float mergeEpsilon)
        {
            var cleaned = DedupAdjacent(loop, mergeEpsilon);
            if (cleaned.Count < 4 || subdivisions <= 0)
                return cleaned;

            int n = cleaned.Count;
            var result = new List<Vector3>(n * (subdivisions + 1));

            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = cleaned[(i - 1 + n) % n];
                Vector3 p1 = cleaned[i];
                Vector3 p2 = cleaned[(i + 1) % n];
                Vector3 p3 = cleaned[(i + 2) % n];

                for (int s = 0; s < subdivisions; s++)
                {
                    float t = (float)s / subdivisions;
                    result.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }

            return result;
        }

        // ---------------- 三角化（Ear Clipping） ----------------

        /// <summary>
        /// 对闭合多边形（XZ 平面，忽略 Y）做 Ear Clipping 三角化。
        /// 返回三角形顶点索引（引用入参 poly 的顺序）；自交/退化时尽力而为，失败返回空。
        /// 调用方应保证 poly 已去重、无明显自交（见 SmoothClosedLoop + HasSelfIntersection）。
        /// </summary>
        public static List<int> Triangulate(List<Vector3> poly)
        {
            var indices = new List<int>();
            int n = poly.Count;
            if (n < 3) return indices;

            // 计算缠绕方向，统一成 CCW 以便 IsEar 判定。
            float area = SignedAreaXZ(poly);
            var order = new List<int>(n);
            if (area < 0f)
                for (int i = 0; i < n; i++) order.Add(i);
            else
                for (int i = n - 1; i >= 0; i--) order.Add(i);

            var v = new List<int>(order);
            int guard = 0;
            int guardMax = n * n + 16;

            while (v.Count > 2 && guard++ < guardMax)
            {
                bool earFound = false;
                int count = v.Count;

                for (int i = 0; i < count; i++)
                {
                    int i0 = v[(i - 1 + count) % count];
                    int i1 = v[i];
                    int i2 = v[(i + 1) % count];

                    Vector2 a = XZ(poly[i0]);
                    Vector2 b = XZ(poly[i1]);
                    Vector2 c = XZ(poly[i2]);

                    if (Cross(b - a, c - a) <= 0f) continue; // 反凸角，非耳

                    bool anyInside = false;
                    for (int j = 0; j < count; j++)
                    {
                        int vj = v[j];
                        if (vj == i0 || vj == i1 || vj == i2) continue;
                        if (PointInTriangle(XZ(poly[vj]), a, b, c)) { anyInside = true; break; }
                    }
                    if (anyInside) continue;

                    indices.Add(i0);
                    indices.Add(i1);
                    indices.Add(i2);
                    v.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound) break; // 无法继续（可能存在自交/退化），交回上层
            }

            return indices;
        }

        /// <summary>朴素 O(n^2) 自交检测：任意两条不相邻边是否相交。用于平滑后 mesh 破面预警。</summary>
        public static bool HasSelfIntersection(List<Vector3> loop)
        {
            int n = loop.Count;
            if (n < 4) return false;

            for (int i = 0; i < n; i++)
            {
                Vector2 a1 = XZ(loop[i]);
                Vector2 a2 = XZ(loop[(i + 1) % n]);
                for (int j = i + 1; j < n; j++)
                {
                    // 跳过相邻边与共享端点边
                    if (j == i) continue;
                    if ((j + 1) % n == i) continue;
                    if (i + 1 == j) continue;

                    Vector2 b1 = XZ(loop[j]);
                    Vector2 b2 = XZ(loop[(j + 1) % n]);
                    if (SegmentsIntersect(a1, a2, b1, b2)) return true;
                }
            }
            return false;
        }

        // ---------------- 内部辅助 ----------------

        /// <summary>
        /// 取单格的 6 个外圈角点（世界空间 XZ，Y=格心高度），按 CornerAnglesDeg 顺序。
        /// 供上层「逐格扇形三角化直填」使用：中心 + 相邻两角点 = 一个三角形，6 个覆盖整格。
        /// 用 OuterRadius 而非 SolidAreaRatio，天然外扩、覆盖地块间过渡区。
        /// </summary>
        public static void GetCellRingWorld(HexCellData cell, float outerRadius, List<Vector3> outRing)
        {
            outRing.Clear();
            if (cell == null) return;
            Vector3 center = cell.RealCenterWorldCoordinate;
            for (int i = 0; i < 6; i++)
                outRing.Add(Corner(center, i, outerRadius));
        }

        private static Vector3 Corner(Vector3 center, int cornerIndex, float outerRadius)
        {
            float rad = CornerAnglesDeg[cornerIndex] * Mathf.Deg2Rad;
            // +Z 为 0 度、顺时针增大 → x = sin, z = cos。
            return new Vector3(
                center.x + outerRadius * Mathf.Sin(rad),
                center.y,
                center.z + outerRadius * Mathf.Cos(rad));
        }

        // 世界坐标量化为整数键（0.01 单位精度），共享角点合并为同一顶点。
        private const float QuantScale = 100f;

        private static long Quantize(Vector3 p, Dictionary<long, Vector3> table)
        {
            long qx = (long)Mathf.Round(p.x * QuantScale);
            long qz = (long)Mathf.Round(p.z * QuantScale);
            long key = (qx & 0xFFFFFFFFL) << 32 | (qz & 0xFFFFFFFFL);
            if (!table.ContainsKey(key)) table[key] = p;
            return key;
        }

        private static List<Vector3> DedupAdjacent(List<Vector3> loop, float epsilon)
        {
            var result = new List<Vector3>(loop.Count);
            if (loop.Count == 0) return result;

            float eps2 = epsilon * epsilon;
            foreach (var p in loop)
            {
                if (result.Count == 0 || (XZ(p) - XZ(result[result.Count - 1])).sqrMagnitude > eps2)
                    result.Add(p);
            }
            // 首尾重合去除
            if (result.Count > 1 && (XZ(result[0]) - XZ(result[result.Count - 1])).sqrMagnitude <= eps2)
                result.RemoveAt(result.Count - 1);
            return result;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static float SignedAreaXZ(List<Vector3> poly)
        {
            float area = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = XZ(poly[i]);
                Vector2 b = XZ(poly[(i + 1) % n]);
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        private static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = Cross(p4 - p3, p1 - p3);
            float d2 = Cross(p4 - p3, p2 - p3);
            float d3 = Cross(p2 - p1, p3 - p1);
            float d4 = Cross(p2 - p1, p4 - p1);

            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
                return true;

            return false;
        }
    }
}
