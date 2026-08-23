using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
//创建人：易生
//功能说明：普通建筑控制器（城市、雕像、祭坛等）
// 【公共建筑系统-决策#17】继承 BuildingBase，保留现有逻辑，城市易主/普通建筑销毁不动
//****************************************

public class BuildingController : BuildingBase
{
    [Inject] private GameLoop _gameLoop;
    [Inject] private EndGame _endGame;
    //城市易主锁（防止重复触发）
    public bool isCityChangeOwner = false;

    void Update()
    {
        if (_gameLoop != null && _gameLoop.IsPaused) return;

        if (CheckDeath() && !isCityChangeOwner)
        {
            if (bulidingType == Enums.BulidingType.City &&
                _endGame != null &&
                _endGame.TryEndFromMainCity(this))
            {
                return;
            }
            OnDeath();
        }
    }

    // ── 实现基类死亡抽象方法 ──────────────────────────
    public override void OnDeath()
    {
        if (bulidingType == Enums.BulidingType.City)
        {
            isCityChangeOwner = true;
            CityDestroyed();
        }
        else
        {
            BuildingDestroyed();
        }
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
        RemoveEntriesByValue(_playerModelManager.Index_TechnologyAndCulturalBuilding, target);
        RemoveEntriesByValue(_playerModelManager.Index_BarracksBuilding, target);
        RemoveEntriesByValue(_playerModelManager.Index_ArrowTowerBuilding, target);
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

    // 【断供方案-阶段3】建筑易主视觉切换：tag/父节点/血条颜色（占领与吞并共用）
    public void ApplyTransferVisual(int newFaction)
    {
        if (newFaction == 0)
            SetCityVisual("PlayerBuilding", "PlayerBuilding", Color.green);
        else
            SetCityVisual("EnemyBuilding", "EnemyBuilding", Color.red);
    }

    // 【断供方案-阶段3】从玩家按类型索引字典中移除该建筑（易主/销毁共用）
    public void RemoveFromPlayerIndexes()
    {
        RemoveFromPlayerBuildingIndexes(gameObject);
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

        TryCaptureAfterBuildingDestroyed(h);

        //删除该建筑
        Destroy(gameObject);      
    }

    private void TryCaptureAfterBuildingDestroyed(HexCellData cell)
    {
        if (_logisticsService == null || cell == null) return;

        int cellOwner = cell.Player_City_Index.Key;
        // 【断供方案-阶段3/决策10】占领只对阵营 0/1 有效；中立与公共建筑伪阵营（Key>=2）豁免
        if (cellOwner < 0 || cellOwner >= 2) return;

        if (!cell.IsHaveUnit()) return;
        GameObject unit = cell.GetUnit();
        if (unit == null) return;

        var controller = unit.GetComponent<UnitMovementController>();
        if (controller == null) return;

        int attackerFaction = controller.PlayerIndex;
        if (cellOwner == attackerFaction) return;

        _logisticsService.TransferOwner(cell, attackerFaction);
    }
}
