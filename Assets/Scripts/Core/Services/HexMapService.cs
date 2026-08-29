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

        float minDist = float.MaxValue;
        Vector3 closestCenter = _centerWorldCoordinates[0];
        foreach (var center in _centerWorldCoordinates)
        {
            float dist = (new Vector2(center.x, center.z) - new Vector2(worldPosition.x, worldPosition.z)).sqrMagnitude;
            if (dist < minDist)
            {
                minDist = dist;
                closestCenter = center;
            }
        }

        float cellRadius = GetCellRadius();
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
