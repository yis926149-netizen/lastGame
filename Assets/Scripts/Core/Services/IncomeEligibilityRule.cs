/// <summary>
/// 阵营收入资格判定（纯函数）。
/// 地貌金矿（LandFormEffectRule）与建筑金矿（BuildingIncomeRule）共用同一套
/// "归属 + 后勤畅通"口径，避免两种收入来源的资格规则漂移。
/// </summary>
public static class IncomeEligibilityRule
{
    /// <summary>
    /// 该地块是否可为指定阵营产生收入：归属该阵营（Player_City_Index.Key == factionId，
    /// 与 TerritoryService/探索/公共建筑占领语义一致），且提供后勤服务时要求后勤畅通
    /// （断供地区暂停产金，恢复供应后自动恢复）。
    /// </summary>
    public static bool IsIncomeEligible(HexCellData cell, int factionId, ILogisticsService logisticsService)
    {
        if (cell == null || cell.Player_City_Index.Key != factionId) return false;
        if (logisticsService != null && !logisticsService.IsLogisticsConnected(cell, factionId)) return false;
        return true;
    }
}
