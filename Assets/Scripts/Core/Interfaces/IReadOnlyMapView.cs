using UnityEngine;

//****************************************
//功能说明：只读地图视图——网格几何构建（MeshGenerator）的只读邻居/格子查询入口。
//无状态化（阶段一）：生成器不再把渲染缓存写回 HexCellData，改为通过该视图
//查询邻居数据；视图只暴露读接口，禁止任何写入。
//****************************************

public interface IReadOnlyMapView
{
    HexCellData GetCell(Vector3 hexCoordinate);

    HexCellData GetNeighbor(HexCellData cell, Enums.HexDirection direction);
}

/// <summary>
/// IMapDataService 的只读适配器：把只读查询收敛到 IReadOnlyMapView，
/// 供 MeshGenerator 无状态构建方法使用（阶段一由 MapRenderer 创建）。
/// </summary>
public sealed class MapDataReadOnlyView : IReadOnlyMapView
{
    private readonly IMapDataService _mapDataService;

    public MapDataReadOnlyView(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    public HexCellData GetCell(Vector3 hexCoordinate)
    {
        return _mapDataService.GetCell(hexCoordinate);
    }

    public HexCellData GetNeighbor(HexCellData cell, Enums.HexDirection direction)
    {
        return _mapDataService.GetNeighbor(cell, direction);
    }
}
