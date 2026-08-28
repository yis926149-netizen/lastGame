using UnityEngine;
using Zenject;

//****************************************
// 可拾取型公共建筑（公共单位）
// 特点：只占一格，生成即激活，无迷雾扩展
// 触发时机：所在格被翻（ExplorationService 探索成功）时，触发效果并销毁
// 实现：订阅 IExplorationBroadcastSource.Broadcast，匹配根格后调用 OnCaptured
// 效果：将 _rewardCard 插入触发方手牌第一位
//****************************************

public class PublicBuilding_Collectible : PublicBuildingBase
{
    /// <summary>拾取后放入手牌的卡牌配置，在 Inspector 中拖入对应 Asset。</summary>
    [SerializeField] private NormalCardConfigSO _rewardCard;

    [Inject] private IExplorationBroadcastSource _broadcastSource;
    // 玩家手牌（faction 0）
    [Inject] private CardPresenter _cardPresenter;
    // AI 手牌状态（faction 1）
    [Inject] private AIPlayerState _aiPlayerState;

    /// <summary>生成即激活，不隐藏在迷雾中。</summary>
    public override bool StartsHidden => false;

    private void Start()
    {
        if (_broadcastSource != null)
            _broadcastSource.Broadcast += OnHexExplored;
    }

    public override void TickDiscovery()
    {
        // 生成即激活，不参与发现流程
    }

    /// <summary>
    /// 根格被探索时由广播回调驱动。
    /// 不走易主逻辑，直接触发效果并销毁。
    /// </summary>
    public override void OnCaptured(int newOwnerPlayerIndex, bool triggerRecalculate = true)
    {
        TriggerEffect(newOwnerPlayerIndex);
        Destroy(gameObject);
    }

    private void OnHexExplored(ExplorationAcquisition acquisition)
    {
        if (acquisition == null) return;
        // 只响应首次探索阶段，避免 Settled/RewardPoint 重复触发
        if (acquisition.Phase != ExplorationBroadcastPhase.Explored) return;
        if (acquisition.Cell != RootHex) return;

        OnCaptured(acquisition.FactionId);
    }

    /// <summary>将 _rewardCard 插入触发方手牌第一位。</summary>
    private void TriggerEffect(int triggeringPlayerIndex)
    {
        if (_rewardCard == null)
        {
            Debug.LogWarning("[PublicBuilding_Collectible] _rewardCard 未配置，跳过效果触发");
            return;
        }

        const int playerFaction = 0;
        if (triggeringPlayerIndex == playerFaction)
        {
            // 玩家阵营：通过 CardPresenter 插入带 UI 的手牌
            _cardPresenter.InsertCardAtFront(_rewardCard);
        }
        else
        {
            // AI 或其他阵营：直接操作数据层，无 UI
            var handCards = _aiPlayerState.Card.HandCards;
            if (handCards.Count >= AICardState.MaxHandCards)
                handCards.RemoveAt(handCards.Count - 1);  // 挤掉末位
            handCards.Insert(0, _rewardCard);
        }

        Debug.Log($"[PublicBuilding_Collectible] PlayerIndex={triggeringPlayerIndex} 获取了 {_rewardCard.name}");
    }

    protected override void OnDestroy()
    {
        if (_broadcastSource != null)
            _broadcastSource.Broadcast -= OnHexExplored;

        base.OnDestroy();
    }
}
