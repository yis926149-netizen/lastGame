using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 势力范围服务实现：基于领地集合的新模型。
/// 【探索重构-阶段5.5】替代旧的"城市圈"模型。
/// 势力范围 = 主城固有范围（初始化时圈入）+ 探索占领 + 公共建筑占领
/// </summary>
public class TerritoryService : ITerritoryService
{
    private readonly IMapDataService _mapDataService;
    private readonly PlayerModelManager _playerModelManager;
    private readonly MapVisualEventSO _mapVisualEvent;

    // 玩家主城 KeyValuePair（0,0），用于 Player_City_Index 赋值
    private static readonly KeyValuePair<int, int> PlayerOwner = new KeyValuePair<int, int>(0, 0);

    public TerritoryService(
        IMapDataService mapDataService,
        PlayerModelManager playerModelManager,
        MapVisualEventSO mapVisualEvent)
    {
        _mapDataService = mapDataService;
        _playerModelManager = playerModelManager;
        _mapVisualEvent = mapVisualEvent;
    }

    /// <summary>
    /// 将地块圈入玩家势力范围。
    /// 同时同步到 PlayerModelManager.SphereOfInfluence_HexC_HexCellData（兼容 SphereOfInfluenceRenderer）。
    /// </summary>
    public void Claim(HexCellData cell)
    {
        if (cell == null) return;
        var coord = cell.HexCoordinate;

        // 更新归属标记
        cell.Player_City_Index = PlayerOwner;

        // 同步到渲染器使用的数据字典
        _playerModelManager.SphereOfInfluence_HexC_HexCellData[coord] = cell;
    }

    /// <summary>
    /// 检查地块是否在玩家势力范围内。
    /// </summary>
    public bool IsInPlayerTerritory(HexCellData cell)
    {
        if (cell == null) return false;
        // 主城标记：PlayerIndex == 0
        return cell.Player_City_Index.Key == 0;
    }
}
