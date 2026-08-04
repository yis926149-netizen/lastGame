using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class EnemyModelManager : MonoBehaviour
{
    [Inject] private IMapDataService _mapDataService;

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

    // 【公共建筑系统-决策#30】记录哪些 PlayerIndex 属于公共建筑伪AI（无行为逻辑）
    // 用于血条颜色、视野跳过等判断
    [HideInInspector]
    public HashSet<int> PublicBuildingPlayerIndexes = new HashSet<int>();

    /// <summary>判断指定 PlayerIndex 是否为公共建筑伪AI（全局查询入口）。</summary>
    public bool IsPublicBuilding(int playerIndex) => PublicBuildingPlayerIndexes.Contains(playerIndex);

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

        var newSingleCity = new Dictionary<KeyValuePair<int, int>, Dictionary<Vector3, HexCellData>>();

        var cells = _mapDataService.GetAllCells();
        if (cells != null)
        {
            foreach (var cell in cells)
            {
                if (cell == null || cell.Player_City_Index.Key != aiIndex) continue;

                totalSphere[cell.HexCoordinate] = cell;

                // 【断供方案-阶段1/§4.3】公共建筑不伪装为城市条目：占位格不进单城字典
                //（避免与 AI 主城 (1,0) 键冲突；总领地字典仍包含）
                if (cell.publicBuildingRoot != null) continue;

                var cityKey = cell.Player_City_Index;
                if (!newSingleCity.TryGetValue(cityKey, out var cityDict))
                {
                    cityDict = new Dictionary<Vector3, HexCellData>();
                    newSingleCity[cityKey] = cityDict;
                }
                cityDict[cell.HexCoordinate] = cell;
            }
        }

        var keysToRemove = new List<KeyValuePair<int, int>>();
        foreach (var kv in Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData)
            if (kv.Key.Key == aiIndex)
                keysToRemove.Add(kv.Key);
        foreach (var key in keysToRemove)
            Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.Remove(key);

        foreach (var kv in newSingleCity)
            Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[kv.Key] = kv.Value;

        if (!CityCount.ContainsKey(aiIndex))
            CityCount[aiIndex] = 0;
        CityCount[aiIndex] = newSingleCity.Count;
    }
}
