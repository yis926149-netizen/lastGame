using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MeshGenerator : MonoBehaviour
{
    private MeshFilter m_meshFilter;
    private MeshRenderer m_meshRenderer;
    private Mesh m_mesh;

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
