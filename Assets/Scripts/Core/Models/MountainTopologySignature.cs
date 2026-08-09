using System;
using System.Collections.Generic;

/// <summary>
/// 【阶段 5.1】山体拓扑签名（动画资格判定的纯函数契约，决策 ㉙/㉛）。
/// 只描述"哪些拓扑事实决定能否动画"：HasMountain、顶点/山体索引数量、subMesh 布局、
/// 山体与碰撞 indices 内容摘要、本 Chunk 有效山格集合摘要。
/// 签名绝不包含任何纯 Y 高度值——普通 Height 变化必须保持签名不变，
/// 否则会被误判为拓扑改变而错误降级动画（阶段 5.5 路由依据）。
/// 无山 Chunk 恒为默认值 Empty，不分配大数组（调用方先判 HasMountain 再构建）。
/// </summary>
public readonly struct MountainTopologySignature : IEquatable<MountainTopologySignature>
{
    /// <summary>拓扑摘要专用域（"TOPO"），与几何 hash 域隔离。</summary>
    private const uint TopologyDomain = 0x544f504f;

    public readonly bool HasMountain;
    public readonly int TotalVertexCount;
    public readonly int MountainIndexCount;
    /// <summary>subMesh 布局 + 山体/碰撞 indices 内容摘要（FNV 风格，不包含顶点位置）。</summary>
    public readonly int LayoutHash;
    /// <summary>本 Chunk 有效山格集合摘要（排序后 GenerateOrder 混合，遍历顺序无关，决策 ㉓）。</summary>
    public readonly int CellSetHash;

    public MountainTopologySignature(bool hasMountain, int totalVertexCount, int mountainIndexCount, int layoutHash, int cellSetHash)
    {
        HasMountain = hasMountain;
        TotalVertexCount = totalVertexCount;
        MountainIndexCount = mountainIndexCount;
        LayoutHash = layoutHash;
        CellSetHash = cellSetHash;
    }

    public static MountainTopologySignature Empty => default;

    /// <summary>
    /// 纯函数构造。全部输入 = 拓扑事实 + 有效山格 GenerateOrder 集合；
    /// 不含任何顶点 Y 值。!hasMountain 时恒返回默认空签名（与 Empty 相等）。
    /// </summary>
    public static MountainTopologySignature Build(
        bool hasMountain,
        int totalVertexCount,
        int mountainIndexCount,
        IReadOnlyList<int> subMeshIndexCounts,
        IReadOnlyList<int> mountainIndices,
        IReadOnlyList<int> collisionIndices,
        IReadOnlyCollection<int> visibleMountainCellOrders)
    {
        if (!hasMountain) return default;

        int layoutHash = (int)MountainHash.Hash((int)TopologyDomain, totalVertexCount, mountainIndexCount);
        if (subMeshIndexCounts != null)
        {
            for (int i = 0; i < subMeshIndexCounts.Count; i++)
                layoutHash = (int)MountainHash.Hash(layoutHash, i, subMeshIndexCounts[i]);
        }
        layoutHash = FoldIndices(layoutHash, mountainIndices);
        layoutHash = FoldIndices(layoutHash, collisionIndices);

        int cellSetHash = (int)MountainHash.Hash((int)TopologyDomain, -1);
        if (visibleMountainCellOrders != null && visibleMountainCellOrders.Count > 0)
        {
            var orders = new List<int>(visibleMountainCellOrders);
            orders.Sort();
            cellSetHash = (int)MountainHash.Hash(cellSetHash, orders.ToArray());
        }

        return new MountainTopologySignature(true, totalVertexCount, mountainIndexCount, layoutHash, cellSetHash);
    }

    private static int FoldIndices(int seed, IReadOnlyList<int> indices)
    {
        if (indices == null || indices.Count == 0) return seed;
        var keys = new int[indices.Count];
        for (int i = 0; i < keys.Length; i++) keys[i] = indices[i];
        return (int)MountainHash.Hash(seed, keys);
    }

    /// <summary>
    /// 【阶段 5.5】由格集合派生有效山格集合摘要（决策 ㉓ 顺序无关：内部按 GenerateOrder 排序）。
    /// 任何会改变 Chunk 山体 solid/rect/tri 拓扑的数据变化（清除/恢复/陆水/阈值跨越/新增）
    /// 都必然翻转集合中某格的 HasVisibleMountain ⇒ 摘要改变；纯 Height 变化不影响 HasVisibleMountain
    /// ⇒ 摘要不变。供 MapMutationService 动画前路由比较（整笔事务降级决策）。
    /// </summary>
    public static int VisibleCellSetHash(IEnumerable<HexCellData> cells)
    {
        var orders = new List<int>();
        if (cells != null)
        {
            foreach (HexCellData cell in cells)
            {
                if (cell != null && MountainGeometryBuilder.HasVisibleMountain(cell))
                    orders.Add(cell.GenerateOrder);
            }
        }
        orders.Sort();
        return (int)MountainHash.Hash((int)TopologyDomain, orders.ToArray());
    }

    public bool Equals(MountainTopologySignature other)
    {
        return HasMountain == other.HasMountain
            && TotalVertexCount == other.TotalVertexCount
            && MountainIndexCount == other.MountainIndexCount
            && LayoutHash == other.LayoutHash
            && CellSetHash == other.CellSetHash;
    }

    public override bool Equals(object obj)
    {
        return obj is MountainTopologySignature other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (HasMountain ? 1 : 0);
            hash = hash * 31 + TotalVertexCount;
            hash = hash * 31 + MountainIndexCount;
            hash = hash * 31 + LayoutHash;
            hash = hash * 31 + CellSetHash;
            return hash;
        }
    }

    public static bool operator ==(MountainTopologySignature a, MountainTopologySignature b) => a.Equals(b);
    public static bool operator !=(MountainTopologySignature a, MountainTopologySignature b) => !a.Equals(b);
}
