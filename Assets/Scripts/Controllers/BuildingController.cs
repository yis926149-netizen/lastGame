using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class BuildingController : MonoBehaviour
{
    //注入
    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private AudioManager _audioManager;
    [Inject] private EnemyModelManager _enemyModelManager;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private ITechCultureService _techCultureService;

    //对应的数据类
    [HideInInspector]
    public BuildingData buildingData;

    //建筑血条
    [HideInInspector]
    public Slider uiHealthBar;

    //建筑类型
    public Enums.BulidingType bulidingType;
    
    //建筑所属
    public KeyValuePair<int,int> Player_City_Index = new KeyValuePair<int,int>();

    //攻击该建筑的单位
    public GameObject Attacker;

    //城市易主
    public bool isCityChangeOwner = false;
    private bool _isDestroyed;

    void Start()
    {

    }


    void Update()
    {
        if (!_isDestroyed && !isCityChangeOwner && uiHealthBar != null && uiHealthBar.value <= 0)
        {            
            if(bulidingType == Enums.BulidingType.City)
            {
                isCityChangeOwner = true;
                CityDestroyed();
            }
            else
            {
                BuildingDestroyed();
            }
          
        }
    }

    //建筑被攻击
    public void BuildingAttacked(GameObject enemyAttacker)
    {
        if (buildingData == null || enemyAttacker == null)
        {
            Debug.LogWarning("[BuildingController] BuildingAttacked skipped: missing buildingData or attacker.");
            return;
        }

        var attackerController = enemyAttacker.GetComponent<UnitMovementController>();
        if (attackerController == null || attackerController.characterData == null)
        {
            Debug.LogWarning("[BuildingController] BuildingAttacked skipped: attacker unit data is missing.");
            return;
        }

        //受击数据处理
        //血量计算
        buildingData.currentHp -= AttackDataComputation(attackerController.characterData, buildingData);

        // 兜底：某些运行时创建路径可能未正确缓存血条
        if (uiHealthBar == null)
        {
            uiHealthBar = GetComponentInChildren<Slider>();
        }
        if (uiHealthBar != null)
        {
            uiHealthBar.value = buildingData.currentHp / buildingData.hp;
        }

        Attacker = enemyAttacker;
    }

    //攻击数据计算
    public float AttackDataComputation(CharacterData attacker, BuildingData theAttacked)
    {
        //被攻击者的所在地块

        HexCellData h = _mapDataService.GetCellByWorldPosition(transform.position);
        //攻击者所在地块
        
        HexCellData attackerHex = _mapDataService.GetCellByWorldPosition(attacker.model.transform.position);

        //建筑：加防御力建筑：一环内城市无敌
        float HaveDefenseBuilding = 1;
        for (int i = 0; i < 6; i++)
        {
            HexCellData neighborHex = _mapDataService.GetNeighbor(h, (Enums.HexDirection)i);
            if (neighborHex != null && neighborHex.BulidingTypeOnHex_Building.Key == Enums.BulidingType.DefenseStatue && bulidingType == Enums.BulidingType.City)
            {
                HaveDefenseBuilding = 0;
            }
        }

        //建筑：加攻击力建筑：一环内加攻击力(可叠加)
        float AttackStatueGain = 0;
        for (int i = 0; i < 6; i++)
        {
            HexCellData neighborHex = _mapDataService.GetNeighbor(attackerHex, (Enums.HexDirection)i);
            if (neighborHex != null && neighborHex.BulidingTypeOnHex_Building.Key == Enums.BulidingType.AttackStatue)
            {
                AttackStatueGain += 0.7f;
            }
        }


        //剩余血量 = 血量  - Mathf.Max(0,(攻击力 * 攻击增益 - 防御力*防御增益))      
        float AttackPower = attacker.currentAttackValue;
        float AttackGain = 1 + attacker.Resource_Animals + AttackStatueGain;       

        /*
        Debug.Log("AttackPower：" + AttackPower);
        Debug.Log("AttackGain：" + AttackGain);
        Debug.Log("Defense：" + Defense);
        Debug.Log("DefenseGain：" + DefenseGain);
        Debug.Log($"伤害数值：{AttackPower * AttackGain - Defense * DefenseGain}");
        */

        //因为资源增益是一次性效果，所以用完要清空
        if (AttackGain > 1)
        {
            attacker.Resource_Animals = 0;
        }

        return Mathf.Max(0, AttackPower * AttackGain * HaveDefenseBuilding);
    }

    //市中心死亡
    public void CityDestroyed()
    {
        if (Attacker == null || !Attacker.TryGetComponent<UnitMovementController>(out var attackerController))
        {
            Debug.LogWarning("[BuildingController] CityDestroyed aborted: attacker is missing.");
            isCityChangeOwner = false;
            return;
        }

        //建筑死亡音效
        _audioManager.PlaySFX("Launcher2");

        // 1) 从原属主势力范围中移除该城市
        List<HexCellData> hexCellDatas = RemoveCityFromCurrentOwner();
        // 1.1) 清除该城市势力范围中的附属建筑（保留被接管的城市本体）
        DestroyNonCityBuildingsOnHexes(hexCellDatas);

        // 2) 按攻击者阵营接管该城市及地块
        if (attackerController.PlayerIndex == 0)
        {
            CaptureCityToPlayer(hexCellDatas);
            SetCityVisual("PlayerBuilding", "PlayerBuilding", Color.green);
        }
        else
        {
            CaptureCityToEnemy(attackerController.PlayerIndex, hexCellDatas);
            SetCityVisual("EnemyBuilding", "EnemyBuilding", Color.red);
        }

        //城市血量回满
        if (buildingData != null)
        {
            buildingData.currentHp = buildingData.hp;
        }
        if (uiHealthBar != null)
        {
            uiHealthBar.value = 1;
        }

        // 允许后续再次被接管（否则下一次空血不会再触发 CityDestroyed）
        isCityChangeOwner = false;

    }

    private List<HexCellData> RemoveCityFromCurrentOwner()
    {
        List<HexCellData> cityHexes = new List<HexCellData>();

        // 玩家城市
        if (Player_City_Index.Key == 0)
        {
            int cityIndex = Player_City_Index.Value;
            if (_playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData.TryGetValue(cityIndex, out var citySphere))
            {
                cityHexes = citySphere.Values.Where(v => v != null).ToList();
                _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData.Remove(cityIndex);
            }
            ClearRemovedCityOwnership(cityHexes, Player_City_Index);
            _playerModelManager.RebuildSphereOfInfluence();
            cityHexes = cityHexes.Where(hex => hex.Player_City_Index.Key != 0).ToList();

            if (_playerModelManager.CityCount > 0)
            {
                _playerModelManager.CityCount -= 1;
            }
        }
        // AI城市
        else
        {
            int aiIndex = Player_City_Index.Key;
            var cityKey = Player_City_Index;

            if (_enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.TryGetValue(cityKey, out var citySphere))
            {
                cityHexes = citySphere.Values.Where(v => v != null).ToList();
                _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.Remove(cityKey);
            }

            ClearRemovedCityOwnership(cityHexes, cityKey);
            _enemyModelManager.RebuildSphereOfInfluence(aiIndex);
            cityHexes = cityHexes.Where(hex => hex.Player_City_Index.Key != aiIndex).ToList();

            if (_enemyModelManager.CityCount.ContainsKey(aiIndex) && _enemyModelManager.CityCount[aiIndex] > 0)
            {
                _enemyModelManager.CityCount[aiIndex] -= 1;
            }
        }

        // 兜底：至少保证包含当前城市所在地块
        if (cityHexes.Count == 0)
        {
            HexCellData currentHex = _mapDataService.GetCellByWorldPosition(transform.position);
            if (currentHex != null)
            {
                cityHexes.Add(currentHex);
            }
        }

        _mapVisualEvent.Raise();
        return cityHexes;
    }

    private static void ClearRemovedCityOwnership(
        IEnumerable<HexCellData> cityHexes,
        KeyValuePair<int, int> removedCity)
    {
        foreach (HexCellData hex in cityHexes)
        {
            if (hex != null && hex.Player_City_Index.Equals(removedCity))
            {
                hex.Player_City_Index = new KeyValuePair<int, int>(-1, -1);
            }
        }
    }

    private void DestroyNonCityBuildingsOnHexes(List<HexCellData> cityHexes)
    {
        if (cityHexes == null) return;

        foreach (var hex in cityHexes)
        {
            if (hex == null) continue;

            var buildingOnHex = hex.BulidingTypeOnHex_Building;
            if (buildingOnHex.Key == Enums.BulidingType.NoBuilding || buildingOnHex.Value == null) continue;

            GameObject buildingObj = buildingOnHex.Value;
            if (buildingObj == gameObject) continue; // 保留当前被接管的城市本体
            if (buildingOnHex.Key == Enums.BulidingType.City) continue; // 保险：不在这里处理城市

            // 清空地块建筑占用
            hex.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(Enums.BulidingType.NoBuilding, null);
            hex.movementCost = 1;

            // 清理玩家建筑索引中的失效引用（敌方目前无独立建筑索引）
            RemoveFromPlayerBuildingIndexes(buildingObj);

            Destroy(buildingObj);
        }
    }

    private void RemoveFromPlayerBuildingIndexes(GameObject target)
    {
        if (target == null) return;

        RemoveEntriesByValue(_playerModelManager.Index_AttackBuilding, target);
        RemoveEntriesByValue(_playerModelManager.Index_DefenseBuilding, target);
        RemoveEntriesByValue(_playerModelManager.Index_AltarBuilding, target);
        if (RemoveEntriesByValue(_playerModelManager.Index_TechnologyAndCulturalBuilding, target))
        {
            _techCultureService.AddTechPointsPerTurn(-10);
            _techCultureService.AddCulturePointsPerTurn(-10);
        }
    }

    private bool RemoveEntriesByValue(Dictionary<int, GameObject> dict, GameObject target)
    {
        if (dict == null || target == null) return false;

        List<int> keysToRemove = new List<int>();
        foreach (var kv in dict)
        {
            if (kv.Value == target)
            {
                keysToRemove.Add(kv.Key);
            }
        }

        foreach (int key in keysToRemove)
        {
            dict.Remove(key);
        }

        return keysToRemove.Count > 0;
    }

    private void CaptureCityToPlayer(List<HexCellData> cityHexes)
    {
        int newCityIndex = _playerModelManager.AllocateCityIndex();
        if (!_playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData.ContainsKey(newCityIndex))
        {
            _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData[newCityIndex] = new Dictionary<Vector3, HexCellData>();
        }

        foreach (var hex in cityHexes)
        {
            if (hex == null) continue;

            _playerModelManager.SphereOfInfluence_HexC_HexCellData[hex.HexCoordinate] = hex;
            _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData[newCityIndex][hex.HexCoordinate] = hex;
            hex.Player_City_Index = new KeyValuePair<int, int>(0, newCityIndex);
        }

        Player_City_Index = new KeyValuePair<int, int>(0, newCityIndex);
        _playerModelManager.CityCount += 1;
        _mapVisualEvent.Raise();
    }

    private void CaptureCityToEnemy(int aiIndex, List<HexCellData> cityHexes)
    {
        if (!_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.ContainsKey(aiIndex))
        {
            _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[aiIndex] = new Dictionary<Vector3, HexCellData>();
        }

        if (!_enemyModelManager.CityCount.ContainsKey(aiIndex))
        {
            _enemyModelManager.CityCount[aiIndex] = 0;
        }

        int newCityIndex = _enemyModelManager.AllocateCityIndex(aiIndex);
        var cityKey = new KeyValuePair<int, int>(aiIndex, newCityIndex);
        if (!_enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.ContainsKey(cityKey))
        {
            _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[cityKey] = new Dictionary<Vector3, HexCellData>();
        }

        foreach (var hex in cityHexes)
        {
            if (hex == null) continue;

            _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[aiIndex][hex.HexCoordinate] = hex;
            _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[cityKey][hex.HexCoordinate] = hex;
            hex.Player_City_Index = cityKey;
        }

        Player_City_Index = cityKey;
        _enemyModelManager.CityCount[aiIndex] += 1;
        _mapVisualEvent.Raise();
    }

    private void SetCityVisual(string newTag, string parentName, Color healthColor)
    {
        tag = newTag;

        Transform parent = GameObject.Find(parentName)?.transform;
        if (parent != null)
        {
            transform.SetParent(parent, true);
        }

        UITool.TrySetSliderFillColor(uiHealthBar, healthColor);
    }

    //建筑死亡
    public void BuildingDestroyed()
    {
        if (_isDestroyed) return;
        _isDestroyed = true;

        //建筑死亡音效
        _audioManager.PlaySFX("Launcher2");

        //剔除 
       
        HexCellData h = _mapDataService.GetCellByWorldPosition(transform.position);
        if (h != null && h.BulidingTypeOnHex_Building.Value == gameObject)
        {
            h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(Enums.BulidingType.NoBuilding, null);
            h.movementCost = 1;
        }

        RemoveFromPlayerBuildingIndexes(gameObject);
        _mapVisualEvent.Raise();

        //删除该建筑
        Destroy(gameObject);      
    }
}
