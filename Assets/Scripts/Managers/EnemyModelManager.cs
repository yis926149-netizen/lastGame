using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class EnemyModelManager : MonoBehaviour
{
    //总势力范围
    //人机编号_总势力范围(地块的六边形坐标_地块的HexCell)
    [HideInInspector]
    public Dictionary<int, Dictionary<Vector3, HexCellData>> Enemy_SphereOfInfluence_HexC_HexCellData = new Dictionary<int, Dictionary<Vector3, HexCellData>>();

    //某位人机一个城市的势力范围 - 【<人机编号，人机城市编号>, 势力范围】
    [HideInInspector]
    public Dictionary<KeyValuePair<int,int>, Dictionary<Vector3, HexCellData>> Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData = new Dictionary<KeyValuePair<int, int>, Dictionary<Vector3, HexCellData>>();
    //城市数量
    [HideInInspector]
    public Dictionary<int, int> CityCount = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _nextCityIndex = new Dictionary<int, int>();

    //人机编号_势力范围颜色
    private Dictionary<int, Color> sphereOfInfluenceColor = new Dictionary<int, Color>();

    public void Awake()
    {
        //添加人机
        Enemy_SphereOfInfluence_HexC_HexCellData.Add(1, new Dictionary<Vector3, HexCellData>());
        CityCount.Add(1, 0);
    }

    public int AllocateCityIndex(int aiIndex)
    {
        if (!_nextCityIndex.TryGetValue(aiIndex, out int nextIndex))
        {
            nextIndex = 0;
        }

        _nextCityIndex[aiIndex] = nextIndex + 1;
        return nextIndex;
    }

    public void RebuildSphereOfInfluence(int aiIndex)
    {
        if (!Enemy_SphereOfInfluence_HexC_HexCellData.TryGetValue(aiIndex, out var totalSphere))
        {
            totalSphere = new Dictionary<Vector3, HexCellData>();
            Enemy_SphereOfInfluence_HexC_HexCellData[aiIndex] = totalSphere;
        }
        totalSphere.Clear();

        foreach (var cityEntry in Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData)
        {
            if (cityEntry.Key.Key != aiIndex) continue;

            foreach (var cellEntry in cityEntry.Value)
            {
                HexCellData cell = cellEntry.Value;
                if (cell == null) continue;

                totalSphere[cellEntry.Key] = cell;
                cell.Player_City_Index = cityEntry.Key;
            }
        }
    }
}
