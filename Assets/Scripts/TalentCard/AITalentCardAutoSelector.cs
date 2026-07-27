using UnityEngine;

public class AITalentCardAutoSelector
{
    private readonly TalentCardTriggerAdapter _trigger;

    public AITalentCardAutoSelector(TalentCardTriggerAdapter trigger)
    {
        _trigger = trigger;
        _trigger.OnOfferRequested += HandleOffer;
    }

    private void HandleOffer(object sender, TalentCardOfferEventArgs args)
    {
        if (args.Faction < 1) return;

        var cards = args.Cards;
        if (cards == null || cards.Count == 0) return;

        var picked = cards[Random.Range(0, cards.Count)];
        _trigger.ApplyCard(args.Faction, picked);

        Debug.Log($"[TalentCard] AI (faction {args.Faction}) auto-picked: {picked.talentName}");
    }
}
