using UnityEngine;

//****************************************
// 【动态地图-阶段三】IMapRaycastService 默认实现（§11）。
// 统一识别 Chunk 后端的 MapChunkView 后代。
//****************************************

public class MapRaycastService : IMapRaycastService
{
    private readonly IMapDataService _mapDataService;

    public MapRaycastService(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    public bool RaycastMap(Vector2 screenPos, out RaycastHit hit, float maxDistance = 100f)
    {
        Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(screenPos) : default;
        if (ray.direction == default)
        {
            hit = default;
            return false;
        }

        if (Physics.Raycast(ray, out hit, maxDistance, LayerMask.GetMask("Map")))
            return IsMapObject(hit.transform.gameObject);

        return false;
    }

    public bool IsMapObject(GameObject go)
    {
        if (go == null) return false;
        if (go == _mapDataService.MapGameObject) return true;
        return go.GetComponentInParent<MapChunkView>() != null;
    }

    public HexCellData GetCellByWorldPosition(Vector3 worldPos)
    {
        return _mapDataService.GetCellByWorldPosition(worldPos);
    }
}
