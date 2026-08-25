using UnityEngine;

//****************************************
// 【动态地图-阶段三】IMapRaycastService 默认实现（§11）。
// 统一识别 Chunk 后端的 MapChunkView 后代。
//****************************************

public class MapRaycastService : IMapRaycastService
{
    private readonly IMapDataService _mapDataService;
    private RaycastHit[] _raycastHits = new RaycastHit[16];

    public MapRaycastService(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    public bool RaycastMap(Vector2 screenPos, out RaycastHit hit, float maxDistance = 100f)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            hit = default;
            return false;
        }

        return RaycastMap(camera.ScreenPointToRay(screenPos), out hit, maxDistance);
    }

    public bool RaycastMap(Vector2 screenPos, Vector3 cameraPosition, out RaycastHit hit, float maxDistance = 100f)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            hit = default;
            return false;
        }

        Ray screenRay = camera.ScreenPointToRay(screenPos);
        return RaycastMap(new Ray(cameraPosition, screenRay.direction), out hit, maxDistance);
    }

    private bool RaycastMap(Ray ray, out RaycastHit hit, float maxDistance)
    {
        int hitCount;
        while ((hitCount = Physics.RaycastNonAlloc(ray, _raycastHits, maxDistance, LayerMask.GetMask("Map")))
               == _raycastHits.Length)
        {
            _raycastHits = new RaycastHit[_raycastHits.Length * 2];
        }

        float nearestDistance = float.PositiveInfinity;
        hit = default;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = _raycastHits[i];
            if (candidate.distance >= nearestDistance || !IsMapObject(candidate.transform.gameObject))
                continue;

            nearestDistance = candidate.distance;
            hit = candidate;
        }

        return nearestDistance < float.PositiveInfinity;
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
