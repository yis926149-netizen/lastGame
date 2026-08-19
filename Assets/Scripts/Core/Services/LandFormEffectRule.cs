using GameConfig;

/// <summary>
/// 地图地貌效果规则（纯函数，阶段6：Excel 唯一主源）。
/// 【地图地貌配置化 + Excel 数值化】战斗与回血只按效果类型查询，不比较具体地貌 ID。
/// 效果数值仅读 Excel（MapLandFormBalanceDatabaseSO，由 GameInstaller 启动时 Configure）；
/// 地貌未在 Excel 命中时抛异常，暴露配置缺失。无地貌（landForm == null）时返回默认（0/false）。
/// </summary>
public static class LandFormEffectRule
{
    private static MapLandFormBalanceDatabaseSO _balance;

    /// <summary>由 GameInstaller 在绑定阶段配置 Excel 数值库。</summary>
    public static void Configure(MapLandFormBalanceDatabaseSO balance)
    {
        _balance = balance;
    }

    private static MapLandFormBalanceData RequireBalance(MapLandFormSO landForm)
    {
        if (_balance == null)
            throw new System.InvalidOperationException(
                "[LandFormEffect] Excel 地貌数值库未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 MapLandFormBalanceDatabaseSO。");
        if (!_balance.TryGetLandForm(landForm.landFormId, out var b))
            throw new System.InvalidOperationException(
                $"[LandFormEffect] 地貌 {landForm.landFormId} 未在 Excel 地貌数值库命中，无法读取效果数值。");
        return b;
    }

    /// <summary>
    /// 查询地貌的防御加数；无地貌、None 效果或非防御型地貌返回 0。
    /// </summary>
    public static float GetDefenseBonus(MapLandFormSO landForm)
    {
        if (landForm == null) return 0f;
        var b = RequireBalance(landForm);
        return b.effectType == "DefenseBonus" ? b.defenseBonus : 0f;
    }

    /// <summary>
    /// 查询地貌的周期回血配置；无地貌、None 效果或非回血型地貌返回 false。
    /// </summary>
    public static bool TryGetPeriodicHeal(
        MapLandFormSO landForm,
        out float healRatio,
        out float healInterval)
    {
        healRatio = 0f;
        healInterval = 0f;

        if (landForm == null) return false;
        var b = RequireBalance(landForm);
        if (b.effectType != "PeriodicHeal") return false;
        healRatio = b.healRatio;
        healInterval = b.healInterval;
        return healRatio > 0f && healInterval > 0f;
    }

    /// <summary>
    /// 查询地貌是否阻挡建筑部署（blockBuildingSpawn）；无地貌返回 false。
    /// </summary>
    public static bool GetBlockBuildingSpawn(MapLandFormSO landForm)
    {
        if (landForm == null) return false;
        return RequireBalance(landForm).blockBuildingSpawn;
    }

    /// <summary>
    /// 查询地貌的占领金币加成；无地貌、None 效果或非金矿型地貌返回 false。
    /// </summary>
    public static bool TryGetGoldIncomeBonus(MapLandFormSO landForm, out float bonusPerSecond)
    {
        bonusPerSecond = 0f;

        if (landForm == null) return false;
        var b = RequireBalance(landForm);
        if (b.effectType != "GoldIncomeBoost") return false;
        bonusPerSecond = b.goldIncomePerSecond;
        return bonusPerSecond > 0f;
    }

    /// <summary>
    /// 统计某阵营所有占领格中金矿地貌的金币加成总量（纯函数，便于测试）。
    /// 占领语义 = Player_City_Index.Key == factionId（与 TerritoryService/探索/公共建筑占领一致）。
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
            if (!IncomeEligibilityRule.IsIncomeEligible(cell, factionId, logisticsService)) continue;
            if (TryGetGoldIncomeBonus(cell.landForm, out float bonus))
                total += bonus;
        }
        return total;
    }
}
