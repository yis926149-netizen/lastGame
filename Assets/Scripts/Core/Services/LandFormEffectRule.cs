/// <summary>
/// 地图地貌效果规则（纯函数）。
/// 【地图地貌配置化】战斗与回血只按效果类型查询，不比较具体地貌 ID，
/// 收敛 CombatResolver 与旧 AttackDataComputation 中散落的硬编码效果。
/// </summary>
public static class LandFormEffectRule
{
    /// <summary>
    /// 查询地貌的防御加数；无地貌、None 效果或非防御型地貌返回 0。
    /// </summary>
    public static float GetDefenseBonus(MapLandFormSO landForm)
    {
        return landForm != null && landForm.effectType == LandFormEffectType.DefenseBonus
            ? landForm.effect.defenseBonus
            : 0f;
    }

    /// <summary>
    /// 查询地貌的周期回血配置；无地貌、None 效果或非回血型地貌返回 false。
    /// </summary>
    /// <param name="landForm">当前格地貌配置</param>
    /// <param name="healRatio">每次回复最大生命比例</param>
    /// <param name="healInterval">回血间隔（秒）</param>
    public static bool TryGetPeriodicHeal(
        MapLandFormSO landForm,
        out float healRatio,
        out float healInterval)
    {
        healRatio = 0f;
        healInterval = 0f;

        if (landForm == null || landForm.effectType != LandFormEffectType.PeriodicHeal)
            return false;

        healRatio = landForm.effect.healRatio;
        healInterval = landForm.effect.healInterval;
        return healRatio > 0f && healInterval > 0f;
    }

    /// <summary>
    /// 查询地貌的占领金币加成；无地貌、None 效果或非金矿型地貌返回 false。
    /// </summary>
    public static bool TryGetGoldIncomeBonus(MapLandFormSO landForm, out float bonusPerSecond)
    {
        bonusPerSecond = 0f;

        if (landForm == null || landForm.effectType != LandFormEffectType.GoldIncomeBoost)
            return false;

        bonusPerSecond = landForm.effect.goldIncomePerSecond;
        return bonusPerSecond > 0f;
    }

    /// <summary>
    /// 统计某阵营所有占领格中金矿地貌的金币加成总量（纯函数，便于测试）。
    /// 占领语义 = Player_City_Index.Key == factionId（与 TerritoryService/探索/公共建筑占领一致）。
    /// 【断供方案-阶段6.5】logisticsService 非空时追加"后勤畅通"过滤——断供地区的金矿
    /// 暂停产金，恢复供应后自动恢复；易主后按新主归属与供应结算。
    /// </summary>
    public static float SumGoldIncomeBonus(
        System.Collections.Generic.IEnumerable<HexCellData> cells,
        int factionId,
        ILogisticsService logisticsService = null)
    {
        if (cells == null) return 0f;

        float total = 0f;
        foreach (var cell in cells)
        {
            if (cell == null || cell.Player_City_Index.Key != factionId) continue;
            if (logisticsService != null && !logisticsService.IsLogisticsConnected(cell, factionId)) continue;
            if (TryGetGoldIncomeBonus(cell.landForm, out float bonus))
                total += bonus;
        }
        return total;
    }
}
