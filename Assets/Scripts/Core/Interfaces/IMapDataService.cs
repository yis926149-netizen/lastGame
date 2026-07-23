using System.Collections.Generic;
using UnityEngine;

public interface IMapDataService
{
    // �������ݷ���
    HexCellData GetCell(Vector3 hexCoordinate);
    HexCellData GetCell(int generateOrder);
    bool TryGetCell(Vector3 hexCoordinate, out HexCellData cell);
    bool TryGetCell(int generateOrder, out HexCellData cell);
    List<HexCellData> GetAllCells();

    // ��������ת��
    Vector3 WorldToHexCoordinate(Vector3 worldPosition);
    bool TryWorldToHexCoordinate(Vector3 worldPosition, out Vector3 hexCoordinate);
    HexCellData GetCellByWorldPosition(Vector3 worldPosition);
    List<Vector3> GetAllWorldCoordinates();
    List<Vector3> GetAllHexCoordinates();
    Dictionary<Vector3, HexCellData> GetHexToCell();
    Dictionary<int, HexCellData> GetOrderToCell();
    // �ھӲ�ѯ
    HexCellData GetNeighbor(HexCellData cell, Enums.HexDirection direction);
    List<HexCellData> GetNeighbors(HexCellData cell);

    // ��ͼ���󣨿�ѡ������Ҫ���õ�ͼ����ĵط�ʹ�ã�
    GameObject MapGameObject { get; }

    public Vector3[] GetHexVertices();

    // ��ʼ���������ɵ�ͼ������ɺ���ã�ע�����ݣ�
    void Initialize(
        Dictionary<Vector3, HexCellData> hexToCell,
        Dictionary<int, HexCellData> orderToCell,
        List<Vector3> centerWorldCoordinates,
        Dictionary<Vector3, Vector3> centerWorldToHex,
        GameObject mapGameObject,

        //��ͼ����
        Vector3[] hexVertices,

        //��ͼ����ʱ����
        List<Vector3> verticesList,
        Mesh mesh,
        GameObject gridGameObject

    );

    // ������ʱ�������µ�ͼ����ʱ���ݣ������б���mesh���������
    void UpdateRuntimeData(List<Vector3> verticesList, Mesh mesh, GameObject gridGameObject);
}
