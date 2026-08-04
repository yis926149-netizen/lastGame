/// <summary>
/// 探索规则接口：校验目标地块是否允许指定阵营探索。
/// 【探索重构-阶段3】邻接规则（A3 已确认）。
/// 【统一开发入口】IsValid 增加阵营参数，玩家与 AI 共用同一规则。
/// </summary>
public interface IExplorationRule
{
    /// <summary>
    /// 检查目标格子是否满足指定阵营的探索规则
    /// </summary>
    /// <param name="targetCell">目标格子</param>
    /// <param name="factionId">开发方阵营（0=玩家，1=AI）</param>
    /// <returns>是否合法</returns>
    bool IsValid(HexCellData targetCell, int factionId);
}
