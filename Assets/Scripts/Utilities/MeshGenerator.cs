using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
// 【约束-2026-08-05】FogConnector 内圈顶点支持运行时高度偏移（SetConnectorInnerHeightOffsets），
// 驱动方是 FogManager.LateUpdate + MapVisualTransitionService.GetAnimatedWorldY；
// 基线顶点（_connectorBaseVertices）只在本类生成 Connector mesh 时捕获，外部不得改写。
public class MeshGenerator : MonoBehaviour
{
    private MeshFilter m_meshFilter;
    private MeshRenderer m_meshRenderer;
    private Mesh m_mesh;
    private Vector3[] _connectorBaseVertices;
    private int _connectorInnerStart = -1;
    private int _connectorInnerCount;

    private void Awake()
    {
        m_meshFilter = GetComponent<MeshFilter>();
        m_meshRenderer = GetComponent<MeshRenderer>();

        m_mesh = new Mesh();
        m_mesh.name = "GeneratedMesh";
        m_meshFilter.sharedMesh = m_mesh;
    }

    /// <summary>
    /// 生成带孔洞的多边形 Mesh（在 XZ 水平投影面上）
    /// </summary>
    /// <param name="outerBoundary">外轮廓顶点组</param>
    /// <param name="holes">内轮廓（孔洞）顶点组列表</param>
    /// <param name="material">应用的材质</param>
    public void GenerateMesh(List<Vector3> outerBoundary, List<List<Vector3>> holes, Material material)
    {
        if (outerBoundary == null || outerBoundary.Count < 3)
        {
            Debug.LogError("外轮廓顶点不足，无法生成Mesh");
            return;
        }

        // 设置材质
        if (m_meshRenderer != null)
        {
            m_meshRenderer.sharedMaterial = material;
        }

        // --- 1. 准备三角剖分器 ---
        Triangulator triangulator = new Triangulator();

        // 直接添加 Vector3 边界，内部逻辑现在会自动处理 XZ 平面投影
        triangulator.AddBoundary(outerBoundary);

        // 添加内轮廓
        if (holes != null)
        {
            foreach (var hole in holes)
            {
                if (hole.Count >= 3)
                {
                    triangulator.AddBoundary(hole);
                }
            }
        }

        // --- 2. 执行三角剖分 ---
        triangulator.Triangulate();

        // --- 3. 更新 Mesh 数据 ---
        // 此时 triangulator.Vertices 已经是剖分后并还原为 Vector3(x, 0, z) 的点集
        UpdateMesh(triangulator.Vertices, triangulator.Indices);
    }

    public void GenerateSlopeMesh(List<Vector3> innerBoundary, List<Vector3> outerBoundary, Material material)
    {
        if (innerBoundary == null || outerBoundary == null || innerBoundary.Count < 3 ||
            innerBoundary.Count != outerBoundary.Count)
        {
            Debug.LogError("斜坡内外轮廓数量不一致，无法生成 Mesh。");
            return;
        }

        if (m_meshRenderer != null) m_meshRenderer.sharedMaterial = material;

        List<Vector3> vertices = new List<Vector3>(innerBoundary.Count * 2);
        vertices.AddRange(innerBoundary);
        vertices.AddRange(outerBoundary);
        List<int> triangles = new List<int>(innerBoundary.Count * 6);
        int count = innerBoundary.Count;
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            AddUpFacingTriangle(vertices, triangles, i, count + i, count + next);
            AddUpFacingTriangle(vertices, triangles, i, count + next, next);
        }

        UpdateMesh(vertices, triangles);
    }

    /// <summary>让 FogConnector 内圈逐点跟随地图边缘高度；外圈/填充面保持原位。</summary>
    public void SetConnectorInnerHeightOffsets(IReadOnlyList<float> offsets)
    {
        if (m_mesh == null || _connectorBaseVertices == null || _connectorInnerStart < 0 ||
            offsets == null || offsets.Count != _connectorInnerCount ||
            m_mesh.vertexCount != _connectorBaseVertices.Length)
            return;

        Vector3[] vertices = (Vector3[])_connectorBaseVertices.Clone();
        for (int i = 0; i < _connectorInnerCount; i++)
            vertices[_connectorInnerStart + i].y += offsets[i];
        m_mesh.vertices = vertices;
        m_mesh.RecalculateNormals();
        m_mesh.RecalculateBounds();
    }

    public void ResetConnectorInnerHeightOffsets()
    {
        if (m_mesh == null || _connectorBaseVertices == null ||
            m_mesh.vertexCount != _connectorBaseVertices.Length)
            return;
        m_mesh.vertices = (Vector3[])_connectorBaseVertices.Clone();
        m_mesh.RecalculateNormals();
        m_mesh.RecalculateBounds();
    }

    public void GenerateConnectorMesh(List<Vector3> rectangleBoundary, List<Vector3> innerBoundary,
        List<Vector3> slopeOuterBoundary, Material material)
    {
        if (rectangleBoundary == null || rectangleBoundary.Count < 3 || innerBoundary == null ||
            slopeOuterBoundary == null || innerBoundary.Count < 3 ||
            innerBoundary.Count != slopeOuterBoundary.Count)
        {
            Debug.LogError("Connector 轮廓无效，无法生成 Mesh。");
            return;
        }

        if (m_meshRenderer != null) m_meshRenderer.sharedMaterial = material;

        Triangulator fillTriangulator = new Triangulator();
        fillTriangulator.AddBoundary(rectangleBoundary);
        fillTriangulator.AddBoundary(slopeOuterBoundary);
        fillTriangulator.Triangulate();

        List<Vector3> vertices = new List<Vector3>(
            fillTriangulator.Vertices.Count + innerBoundary.Count * 2);
        List<int> triangles = new List<int>(
            fillTriangulator.Indices.Count + innerBoundary.Count * 6);
        vertices.AddRange(fillTriangulator.Vertices);
        triangles.AddRange(fillTriangulator.Indices);

        int innerStart = vertices.Count;
        vertices.AddRange(innerBoundary);
        int outerStart = vertices.Count;
        vertices.AddRange(slopeOuterBoundary);
        int degenerateSegmentCount = 0;
        for (int i = 0; i < innerBoundary.Count; i++)
        {
            int next = (i + 1) % innerBoundary.Count;
            Vector3 firstNormal = Vector3.Cross(
                vertices[outerStart + i] - vertices[innerStart + i],
                vertices[outerStart + next] - vertices[innerStart + i]);
            Vector3 secondNormal = Vector3.Cross(
                vertices[outerStart + next] - vertices[innerStart + i],
                vertices[innerStart + next] - vertices[innerStart + i]);
            if (firstNormal.sqrMagnitude < 1e-8f || secondNormal.sqrMagnitude < 1e-8f)
                degenerateSegmentCount++;

            AddUpFacingTriangle(vertices, triangles,
                innerStart + i, outerStart + i, outerStart + next);
            AddUpFacingTriangle(vertices, triangles,
                innerStart + i, outerStart + next, innerStart + next);
        }

        if (degenerateSegmentCount > 0)
            Debug.LogWarning($"FogConnector: {degenerateSegmentCount}/{innerBoundary.Count} 个坡面段包含退化三角形。");

        UpdateMesh(vertices, triangles);
        _connectorBaseVertices = vertices.ToArray();
        _connectorInnerStart = innerStart;
        _connectorInnerCount = innerBoundary.Count;
    }

    private static void AddUpFacingTriangle(List<Vector3> vertices, List<int> triangles, int a, int b, int c)
    {
        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        triangles.Add(a);
        if (normal.y >= 0f)
        {
            triangles.Add(b);
            triangles.Add(c);
        }
        else
        {
            triangles.Add(c);
            triangles.Add(b);
        }
    }

    private void UpdateMesh(List<Vector3> vertices, List<int> triangles)
    {
        m_mesh.Clear();

        // 直接使用生成的 Vector3 顶点
        m_mesh.vertices = vertices.ToArray();
        m_mesh.triangles = triangles.ToArray();

        // 自动计算 UV (使用适配后的 XZ Planar UV 逻辑)
        m_mesh.uv = UVGenerator.GeneratePlanarUV(vertices);

        // 重新计算法线（由于在 XZ 平面，法线将统一指向上方 Vector3.up）
        m_mesh.RecalculateNormals();
        m_mesh.RecalculateBounds();
    }

    private void OnDestroy()
    {
        if (m_mesh != null)
        {
            Destroy(m_mesh);
        }
    }
}
