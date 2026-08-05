public static class WaterLevelConfig
{
    /// <summary>
    /// 水域判定阈值：Height &lt;= WaterLevel 为湖海，Height &gt; WaterLevel 为陆地。
    /// 由 MapGenerator 在生成前从 MapGenerationConfigSO.seaLevel 同步，保证分类与水面高度一致。
    /// </summary>
    public static float WaterLevel = 1f;

    /// <summary>
    /// 当前地图全局最大高度，用于将高度区间对半分给 3 个子 Mesh。
    /// 地图生成时由 MapGenerator 设置一次。
    /// </summary>
    public static float MaxHeight { get; set; } = 2f;

    public static bool IsWater(float height)
    {
        return height <= WaterLevel;
    }

    public static bool IsWater(HexCellData cell)
    {
        return cell != null && IsWater(cell.Height);
    }

    /// <summary>
    /// 按高度范围分桶：0=水域, 1=低地, 2=高地（对应 subList 索引）。不改动现有三层子 Mesh 结构。
    /// </summary>
    public static int ClassifyHeight(float height)
    {
        if (height <= WaterLevel) return 0;
        float midPoint = WaterLevel + (MaxHeight - WaterLevel + 1f) * 0.5f;
        return height < midPoint ? 1 : 2;
    }
}
