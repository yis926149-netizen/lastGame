using UnityEngine;

//****************************************
// 【动态地图-阶段三】统一地图射线服务（§11 修订：卡牌/拖拽高亮等入口收敛）。
// Chunk 后端下地形 Collider 挂在 Chunk 子对象，整图后端挂在地图根；
// 本服务统一处理"命中对象 == 地图根 || GetComponentInParent&lt;MapChunkView&gt;"，
// 落点一律经 GetCellByWorldPosition(hit.point) 反查。
//****************************************

public interface IMapRaycastService
{
    /// <summary>按屏幕坐标射线命中地图地形（Map Layer），返回世界命中点。</summary>
    bool RaycastMap(Vector2 screenPos, out RaycastHit hit, float maxDistance = 100f);

    /// <summary>以指定相机位置按屏幕坐标射线命中地图地形。</summary>
    bool RaycastMap(Vector2 screenPos, Vector3 cameraPosition, out RaycastHit hit, float maxDistance = 100f);

    /// <summary>判定命中对象属于地图地形（地图根 或 MapChunkView 后代）。</summary>
    bool IsMapObject(GameObject go);

    /// <summary>按世界坐标反查地块。</summary>
    HexCellData GetCellByWorldPosition(Vector3 worldPos);
}
