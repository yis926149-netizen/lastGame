using System;
using System.Collections.Generic;

//****************************************
//功能说明：抽卡生成规则——玩家与 AI 共享的单一实现（对象化）。
//         从解锁池有放回随机抽一张配置；首张保底移民卡由卡池配置提供。
//         玩家与 AI 继续使用各自注入的 System.Random。
//****************************************

public static class CardGenerationRule
{
    private const string UnitCardProbabilityTalentId = "0";
    private const string BuildingCardProbabilityTalentId = "1";
    private const int FavoredCardWeight = 10;

    /// <summary>生成下一张卡。</summary>
    /// <param name="giveFirstSettler">当前是否处于"应保底发移民卡"的时机（玩家=回合1；AI=开局 true）。</param>
    /// <param name="hasGivenFirstSettler">是否已发过保底移民卡（按引用读写；各方各自持有该标志）。</param>
    public static NormalCardConfigSO GenerateNextCard(
        bool giveFirstSettler,
        ref bool hasGivenFirstSettler,
        ICardUnlockRuleProvider unlockProvider,
        System.Random random,
        IFactionBuffService factionBuff,
        int faction)
    {
        if (giveFirstSettler && !hasGivenFirstSettler)
        {
            hasGivenFirstSettler = true;
            NormalCardConfigSO first = unlockProvider.GetGuaranteedFirstCard();
            if (first != null) return first;
        }

        IReadOnlyList<NormalCardConfigSO> cards = unlockProvider.GetUnlockedCards();
        if (cards == null || cards.Count == 0)
        {
            // 空池回退保底（保持旧 return 0 的语义）
            NormalCardConfigSO fallback = unlockProvider.GetGuaranteedFirstCard();
            if (fallback != null) return fallback;
            throw new InvalidOperationException("[CardGenerationRule] 普通卡池为空且无保底卡，无法生成卡牌。");
        }

        bool favorUnits = factionBuff != null && factionBuff.HasBuff(faction, UnitCardProbabilityTalentId);
        bool favorBuildings = factionBuff != null && factionBuff.HasBuff(faction, BuildingCardProbabilityTalentId);
        if (!favorUnits && !favorBuildings)
        {
            return cards[random.Next(cards.Count)];
        }

        int totalWeight = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            totalWeight += GetCardWeight(cards[i], favorUnits, favorBuildings);
        }

        int roll = random.Next(totalWeight);
        for (int i = 0; i < cards.Count; i++)
        {
            roll -= GetCardWeight(cards[i], favorUnits, favorBuildings);
            if (roll < 0) return cards[i];
        }

        return cards[cards.Count - 1];
    }

    private static int GetCardWeight(NormalCardConfigSO card, bool favorUnits, bool favorBuildings)
    {
        if (favorUnits && card is UnitConfigSO) return FavoredCardWeight;
        if (favorBuildings && card is BuildingConfigSO) return FavoredCardWeight;
        return 1;
    }
}
