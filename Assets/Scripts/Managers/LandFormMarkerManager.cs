using System.Collections.Generic;
using UnityEngine;
using Zenject;

/// <summary>
/// 【金矿提示图标】为配置了 markerPrefab 的地貌（当前仅金矿）生成提示浮标，每连通簇（堆）一个。
/// 复用公共建筑浮标视图 PublicBuildingMarkerView：呼吸动画 + 始终面向相机。
/// 仅做视觉提示，不参与单位寻路（不注册进 PublicBuildingMarkerManager）。
/// 玩家或 AI 探索到簇内任意地块后自动移除；已探索的堆开局不生成浮标。
/// </summary>
public class LandFormMarkerManager : ITickable
{
    private sealed class ClusterEntry
    {
        public MapLandFormSO Form;
        public readonly List<HexCellData> Cells = new List<HexCellData>();
        public GameObject View;
    }

    private readonly IMapDataService _mapDataService;
    private readonly List<ClusterEntry> _clusters = new List<ClusterEntry>();

    private float _removeCheckTimer;
    private const float RemoveCheckInterval = 0.25f;

    public LandFormMarkerManager(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    /// <summary>
    /// 地图数据生成完毕后调用（GameFlowManager.Initialize）：
    /// 为所有配置了 markerPrefab 的地貌格按连通簇分组，每堆创建一枚浮标。
    /// </summary>
    public void CreateAllMarkers()
    {
        ClearAllMarkers();

        foreach (ClusterEntry cluster in BuildClusters())
        {
            if (cluster.Cells.Count == 0) continue;

            // 簇内已有任一地块被探索（玩家/AI）的堆不再提示
            if (HasExploredCell(cluster)) continue;

            Vector3 centroid = Vector3.zero;
            foreach (HexCellData cell in cluster.Cells)
                centroid += cell.RealCenterWorldCoordinate;
            centroid /= cluster.Cells.Count;
            centroid += Vector3.up * 5f;

            GameObject view = Object.Instantiate(cluster.Form.markerPrefab, centroid, Quaternion.identity);
            var markerView = view.GetComponent<PublicBuildingMarkerView>();
            if (markerView != null)
                markerView.SetIcon(cluster.Form.markerIcon);

            cluster.View = view;
            _clusters.Add(cluster);
        }

        Debug.Log($"[LandFormMarkerManager] 生成 {_clusters.Count} 枚地貌提示浮标。");
    }

    public void Tick()
    {
        if (_clusters.Count == 0) return;

        _removeCheckTimer += Time.deltaTime;
        if (_removeCheckTimer < RemoveCheckInterval) return;
        _removeCheckTimer = 0f;

        for (int i = _clusters.Count - 1; i >= 0; i--)
        {
            if (HasExploredCell(_clusters[i]))
            {
                RemoveClusterAt(i);
            }
        }
    }

    private void ClearAllMarkers()
    {
        foreach (ClusterEntry cluster in _clusters)
        {
            if (cluster.View != null)
                Object.Destroy(cluster.View);
        }
        _clusters.Clear();
    }

    private void RemoveClusterAt(int index)
    {
        ClusterEntry cluster = _clusters[index];
        if (cluster.View != null)
            Object.Destroy(cluster.View);
        _clusters.RemoveAt(index);
    }

    private static bool HasExploredCell(ClusterEntry cluster)
    {
        foreach (HexCellData cell in cluster.Cells)
        {
            if (cell.IsExploredBy(0) || cell.IsExploredBy(1))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 按"地貌 → 连通簇"分组所有配置了 markerPrefab 的地貌格。
    /// 金矿为簇生成地貌（堆间有 clusterMinSpacing 隔离），BFS 连通性天然对应每堆；
    /// 散落地貌（若未来开启）每个孤立格自成一组。
    /// </summary>
    private List<ClusterEntry> BuildClusters()
    {
        var result = new List<ClusterEntry>();
        var visited = new HashSet<HexCellData>();

        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell == null || cell.landForm == null || cell.landForm.markerPrefab == null) continue;
            if (!visited.Add(cell)) continue;

            ClusterEntry cluster = new ClusterEntry { Form = cell.landForm };
            var frontier = new Queue<HexCellData>();
            frontier.Enqueue(cell);

            while (frontier.Count > 0)
            {
                HexCellData current = frontier.Dequeue();
                cluster.Cells.Add(current);

                foreach (HexCellData neighbor in _mapDataService.GetNeighbors(current))
                {
                    if (neighbor == null || neighbor.landForm != current.landForm) continue;
                    if (!visited.Add(neighbor)) continue;
                    frontier.Enqueue(neighbor);
                }
            }

            result.Add(cluster);
        }

        return result;
    }
}
