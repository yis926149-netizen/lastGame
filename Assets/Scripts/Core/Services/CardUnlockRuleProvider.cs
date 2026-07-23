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
    private static readonly int[] BaseBuildingIds = { 3 };
    private static readonly int[] CultureBuildingIds = { 2, 0, 1 };

    [Inject] private IUnitDataProvider _unitData;
    [Inject] private IBuildingDataProvider _buildingData;

    public List<int> GetUnlockedCardIds(int techLevel, int cultureLevel)
    {
        List<int> unlockedIds = new List<int>();
        int unitCount = (int)_unitData.GetUnitIconCount();
        int buildingCount = _buildingData.GetBuildingCardsCount();
        ValidateCardDatabase(unitCount, buildingCount);

        AddValidUnitIds(unlockedIds, BaseUnitIds, unitCount);
        AddValidUnitIds(unlockedIds, TechUnitIds, unitCount, techLevel + 1);
        AddValidBuildingIds(unlockedIds, BaseBuildingIds, unitCount, buildingCount);
        AddValidBuildingIds(unlockedIds, CultureBuildingIds, unitCount, buildingCount, cultureLevel + 1);

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
