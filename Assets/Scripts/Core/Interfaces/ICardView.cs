using UnityEngine;

public interface ICardDropHandler
{
    bool HandleCardDragEnd(ICardView view, HexCellData targetCell, Vector3 releaseWorldPos);

    /// <summary>拖拽开始时通知（叠放战术牌在此"借出"一张并创建幽灵）。</summary>
    void OnCardDragBegin(ICardView view);

    /// <summary>拖拽被取消/失败且未走 HandleCardDragEnd 时通知（归还借出）。</summary>
    void OnCardDragCancel(ICardView view);

    /// <summary>查询该卡牌能否部署到指定格（放置预览高亮与确认路径共用同一规则）。</summary>
    bool CanDeployTo(CardData data, HexCellData cell);
}

/// <summary>
/// 拖拽视觉通道（卡牌拖拽模型预览特效 §5.2）。
/// 与 ICardDropHandler 分离：放置资格/部署是业务，本接口只承载逐帧视觉状态，
/// 不实现本接口的 drop handler（如战术卡）自动不产生模型预览。
/// </summary>
public interface ICardDragVisualHandler
{
    /// <summary>拖拽逐帧更新：upwardDistance 为向上位移，进度定义见实施计划 §3。</summary>
    void OnCardDragUpdate(ICardView view, Vector2 screenPos, float upwardDistance, float cardProgress, float modelProgress);

    /// <summary>
    /// 拖拽成功结束（有效落点）通知。
    /// 成功路径不会调用 OnCardDragCancel，且卡牌随后被失活销毁，
    /// 因此必须有独立的成功清理入口，不能依赖 OnDisable（§7）。
    /// </summary>
    void OnCardDragEnd(ICardView view);
}

public interface ICardView
{
    /// <summary>
    /// ���ÿ������ݣ����桢ID����ʼλ�õȣ�
    /// </summary>
    void SetData(CardData data, int placementID, Vector3 originPosition);

    /// <summary>
    /// �����볡���������ƣ�
    /// </summary>
    /// <param name="isNextCard">true=Ԥ�濨������NextCardSize�������ţ�</param>
    void PlayDealAnimation(Vector3 targetPosition, System.Action onComplete, bool isNextCard = false);

    /// <summary>
    /// ������קʱ������/λ��
    /// </summary>
    void OnDragUpdate(Vector2 localPoint, Vector2 originPos);

    /// <summary>
    /// ��ԭ��Ƭλ�úʹ�С��ȡ�����ã�
    /// </summary>
    void ResetToOrigin();

    /// <summary>
    /// ���ظ���������ʱ������
    /// </summary>
    void ClearHighlights();

    /// <summary>
    /// ��ȡ����ID
    /// </summary>
    int CardID { get; }

    /// <summary>
    /// 获取完整卡牌数据（普通卡持有 NormalCardConfig，战术卡 NormalCardConfig 为空）。
    /// </summary>
    CardData Data { get; }

    /// <summary>
    /// ���ƶ�Ӧ�ķ��ò�λID
    /// </summary>
    int PlacementID { get; set; }

    /// <summary>
    /// �����Ƿ�Ϊ���ο���
    /// </summary>
    bool IsNextCard { get; set; }

    /// <summary>
    /// ���Ƶ� RectTransform
    /// </summary>
    RectTransform RectTransform { get; }

    /// <summary>
    /// ���ƵĻ�׼ԭʼλ��
    /// </summary>
    Vector3 OriginPosition { get; set; }

    /// <summary>
    /// 设置拖拽代理（幽灵）：非空时拖拽移动/缩放作用于代理而不是本体。
    /// </summary>
    void SetDragProxy(RectTransform proxy);
}