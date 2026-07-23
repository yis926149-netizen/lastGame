using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;
using Zenject;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class PlayerModelManager : MonoBehaviour
{
    //注入
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IMeshGenerator _meshGenerator;

    //一个城市的势力范围
    [HideInInspector]
    public Dictionary<int, Dictionary<Vector3, HexCellData>> SingleCity_SphereOfInfluence_HexC_HexCellData = new Dictionary<int, Dictionary<Vector3, HexCellData>>();
    //城市数量
    [HideInInspector]
    public int CityCount = 0;
    private int _nextCityIndex;

    //科文建筑列表
    [HideInInspector]
    public Dictionary<int, GameObject> Index_TechnologyAndCulturalBuilding = new Dictionary<int, GameObject>();
    [HideInInspector]
    public int TechnologyAndCulturalBuildingIndex = 0;

    //进攻建筑列表
    [HideInInspector]
    public Dictionary<int, GameObject> Index_AttackBuilding = new Dictionary<int, GameObject>();
    [HideInInspector]
    public int AttackBuildingIndex = 0;

    //防御建筑列表
    [HideInInspector]
    public Dictionary<int, GameObject> Index_DefenseBuilding = new Dictionary<int, GameObject>();
    [HideInInspector]
    public int DefenseBuildingIndex = 0;

    //回血建筑列表
    [HideInInspector]
    public Dictionary<int, GameObject> Index_AltarBuilding = new Dictionary<int, GameObject>();
    [HideInInspector]
    public int AltarBuildingIndex = 0;

    //总势力范围
    //地块的六边形坐标_地块的HexCell_字典
    [HideInInspector]
    public Dictionary<Vector3, HexCellData> SphereOfInfluence_HexC_HexCellData = new Dictionary<Vector3, HexCellData>();

    public int AllocateCityIndex()
    {
        return _nextCityIndex++;
    }

    public void RebuildSphereOfInfluence()
    {
        SphereOfInfluence_HexC_HexCellData.Clear();
        foreach (var cityEntry in SingleCity_SphereOfInfluence_HexC_HexCellData)
        {
            var cityKey = new KeyValuePair<int, int>(0, cityEntry.Key);
            foreach (var cellEntry in cityEntry.Value)
            {
                HexCellData cell = cellEntry.Value;
                if (cell == null) continue;

                SphereOfInfluence_HexC_HexCellData[cellEntry.Key] = cell;
                cell.Player_City_Index = cityKey;
            }
        }
    }


    /// <summary>
    /// 添加势力范围
    /// </summary>
    /// <param name="HexC">市中心或建筑的六边形坐标</param>
    public void ExpandTheSphereOfInfluence(Vector3 HexC, Dictionary<Vector3, HexCellData> d, KeyValuePair<int, int> player_city_index)
    {
        SphereOfInfluenceRules.Expand(_mapDataService, HexC, d, player_city_index);
    }

    /* 旧方法，现改为事件。保留以备后续可能需要
    /// <summary>
    /// 势力范围边缘线Mesh创建
    /// </summary>
    /// <param name="hexCells">势力范围</param>
    public void SphereOfInfluenceMeshCreat(List<HexCellData> hexCells, Color c, string gameObjectName )
    {
        Material sphereOfInfluenceMat = null;
        GameObject lastLine = GameObject.Find(gameObjectName);
        if (lastLine) { Destroy(lastLine); }
        //承载网格组件的物体
        GameObject Line = new GameObject(gameObjectName);
        //顶点数组
        int edgeCount = 0;
        List<Vector3> vertices = new List<Vector3>();
        List<List<Vector3>> verticeList = _meshGenerator.GetOneSphereOfInfluenceVertices(hexCells,out edgeCount, _mapDataService);
        foreach (List<Vector3> v in verticeList)
        {
            vertices.AddRange(v);
        }
        //UV 
        List<Vector2> uv = new List<Vector2>();
        //绘制顺序
        List<int> drawOrder = new List<int>();

        //for (int i = 0; i < edgeCount; i++)
        for (int i = 0; i < vertices.Count/4; i++)
        {
            List<int> ints = _meshGenerator.GetOneSphereOfInfluenceDrawOrder();
            for (int j = 0; j < ints.Count; j++)
            {
                ints[j] += i * 4;
            }
            drawOrder.AddRange(ints);
            uv.AddRange(_meshGenerator.GetOneSphereOfInfluenceUV());
        }

        //查找 shader - 实例化材质（避免影响原 shader）
        Shader influenceShader = Shader.Find("Custom/SphereOfInfluence");
        if (sphereOfInfluenceMat == null)
        {
            sphereOfInfluenceMat = new Material(influenceShader); // 实例化材质
        }


        //Debug.Log("vertices.Count：" + vertices.Count);
        //Debug.Log("uv.Count：" + uv.Count);
        //Debug.Log("drawOrder.Count：" + drawOrder.Count);
        //Debug.Log("vertices.Count：" + vertices.Count);

        sphereOfInfluenceMat.SetColor("_Color", c);
        MapController.CreatMesh(vertices.ToArray(), uv.ToArray(), drawOrder.ToArray(), Line, sphereOfInfluenceMat);
    }
    */
}

public static class SphereOfInfluenceRules
{
    public static void Expand(
        IMapDataService mapDataService,
        Vector3 centerCoordinate,
        Dictionary<Vector3, HexCellData> sphere,
        KeyValuePair<int, int> owner)
    {
        if (mapDataService == null || sphere == null) return;

        HexCellData center = mapDataService.GetCell(centerCoordinate);
        if (center == null) return;

        sphere[center.HexCoordinate] = center;
        center.Player_City_Index = owner;

        for (int i = 0; i < 6; i++)
        {
            HexCellData neighbor = mapDataService.GetNeighbor(center, (Enums.HexDirection)i);
            if (neighbor == null || sphere.ContainsKey(neighbor.HexCoordinate)) continue;
            if (neighbor.Player_City_Index.Key != -1 && neighbor.Player_City_Index.Key != owner.Key) continue;

            sphere[neighbor.HexCoordinate] = neighbor;
            neighbor.Player_City_Index = owner;
        }
    }
}
