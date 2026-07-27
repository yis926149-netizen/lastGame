using UnityEngine;

//****************************************
// 功能说明：势力范围边界线段（供实体城墙/城墩渲染使用）。
//   由 MeshDataGenerator.ExtractSphereOfInfluenceBoundary 从边界判定结果提取。
//
// 类型区分（见《势力范围实体城墙改造方案.md》）：
//   HexEdge    —— 三-1：地块自身的边界边，长度≈标准六边形边长，两端同格
//   Transition —— 三-2：相邻边缘地块之间的过渡边，长度/坡度可变，两端异格
//****************************************

public enum BoundarySegmentType
{
    HexEdge,     // 标准六边形边
    Transition   // 矩形过渡区域边
}

public struct BoundarySegment
{
    public Vector3 Start;
    public Vector3 End;
    public BoundarySegmentType Type;

    public BoundarySegment(Vector3 start, Vector3 end, BoundarySegmentType type)
    {
        Start = start;
        End = end;
        Type = type;
    }

    /// <summary>水平投影长度（忽略 Y）。</summary>
    public float HorizontalLength =>
        new Vector2(End.x - Start.x, End.z - Start.z).magnitude;

    /// <summary>两端高度差（End.y - Start.y）。</summary>
    public float HeightDelta => End.y - Start.y;

    /// <summary>三维中点。</summary>
    public Vector3 Midpoint => (Start + End) * 0.5f;
}
