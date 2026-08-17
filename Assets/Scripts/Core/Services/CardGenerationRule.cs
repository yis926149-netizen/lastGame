using System;
using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：抽卡生成规则——玩家与 AI 共享的单一实现（对象化）。
//         从解锁池有放回随机抽一张配置；首张保底由卡池配置提供。
//         抽卡偏好由天赋抽卡规则表（TalentDrawRuleDatabaseSO）显式声明：
//         triggerTalentId → targetCardType → weightMultiplier，不再由天赋 ID 隐式推断。
//****************************************

public static class CardGenerationRule
{
    /// <summary>生成下一张卡。</summary>
    /// <param name="giveFirstSettler">当前是否处于"应保底发卡"的时机（玩家=回合1；AI=开局 true）。</param>
    /// <param name="hasGivenFirstSettler">是否已发过保底卡（按引用读写；各方各自持有该标志）。</param>
    /// <param name="drawRules">天赋抽卡偏好规则库（Excel 生成，可选；为空则均匀随机）。</param>
    public static NormalCardConfigSO GenerateNextCard(
        bool giveFirstSettler,
        ref bool hasGivenFirstSettler,
        ICardUnlockRuleProvider unlockProvider,
        System.Random random,
        IFactionBuffService factionBuff,
        int faction,
        TalentDrawRuleDatabaseSO drawRules = null)
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

        // 抽卡偏好：由规则表显式声明，缺失/未触发则为均匀随机（权重 1）。
        int unitWeight = 1;
        int buildingWeight = 1;
        if (drawRules != null && factionBuff != null)
        {
            foreach (var rule in drawRules.EnabledRules)
            {
                if (rule == null || !factionBuff.HasBuff(faction, rule.triggerTalentId)) continue;
                if (rule.targetCardType == "Unit") unitWeight = Math.Max(unitWeight, rule.weightMultiplier);
                else if (rule.targetCardType == "Building") buildingWeight = Math.Max(buildingWeight, rule.weightMultiplier);
            }
        }

        bool favorUnits = unitWeight > 1;
        bool favorBuildings = buildingWeight > 1;
        if (!favorUnits && !favorBuildings)
        {
            return cards[random.Next(cards.Count)];
        }

        int totalWeight = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            totalWeight += GetCardWeight(cards[i], favorUnits, favorBuildings, unitWeight, buildingWeight);
        }

        int roll = random.Next(totalWeight);
        for (int i = 0; i < cards.Count; i++)
        {
            roll -= GetCardWeight(cards[i], favorUnits, favorBuildings, unitWeight, buildingWeight);
            if (roll < 0) return cards[i];
        }

        return cards[cards.Count - 1];
    }

    private static int GetCardWeight(NormalCardConfigSO card, bool favorUnits, bool favorBuildings, int unitWeight, int buildingWeight)
    {
        if (favorUnits && card is UnitConfigSO) return unitWeight;
        if (favorBuildings && card is BuildingConfigSO) return buildingWeight;
        return 1;
    }
}
