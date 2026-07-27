using System.Collections.Generic;
using System.Linq;

//****************************************
//功能说明：AI 卡牌脑。负责抽卡状态、每回合卡牌管线（抽卡 + 科文推进 + 出牌）与出牌落点决策。
//         逻辑与拆分前 AIManager 的卡牌相关方法一致。
//         注：AIIndex 暂固定 1；Tier 3 多阵营化时改为按 aiIndex 参数化。
//****************************************

public class AICardBrain
{
    private const int AIIndex = 1;

    private readonly AIPlayerState _aiPlayerState;
    private readonly ICardUnlockRuleProvider _cardUnlockRuleProvider;
    private readonly IUnitDataProvider _unitDataProvider;
    private readonly IBuildingDataProvider _buildingDataProvider;
    private readonly UnitMovementSystem _movementSystem;
    private readonly EnemyModelManager _enemyModelManager;
    private readonly AIEntityFactory _factory;
    private readonly AIRandomProvider _rng;
    private readonly GoldWallet _goldWallet;

    public AICardBrain(
        AIPlayerState aiPlayerState,
        ICardUnlockRuleProvider cardUnlockRuleProvider,
        IUnitDataProvider unitDataProvider,
        IBuildingDataProvider buildingDataProvider,
        UnitMovementSystem movementSystem,
        EnemyModelManager enemyModelManager,
        AIEntityFactory factory,
        AIRandomProvider rng,
        GoldWallet goldWallet)
    {
        _aiPlayerState = aiPlayerState;
        _cardUnlockRuleProvider = cardUnlockRuleProvider;
        _unitDataProvider = unitDataProvider;
        _buildingDataProvider = buildingDataProvider;
        _movementSystem = movementSystem;
        _enemyModelManager = enemyModelManager;
        _factory = factory;
        _rng = rng;
        _goldWallet = goldWallet;
    }

    private System.Random Random => _rng.Random;

    public void InitializeCardState()
    {
        _aiPlayerState.Card.HandCardIds.Clear();
        _aiPlayerState.Card.NextCardId = -1;
        _aiPlayerState.Card.HasDealtThisTurn = false;
        _aiPlayerState.Card.HasGivenFirstTurnSettler = false;

        // 和玩家一致：第一张保证是移民卡
        for (int i = 0; i < AICardState.MaxHandCards; i++)
        {
            _aiPlayerState.Card.HandCardIds.Add(GenerateCardId());
        }
        _aiPlayerState.Card.NextCardId = GenerateCardId();
    }

    private int GenerateCardId()
    {
        // 科技/文化系统已移除：传 0, 0 无条件解锁全部卡牌。
        return CardGenerationRule.GenerateNextCardId(
            giveFirstSettler: true,
            ref _aiPlayerState.Card.HasGivenFirstTurnSettler,
            0,
            0,
            _cardUnlockRuleProvider,
            Random);
    }

    /// <summary>每回合卡牌管线：抽卡（一次）→ 科文推进 → 出牌。</summary>
    public void RunCardPipeline()
    {
        // 与玩家类似，每回合只把“次卡”推进手牌一次
        _aiPlayerState.Card.HasDealtThisTurn = false;
        DealFromNextCardIfPossible();
        PlayAICards();
    }

    private void DealFromNextCardIfPossible()
    {
        if (_aiPlayerState.Card.NextCardId < 0)
        {
            _aiPlayerState.Card.NextCardId = GenerateCardId();
        }

        if (_aiPlayerState.Card.HasDealtThisTurn) return;
        if (_aiPlayerState.Card.HandCardIds.Count >= AICardState.MaxHandCards) return;
        if (_aiPlayerState.Card.NextCardId < 0) return;

        _aiPlayerState.Card.HandCardIds.Add(_aiPlayerState.Card.NextCardId);
        _aiPlayerState.Card.HasDealtThisTurn = true;
        _aiPlayerState.Card.NextCardId = GenerateCardId();
    }

    private void PlayAICards()
    {
        if (_aiPlayerState.Card.HandCardIds.Count == 0) return;

        List<int> orderedCards = _aiPlayerState.Card.HandCardIds
            .OrderByDescending(GetCardPriority)
            .ToList();
        List<int> playedCards = new List<int>();

        foreach (int cardId in orderedCards)
        {
            if (TryPlaySingleCard(cardId))
            {
                playedCards.Add(cardId);
            }
        }

        foreach (int cardId in playedCards)
        {
            _aiPlayerState.Card.HandCardIds.Remove(cardId);
        }
    }

    private int GetCardPriority(int cardId)
    {
        int unitCount = (int)_unitDataProvider.GetUnitIconCount();
        bool isUnitCard = cardId < unitCount;
        if (isUnitCard)
        {
            return cardId == 0 ? 100 : 70;
        }

        int buildingId = cardId - unitCount;
        if (buildingId == 3) return 90; // 科技文化建筑优先
        return 60;
    }

    private bool TryPlaySingleCard(int cardId)
    {
        // 【探索重构-阶段7】出牌消耗金币
        if (_goldWallet.GetGold(AIIndex) < _goldWallet.CardCost) return false;

        int unitCount = (int)_unitDataProvider.GetUnitIconCount();
        bool isBuildingCard = cardId >= unitCount;
        bool success = isBuildingCard
            ? TrySpawnBuildingFromCard(cardId)
            : TrySpawnUnitFromCard(cardId);

        if (success)
            _goldWallet.TrySpendGold(AIIndex, _goldWallet.CardCost);
        return success;
    }

    private bool TrySpawnUnitFromCard(int unitId)
    {
        List<HexCellData> candidateCells = GetAIOwnedCells()
            .Where(IsValidSpawnCellForUnit)
            .ToList();
        if (candidateCells.Count == 0) return false;

        HexCellData selected = candidateCells[Random.Next(candidateCells.Count)];
        _factory.GenerateUnit(unitId, selected.RealCenterWorldCoordinate);
        return true;
    }

    private bool TrySpawnBuildingFromCard(int cardId)
    {
        int unitCount = (int)_unitDataProvider.GetUnitIconCount();
        int buildingId = cardId - unitCount;
        if (buildingId < 0 || buildingId >= _buildingDataProvider.GetBuildingCardsCount())
        {
            return false;
        }

        List<HexCellData> candidateCells = GetAIOwnedCells()
            .Where(IsValidSpawnCellForBuilding)
            .ToList();
        if (candidateCells.Count == 0) return false;

        // 科文建筑优先放在已有城市圈地内
        HexCellData selected = candidateCells[Random.Next(candidateCells.Count)];
        _factory.GenerateBuilding(cardId, selected.RealCenterWorldCoordinate);
        return true;
    }

    private bool IsValidSpawnCellForUnit(HexCellData cell)
    {
        if (cell == null) return false;
        if (_movementSystem.IsDestinationReserved(cell.HexCoordinate)) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.IsHaveUnit()) return false;
        return cell.Player_City_Index.Key == AIIndex;
    }

    private bool IsValidSpawnCellForBuilding(HexCellData cell)
    {
        if (cell == null) return false;
        if (_movementSystem.IsDestinationReserved(cell.HexCoordinate)) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        return cell.Player_City_Index.Key == AIIndex;
    }

    private List<HexCellData> GetAIOwnedCells()
    {
        if (!_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.ContainsKey(AIIndex))
        {
            return new List<HexCellData>();
        }
        return _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[AIIndex]
            .Values
            .Where(c => c != null)
            .ToList();
    }
}
