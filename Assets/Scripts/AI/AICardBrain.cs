using System.Collections.Generic;
using System.Linq;

//****************************************
//功能说明：AI 卡牌脑。负责抽卡状态、每回合卡牌管线（抽卡 + 科文推进 + 出牌）与出牌落点决策。
//         逻辑与拆分前 AIManager 的卡牌相关方法一致（对象化：手牌/出牌直接持有普通卡配置）。
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
    private readonly ILogisticsService _logisticsService;

    public AICardBrain(
        AIPlayerState aiPlayerState,
        ICardUnlockRuleProvider cardUnlockRuleProvider,
        IUnitDataProvider unitDataProvider,
        IBuildingDataProvider buildingDataProvider,
        UnitMovementSystem movementSystem,
        EnemyModelManager enemyModelManager,
        AIEntityFactory factory,
        AIRandomProvider rng,
        GoldWallet goldWallet,
        ILogisticsService logisticsService)
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
        _logisticsService = logisticsService;
    }

    private System.Random Random => _rng.Random;

    public void InitializeCardState()
    {
        _aiPlayerState.Card.HandCards.Clear();
        _aiPlayerState.Card.NextCard = null;
        _aiPlayerState.Card.HasDealtThisTurn = false;
        _aiPlayerState.Card.HasGivenFirstTurnSettler = false;

        // 和玩家一致：第一张保证是移民卡
        for (int i = 0; i < AICardState.MaxHandCards; i++)
        {
            _aiPlayerState.Card.HandCards.Add(GenerateCard());
        }
        _aiPlayerState.Card.NextCard = GenerateCard();
    }

    private NormalCardConfigSO GenerateCard()
    {
        return CardGenerationRule.GenerateNextCard(
            giveFirstSettler: true,
            ref _aiPlayerState.Card.HasGivenFirstTurnSettler,
            _cardUnlockRuleProvider,
            Random);
    }

    /// <summary>每回合卡牌管线：抽卡（一次）→ 科文推进 → 出牌。</summary>
    public bool RunCardPipeline()
    {
        // 与玩家类似，每回合只把“次卡”推进手牌一次
        _aiPlayerState.Card.HasDealtThisTurn = false;
        DealFromNextCardIfPossible();
        return PlayAICards();
    }

    private void DealFromNextCardIfPossible()
    {
        if (_aiPlayerState.Card.NextCard == null)
        {
            _aiPlayerState.Card.NextCard = GenerateCard();
        }

        if (_aiPlayerState.Card.HasDealtThisTurn) return;
        if (_aiPlayerState.Card.HandCards.Count >= AICardState.MaxHandCards) return;
        if (_aiPlayerState.Card.NextCard == null) return;

        _aiPlayerState.Card.HandCards.Add(_aiPlayerState.Card.NextCard);
        _aiPlayerState.Card.HasDealtThisTurn = true;
        _aiPlayerState.Card.NextCard = GenerateCard();
    }

    private bool PlayAICards()
    {
        if (_aiPlayerState.Card.HandCards.Count == 0) return false;

        List<NormalCardConfigSO> orderedCards = _aiPlayerState.Card.HandCards
            .OrderByDescending(GetCardPriority)
            .ToList();

        foreach (NormalCardConfigSO card in orderedCards)
        {
            if (TryPlaySingleCard(card))
            {
                _aiPlayerState.Card.HandCards.Remove(card);
                return true;
            }
        }

        return false;
    }

    private int GetCardPriority(NormalCardConfigSO card)
    {
        if (card is UnitConfigSO unitConfig)
        {
            return unitConfig.strategyType == UnitStrategyType.Settler ? 100 : 70;
        }

        if (card is BuildingConfigSO buildingConfig)
        {
            return buildingConfig.buildingType == Enums.BulidingType.TechnologyAndCultural ? 90 : 60;
        }

        return 60;
    }

    private bool TryPlaySingleCard(NormalCardConfigSO card)
    {
        // 【探索重构-阶段7】出牌消耗金币
        if (_goldWallet.GetGold(AIIndex) < _goldWallet.CardCost) return false;

        bool success = card is BuildingConfigSO
            ? TrySpawnBuildingFromCard((BuildingConfigSO)card)
            : TrySpawnUnitFromCard((UnitConfigSO)card);

        if (success)
            _goldWallet.TrySpendGold(AIIndex, _goldWallet.CardCost);
        return success;
    }

    private bool TrySpawnUnitFromCard(UnitConfigSO unitConfig)
    {
        List<HexCellData> candidateCells = GetAIOwnedCells()
            .Where(IsValidSpawnCellForUnit)
            .ToList();
        if (candidateCells.Count == 0) return false;

        HexCellData selected = candidateCells[Random.Next(candidateCells.Count)];
        _factory.GenerateUnit(unitConfig.Id, selected.RealCenterWorldCoordinate);
        return true;
    }

    private bool TrySpawnBuildingFromCard(BuildingConfigSO buildingConfig)
    {
        List<HexCellData> candidateCells = GetAIOwnedCells()
            .Where(IsValidSpawnCellForBuilding)
            .ToList();
        if (candidateCells.Count == 0) return false;

        // 科文建筑优先放在已有城市圈地内
        HexCellData selected = candidateCells[Random.Next(candidateCells.Count)];
        _factory.GenerateBuilding(buildingConfig, selected.RealCenterWorldCoordinate);
        return true;
    }

    private bool IsValidSpawnCellForUnit(HexCellData cell)
    {
        if (cell == null) return false;
        if (_movementSystem.IsDestinationReserved(cell.HexCoordinate)) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.IsHaveUnit()) return false;
        return cell.Player_City_Index.Key == AIIndex &&
               (_logisticsService == null || _logisticsService.IsLogisticsConnected(cell, AIIndex));
    }

    private bool IsValidSpawnCellForBuilding(HexCellData cell)
    {
        if (cell == null) return false;
        if (_movementSystem.IsDestinationReserved(cell.HexCoordinate)) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.IsHaveUnit()) return false;
        return cell.Player_City_Index.Key == AIIndex &&
               (_logisticsService == null || _logisticsService.IsLogisticsConnected(cell, AIIndex));
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
