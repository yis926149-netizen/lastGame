using System.Collections.Generic;
using UnityEngine;

public interface IMapDataService
{
    // 基础数据访问
    HexCellData GetCell(Vector3 hexCoordinate);
    HexCellData GetCell(int generateOrder);
    bool TryGetCell(Vector3 hexCoordinate, out HexCellData cell);
    bool TryGetCell(int generateOrder, out HexCellData cell);
    List<HexCellData> GetAllCells();

    // 世界坐标转换
    Vector3 WorldToHexCoordinate(Vector3 worldPosition);
    HexCellData GetCellByWorldPosition(Vector3 worldPosition);
    List<Vector3> GetAllWorldCoordinates();
    List<Vector3> GetAllHexCoordinates();
    Dictionary<Vector3, HexCellData> GetHexToCell();
    Dictionary<int, HexCellData> GetOrderToCell();
    // 邻居查询
    HexCellData GetNeighbor(HexCellData cell, Enums.HexDirection direction);
    List<HexCellData> GetNeighbors(HexCellData cell);

    // 地图对象（可选，供需要引用地图物体的地方使用）
    GameObject MapGameObject { get; }

    public Vector3[] GetHexVertices();

    // 初始化方法，由地图生成完成后调用（注入数据）
    void Initialize(
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

    );

    // 在运行时后续更新地图运行时数据（顶点列表、mesh、网格对象）
    void UpdateRuntimeData(List<Vector3> verticesList, Mesh mesh, GameObject gridGameObject);
}