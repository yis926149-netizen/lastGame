using System.Collections.Generic;
using UnityEngine;

public class TalentCardOfferEventArgs : System.EventArgs
{
    public int Faction;
    public List<TalentCardConfigSO> Cards;
}

public class TalentCardTriggerAdapter
{
    private readonly TalentCardPoolSO _pool;
    private readonly IFactionBuffService _factionBuff;

    public event System.EventHandler<TalentCardOfferEventArgs> OnOfferRequested;

    public TalentCardTriggerAdapter(TalentCardPoolSO pool, IFactionBuffService factionBuff)
    {
        _pool = pool;
        _factionBuff = factionBuff;
    }

    public void RequestOffer(int faction)
    {
        if (_pool == null)
        {
            Debug.LogWarning("[TalentCardTrigger] Pool is null, cannot offer.");
            return;
        }
        if (_pool.cards == null || _pool.cards.Count < 3)
        {
            Debug.LogWarning($"[TalentCardTrigger] Pool has {_pool.cards?.Count ?? 0} cards (<3), cannot offer.");
            return;
        }

        var cards = TalentCardPoolResolver.DrawRandom(_pool, 3);
        if (cards.Count == 0)
        {
            Debug.LogWarning("[TalentCardTrigger] Drew 0 cards.");
            return;
        }

        Debug.Log($"[TalentCardTrigger] Offering {cards.Count} cards to faction {faction}. Subscribers: {OnOfferRequested?.GetInvocationList()?.Length ?? 0}");
        OnOfferRequested?.Invoke(this, new TalentCardOfferEventArgs
        {
            Faction = faction,
            Cards = cards,
        });
    }

    public void ApplyCard(int faction, TalentCardConfigSO card)
    {
        Debug.Log($"[TalentCardTrigger] ApplyCard: faction={faction}, card={card?.talentName}");
        TalentCardEffectApplier.ApplyToFaction(_factionBuff, faction, card);
    }
}
