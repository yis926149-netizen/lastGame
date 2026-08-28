// CardService.cs
using System.Collections.Generic;
using UnityEngine;
using Zenject;
using GameConfig;

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

    /// <summary>
    /// 将所有 slot 整体右移一位（slot i -> slot i+1）。
    /// slot 0 清空供新卡使用；若末位原有内容则通过 droppedView 返回，由调用方销毁。
    /// </summary>
    void ShiftSlotsRight(out ICardView droppedView);
}

public class CardService : ICardService
{
    // 【Excel 数值化】手牌上限迁移至 CoreGameplayConfigProvider。
    private static int MaxCardsCount => CoreGameplayConfigProvider.HandCardLimit;
    private ICardView[] _slots = new ICardView[MaxCardsCount];
    private bool _hasDrawnThisTurn = false;
    private bool _hasDealtThisTurn = false;     // ?? ????
    private System.Random _random;
    private System.Random Random => _random ??= SeedService.GetRandom("Card");

    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private ICardUnlockRuleProvider _cardUnlockRuleProvider;
    [Inject] private IFactionBuffService _factionBuff;
    [Inject(Optional = true)] private TalentDrawRuleDatabaseSO _drawRules;

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
            faction: 0,
            drawRules: _drawRules);
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

    public void ShiftSlotsRight(out ICardView droppedView)
    {
        // 末位的卡牌将被挤掉
        droppedView = _slots[MaxCardsCount - 1];

        // 从末尾向前逐位右移
        for (int i = MaxCardsCount - 1; i > 0; i--)
            _slots[i] = _slots[i - 1];

        // slot 0 留给新卡
        _slots[0] = null;
    }
}
