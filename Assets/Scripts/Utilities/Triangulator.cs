using System.Collections.Generic;
using UnityEngine;
using LibTessDotNet;

public class Triangulator
{
    private List<Vector3> m_vertices = new List<Vector3>();
    private List<List<int>> m_boundaries = new List<List<int>>(); // 存储各边界的顶点索引
    private List<int> m_indices = new List<int>();

    // 对外暴露处理后的顶点（Y轴将统一为0或保持平齐）
    public List<Vector3> Vertices => m_vertices;
    // 对外暴露生成的三角形索引
    public List<int> Indices => m_indices;

    /// <summary>
    /// 添加一个边界（外边界/内边界），忽略传入 Vector3 的 Y 值进行计算
    /// </summary>
    public void AddBoundary(List<Vector3> boundary)
    {
        if (boundary.Count < 3) return;

        int startIndex = m_vertices.Count;
        m_vertices.AddRange(boundary);

        List<int> boundaryIndices = new List<int>();
        for (int i = 0; i < boundary.Count; i++)
        {
            boundaryIndices.Add(startIndex + i);
        }

        // 校验边界方向：在 XZ 平面上，外边界强制顺时针，内边界强制逆时针
        float area = CalculateAreaXZ(boundaryIndices);
        if (m_boundaries.Count == 0)
        {
            if (area > 0) boundaryIndices.Reverse(); // 第一个是外边界，确保顺时针
        }
        else
        {
            if (area < 0) boundaryIndices.Reverse(); // 之后是内边界，确保逆时针
        }

        m_boundaries.Add(boundaryIndices);
    }

    private float CalculateAreaXZ(List<int> indices)
    {
        float area = 0;
        int count = indices.Count;
        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            Vector3 v1 = m_vertices[indices[i]];
            Vector3 v2 = m_vertices[indices[j]];
            // 使用 X 和 Z 进行面积计算（忽略 Y）
            area += (v1.x * v2.z) - (v2.x * v1.z);
        }
        return area * 0.5f;
    }

    /// <summary>
    /// 使用 LibTessDotNet 进行三角剖分
    /// </summary>
    public void Triangulate()
    {
        m_indices.Clear();
        if (m_boundaries.Count == 0) return;

        var tess = new Tess();

        foreach (var boundaryIndexList in m_boundaries)
        {
            int count = boundaryIndexList.Count;
            ContourVertex[] contour = new ContourVertex[count];

            for (int j = 0; j < count; j++)
            {
                Vector3 v = m_vertices[boundaryIndexList[j]];
                // 传入时记录原始 Y 坐标到 Vec3.Z（LibTess计算时不使用Z）
                contour[j].Position = new Vec3 { X = v.x, Y = v.z, Z = v.y };
            }

            tess.AddContour(contour);
        }

        // 设置合并阈值，防止微小位移导致无法匹配原始高度
        tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3, (pos, data, weights) => {
            // 当 LibTess 产生新顶点（如相交点）时，计算加权平均高度
            float interpolatedY = 0;
            for (int i = 0; i < 4; i++)
            {
                if (data[i] != null)
                    interpolatedY += ((Vec3)data[i]).Z * weights[i];
            }
            return new Vec3 { X = pos.X, Y = pos.Y, Z = interpolatedY };
        });

        // 重新获取处理后的顶点
        m_vertices.Clear();
        foreach (var vertex in tess.Vertices)
        {
            // 核心修改：从 vertex.Position.Z 中取回原始或插值后的 Y 坐标
            m_vertices.Add(new Vector3(vertex.Position.X, vertex.Position.Z, vertex.Position.Y));
        }

        // 获取三角形索引逻辑保持不变...
        m_indices.Clear();
        int elementCount = tess.ElementCount;
        for (int i = 0; i < elementCount; i++)
        {
            m_indices.Add(tess.Elements[i * 3]);
            m_indices.Add(tess.Elements[i * 3 + 1]);
            m_indices.Add(tess.Elements[i * 3 + 2]);
        }
    }
}