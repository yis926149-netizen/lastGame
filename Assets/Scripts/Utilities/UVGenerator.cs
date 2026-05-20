using UnityEngine;
using System.Collections.Generic;

public static class UVGenerator
{
    /// <summary>
    /// 根据顶点的 XZ 坐标自动计算 Planar UV（忽略 Y 轴）
    /// </summary>
    public static Vector2[] GeneratePlanarUV(List<Vector3> vertices)
    {
        if (vertices == null || vertices.Count == 0) return new Vector2[0];

        // 1. 计算 XZ 平面的包围盒 (Bounds)
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var v in vertices)
        {
            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.z < minZ) minZ = v.z;
            if (v.z > maxZ) maxZ = v.z;
        }

        float width = maxX - minX;
        float depth = maxZ - minZ;

        // 2. 映射到 [0, 1] 空间
        Vector2[] uvs = new Vector2[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            // 避免除以 0
            // U 轴映射 X，V 轴映射 Z
            float u = (width > 0) ? (vertices[i].x - minX) / width : 0.5f;
            float v = (depth > 0) ? (vertices[i].z - minZ) / depth : 0.5f;
            uvs[i] = new Vector2(u, v);
        }

        return uvs;
    }
}