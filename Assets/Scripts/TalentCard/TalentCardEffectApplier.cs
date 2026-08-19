using UnityEngine;
using GameConfig;

public static class TalentCardEffectApplier
{
    public static void ApplyToFaction(IFactionBuffService factionBuff, int faction, TalentCardConfigSO card, TalentCardBalanceData balance = null)
    {
        if (factionBuff == null)
        {
            Debug.LogError("[TalentCardEffectApplier] factionBuff is null");
            return;
        }
        if (card == null)
        {
            Debug.LogError("[TalentCardEffectApplier] card is null");
            return;
        }

        var eff = GetEffect(card, balance);
        string statId = eff.GetStatIdString();
        Buff buff;
        switch (eff.type)
        {
            case TalentEffectType.StatMultiplier:
                buff = new StatMultiplierBuff(card.talentId, statId, eff.value);
                break;
            case TalentEffectType.StatAddition:
                buff = new StatAdditiveBuff(card.talentId, statId, eff.value);
                break;
            default:
                Debug.LogError($"[TalentCardEffectApplier] Unknown effect type: {eff.type}");
                return;
        }

        Debug.Log($"[TalentCardEffectApplier] Applying: faction={faction}, card={card.talentName}, statId={statId}, type={eff.type}, value={eff.value}");
        factionBuff.AddBuff(faction, buff);
    }

    /// <summary>效果数值：仅 Excel 数值库（阶段6 唯一主源，balance 为 null 抛异常）。</summary>
    private static TalentCardEffect GetEffect(TalentCardConfigSO card, TalentCardBalanceData balance)
    {
        if (balance == null)
            throw new System.InvalidOperationException(
                $"[TalentCardEffectApplier] 天赋 {card?.talentId ?? "(null)"} 缺少 Excel 数值（balance 为 null），无法应用效果。");
        return new TalentCardEffect
        {
            type = ParseEffectType(balance.effectType),
            statId = ParseStatId(balance.statId),
            value = balance.value,
        };
    }

    private static TalentEffectType ParseEffectType(string s)
        => s == "StatAddition" ? TalentEffectType.StatAddition : TalentEffectType.StatMultiplier;

    private static TalentStatId ParseStatId(string s)
    {
        switch (s)
        {
            case "defense": return TalentStatId.defense;
            case "gold": return TalentStatId.gold;
            case "buildingHp": return TalentStatId.buildingHp;
            default: return TalentStatId.damage;
        }
    }
}
