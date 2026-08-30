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

        var cards = DrawOfferCards();
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

    /// <summary>
    /// 按与 RequestOffer 相同的规则重新抽一组候选卡，直接返回而不广播事件。
    /// 供选卡界面的「刷新」按钮就地替换当前候选使用。
    /// </summary>
    public List<TalentCardConfigSO> DrawOfferCards()
    {
        if (_provider == null)
        {
            Debug.LogWarning("[TalentCardTrigger] Provider is null, cannot draw.");
            return new List<TalentCardConfigSO>();
        }
        return _provider.DrawRandom(CoreGameplayConfigProvider.TalentOfferCount);
    }

    public void ApplyCard(int faction, TalentCardConfigSO card)
    {
        Debug.Log($"[TalentCardTrigger] ApplyCard: faction={faction}, card={card?.talentName}");
        TalentCardEffectApplier.ApplyToFaction(_factionBuff, faction, card, _provider.GetBalance(card));
    }
}
