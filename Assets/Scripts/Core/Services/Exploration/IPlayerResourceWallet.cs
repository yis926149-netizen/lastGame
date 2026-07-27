/// <summary>
/// 玩家资源钱包接口：探索消耗资源的抽象层。
/// 【探索重构-阶段3】占位接口，资源系统完成后对接正式实现。
/// </summary>
public interface IPlayerResourceWallet
{
    /// <summary>
    /// 尝试消耗指定资源
    /// </summary>
    /// <param name="cost">成本</param>
    /// <returns>是否扣费成功</returns>
    bool TrySpend(ExplorationCost cost);

    /// <summary>
    /// 检查是否有足够资源（不扣费）
    /// </summary>
    /// <param name="cost">成本</param>
    /// <returns>是否足够</returns>
    bool CanAfford(ExplorationCost cost);
}
