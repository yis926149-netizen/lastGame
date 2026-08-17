using System.Collections.Generic;
using GameConfig;

/// <summary>普通建筑产生的阵营收入规则（金矿收入数值来自 Excel 平衡库）。</summary>
public static class BuildingIncomeRule
{
    public static float SumGoldMineIncome(
        IEnumerable<HexCellData> cells,
        int factionId,
        BuildingBalanceDatabaseSO buildingBalance,
        ILogisticsService logisticsService = null)
    {
        if (cells == null || buildingBalance == null)
            return 0f;

        // 金矿每秒收入：从 Excel 平衡库按 buildingType=="GoldMine" 定位，避免硬编码 legacyId。
        float goldIncomePerSecond = 0f;
        bool found = false;
        foreach (var b in buildingBalance.Buildings)
        {
            if (b != null && b.buildingType == "GoldMine")
            {
                goldIncomePerSecond = b.goldIncomePerSecond;
                found = true;
                break;
            }
        }

        if (!found || goldIncomePerSecond <= 0f)
            return 0f;

        float total = 0f;
        foreach (HexCellData cell in cells)
        {
            if (!IncomeEligibilityRule.IsIncomeEligible(cell, factionId, logisticsService)) continue;

            KeyValuePair<Enums.BulidingType, UnityEngine.GameObject> building =
                cell.BulidingTypeOnHex_Building;
            if (building.Key != Enums.BulidingType.GoldMine || building.Value == null) continue;

            total += goldIncomePerSecond;
        }

        return total;
    }
}
