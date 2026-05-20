// CardService.cs
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public interface ICardService
{
    int GetFirstEmptySlot();
    int GenerateNextCardID();
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
    private readonly System.Random _random = new System.Random();

    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private ITechCultureService _techCulture;
    [Inject] private IGameStateMachine _gameState;
    [Inject] private ICardUnlockRuleProvider _cardUnlockRuleProvider;

    private bool _hasGivenFirstTurnSettler = false;

    public int GetFirstEmptySlot()
    {
        for (int i = 0; i < MaxCardsCount; i++)
            if (_slots[i] == null) return i;
        return -1;
    }

    public int GenerateNextCardID()
    {
        // 开局阶段（引擎里 CurrentTurn 从 1 计，首次发牌等价「回合0」）：第一张固定移民
        if (_gameState != null &&
            _gameState.CurrentTurn == 1 &&
            !_hasGivenFirstTurnSettler)
        {
            _hasGivenFirstTurnSettler = true;
            return 0;
        }

        // 原逻辑：按科技/文化从解锁池均匀随机（暂且关闭，需要时恢复）
        // int techLevel = _techCulture?.TechLevel ?? 0;
        // int cultureLevel = _techCulture?.CultureLevel ?? 0;
        // List<int> unlockedIDs = _cardUnlockRuleProvider.GetUnlockedCardIds(techLevel, cultureLevel);
        //
        // if (unlockedIDs.Count == 0) return 0;
        //
        // int randomIndex = _random.Next(unlockedIDs.Count);
        // return unlockedIDs[randomIndex];

        // 暂且：全卡牌随机 [0, 15)
        return _random.Next(0, 15);
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
        return new Vector2((slot + 1) * 150, 0);
    }

    public bool CanDrawThisTurn() => !_hasDrawnThisTurn;
    public void ResetDrawOpportunity() => _hasDrawnThisTurn = false;
    public void MarkDrawThisTurn() => _hasDrawnThisTurn = true;
    public bool CanDealThisTurn() => !_hasDealtThisTurn;
    public void ResetDealOpportunity() => _hasDealtThisTurn = false;
    public void MarkDealtThisTurn() => _hasDealtThisTurn = true;
}