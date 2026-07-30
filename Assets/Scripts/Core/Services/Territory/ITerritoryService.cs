/// <summary>
/// 势力范围服务接口：管理玩家的领地归属。
/// 【探索重构-阶段5.5】新增服务，替代旧的"城市圈"模型。
/// 新模型：势力范围 = 主城固有范围 + 探索占领的地块 + 公共建筑占领的地块
/// </summary>
public interface ITerritoryService
{
    /// <summary>
    /// 将地块圈入玩家势力范围（探索占领/公共建筑占领时调用）
    /// </summary>
    /// <param name="cell">目标地块</param>
    void Claim(HexCellData cell);

    /// <summary>
    /// 检查地块是否在玩家势力范围内（部署合法性检查时调用）
    /// </summary>
    /// <param name="cell">目标地块</param>
    /// <returns>是否在势力范围内</returns>
    bool IsInPlayerTerritory(HexCellData cell);
}

public interface ILogisticsService
{
    event System.Action LogisticsChanged;

    int Version { get; }

    void RegisterMainCity(int factionId, HexCellData rootCell);
    void SetOwner(HexCellData cell, int factionId);
    void TransferOwner(HexCellData cell, int factionId);
    void ClearOwner(HexCellData cell);
    void RecalculateAll();

    bool IsOwnedBy(HexCellData cell, int factionId);
    bool IsLogisticsConnected(HexCellData cell, int factionId);
    bool IsExploredByFaction(HexCellData cell, int factionId);
    bool IsVisibleToFaction(HexCellData cell, int viewerFactionId);
}
