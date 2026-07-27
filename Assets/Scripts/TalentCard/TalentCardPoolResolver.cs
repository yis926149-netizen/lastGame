using System.Collections.Generic;
using System.Linq;

public static class TalentCardPoolResolver
{
    public static List<TalentCardConfigSO> DrawRandom(TalentCardPoolSO pool, int count)
    {
        if (pool == null || pool.cards == null || pool.cards.Count == 0)
            return new List<TalentCardConfigSO>();

        var available = pool.cards.Where(c => c != null).ToList();
        if (available.Count == 0)
            return new List<TalentCardConfigSO>();

        int drawCount = System.Math.Min(count, available.Count);

        for (int i = 0; i < drawCount; i++)
        {
            int j = i + UnityEngine.Random.Range(0, available.Count - i);
            var temp = available[i];
            available[i] = available[j];
            available[j] = temp;
        }

        return available.GetRange(0, drawCount);
    }
}
