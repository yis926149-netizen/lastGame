using UnityEngine;

//****************************************
// 【动态地图-阶段三】Chunk 根节点标记组件（§11 卡牌射线兼容层）。
// 卡牌落点射线不再要求命中对象 == 地图根（CardController.cs:197），
// 改为 GetComponentInParent&lt;MapChunkView&gt;() != null。
//****************************************

public sealed class MapChunkView : MonoBehaviour
{
    public ChunkIndex Index;

    /// <summary>本 Chunk 根下是否持有地形碰撞体（供射线服务快速判断）。</summary>
    public MeshCollider TerrainCollider;
}
