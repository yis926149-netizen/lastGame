using System.Collections.Generic;
using UnityEngine;

//****************************************
// 功能说明：城墙运行时 Mesh 变形工具（静态）。
//   将城墙预制体的 Mesh 沿 Z 轴（长度轴）逐渐变化底部高度，
//   使墙体在竖直方向保持不倾斜的同时适应过渡边的坡度。
//
// 适用场景：BoundarySegmentType.Transition 且高度差超过容差的墙段。
//
// 预制体约定：
//   - Pivot 在底部中心；
//   - Z 轴为长度方向；
//   - Y 轴为竖直方向；
//   - 长度方向须有足够顶点分段以获得平滑效果。
//
// 变形逻辑：
//   每个顶点的局部 Z 值决定其在墙段总长度中的比例 t（0=Z 起端，1=Z 末端）。
//   pivot 在底部中心（Z=0），故 t=0.5 对应墙体中点。
//   Y 偏移量 = (t - 0.5) * heightDelta
//     → t=0 端：Y -= heightDelta/2（对齐 start.y）
//     → t=1 端：Y += heightDelta/2（对齐 end.y）
//   宽度（X）和墙体相对高度（Y 的其余分量）不变。
//****************************************

public static class WallMeshDeformer
{
    /// <summary>
    /// 基于高度差创建变形后的城墙 Mesh。
    /// </summary>
    /// <param name="sourceMesh">预制体原始 Mesh（只读，不修改）。</param>
    /// <param name="heightDelta">终点 Y - 起点 Y（可为负）。</param>
    /// <returns>新建的变形 Mesh（调用方负责生命周期管理）。</returns>
    public static Mesh CreateDeformedMesh(Mesh sourceMesh, float heightDelta)
    {
        Mesh deformed = Object.Instantiate(sourceMesh);
        deformed.name = "WallDeformed";

        Vector3[] verts = deformed.vertices;

        // 求 Z 范围（预制体局部坐标）
        float zMin = float.MaxValue, zMax = float.MinValue;
        foreach (var v in verts)
        {
            if (v.z < zMin) zMin = v.z;
            if (v.z > zMax) zMax = v.z;
        }
        float zLength = zMax - zMin;
        if (zLength < 0.0001f)
        {
            // Z 方向无延伸，无法变形，原样返回
            return deformed;
        }

        for (int i = 0; i < verts.Length; i++)
        {
            // pivot 在底部中心（Z=0），t=0 对应 Z 起端，t=1 对应 Z 末端。
            // 偏移量 = (t - 0.5) * heightDelta，使两端分别对齐 start.y / end.y。
            float t = (verts[i].z - zMin) / zLength;
            verts[i].y += (t - 0.5f) * heightDelta;
        }

        deformed.vertices = verts;
        deformed.RecalculateBounds();
        deformed.RecalculateNormals();
        return deformed;
    }

    /// <summary>
    /// 将变形 Mesh 应用到目标 MeshFilter，并销毁旧变形 Mesh（若有）。
    /// </summary>
    public static void ApplyDeformedMesh(MeshFilter filter, Mesh sourceMesh, float heightDelta)
    {
        // 销毁之前动态创建的 Mesh（避免内存泄漏）
        if (filter.sharedMesh != null && filter.sharedMesh.name == "WallDeformed")
            Object.Destroy(filter.sharedMesh);

        filter.sharedMesh = CreateDeformedMesh(sourceMesh, heightDelta);
    }

    /// <summary>
    /// 若 filter 持有动态创建的变形 Mesh，销毁它并还原为 null。
    /// </summary>
    public static void ReleaseDeformedMesh(MeshFilter filter)
    {
        if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name == "WallDeformed")
        {
            Object.Destroy(filter.sharedMesh);
            filter.sharedMesh = null;
        }
    }
}
