using System.Collections.Generic;
using UnityEngine;

internal sealed class TerrainGeometry
{
    public Vector3[] Vertices;
    public Vector2[] UVs;
    public int[][] SubMeshIndices;
    public Material[] BaseMaterials;
    public Material[] RectAs;
    public Material[] RectBs;
    public Material[] TriAs;
    public Material[] TriBs;
    public Material[] TriCs;
    public Vector2[] UV2s;
    public Vector2[] UV3s;
    /// <summary>
    /// 山脚材质融合数据（UV4/TEXCOORD3）：xy=原 rect UV，z=岩石权重（山侧 1 → 格界 0）。
    /// 仅存在山-普通边界槽时分配；其他顶点保持零。
    /// </summary>
    public Vector4[] UV4s;

    /// <summary>山-普通山侧半 rect 的普通侧地形材质；槽位位于 tri 组合之后、主山体槽之前。</summary>
    public Material[] MountainBoundaryMaterials;

    /// <summary>末尾山体槽索引（阶段 3.6 起填充）；null = 本 Chunk 无山体几何。</summary>
    public int[] MountainIndices;

    /// <summary>
    /// 独立碰撞网格索引（决策 ㉚；共享 Vertices，不含被山体替换的原始面）；
    /// null = 无山 Chunk，Commit 时碰撞回落渲染 mesh，零额外内存。
    /// </summary>
    public int[] CollisionIndices;

    /// <summary>
    /// 槽存在条件以最终非空 indices 为准，不看残留 landForm（源码审计修正 B-2）。
    /// 渲染 mesh 的末尾山体槽与独立碰撞网格都以此为准。
    /// </summary>
    public bool HasMainMountainSlot => MountainIndices != null && MountainIndices.Length > 0;
    public bool HasMountain => HasMainMountainSlot
        || (MountainBoundaryMaterials != null && MountainBoundaryMaterials.Length > 0);

    /// <summary>
    /// 【程序化山脉-阶段 5.7】动画构建的保守 bounds（含山峰与 clip 余量；决策 ㉛）。
    /// 仅动画路径（anim ≠ null）填充；普通构建为 null，FillMeshData 维持 RecalculateBounds 原行为。
    /// CPU 顶点动画逐帧写 vertices 不更新 bounds，提交时预扩覆盖 start→target 全程，防峰顶被
    /// 视锥/阴影剔除（源码审计修正 B-6）。
    /// </summary>
    public Bounds? ConservativeBounds;

    /// <summary>
    /// 【程序化山脉-阶段 5.8】仅山体渲染顶点区间（平坦 (start,count) 对：start,count,start,count...）。
    /// 独立碰撞网格索引（CollisionIndices）不得引用这些区间内的顶点（决策 ㉛ 校验；
    /// 构造上由分槽构建保证——碰撞索引只引用基础 solid/rect/tri 顶点，供回归拦截）。
    /// </summary>
    public int[] MountainVertexRanges;
}

internal sealed class RiverGeometry
{
    public Vector3[] Vertices;
    public Vector2[] UVs;
    public int[] Indices;
}

internal sealed class WaterGeometry
{
    public Vector3[] Vertices;
    public Vector2[] UVs;
    public int[][] Indices;
}
