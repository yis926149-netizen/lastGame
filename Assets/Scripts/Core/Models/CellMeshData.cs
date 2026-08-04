using System.Collections.Generic;
using UnityEngine;

//****************************************
//功能说明：无状态网格构建的输出/输入模型（动态地图变化方案-阶段一）。
//构建结果只存"格级几何产物"，不再写回 HexCellData 的渲染缓存字段。
//****************************************

/// <summary>
/// 实心区域构建结果：44 顶点（含河道）+ 中心点。
/// 中心点 = Vertices[0]，等价于原 GetSolidAreaVertices 写入的 RealCenterWorldCoordinate。
/// </summary>
public sealed class SolidAreaMeshData
{
    /// <summary>实心区域 44 个顶点（含河道 25 点），世界坐标。</summary>
    public Vector3[] Vertices;

    /// <summary>地块中心世界坐标（Vertices[0]），供调用方同步逻辑字段。</summary>
    public Vector3 Center;
}

/// <summary>
/// 单格几何构建的输入上下文。由调用方（MapRenderer）为每个格子构建时创建，
/// 生成器方法只读该上下文与显式参数，不写回任何 HexCellData 渲染缓存——保证无状态、
/// 可重复调用（同一输入必得同一输出）。
/// </summary>
public sealed class CellBuildContext
{
    /// <summary>正在构建的地块（逻辑字段只读）。</summary>
    public HexCellData Cell;

    /// <summary>只读地图视图（邻居/格子查询）。</summary>
    public IReadOnlyMapView View;

    /// <summary>本格实心区域 44 点（BuildSolidArea 的输出，由调用方回填）。</summary>
    public Vector3[] Solid;

    /// <summary>全图实心 44 点（GenerateOrder → 44 点）。供跨格依赖（矩形/三角过渡）读取。</summary>
    public IReadOnlyDictionary<int, Vector3[]> Solids;

    /// <summary>全图湖海实心 25 点（GenerateOrder → 25 点，仅水格）。供湖海过渡读取。</summary>
    public IReadOnlyDictionary<int, Vector3[]> LakeOrSeas;

    /// <summary>
    /// 全图矩形过渡顶点组（GenerateOrder, 方向）→ 顶点列表。
    /// 供三角过渡方法三/四（TriStep3/TriStep4）读取矩形后(前)段顶点。
    /// 非河流矩形保持空列表（与旧行为一致：旧代码仅在河流矩形时写回该缓存）。
    /// </summary>
    public IReadOnlyDictionary<(int order, Enums.HexDirection dir), List<Vector3>> RectVertices;

    /// <summary>阶梯插值数（原 hexCellData.interpCount，默认 1）。</summary>
    public int InterpCount = 1;

    /// <summary>本格湖海实心 25 点（非水格返回 null）。</summary>
    public Vector3[] SelfLake
    {
        get
        {
            if (LakeOrSeas == null) return null;
            LakeOrSeas.TryGetValue(Cell.GenerateOrder, out Vector3[] lake);
            return lake;
        }
    }

    /// <summary>按方向取邻居实心 44 点；邻居不存在或无数据返回 null。</summary>
    public Vector3[] GetNeighborSolid(Enums.HexDirection direction)
    {
        if (View == null || Solids == null) return null;
        HexCellData neighbor = View.GetNeighbor(Cell, direction);
        if (neighbor == null) return null;
        Solids.TryGetValue(neighbor.GenerateOrder, out Vector3[] solid);
        return solid;
    }

    /// <summary>按方向取邻居湖海 25 点；邻居非水/无数据返回 null。</summary>
    public Vector3[] GetNeighborLake(Enums.HexDirection direction)
    {
        if (View == null || LakeOrSeas == null) return null;
        HexCellData neighbor = View.GetNeighbor(Cell, direction);
        if (neighbor == null) return null;
        LakeOrSeas.TryGetValue(neighbor.GenerateOrder, out Vector3[] lake);
        return lake;
    }

    /// <summary>取本格指定方向的矩形过渡顶点组（缺失时返回空列表，等价旧行为：非河流矩形不写缓存）。</summary>
    public List<Vector3> GetRectVertices(Enums.HexDirection direction)
    {
        if (RectVertices == null) return EmptyRectVertices;
        RectVertices.TryGetValue((Cell.GenerateOrder, direction), out List<Vector3> list);
        return list ?? EmptyRectVertices;
    }

    /// <summary>取 neighborDirection 邻居的 rectDirection 方向矩形过渡顶点组（缺失时返回空列表）。</summary>
    public List<Vector3> GetNeighborRectVertices(Enums.HexDirection neighborDirection, Enums.HexDirection rectDirection)
    {
        if (View == null || RectVertices == null) return EmptyRectVertices;
        HexCellData neighbor = View.GetNeighbor(Cell, neighborDirection);
        if (neighbor == null) return EmptyRectVertices;
        RectVertices.TryGetValue((neighbor.GenerateOrder, rectDirection), out List<Vector3> list);
        return list ?? EmptyRectVertices;
    }

    private static readonly List<Vector3> EmptyRectVertices = new List<Vector3>();
}
