/// <summary>
/// 探索规则接口：校验目标地块是否允许探索。
/// 【探索重构-阶段3】邻接规则（A3 已确认）。
/// </summary>
public interface IExplorationRule
{
    /// <summary>
    /// 检查目标格子是否满足探索规则
    /// </summary>
    /// <param name="targetCell">目标格子</param>
    /// <returns>是否合法</returns>
    bool IsValid(HexCellData targetCell);
}
