// CardService.cs
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public interface ICardService
{
    int GetFirstEmptySlot();
    NormalCardConfigSO GenerateNextCard();
    void RegisterCardView(int slot, ICardView view);
    void RemoveCard(int slot);
    Vector2 GetSlotOffset(int slot);
    bool CanDrawThisTurn();
    void ResetDrawOpportunity();
    void MarkDrawThisTurn();
    bool CanDealThisTurn();           // ?????????????
    void ResetDealOpportunity();
    void MarkDealtThisTurn();
}

public class CardService : ICardService
{
    private const int MaxCardsCount = 5;
    private ICardView[] _slots = new ICardView[MaxCardsCount];
    private bool _hasDrawnThisTurn = false;
    private bool _hasDealtThisTurn = false;     // ?? ????
    private System.Random _random;
    private System.Random Random => _random ??= SeedService.GetRandom("Card");

    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private ICardUnlockRuleProvider _cardUnlockRuleProvider;
    [Inject] private IFactionBuffService _factionBuff;

    private bool _hasGivenFirstTurnSettler = false;

    public int GetFirstEmptySlot()
    {
        for (int i = 0; i < MaxCardsCount; i++)
            if (_slots[i] == null) return i;
        return -1;
    }

    public NormalCardConfigSO GenerateNextCard()
    {
        // 【检查点 6】实时化：首张移民保底不再依赖回合数，用 _hasGivenFirstTurnSettler 确保仅一次
        bool giveFirstSettler = !_hasGivenFirstTurnSettler;
        return CardGenerationRule.GenerateNextCard(
            giveFirstSettler,
            ref _hasGivenFirstTurnSettler,
            _cardUnlockRuleProvider,
            Random,
            _factionBuff,
            faction: 0);
    }

    public void Reset()
    {
        _hasDrawnThisTurn = false;
        _hasGivenFirstTurnSettler = false;
        _hasDealtThisTurn = false;          // ?? ????
    }

    public void RegisterCardView(int slot, ICardView view)
    {
        _slots[slot] = view;
        _hasDrawnThisTurn = true;
    }

    public void RemoveCard(int slot)
    {
        _slots[slot] = null;
    }

    public Vector2 GetSlotOffset(int slot)
    {
        float offsetFromNextCard = _uiConfig.NextCardSlotGap
                                 + (slot + 1) * _uiConfig.CardSlotSpacing;
        return new Vector2(offsetFromNextCard, 0);
    }

    public bool CanDrawThisTurn() => !_hasDrawnThisTurn;
    public void ResetDrawOpportunity() => _hasDrawnThisTurn = false;
    public void MarkDrawThisTurn() => _hasDrawnThisTurn = true;
    public bool CanDealThisTurn() => !_hasDealtThisTurn;
    public void ResetDealOpportunity() => _hasDealtThisTurn = false;
    public void MarkDealtThisTurn() => _hasDealtThisTurn = true;
}
