using System.Collections.Generic;
using UnityEngine;

public class TalentCardOfferEventArgs : System.EventArgs
{
    public int Faction;
    public List<TalentCardConfigSO> Cards;
}

public class TalentCardTriggerAdapter
{
    private readonly TalentCardProvider _provider;
    private readonly IFactionBuffService _factionBuff;

    public event System.EventHandler<TalentCardOfferEventArgs> OnOfferRequested;

    public TalentCardTriggerAdapter(TalentCardProvider provider, IFactionBuffService factionBuff)
    {
        _provider = provider;
        _factionBuff = factionBuff;
    }

    public void RequestOffer(int faction)
    {
        if (_provider == null)
        {
            Debug.LogWarning("[TalentCardTrigger] Provider is null, cannot offer.");
            return;
        }

        var cards = _provider.DrawRandom(3);
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
        TalentCardEffectApplier.ApplyToFaction(_factionBuff, faction, card, _provider.GetBalance(card));
    }
}
