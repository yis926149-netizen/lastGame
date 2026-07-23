using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class SharedTransitionMeshTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void CreateStepPoints_WhenReversed_ProducesSamePointsInReverseOrder(int subdivision)
    {
        Vector3 start = new Vector3(0f, 1f, 0f);
        Vector3 end = new Vector3(3f, 5f, 2f);
        List<Vector3> forward = RectangleTransitionMesh.CreateStepPoints(start, end, subdivision);
        List<Vector3> backward = RectangleTransitionMesh.CreateStepPoints(end, start, subdivision);

        Assert.AreEqual(forward.Count, backward.Count);
        for (int i = 0; i < forward.Count; i++)
        {
            Assert.Less((forward[i] - backward[backward.Count - 1 - i]).sqrMagnitude, 0.000001f);
        }
    }

    [Test]
    public void CreateStepPoints_WithTwoSubdivisions_CreatesExpectedHeightPairs()
    {
        List<Vector3> points = RectangleTransitionMesh.CreateStepPoints(
            new Vector3(0f, 0f, 0f),
            new Vector3(3f, 3f, 0f),
            2);

        Assert.AreEqual(6, points.Count);
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 1f, 2f, 2f, 3f },
            points.ConvertAll(point => point.y));
    }

    [Test]
    [TestCase(Enums.TransitionEdgeType.Slope, Enums.TransitionEdgeType.Slope, Enums.TransitionEdgeType.Slope)]
    [TestCase(Enums.TransitionEdgeType.Step, Enums.TransitionEdgeType.Slope, Enums.TransitionEdgeType.Slope)]
    [TestCase(Enums.TransitionEdgeType.Slope, Enums.TransitionEdgeType.Step, Enums.TransitionEdgeType.Slope)]
    [TestCase(Enums.TransitionEdgeType.Slope, Enums.TransitionEdgeType.Slope, Enums.TransitionEdgeType.Step)]
    [TestCase(Enums.TransitionEdgeType.Step, Enums.TransitionEdgeType.Step, Enums.TransitionEdgeType.Slope)]
    [TestCase(Enums.TransitionEdgeType.Step, Enums.TransitionEdgeType.Slope, Enums.TransitionEdgeType.Step)]
    [TestCase(Enums.TransitionEdgeType.Slope, Enums.TransitionEdgeType.Step, Enums.TransitionEdgeType.Step)]
    [TestCase(Enums.TransitionEdgeType.Step, Enums.TransitionEdgeType.Step, Enums.TransitionEdgeType.Step)]
    public void BuildFan_WithRectangleProfiles_ProducesValidUpwardTriangles(
        Enums.TransitionEdgeType edge0Type,
        Enums.TransitionEdgeType edge1Type,
        Enums.TransitionEdgeType edge2Type)
    {
        int edge0Subdivision = edge0Type == Enums.TransitionEdgeType.Step ? 1 : 0;
        int edge1Subdivision = edge1Type == Enums.TransitionEdgeType.Step ? 2 : 0;
        int edge2Subdivision = edge2Type == Enums.TransitionEdgeType.Step ? 3 : 0;
        Vector3 self = new Vector3(0f, 0f, 0f);
        Vector3 a = new Vector3(1f, 2f, 2f);
        Vector3 b = new Vector3(2f, 4f, 0f);
        RectangleTransitionMeshData rectangle0 = BuildRectangle(self, a, edge0Type, edge0Subdivision);
        RectangleTransitionMeshData rectangle1 = BuildRectangle(a, b, edge1Type, edge1Subdivision);
        RectangleTransitionMeshData rectangle2 = BuildRectangle(self, b, edge2Type, edge2Subdivision);
        TriangleTransitionMeshData mesh = RectangleDrivenTriangleMesh.BuildNEE(
            rectangle0,
            rectangle1,
            rectangle2);

        int boundaryCount = mesh.Vertices.Count - 1;
        Assert.LessOrEqual(mesh.Indices.Count, boundaryCount * 3);
        Assert.AreEqual(0, mesh.Indices.Count % 3);
        Assert.Greater(mesh.Indices.Count, 0);
        Assert.AreEqual(mesh.Vertices.Count, mesh.UVs.Count);
        foreach (int index in mesh.Indices)
        {
            Assert.That(index, Is.InRange(0, mesh.Vertices.Count - 1));
        }
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            Vector3 vertexA = mesh.Vertices[mesh.Indices[i]];
            Vector3 vertexB = mesh.Vertices[mesh.Indices[i + 1]];
            Vector3 vertexC = mesh.Vertices[mesh.Indices[i + 2]];
            float signedTwiceArea =
                (vertexB.x - vertexA.x) * (vertexC.z - vertexA.z) -
                (vertexC.x - vertexA.x) * (vertexB.z - vertexA.z);
            Assert.Less(signedTwiceArea, -0.000001f);
        }
    }

    [Test]
    public void BuildFan_WithSynchronizedStepsAndInwardFarEdge_DoesNotOverlap()
    {
        Vector3 self = new Vector3(0f, 0f, 0f);
        Vector3 a = new Vector3(0f, 0f, 3f);
        Vector3 b = new Vector3(3f, 0f, 0f);
        var edge0 = new TransitionEdgeProfile(Enums.TransitionEdgeType.Step, new[]
        {
            self, new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 2f), a
        });
        var edge1 = new TransitionEdgeProfile(Enums.TransitionEdgeType.Step, new[]
        {
            a, new Vector3(1f, 0f, 1f), new Vector3(2f, 0f, 0.5f), b
        });
        var edge2 = new TransitionEdgeProfile(Enums.TransitionEdgeType.Step, new[]
        {
            b, new Vector3(2f, 0f, 0f), new Vector3(1f, 0f, 0f), self
        });

        TriangleTransitionMeshData mesh = TriangleTransitionMesh.BuildFan(edge0, edge1, edge2);

        float triangleArea = 0f;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            Vector3 p0 = mesh.Vertices[mesh.Indices[i]];
            Vector3 p1 = mesh.Vertices[mesh.Indices[i + 1]];
            Vector3 p2 = mesh.Vertices[mesh.Indices[i + 2]];
            triangleArea += Mathf.Abs(
                (p1.x - p0.x) * (p2.z - p0.z) -
                (p2.x - p0.x) * (p1.z - p0.z)) * 0.5f;
        }

        Assert.AreEqual(4.5f, triangleArea, 0.0001f);
    }

    private static RectangleTransitionMeshData BuildRectangle(
        Vector3 start,
        Vector3 end,
        Enums.TransitionEdgeType type,
        int subdivision)
    {
        return RectangleTransitionMesh.Build(
            new[] { start, start, start, start },
            new[] { end, end, end, end },
            type,
            subdivision,
            false);
    }

    [Test]
    public void BuildTerrace_TwoStepOneSlope_IsWatertightAndTerraced()
    {
        Vector3 self = new Vector3(0f, 0f, 0f);
        Vector3 a = new Vector3(1f, 2f, 2f);
        Vector3 b = new Vector3(2f, 4f, 0f);
        // 两梯一坡，且两条梯边爬到不同高度（a=2, b=4）→ 非同步 → 走通用 BuildTerrace。
        RectangleTransitionMeshData r0 = BuildRectangle(self, a, Enums.TransitionEdgeType.Step, 1);
        RectangleTransitionMeshData r1 = BuildRectangle(a, b, Enums.TransitionEdgeType.Slope, 0);
        RectangleTransitionMeshData r2 = BuildRectangle(self, b, Enums.TransitionEdgeType.Step, 1);
        TriangleTransitionMeshData mesh = RectangleDrivenTriangleMesh.BuildNEE(r0, r1, r2);

        List<Vector3> loop = BuildBoundaryLoop(r0.Profiles[3], r1.Profiles[3], ReverseProfile(r2.Profiles[0]));

        // 水密：三角面 XZ 面积之和 == 边界多边形 XZ 面积（无重叠、无漏面 → 不是内凹漏斗）。
        Assert.AreEqual(XZPolygonArea(loop), XZMeshArea(mesh), 0.001f);
        // 阶梯：至少有一个水平踏面（三顶点同高）。
        Assert.IsTrue(HasFlatTread(mesh), "terrace should contain at least one flat tread");
        // 无中心枢纽内部顶点：每个顶点都落在某条边界边上（阶梯角不引入内部点）。
        foreach (Vector3 v in mesh.Vertices)
        {
            Assert.IsTrue(OnAnyBoundarySegment(v, loop), "terrace vertices must lie on the boundary");
        }
    }

    private static List<Vector3> BuildBoundaryLoop(
        TransitionEdgeProfile e0, TransitionEdgeProfile e1, TransitionEdgeProfile e2)
    {
        var loop = new List<Vector3>();
        for (int i = 0; i < e0.Points.Count; i++) loop.Add(e0.Points[i]);
        for (int i = 1; i < e1.Points.Count; i++) loop.Add(e1.Points[i]);
        for (int i = 1; i < e2.Points.Count - 1; i++) loop.Add(e2.Points[i]);
        return loop;
    }

    private static TransitionEdgeProfile ReverseProfile(TransitionEdgeProfile p)
    {
        var pts = new List<Vector3>(p.Points.Count);
        for (int i = p.Points.Count - 1; i >= 0; i--) pts.Add(p.Points[i]);
        return new TransitionEdgeProfile(p.Type, pts);
    }

    private static float XZPolygonArea(List<Vector3> poly)
    {
        float a = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector3 p = poly[i];
            Vector3 q = poly[(i + 1) % poly.Count];
            a += p.x * q.z - q.x * p.z;
        }
        return Mathf.Abs(a) * 0.5f;
    }

    private static float XZMeshArea(TriangleTransitionMeshData m)
    {
        float a = 0f;
        for (int i = 0; i < m.Indices.Count; i += 3)
        {
            Vector3 va = m.Vertices[m.Indices[i]];
            Vector3 vb = m.Vertices[m.Indices[i + 1]];
            Vector3 vc = m.Vertices[m.Indices[i + 2]];
            a += Mathf.Abs((vb.x - va.x) * (vc.z - va.z) - (vc.x - va.x) * (vb.z - va.z)) * 0.5f;
        }
        return a;
    }

    private static bool HasFlatTread(TriangleTransitionMeshData m)
    {
        for (int i = 0; i < m.Indices.Count; i += 3)
        {
            float ya = m.Vertices[m.Indices[i]].y;
            float yb = m.Vertices[m.Indices[i + 1]].y;
            float yc = m.Vertices[m.Indices[i + 2]].y;
            if (Mathf.Abs(ya - yb) < 0.001f && Mathf.Abs(yb - yc) < 0.001f) return true;
        }
        return false;
    }

    private static bool OnAnyBoundarySegment(Vector3 p, List<Vector3> loop)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            Vector3 a = loop[i];
            Vector3 b = loop[(i + 1) % loop.Count];
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-9f)
            {
                if ((p - a).sqrMagnitude < 1e-6f) return true;
                continue;
            }
            float t = Vector3.Dot(p - a, ab) / len2;
            if (t < -0.0001f || t > 1.0001f) continue;
            Vector3 proj = a + ab * Mathf.Clamp01(t);
            if ((p - proj).sqrMagnitude < 1e-6f) return true;
        }
        return false;
    }

}

public class RectangleTransitionMeshTests
{
    [TestCase(Enums.TransitionEdgeType.Slope, 0, 8, 18)]
    [TestCase(Enums.TransitionEdgeType.Step, 0, 8, 18)]
    [TestCase(Enums.TransitionEdgeType.Step, 1, 16, 54)]
    [TestCase(Enums.TransitionEdgeType.Step, 3, 32, 126)]
    public void Build_CreatesExpectedTopology(
        Enums.TransitionEdgeType type,
        int subdivision,
        int expectedVertexCount,
        int expectedIndexCount)
    {
        RectangleTransitionMeshData mesh = Build(type, subdivision);

        Assert.AreEqual(expectedVertexCount, mesh.Vertices.Count);
        Assert.AreEqual(expectedVertexCount, mesh.UVs.Count);
        Assert.AreEqual(expectedIndexCount, mesh.Indices.Count);
        Assert.AreEqual(4, mesh.Profiles.Count);
        foreach (int index in mesh.Indices)
        {
            Assert.That(index, Is.InRange(0, mesh.Vertices.Count - 1));
        }
    }

    [Test]
    public void Build_ExposesProfilesForTriangleBoundary()
    {
        RectangleTransitionMeshData rectangle = Build(Enums.TransitionEdgeType.Step, 2);
        TransitionEdgeProfile triangleEdge = rectangle.Profiles[3];

        Assert.AreEqual(rectangle.Profiles[3].Points.Count, triangleEdge.Points.Count);
        for (int i = 0; i < triangleEdge.Points.Count; i++)
        {
            Assert.Less((rectangle.Profiles[3].Points[i] - triangleEdge.Points[i]).sqrMagnitude, 0.000001f);
        }
    }

    [Test]
    public void BuildNEE_SelectsFourthFourthFirstReversedProfiles()
    {
        Vector3 self = new Vector3(0f, 0f, 0f);
        Vector3 a = new Vector3(1f, 0f, 2f);
        Vector3 b = new Vector3(2f, 0f, 0f);
        RectangleTransitionMeshData selfNE = CreateMarkedRectangle(self, a, 10f, 30f);
        RectangleTransitionMeshData neighborSE = CreateMarkedRectangle(a, b, 20f, 40f);
        RectangleTransitionMeshData selfE = CreateMarkedRectangle(self, b, 50f, 60f);

        TriangleTransitionMeshData triangle = RectangleDrivenTriangleMesh.BuildNEE(selfNE, neighborSE, selfE);

        AssertBoundaryMarkers(triangle, 30f, 40f, 50f);
    }

    [Test]
    public void BuildESE_SelectsFourthFirstReversedFirstReversedProfiles()
    {
        Vector3 self = new Vector3(0f, 0f, 0f);
        Vector3 a = new Vector3(1f, 0f, 2f);
        Vector3 b = new Vector3(2f, 0f, 0f);
        RectangleTransitionMeshData selfE = CreateMarkedRectangle(self, a, 10f, 30f);
        RectangleTransitionMeshData neighborNE = CreateMarkedRectangle(b, a, 40f, 60f);
        RectangleTransitionMeshData selfSE = CreateMarkedRectangle(self, b, 50f, 70f);

        TriangleTransitionMeshData triangle = RectangleDrivenTriangleMesh.BuildESE(selfE, neighborNE, selfSE);

        AssertBoundaryMarkers(triangle, 30f, 40f, 50f);
    }

    private static RectangleTransitionMeshData CreateMarkedRectangle(
        Vector3 start,
        Vector3 end,
        float firstMarker,
        float fourthMarker)
    {
        var profiles = new List<TransitionEdgeProfile>();
        for (int i = 0; i < 4; i++)
        {
            float marker = i == 0 ? firstMarker : i == 3 ? fourthMarker : -i;
            profiles.Add(new TransitionEdgeProfile(
                Enums.TransitionEdgeType.Step,
                new[] { start, new Vector3(marker, marker, marker), end }));
        }
        return new RectangleTransitionMeshData(
            new List<Vector3>(),
            new List<Vector2>(),
            new List<int>(),
            profiles);
    }

    private static void AssertBoundaryMarkers(
        TriangleTransitionMeshData triangle,
        float edge0Marker,
        float edge1Marker,
        float edge2Marker)
    {
        // 去掉 center-fan 的中心枢纽点后，顶点按边界顺序排列：
        // loop = [self, e0中点, A, e1中点, B, e2中点]，三条边中点落在 [1]/[3]/[5]。
        Assert.AreEqual(edge0Marker, triangle.Vertices[1].x);
        Assert.AreEqual(edge1Marker, triangle.Vertices[3].x);
        Assert.AreEqual(edge2Marker, triangle.Vertices[5].x);
    }

    private static RectangleTransitionMeshData Build(Enums.TransitionEdgeType type, int subdivision)
    {
        var starts = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 0.3f),
            new Vector3(0f, 0f, 0.7f),
            new Vector3(0f, 0f, 1f),
        };
        var ends = new List<Vector3>
        {
            new Vector3(2f, 4f, 0f),
            new Vector3(2f, 4f, 0.3f),
            new Vector3(2f, 4f, 0.7f),
            new Vector3(2f, 4f, 1f),
        };
        return RectangleTransitionMesh.Build(starts, ends, type, subdivision, false);
    }
}
