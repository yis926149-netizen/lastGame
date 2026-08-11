using System.Collections.Generic;

/// <summary>普通建筑产生的阵营收入规则。</summary>
public static class BuildingIncomeRule
{
    public static float SumGoldMineIncome(
        IEnumerable<HexCellData> cells,
        int factionId,
        BuildingDatabaseSO buildingDatabase,
        ILogisticsService logisticsService = null)
    {
        if (cells == null || buildingDatabase == null || buildingDatabase.buildings == null)
            return 0f;

        BuildingConfigSO goldMineConfig = null;
        foreach (BuildingConfigSO config in buildingDatabase.buildings)
        {
            if (config != null && config.buildingType == Enums.BulidingType.GoldMine)
            {
                goldMineConfig = config;
                break;
            }
        }

        if (goldMineConfig == null || goldMineConfig.goldIncomePerSecond <= 0f)
            return 0f;

        float total = 0f;
        foreach (HexCellData cell in cells)
        {
            if (!IncomeEligibilityRule.IsIncomeEligible(cell, factionId, logisticsService)) continue;

            KeyValuePair<Enums.BulidingType, UnityEngine.GameObject> building =
                cell.BulidingTypeOnHex_Building;
            if (building.Key != Enums.BulidingType.GoldMine || building.Value == null) continue;

            total += goldMineConfig.goldIncomePerSecond;
        }

        return total;
    }
}
