using System.Collections.Generic;
using System;
using UnityEngine;
using Zenject;

public interface ICardUnlockRuleProvider
{
    List<int> GetUnlockedCardIds(int techLevel, int cultureLevel);
}

public class CardUnlockRuleProvider : ICardUnlockRuleProvider
{
    private static readonly int[] BaseUnitIds = { 0, 1 };
    private static readonly int[] TechUnitIds = { 2, 3, 4, 9, 10, 11, 6, 5, 7, 8 };
        private static readonly int[] BaseBuildingIds = { 3, 4, 5 };
    private static readonly int[] CultureBuildingIds = { 2, 0, 1 };

    [Inject] private IUnitDataProvider _unitData;
    [Inject] private IBuildingDataProvider _buildingData;

    public List<int> GetUnlockedCardIds(int techLevel, int cultureLevel)
    {
        // 科技/文化系统已移除：现阶段无条件解锁全部单位卡与建筑卡。
        // techLevel / cultureLevel 参数保留以兼容调用方签名，当前忽略。
        // 后续如需按其他条件（时间、击杀数等）解锁，在此处填充规则即可。
        List<int> unlockedIds = new List<int>();
        int unitCount = (int)_unitData.GetUnitIconCount();
        int buildingCount = _buildingData.GetBuildingCardsCount();
        ValidateCardDatabase(unitCount, buildingCount);

        // 【临时测试】禁用单位卡，只抽建筑卡
        // AddValidUnitIds(unlockedIds, BaseUnitIds, unitCount);
        // AddValidUnitIds(unlockedIds, TechUnitIds, unitCount);
        AddValidBuildingIds(unlockedIds, BaseBuildingIds, unitCount, buildingCount);
        AddValidBuildingIds(unlockedIds, CultureBuildingIds, unitCount, buildingCount);

        return unlockedIds;
    }

    private static void ValidateCardDatabase(int unitCount, int buildingCount)
    {
        ValidateIds(BaseUnitIds, TechUnitIds, unitCount, "unit");
        ValidateIds(BaseBuildingIds, CultureBuildingIds, buildingCount, "building");
    }

    private static void ValidateIds(int[] baseIds, int[] progressionIds, int count, string cardType)
    {
        var ids = new HashSet<int>();
        foreach (int id in baseIds)
        {
            if (id < 0 || id >= count || !ids.Add(id))
                throw new InvalidOperationException($"Invalid or duplicate {cardType} card ID {id} for database count {count}.");
        }

        foreach (int id in progressionIds)
        {
            if (id < 0 || id >= count || !ids.Add(id))
                throw new InvalidOperationException($"Invalid or duplicate {cardType} card ID {id} for database count {count}.");
        }
    }

    private static void AddValidUnitIds(List<int> cards, int[] unitIds, int unitCount, int count = int.MaxValue)
    {
        int end = Mathf.Min(unitIds.Length, Mathf.Max(0, count));
        for (int i = 0; i < end; i++)
        {
            int unitId = unitIds[i];
            if (unitId >= 0 && unitId < unitCount && !cards.Contains(unitId))
            {
                cards.Add(unitId);
            }
        }
    }

    private static void AddValidBuildingIds(
        List<int> cards,
        int[] buildingIds,
        int unitCount,
        int buildingCount,
        int count = int.MaxValue)
    {
        int end = Mathf.Min(buildingIds.Length, Mathf.Max(0, count));
        for (int i = 0; i < end; i++)
        {
            int buildingId = buildingIds[i];
            if (buildingId < 0 || buildingId >= buildingCount) continue;

            int cardId = unitCount + buildingId;
            if (!cards.Contains(cardId))
            {
                cards.Add(cardId);
            }
        }
    }
}
