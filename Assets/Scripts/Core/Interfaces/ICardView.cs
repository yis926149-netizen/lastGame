using UnityEngine;

public interface ICardView
{
    /// <summary>
    /// 设置卡牌数据（卡面、ID、初始位置等）
    /// </summary>
    void SetData(CardData data, int placementID, Vector3 originPosition);

    /// <summary>
    /// 播放入场动画（发牌）
    /// </summary>
    /// <param name="isNextCard">true=预告卡（保持NextCardSize，不缩放）</param>
    void PlayDealAnimation(Vector3 targetPosition, System.Action onComplete, bool isNextCard = false);

    /// <summary>
    /// 播放拖拽时的缩放/位移
    /// </summary>
    void OnDragUpdate(Vector2 localPoint, Vector2 originPos);

    /// <summary>
    /// 还原卡片位置和大小（取消放置）
    /// </summary>
    void ResetToOrigin();

    /// <summary>
    /// 隐藏高亮（松手时清理）
    /// </summary>
    void ClearHighlights();

    /// <summary>
    /// 获取卡牌ID
    /// </summary>
    int CardID { get; }

    /// <summary>
    /// 卡牌对应的放置槽位ID
    /// </summary>
    int PlacementID { get; set; }

    /// <summary>
    /// 卡牌是否为“次卡”
    /// </summary>
    bool IsNextCard { get; set; }

    /// <summary>
    /// 卡牌的 RectTransform
    /// </summary>
    RectTransform RectTransform { get; }

    /// <summary>
    /// 卡牌的基准原始位置
    /// </summary>
    Vector3 OriginPosition { get; set; }
}