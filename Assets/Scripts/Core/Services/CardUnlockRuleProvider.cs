using System.Collections.Generic;
using Zenject;

public interface ICardUnlockRuleProvider
{
    List<int> GetUnlockedCardIds(int techLevel, int cultureLevel);
}

public class CardUnlockRuleProvider : ICardUnlockRuleProvider
{
    [Inject] private IUnitDataProvider _unitData;
    [Inject] private IBuildingDataProvider _buildingData;

    public List<int> GetUnlockedCardIds(int techLevel, int cultureLevel)
    {
        List<int> unlockedIds = new List<int>();

        int unitCount = (int)_unitData.GetUnitIconCount();
        for (int i = 0; i < unitCount; i++)
        {
            if (i <= techLevel + 1)
            {
                unlockedIds.Add(i);
            }
        }

        int buildingCount = _buildingData.GetBuildingCardsCount();
        for (int i = 0; i < buildingCount; i++)
        {
            if (i <= cultureLevel)
            {
                unlockedIds.Add(unitCount + i);
            }
        }

        return unlockedIds;
    }
}
