using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

public class HexMapService : IMapDataService
{
    private Dictionary<Vector3, HexCellData> _hexToCell;
    private Dictionary<int, HexCellData> _orderToCell;
    private List<Vector3> _centerWorldCoordinates;
    private Dictionary<Vector3, Vector3> _centerWorldToHex;
    private GameObject _mapGameObject;
    private float? _cachedCellRadius;

    // ---------- 世界坐标 → 格心 的空间哈希索引 ----------
    // 【卡顿分析·第八节】原实现每次反查都线性扫描全部格心（约 600 个），且全代码库约 40 处调用、
    // 部分在每帧路径上。改为按 xz 平面分桶：桶边长 = 相邻格心间距 d，而接受阈值是外接圆半径
    // cellRadius = d/√3 < d，所以「最近且距离 ≤ cellRadius 的格心」必定落在查询点所在桶的 3×3 邻域内，
    // 与线性扫描结果完全等价（仅在恰好等距的退化情形下可能选中另一个同距格心）。
    private Dictionary<long, List<Vector3>> _centerSpatialGrid;
    private float _spatialCellSize;

    //��ͼ����
    private Vector3[] _hexVertices;

    public GameObject MapGameObject => _mapGameObject;

    private List<HexCellData> _cachedAllCells;
    private List<Vector3> _cachedAllHexCoords;

    public void Initialize(
        Dictionary<Vector3, HexCellData> hexToCell,
        Dictionary<int, HexCellData> orderToCell,
        List<Vector3> centerWorldCoordinates,
        Dictionary<Vector3, Vector3> centerWorldToHex,
        GameObject mapGameObject,

        //��ͼ����
        Vector3[] hexVertices
        )
    {
        _hexToCell = hexToCell;
        _orderToCell = orderToCell;
        _centerWorldCoordinates = centerWorldCoordinates;
        _centerWorldToHex = centerWorldToHex;
        _mapGameObject = mapGameObject;

        _hexVertices = hexVertices;

        // 重新生成地图时必须失效旧缓存：调用方（寻路等）现在直接持有这两个列表的引用，
        // 不再逐次拷贝，残留旧表会让新地图沿用上一张图的坐标集。
        _cachedAllCells = null;
        _cachedAllHexCoords = null;

        _cachedCellRadius = ComputeCellRadius();
        BuildCenterSpatialGrid();
    }

    /// <summary>
    /// 构建格心空间哈希。桶边长取相邻格心间距 d = cellRadius * √3，
    /// 保证任何距查询点 ≤ cellRadius 的格心一定落在查询点所在桶的 3×3 邻域内。
    /// </summary>
    private void BuildCenterSpatialGrid()
    {
        _centerSpatialGrid = null;
        _spatialCellSize = 0f;

        if (_centerWorldCoordinates == null || _centerWorldCoordinates.Count == 0) return;

        float cellRadius = _cachedCellRadius ?? 0f;
        // 单格地图（cellRadius == 0）没有可用的桶尺寸，保持索引为空，反查退回线性路径。
        if (cellRadius <= 0f) return;

        _spatialCellSize = cellRadius * Mathf.Sqrt(3f);
        _centerSpatialGrid = new Dictionary<long, List<Vector3>>(_centerWorldCoordinates.Count);

        foreach (var center in _centerWorldCoordinates)
        {
            long key = BucketKey(
                Mathf.FloorToInt(center.x / _spatialCellSize),
                Mathf.FloorToInt(center.z / _spatialCellSize));

            if (!_centerSpatialGrid.TryGetValue(key, out var bucket))
            {
                bucket = new List<Vector3>(2);
                _centerSpatialGrid[key] = bucket;
            }
            bucket.Add(center);
        }
    }

    private static long BucketKey(int bx, int bz)
    {
        return ((long)bx << 32) ^ (uint)bz;
    }

    public HexCellData GetCell(Vector3 hexCoordinate)
    {
        if (_hexToCell == null) return null;
        _hexToCell.TryGetValue(hexCoordinate, out var cell);
        return cell;
    }

    public HexCellData GetCell(int generateOrder)
    {
        if (_orderToCell == null) return null;
        _orderToCell.TryGetValue(generateOrder, out var cell);
        return cell;
    }

    public bool TryGetCell(Vector3 hexCoordinate, out HexCellData cell)
    {
        cell = null;
        return _hexToCell != null && _hexToCell.TryGetValue(hexCoordinate, out cell);
    }

    public bool TryGetCell(int generateOrder, out HexCellData cell)
    {
        cell = null;
        return _orderToCell != null && _orderToCell.TryGetValue(generateOrder, out cell);
    }

    public List<HexCellData> GetAllCells()
    {
        if (_cachedAllCells == null && _hexToCell != null)
            _cachedAllCells = new List<HexCellData>(_hexToCell.Values);
        return _cachedAllCells ?? new List<HexCellData>();
    }

    public Vector3 WorldToHexCoordinate(Vector3 worldPosition)
    {
        return TryWorldToHexCoordinate(worldPosition, out Vector3 hexCoordinate)
            ? hexCoordinate
            : Vector3.zero;
    }

    public bool TryWorldToHexCoordinate(Vector3 worldPosition, out Vector3 hexCoordinate)
    {
        hexCoordinate = default;
        if (_centerWorldCoordinates == null || _centerWorldCoordinates.Count == 0 || _centerWorldToHex == null)
            return false;

        float cellRadius = GetCellRadius();

        float minDist;
        Vector3 closestCenter;

        if (_centerSpatialGrid != null && _spatialCellSize > 0f)
        {
            // O(1)：只检查查询点所在桶的 3×3 邻域。桶边长 > 接受阈值 cellRadius，
            // 因此阈值内的格心不可能落在邻域之外。
            minDist = float.MaxValue;
            closestCenter = _centerWorldCoordinates[0];

            int bx = Mathf.FloorToInt(worldPosition.x / _spatialCellSize);
            int bz = Mathf.FloorToInt(worldPosition.z / _spatialCellSize);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_centerSpatialGrid.TryGetValue(BucketKey(bx + dx, bz + dz), out var bucket)) continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        Vector3 center = bucket[i];
                        float ddx = center.x - worldPosition.x;
                        float ddz = center.z - worldPosition.z;
                        float dist = ddx * ddx + ddz * ddz;
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestCenter = center;
                        }
                    }
                }
            }
        }
        else
        {
            // 兜底：单格地图等无法建索引的情形，退回线性扫描。
            minDist = float.MaxValue;
            closestCenter = _centerWorldCoordinates[0];
            foreach (var center in _centerWorldCoordinates)
            {
                float ddx = center.x - worldPosition.x;
                float ddz = center.z - worldPosition.z;
                float dist = ddx * ddx + ddz * ddz;
                if (dist < minDist)
                {
                    minDist = dist;
                    closestCenter = center;
                }
            }
        }

        if (minDist > cellRadius * cellRadius || !_centerWorldToHex.TryGetValue(closestCenter, out hexCoordinate))
            return false;

        return true;
    }

    public HexCellData GetCellByWorldPosition(Vector3 worldPosition)
    {
        return TryWorldToHexCoordinate(worldPosition, out Vector3 hex) ? GetCell(hex) : null;
    }

    private float GetCellRadius()
    {
        if (_cachedCellRadius.HasValue) return _cachedCellRadius.Value;
        _cachedCellRadius = ComputeCellRadius();
        return _cachedCellRadius.Value;
    }

    private float ComputeCellRadius()
    {
        if (_centerWorldCoordinates.Count == 1) return 0f;

        Vector3 first = _centerWorldCoordinates[0];
        float nearestSqrDistance = float.MaxValue;
        for (int i = 1; i < _centerWorldCoordinates.Count; i++)
        {
            Vector3 center = _centerWorldCoordinates[i];
            float sqrDistance = (new Vector2(first.x, first.z) - new Vector2(center.x, center.z)).sqrMagnitude;
            if (sqrDistance > 0f && sqrDistance < nearestSqrDistance) nearestSqrDistance = sqrDistance;
        }

        return nearestSqrDistance < float.MaxValue ? Mathf.Sqrt(nearestSqrDistance / 3f) : 0f;
    }

    public List<Vector3> GetAllWorldCoordinates() => _centerWorldCoordinates;
    public List<Vector3> GetAllHexCoordinates()
    {
        if (_cachedAllHexCoords == null && _hexToCell != null)
            _cachedAllHexCoords = new List<Vector3>(_hexToCell.Keys);
        return _cachedAllHexCoords ?? new List<Vector3>();
    }

    public Dictionary<Vector3, HexCellData> GetHexToCell() => _hexToCell;

    public Dictionary<int, HexCellData> GetOrderToCell() => _orderToCell;

    public HexCellData GetNeighbor(HexCellData cell, Enums.HexDirection direction)
    {
        if (cell == null) return null;

        Vector3 neighborHex;
        switch (direction)
        {
            case Enums.HexDirection.NE: neighborHex = cell.HexCoordinate + new Vector3(0, -1, 1); break;
            case Enums.HexDirection.E: neighborHex = cell.HexCoordinate + new Vector3(1, -1, 0); break;
            case Enums.HexDirection.SE: neighborHex = cell.HexCoordinate + new Vector3(1, 0, -1); break;
            case Enums.HexDirection.SW: neighborHex = cell.HexCoordinate + new Vector3(0, 1, -1); break;
            case Enums.HexDirection.W: neighborHex = cell.HexCoordinate + new Vector3(-1, 1, 0); break;
            case Enums.HexDirection.NW: neighborHex = cell.HexCoordinate + new Vector3(-1, 0, 1); break;
            default: return null;
        }
        return GetCell(neighborHex);
    }

    public List<HexCellData> GetNeighbors(HexCellData cell)
    {
        var neighbors = new List<HexCellData>(); // �����б����ڴ洢���
        var dirs = new Enums.HexDirection[]
        {
        Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE,
        Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW
        };

        foreach (var dir in dirs)
        {
            var n = GetNeighbor(cell, dir);
            if (n != null)
            {
                neighbors.Add(n); // ������������Ԫ�����ӵ��б���
            }
        }

        return neighbors; // �����б�
    }

    public Vector3[] GetHexVertices() => _hexVertices;
}
