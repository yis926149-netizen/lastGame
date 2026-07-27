using UnityEngine;

public static class TalentCardEffectApplier
{
    public static void ApplyToFaction(IFactionBuffService factionBuff, int faction, TalentCardConfigSO card)
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

        var eff = card.effect;
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
}
