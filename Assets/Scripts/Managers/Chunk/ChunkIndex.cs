using System;

//****************************************
// 【动态地图-阶段三】Chunk 索引与 8×8 offset-grid 划分（§二十-1 锁定）
// row = GenerateOrder / xNumber、column = GenerateOrder % xNumber、
// chunkX = column / 8、chunkZ = row / 8。不依赖 cube 坐标奇偶/负值。
//****************************************

public readonly struct ChunkIndex : IEquatable<ChunkIndex>
{
    public readonly int X;
    public readonly int Z;

    public ChunkIndex(int x, int z)
    {
        X = x;
        Z = z;
    }

    /// <summary>按生成网格索引计算所属 Chunk（§二十-1 公式）。</summary>
    public static ChunkIndex Of(HexCellData cell, int xNumber)
    {
        int row = cell.GenerateOrder / xNumber;
        int column = cell.GenerateOrder % xNumber;
        return new ChunkIndex(column / ChunkMapRenderer.ChunkSize, row / ChunkMapRenderer.ChunkSize);
    }

    public bool Equals(ChunkIndex other) => X == other.X && Z == other.Z;
    public override bool Equals(object obj) => obj is ChunkIndex other && Equals(other);
    public override int GetHashCode() => (X * 397) ^ Z;
    public static bool operator ==(ChunkIndex a, ChunkIndex b) => a.Equals(b);
    public static bool operator !=(ChunkIndex a, ChunkIndex b) => !a.Equals(b);
    public override string ToString() => $"Chunk({X},{Z})";
}
