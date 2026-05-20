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

    //地图生成
    private Vector3[] _hexVertices;

    //地图运行时数据
    private List<Vector3> _verticesList;
    private Mesh _mesh;
    private GameObject _gridGameObject;

    public GameObject MapGameObject => _mapGameObject;

    public void Initialize(
        Dictionary<Vector3, HexCellData> hexToCell,
        Dictionary<int, HexCellData> orderToCell,
        List<Vector3> centerWorldCoordinates,
        Dictionary<Vector3, Vector3> centerWorldToHex,
        GameObject mapGameObject,

        //地图生成
        Vector3[] hexVertices,

        //地图运行时数据
        List<Vector3> verticesList,
        Mesh mesh,
        GameObject gridGameObject


        )
    {
        _hexToCell = hexToCell;
        _orderToCell = orderToCell;
        _centerWorldCoordinates = centerWorldCoordinates;
        _centerWorldToHex = centerWorldToHex;
        _mapGameObject = mapGameObject;

        _hexVertices = hexVertices;

        _verticesList = verticesList;
        _mesh = mesh;
        _gridGameObject = gridGameObject;
    }

    public void UpdateRuntimeData(List<Vector3> verticesList, Mesh mesh, GameObject gridGameObject)
    {
        _verticesList = verticesList;
        _mesh = mesh;
        _gridGameObject = gridGameObject;
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

    public List<HexCellData> GetAllCells() => _hexToCell?.Values.ToList() ?? new List<HexCellData>();

    public Vector3 WorldToHexCoordinate(Vector3 worldPosition)
    {
        if (_centerWorldCoordinates == null || _centerWorldCoordinates.Count == 0)
            return Vector3.zero;

        float minDist = float.MaxValue;
        Vector3 closestCenter = _centerWorldCoordinates[0];
        foreach (var center in _centerWorldCoordinates)
        {
            float dist = Vector3.Distance(center, worldPosition);
            if (dist < minDist)
            {
                minDist = dist;
                closestCenter = center;
            }
        }
        return _centerWorldToHex[closestCenter];
    }

    public HexCellData GetCellByWorldPosition(Vector3 worldPosition)
    {
        var hex = WorldToHexCoordinate(worldPosition);
        return GetCell(hex);
    }

    public List<Vector3> GetAllWorldCoordinates() => _centerWorldCoordinates;
    public List<Vector3> GetAllHexCoordinates() => _hexToCell.Keys.ToList();

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
        var neighbors = new List<HexCellData>(); // 创建列表用于存储结果
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
                neighbors.Add(n); // 将符合条件的元素添加到列表中
            }
        }

        return neighbors; // 返回列表
    }

    public Vector3[] GetHexVertices() => _hexVertices;
}