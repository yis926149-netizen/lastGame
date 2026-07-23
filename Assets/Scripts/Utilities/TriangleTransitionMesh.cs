using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TransitionEdgeProfile
{
    public Enums.TransitionEdgeType Type { get; }
    public IReadOnlyList<Vector3> Points { get; }

    public TransitionEdgeProfile(Enums.TransitionEdgeType type, IReadOnlyList<Vector3> points)
    {
        Type = type;
        Points = points ?? throw new ArgumentNullException(nameof(points));
        if (points.Count < 2)
        {
            throw new ArgumentException("An edge profile requires at least two points.", nameof(points));
        }
    }
}

public sealed class TriangleTransitionMeshData
{
    public List<Vector3> Vertices { get; }
    public List<Vector2> UVs { get; }
    public List<int> Indices { get; }

    public TriangleTransitionMeshData(List<Vector3> vertices, List<Vector2> uvs, List<int> indices)
    {
        Vertices = vertices;
        UVs = uvs;
        Indices = indices;
    }
}

public sealed class RectangleTransitionMeshData
{
    public List<Vector3> Vertices { get; }
    public List<Vector2> UVs { get; }
    public List<int> Indices { get; }
    public IReadOnlyList<TransitionEdgeProfile> Profiles { get; }

    public RectangleTransitionMeshData(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<int> indices,
        IReadOnlyList<TransitionEdgeProfile> profiles)
    {
        Vertices = vertices;
        UVs = uvs;
        Indices = indices;
        Profiles = profiles;
    }
}

public static class TriangleTransitionMesh
{
    private const float EndpointTolerance = 0.0001f;
    private const float TriangleAreaTolerance = 0.000001f;
    private const float StepSyncTolerance = 0.0001f;
    private const float LevelTolerance = 0.0005f;

    public static TriangleTransitionMeshData BuildFan(
        TransitionEdgeProfile edge0,
        TransitionEdgeProfile edge1,
        TransitionEdgeProfile edge2)
    {
        ValidateConnectedEdges(edge0, edge1, edge2);

        TriangleTransitionMeshData result;
        // 坡坡坡：三个角点组成单个平滑三角。
        if (!ContainsStep(edge0, edge1, edge2))
        {
            result = BuildSimpleTriangle(edge0, edge1, edge2);
        }
        // 同步梯对：保留原快路径（同高度侧翼台阶严格对齐）。
        else
        {
            var striped = TryBuildStriped(edge0, edge1, edge2);
            if (striped != null)
            {
                result = striped;
            }
            // 其余含梯组合：通用阶梯角剖分（替换会塌成漏斗/内凹的 center-fan）。
            else
            {
                result = BuildTerrace(edge0, edge1, edge2);
            }
        }

        var baryUVs = ComputeBarycentricUVs(result.Vertices, edge0.Points[0],
            edge0.Points[edge0.Points.Count - 1],
            edge1.Points[edge1.Points.Count - 1]);
        return new TriangleTransitionMeshData(result.Vertices, baryUVs, result.Indices);
    }

    /// <summary>
    /// 把带索引的网格展开成“每个三角形三个独立顶点”的形式，从而让 RecalculateNormals 产生逐面平坦法线。
    /// <para>
    /// 三角过渡内部各面朝向差异很大，若共享顶点（尤其 center-fan 的中心枢纽顶点被所有扇形面共享），
    /// RecalculateNormals 会把发散的面法线平均成一个被冲淡/偏斜的法线，使三角比同朝向的矩形明显偏暗、发糊。
    /// 逐面独立后，每个切面按自身真实几何法线着色 —— 得到干净的硬切面，中心不再发黑。
    /// </para>
    /// 仅在渲染阶段调用；BuildFan 仍返回带索引的几何，保证拓扑/绕序/剖分测试语义不变。UV 按展开后的顶点重新生成。
    /// </summary>
    public static TriangleTransitionMeshData ToFlatShaded(TriangleTransitionMeshData data)
    {
        var vertices = new List<Vector3>(data.Indices.Count);
        var uvs = new List<Vector2>(data.Indices.Count);
        var indices = new List<int>(data.Indices.Count);
        for (int i = 0; i < data.Indices.Count; i++)
        {
            int src = data.Indices[i];
            vertices.Add(data.Vertices[src]);
            uvs.Add(data.UVs[src]);
            indices.Add(i);
        }

        return new TriangleTransitionMeshData(
            vertices,
            uvs,
            indices);
    }

    /// <summary>
    /// 重心坐标 UV：(u,v) = (cornerA 权重, cornerB 权重)。
    /// cornerSelf 权重 = 1 - u - v。
    /// 配合 RGB 遮罩 R=1-u-v, G=u, B=v，使三角三条边各自融合到对应对角材质。
    /// </summary>
    private static List<Vector2> ComputeBarycentricUVs(
        List<Vector3> vertices, Vector3 cornerSelf, Vector3 cornerA, Vector3 cornerB)
    {
        float totalArea = Mathf.Abs(SignedAreaXZ(cornerSelf, cornerA, cornerB));
        if (totalArea < 0.0001f) totalArea = 1f;

        var uvs = new List<Vector2>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            float wA = Mathf.Abs(SignedAreaXZ(vertices[i], cornerB, cornerSelf)) / totalArea;
            float wB = Mathf.Abs(SignedAreaXZ(vertices[i], cornerSelf, cornerA)) / totalArea;
            uvs.Add(new Vector2(Mathf.Clamp01(wA), Mathf.Clamp01(wB)));
        }
        return uvs;
    }

    private static TriangleTransitionMeshData BuildCenterFan(
        TransitionEdgeProfile edge0,
        TransitionEdgeProfile edge1,
        TransitionEdgeProfile edge2)
    {
        var boundary = new List<Vector3>();
        AppendRange(boundary, edge0.Points, 0, edge0.Points.Count);
        AppendRange(boundary, edge1.Points, 1, edge1.Points.Count);
        AppendRange(boundary, edge2.Points, 1, edge2.Points.Count - 1);

        Vector3 center = (edge0.Points[0] + edge0.Points[edge0.Points.Count - 1] + edge1.Points[edge1.Points.Count - 1]) / 3f;
        var vertices = new List<Vector3>(boundary.Count + 1) { center };
        vertices.AddRange(boundary);

        var indices = new List<int>(boundary.Count * 3);
        for (int i = 0; i < boundary.Count; i++)
        {
            int current = i + 1;
            int next = (i + 1) % boundary.Count + 1;
            if (current == next)
            {
                continue;
            }

            float signedArea = SignedAreaXZ(center, vertices[current], vertices[next]);
            indices.Add(0);
            if (signedArea < 0f)
            {
                indices.Add(current);
                indices.Add(next);
            }
            else
            {
                indices.Add(next);
                indices.Add(current);
            }
        }

        return new TriangleTransitionMeshData(
            vertices,
            new List<Vector2>(UVGenerator.GeneratePlanarUV(vertices)),
            indices);
    }

    // ---------------- 通用阶梯角（BuildTerrace）----------------

    private static bool ContainsStep(
        TransitionEdgeProfile e0, TransitionEdgeProfile e1, TransitionEdgeProfile e2)
    {
        return e0.Type == Enums.TransitionEdgeType.Step
            || e1.Type == Enums.TransitionEdgeType.Step
            || e2.Type == Enums.TransitionEdgeType.Step;
    }

    // 坡坡坡：三个角点（Self / A / B）组成单个三角。
    private static TriangleTransitionMeshData BuildSimpleTriangle(
        TransitionEdgeProfile e0, TransitionEdgeProfile e1, TransitionEdgeProfile e2)
    {
        var vertices = new List<Vector3>(3)
        {
            e0.Points[0],
            e0.Points[e0.Points.Count - 1],
            e1.Points[e1.Points.Count - 1],
        };
        var indices = new List<int>(3);
        AddUpwardTriangle(indices, vertices, 0, 1, 2);
        return BuildResult(vertices, indices);
    }

    /// <summary>
    /// 通用阶梯角剖分：把含梯边的三角角落续接成连续台阶（水平踏面 + 斜向踢面），不再内凹。
    /// 思路：边界是高度单调多边形 —— 从最低角/最高角切成两条上升轨，在每个高度层为坡段插入共线点，
    /// 再按高度层逐级拉链（同级横带=踏面、跨级=踢面）。全部顶点在边界上（0 内部顶点），是合法多边形三角剖分。
    /// </summary>
    private static TriangleTransitionMeshData BuildTerrace(
        TransitionEdgeProfile e0, TransitionEdgeProfile e1, TransitionEdgeProfile e2)
    {
        var vertices = new List<Vector3>();
        AppendRange(vertices, e0.Points, 0, e0.Points.Count);
        AppendRange(vertices, e1.Points, 1, e1.Points.Count);
        AppendRange(vertices, e2.Points, 1, e2.Points.Count - 1);
        int loopCount = vertices.Count;

        int idxSelf = 0;
        int idxA = e0.Points.Count - 1;
        int idxB = e0.Points.Count + e1.Points.Count - 2;

        int minC = idxSelf, maxC = idxSelf;
        int[] corners = { idxSelf, idxA, idxB };
        for (int k = 0; k < corners.Length; k++)
        {
            if (vertices[corners[k]].y < vertices[minC].y) minC = corners[k];
            if (vertices[corners[k]].y > vertices[maxC].y) maxC = corners[k];
        }

        var indices = new List<int>();

        // 角落无高度跨度（近平）或非单调（病态输入）→ 退回边界扇形：仍是有效网格、非内凹。
        if (vertices[maxC].y - vertices[minC].y <= LevelTolerance)
        {
            FanPolygon(vertices, loopCount, indices);
            return BuildResult(vertices, indices);
        }

        List<int> railL = ExtractArc(loopCount, minC, maxC, +1);
        List<int> railR = ExtractArc(loopCount, minC, maxC, -1);

        if (!IsAscending(vertices, railL) || !IsAscending(vertices, railR))
        {
            indices.Clear();
            FanPolygon(vertices, loopCount, indices);
            return BuildResult(vertices, indices);
        }

        List<float> levels = CollectInnerLevels(vertices, loopCount, vertices[minC].y, vertices[maxC].y);
        EnsureLevelSamples(vertices, railL, levels);
        EnsureLevelSamples(vertices, railR, levels);

        ZipTerrace(vertices, railL, railR, indices);
        return BuildResult(vertices, indices);
    }

    // 从 from 沿 dir(±1) 环绕走到 to，返回经过的顶点索引序列（含首尾）。
    private static List<int> ExtractArc(int count, int from, int to, int dir)
    {
        var arc = new List<int> { from };
        int i = from;
        int guard = 0;
        while (i != to && guard++ <= count)
        {
            i = ((i + dir) % count + count) % count;
            arc.Add(i);
        }
        return arc;
    }

    private static bool IsAscending(List<Vector3> v, List<int> rail)
    {
        for (int i = 1; i < rail.Count; i++)
        {
            if (v[rail[i]].y < v[rail[i - 1]].y - LevelTolerance) return false;
        }
        return true;
    }

    // 收集严格位于 (minY, maxY) 之间的去重高度层，升序。
    private static List<float> CollectInnerLevels(List<Vector3> v, int loopCount, float minY, float maxY)
    {
        var levels = new List<float>();
        for (int i = 0; i < loopCount; i++)
        {
            float y = v[i].y;
            if (y <= minY + LevelTolerance || y >= maxY - LevelTolerance) continue;
            bool found = false;
            for (int j = 0; j < levels.Count; j++)
            {
                if (Mathf.Abs(levels[j] - y) <= LevelTolerance) { found = true; break; }
            }
            if (!found) levels.Add(y);
        }
        levels.Sort();
        return levels;
    }

    // 让轨在每个 level 都有顶点：跨越 level 的段（坡）插入共线采样点；梯段已带台阶点，跳过。
    private static void EnsureLevelSamples(List<Vector3> v, List<int> rail, List<float> levels)
    {
        var newRail = new List<int>(rail.Count + levels.Count);
        for (int k = 0; k < rail.Count - 1; k++)
        {
            newRail.Add(rail[k]);
            float yLow = v[rail[k]].y;
            float yHigh = v[rail[k + 1]].y;
            if (yHigh - yLow <= LevelTolerance) continue;
            for (int L = 0; L < levels.Count; L++)
            {
                float lv = levels[L];
                if (lv > yLow + LevelTolerance && lv < yHigh - LevelTolerance)
                {
                    int newIdx = v.Count;
                    v.Add(SampleAtHeight(v[rail[k]], v[rail[k + 1]], lv));
                    newRail.Add(newIdx);
                }
            }
        }
        newRail.Add(rail[rail.Count - 1]);
        rail.Clear();
        rail.AddRange(newRail);
    }

    // 按高度在直段上线性插值（TriStep3_GetInsertionPoint 的泛化）。
    private static Vector3 SampleAtHeight(Vector3 low, Vector3 high, float y)
    {
        float denom = high.y - low.y;
        if (Mathf.Abs(denom) < 1e-6f) return low;
        float t = (y - low.y) / denom;
        return low + (high - low) * t;
    }

    // 把轨按高度层分组（连续同 Y 的点归一组），升序。
    private static List<List<int>> GroupRailByLevel(List<Vector3> v, List<int> rail)
    {
        var groups = new List<List<int>>();
        int i = 0;
        while (i < rail.Count)
        {
            var g = new List<int> { rail[i] };
            float y = v[rail[i]].y;
            int j = i + 1;
            while (j < rail.Count && Mathf.Abs(v[rail[j]].y - y) <= LevelTolerance)
            {
                g.Add(rail[j]);
                j++;
            }
            groups.Add(g);
            i = j;
        }
        return groups;
    }

    private static void ZipTerrace(List<Vector3> v, List<int> railL, List<int> railR, List<int> indices)
    {
        var gL = GroupRailByLevel(v, railL);
        var gR = GroupRailByLevel(v, railR);

        // 层数不一致（罕见浮点边界）→ 退回轨边界扇形，仍非内凹。
        if (gL.Count != gR.Count)
        {
            FanPolygonFromRails(v, railL, railR, indices);
            return;
        }

        // 每层水平踏面
        for (int g = 0; g < gL.Count; g++)
        {
            FanFlat(v, gL[g], gR[g], indices);
        }
        // 层间踢面
        for (int g = 0; g < gL.Count - 1; g++)
        {
            int lHigh = gL[g][gL[g].Count - 1];
            int lLow = gL[g + 1][0];
            int rLow = gR[g + 1][0];
            int rHigh = gR[g][gR[g].Count - 1];
            AddUpwardTriangle(indices, v, lHigh, lLow, rLow);
            AddUpwardTriangle(indices, v, lHigh, rLow, rHigh);
        }
    }

    // 一层的横带踏面：左组顺序 + 右组逆序 组成同高度多边形，扇形三角化。
    private static void FanFlat(List<Vector3> v, List<int> groupL, List<int> groupR, List<int> indices)
    {
        var poly = new List<int>(groupL.Count + groupR.Count);
        poly.AddRange(groupL);
        for (int i = groupR.Count - 1; i >= 0; i--) poly.Add(groupR[i]);
        for (int i = 1; i < poly.Count - 1; i++)
        {
            AddUpwardTriangle(indices, v, poly[0], poly[i], poly[i + 1]);
        }
    }

    // 从 0 号顶点扇形三角化边界环（退化/病态兜底）。
    private static void FanPolygon(List<Vector3> v, int loopCount, List<int> indices)
    {
        for (int i = 1; i < loopCount - 1; i++)
        {
            AddUpwardTriangle(indices, v, 0, i, i + 1);
        }
    }

    // 层数不一致兜底：用两条轨拼出边界多边形并从最低角扇形化。
    private static void FanPolygonFromRails(List<Vector3> v, List<int> railL, List<int> railR, List<int> indices)
    {
        var poly = new List<int>(railL.Count + railR.Count);
        poly.AddRange(railL);
        for (int i = railR.Count - 2; i >= 1; i--) poly.Add(railR[i]);
        for (int i = 1; i < poly.Count - 1; i++)
        {
            AddUpwardTriangle(indices, v, poly[0], poly[i], poly[i + 1]);
        }
    }

    private static TriangleTransitionMeshData BuildResult(List<Vector3> vertices, List<int> indices)
    {
        return new TriangleTransitionMeshData(
            vertices,
            new List<Vector2>(UVGenerator.GeneratePlanarUV(vertices)),
            indices);
    }

    private static TriangleTransitionMeshData TryBuildStriped(
        TransitionEdgeProfile e0,
        TransitionEdgeProfile e1,
        TransitionEdgeProfile e2)
    {
        var rev0 = ReversedList(e0.Points);
        var rev1 = ReversedList(e1.Points);
        var rev2 = ReversedList(e2.Points);

        if (IsSynchronizedStepPair(e0, e2, e0.Points, rev2))
            return BuildStrips(e0.Points, rev2, e1.Points);
        if (IsSynchronizedStepPair(e0, e1, rev0, e1.Points))
            return BuildStrips(rev0, e1.Points, rev2);
        if (IsSynchronizedStepPair(e1, e2, rev1, e2.Points))
            return BuildStrips(rev1, e2.Points, rev0);

        return null;
    }

    private static bool IsSynchronizedStepPair(
        TransitionEdgeProfile first,
        TransitionEdgeProfile second,
        IReadOnlyList<Vector3> firstPoints,
        IReadOnlyList<Vector3> secondPoints)
    {
        if (first.Type != Enums.TransitionEdgeType.Step) return false;
        if (second.Type != Enums.TransitionEdgeType.Step) return false;
        if (firstPoints.Count != secondPoints.Count) return false;
        for (int i = 0; i < firstPoints.Count; i++)
        {
            if (Mathf.Abs(firstPoints[i].y - secondPoints[i].y) > StepSyncTolerance)
                return false;
        }
        return true;
    }

    private static TriangleTransitionMeshData BuildStrips(
        IReadOnlyList<Vector3> left,
        IReadOnlyList<Vector3> right,
        IReadOnlyList<Vector3> far)
    {
        int L = left.Count;
        int R = right.Count;
        int F = far.Count;

        var vertices = new List<Vector3>(L + R - 1 + Mathf.Max(0, F - 2));
        for (int i = 0; i < L; i++)
            vertices.Add(left[i]);
        for (int i = 1; i < R; i++)
            vertices.Add(right[i]);
        int farStart = L + R - 1;
        for (int i = 1; i < F - 1; i++)
            vertices.Add(far[i]);

        var indices = new List<int>(6 * (L - 1) + 3 * Mathf.Max(0, F - 2));

        for (int i = 0; i < L - 1; i++)
        {
            int li = i;
            int li1 = i + 1;
            int ri = (i == 0) ? 0 : L + i - 1;
            int ri1 = L + i;

            AddUpwardTriangle(indices, vertices, li, ri, ri1);
            AddUpwardTriangle(indices, vertices, li, ri1, li1);
        }

        int aIdx = L - 1;
        int bIdx = L + R - 2;

        if (F >= 3)
        {
            float selfSide = SignedAreaXZ(vertices[aIdx], vertices[bIdx], vertices[0]);
            bool hasOutwardLobe = false;
            for (int i = 1; i < F - 1; i++)
            {
                int vi = L + R + i - 2;
                float farSide = SignedAreaXZ(vertices[aIdx], vertices[bIdx], vertices[vi]);
                if (farSide * selfSide < 0)
                {
                    hasOutwardLobe = true;
                    break;
                }
            }

            if (hasOutwardLobe)
            {
                for (int k = 0; k < F - 2; k++)
                {
                    int farIdx = farStart + k;
                    int nextIdx = (k == F - 3) ? bIdx : farStart + k + 1;
                    AddUpwardTriangle(indices, vertices, aIdx, farIdx, nextIdx);
                }
            }
        }

        return new TriangleTransitionMeshData(
            vertices,
            new List<Vector2>(UVGenerator.GeneratePlanarUV(vertices)),
            indices);
    }

    private static void AddUpwardTriangle(List<int> indices, List<Vector3> vertices, int a, int b, int c)
    {
        if (a == b || b == c || a == c)
        {
            return;
        }

        Vector3 va = vertices[a];
        Vector3 vb = vertices[b];
        Vector3 vc = vertices[c];
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

    private static List<Vector3> ReversedList(IReadOnlyList<Vector3> source)
    {
        var result = new List<Vector3>(source.Count);
        for (int i = source.Count - 1; i >= 0; i--)
        {
            result.Add(source[i]);
        }
        return result;
    }

    private static void ValidateConnectedEdges(
        TransitionEdgeProfile edge0,
        TransitionEdgeProfile edge1,
        TransitionEdgeProfile edge2)
    {
        if (edge0 == null || edge1 == null || edge2 == null)
        {
            throw new ArgumentNullException("Triangle edges cannot be null.");
        }

        RequireSamePoint(edge0.Points[edge0.Points.Count - 1], edge1.Points[0], "edge0 -> edge1");
        RequireSamePoint(edge1.Points[edge1.Points.Count - 1], edge2.Points[0], "edge1 -> edge2");
        RequireSamePoint(edge2.Points[edge2.Points.Count - 1], edge0.Points[0], "edge2 -> edge0");
    }

    private static void RequireSamePoint(Vector3 a, Vector3 b, string connection)
    {
        if ((a - b).sqrMagnitude > EndpointTolerance * EndpointTolerance)
        {
            throw new ArgumentException($"Disconnected triangle boundary at {connection}.");
        }
    }

    private static void AppendRange(List<Vector3> target, IReadOnlyList<Vector3> source, int start, int endExclusive)
    {
        for (int i = start; i < endExclusive; i++)
        {
            target.Add(source[i]);
        }
    }

    private static float SignedAreaXZ(Vector3 a, Vector3 b, Vector3 c)
    {
        return ((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) * 0.5f;
    }
}

public static class RectangleTransitionMesh
{
    public static TransitionEdgeProfile CreateEdge(
        Vector3 start,
        Vector3 end,
        Enums.TransitionEdgeType type,
        int subdivision,
        bool perturbIntermediate)
    {
        if (subdivision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subdivision));
        }

        List<Vector3> points = type == Enums.TransitionEdgeType.Step
            ? CreateStepPoints(start, end, subdivision)
            : new List<Vector3> { start, end };

        if (perturbIntermediate)
        {
            // 仅扰动 XZ、保持 Y 不变：让台阶踏面保持水平、落在精确高度层，
            // 从而三角阶梯角能按高度层干净地续接台阶（Y 被扰动会打乱层结构）。
            for (int i = 1; i < points.Count - 1; i++)
            {
                points[i] = HexMetrics.PerturbXZ(points[i]);
            }
        }

        return new TransitionEdgeProfile(type, points);
    }

    public static List<Vector3> CreateStepPoints(Vector3 start, Vector3 end, int subdivision)
    {
        if (subdivision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subdivision));
        }

        float yIncrement = (end.y - start.y) / (subdivision + 1);
        Vector2 startXZ = new Vector2(start.x, start.z);
        Vector2 endXZ = new Vector2(end.x, end.z);
        Vector2 xzIncrement = (endXZ - startXZ) / (2 * subdivision + 1);
        var points = new List<Vector3>(2 * subdivision + 2);

        for (int i = 0; i < 2 * subdivision + 2; i++)
        {
            int heightIndex = i == 0 ? 0 : (i + 1) / 2;
            Vector2 xz = startXZ + i * xzIncrement;
            points.Add(new Vector3(xz.x, start.y + heightIndex * yIncrement, xz.y));
        }

        return points;
    }

    public static RectangleTransitionMeshData Build(
        IReadOnlyList<Vector3> starts,
        IReadOnlyList<Vector3> ends,
        Enums.TransitionEdgeType type,
        int subdivision,
        bool perturbIntermediate)
    {
        if (starts == null || ends == null)
        {
            throw new ArgumentNullException("Rectangle profile endpoints cannot be null.");
        }
        if (starts.Count != 4 || ends.Count != 4)
        {
            throw new ArgumentException("A rectangle transition requires four profile endpoint pairs.");
        }

        var profiles = new List<TransitionEdgeProfile>(4);
        var vertices = new List<Vector3>();
        for (int i = 0; i < 4; i++)
        {
            TransitionEdgeProfile profile = CreateEdge(
                starts[i], ends[i], type, subdivision, perturbIntermediate);
            profiles.Add(profile);
            for (int j = 0; j < profile.Points.Count; j++)
            {
                vertices.Add(profile.Points[j]);
            }
        }

        int profileLength = profiles[0].Points.Count;
        var indices = new List<int>(3 * 2 * 3 * (profileLength - 1));
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

        return new RectangleTransitionMeshData(
            vertices,
            BuildProfileUV(4, profileLength),
            indices,
            profiles);
    }

    /// <summary>
    /// 沿 profile 方向参数化的过渡 UV，替代与几何朝向无关的世界 XZ 平面 UV。
    /// <para>
    /// 顶点按 profile 顺序平铺：profile[k] 的第 j 个点位于 index = k*profileLength + j。
    /// 每条 profile 从 start(self=材质1) 走到 end(neighbor=材质2)。混合遮罩沿 V 轴渐变，
    /// 故令 V = 沿 self→neighbor 的进度（self 端 V=0、neighbor 端 V=1，匹配遮罩暗→亮渐变），
    /// U = profile 序号，保证融合方向恒为 self→neighbor（边1→边3），不再随世界朝向旋转。
    /// </para>
    /// </summary>
    private static List<Vector2> BuildProfileUV(int profileCount, int profileLength)
    {
        var uvs = new List<Vector2>(profileCount * profileLength);
        float uDen = profileCount > 1 ? profileCount - 1 : 1;
        float vDen = profileLength > 1 ? profileLength - 1 : 1;
        for (int k = 0; k < profileCount; k++)
        {
            float u = k / uDen;
            for (int j = 0; j < profileLength; j++)
            {
                float v = j / vDen;
                uvs.Add(new Vector2(u, v));
            }
        }
        return uvs;
    }

    /// <summary>
    /// 把矩形过渡网格展开成“每个三角形三个独立顶点”的形式，用于渲染期的逐面平坦着色（flat shading）。
    /// <para>
    /// 与三角过渡统一风格：整片过渡面都是硬切面，三角↔矩形交界不再是“平滑 vs 硬面”的突兀跳变；
    /// 同时让阶梯的踏面/踢面各自平坦、边界清晰，不再被 RecalculateNormals 平滑成圆角。
    /// </para>
    /// Profiles 原样保留（三角过渡仍需复用矩形边界顶点序列）。UV 按展开后的顶点重新生成。
    /// </summary>
    public static RectangleTransitionMeshData ToFlatShaded(RectangleTransitionMeshData data)
    {
        var vertices = new List<Vector3>(data.Indices.Count);
        var uvs = new List<Vector2>(data.Indices.Count);
        var indices = new List<int>(data.Indices.Count);
        for (int i = 0; i < data.Indices.Count; i++)
        {
            int src = data.Indices[i];
            vertices.Add(data.Vertices[src]);
            uvs.Add(data.UVs[src]);
            indices.Add(i);
        }

        return new RectangleTransitionMeshData(
            vertices,
            uvs,
            indices,
            data.Profiles);
    }

    private static void AddUpwardTriangle(List<int> indices, List<Vector3> vertices, int a, int b, int c)
    {
        Vector3 va = vertices[a];
        Vector3 vb = vertices[b];
        Vector3 vc = vertices[c];
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

public static class RectangleDrivenTriangleMesh
{
    public static TriangleTransitionMeshData BuildNEE(
        RectangleTransitionMeshData selfNE,
        RectangleTransitionMeshData neighborSE,
        RectangleTransitionMeshData selfE)
    {
        return TriangleTransitionMesh.BuildFan(
            GetProfile(selfNE, 3, false),
            GetProfile(neighborSE, 3, false),
            GetProfile(selfE, 0, true));
    }

    public static TriangleTransitionMeshData BuildESE(
        RectangleTransitionMeshData selfE,
        RectangleTransitionMeshData neighborNE,
        RectangleTransitionMeshData selfSE)
    {
        return TriangleTransitionMesh.BuildFan(
            GetProfile(selfE, 3, false),
            GetProfile(neighborNE, 0, true),
            GetProfile(selfSE, 0, true));
    }

    private static TransitionEdgeProfile GetProfile(
        RectangleTransitionMeshData rectangle,
        int profileIndex,
        bool reverse)
    {
        if (rectangle == null)
        {
            throw new ArgumentNullException(nameof(rectangle));
        }

        TransitionEdgeProfile profile = rectangle.Profiles[profileIndex];
        if (!reverse)
        {
            return profile;
        }

        var points = new List<Vector3>(profile.Points.Count);
        for (int i = profile.Points.Count - 1; i >= 0; i--)
        {
            points.Add(profile.Points[i]);
        }
        return new TransitionEdgeProfile(profile.Type, points);
    }
}
