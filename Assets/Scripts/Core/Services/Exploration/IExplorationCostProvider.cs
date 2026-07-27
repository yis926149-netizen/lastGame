/// <summary>
/// 探索成本提供者接口：计算探索某地块需要的资源。
/// 【探索重构-阶段3】占位接口，资源系统完成后替换实现。
/// </summary>
public interface IExplorationCostProvider
{
    /// <summary>
    /// 获取探索目标格子的成本
    /// </summary>
    /// <param name="targetCell">目标格子</param>
    /// <returns>资源类型和数量</returns>
    ExplorationCost GetCost(HexCellData targetCell);
}

/// <summary>
/// 探索成本数据结构（占位，资源系统完成后扩展）
/// </summary>
public struct ExplorationCost
{
    public string ResourceType;  // 资源类型（当前占位："Gold"）
    public int Amount;            // 数量

    public ExplorationCost(string resourceType, int amount)
    {
        ResourceType = resourceType;
        Amount = amount;
    }
}
