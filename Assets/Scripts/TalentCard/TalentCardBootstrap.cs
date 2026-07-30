using UnityEngine;
using Zenject;

public class TalentCardBootstrap : IInitializable
{
    private readonly TalentCardTriggerAdapter _trigger;

    public TalentCardBootstrap(TalentCardTriggerAdapter trigger)
    {
        _trigger = trigger;
    }

    public void Initialize()
    {
        PublicBuildingBase.OnPublicBuildingCaptured += OnCaptured;
        Debug.Log("[TalentCardBootstrap] Initialized, triggering game start offer.");
        _trigger.RequestOffer(0);
        _trigger.RequestOffer(1);
    }

    private void OnCaptured(int newOwnerPlayerIndex)
    {
        if (newOwnerPlayerIndex < 0) return;
        Debug.Log($"[TalentCardBootstrap] Building captured by faction {newOwnerPlayerIndex}");
        _trigger.RequestOffer(newOwnerPlayerIndex);
    }
}
