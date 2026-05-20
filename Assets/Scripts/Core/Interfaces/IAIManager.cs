using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
//创建人：易生
//功能说明：AI 管理器，负责敌方单位、城市的生成与回合行为
//****************************************

public class IAIManager : MonoBehaviour
{
    // 注入
    [Inject] private IUnitDataProvider _unitDataProvider;
    [Inject] private IBuildingDataProvider _buildingDataProvider;
    [Inject] private ICardUnlockRuleProvider _cardUnlockRuleProvider;
    [Inject] private IUIConfigProvider _uiConfigProvider;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private DiContainer _container;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private UnitMovementSystem _movementSystem;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private EnemyModelManager _enemyModelManager;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private TechData _techData;
    [Inject] private CultureData _cultureData;

    private const int AIIndex = 1;
    private const float TechCultureBuildingPointsPerTurn = 10f;
    private const float ReapChestGain = 30f;

    public GameObject AICity;

    private readonly System.Random _random = new System.Random();
    private readonly AIPlayerState _aiPlayerState = new AIPlayerState();

    private readonly HashSet<int> _playedBuildingCardIds = new HashSet<int>();
    private bool _settlerCardPlayed;

    // AI初始化（开局时调用）
    public void AIInit()
    {
        // 开局随机位置 + 一个单位 + 建城
        HexCellData AIUnitHex;
        HexCellData AICityHex;

        while (true)
        {
            int j = _random.Next(_mapDataService.GetAllCells().Count - 1 - _config.xNumber);
            AIUnitHex = _mapDataService.GetCell(j);
            AICityHex = _mapDataService.GetCell(j + 1);

            if (AIUnitHex == null) { Debug.LogError("AIUnitHex is null for index: " + j); }
            if (AICityHex == null) { Debug.LogError("AICityHex is null for index: " + j + 1); }

            if (AIUnitHex.HexType != Enums.HexType.LakeOrSea && AICityHex.HexType != Enums.HexType.LakeOrSea) { break; }
        }

        Vector3 AIUnitV = AIUnitHex.RealCenterWorldCoordinate;
        Vector3 AICityV = AICityHex.RealCenterWorldCoordinate;

        AIUnitGenerator(1, AIUnitV);
        AICityGenerator(AICityV);
        InitializeAICardState();
    }

    private void InitializeAICardState()
    {
        _aiPlayerState.Card.HandCardIds.Clear();
        _aiPlayerState.Card.NextCardId = -1;
        _aiPlayerState.Card.HasDealtThisTurn = false;
        _aiPlayerState.Card.HasGivenFirstTurnSettler = false;

        // 和玩家一致：第一张保证是移民卡
        for (int i = 0; i < AICardState.MaxHandCards; i++)
        {
            _aiPlayerState.Card.HandCardIds.Add(GenerateAICardID());
        }
        _aiPlayerState.Card.NextCardId = GenerateAICardID();
    }

    private int GenerateAICardID()
    {
        if (!_aiPlayerState.Card.HasGivenFirstTurnSettler)
        {
            _aiPlayerState.Card.HasGivenFirstTurnSettler = true;
            return 0;
        }

        List<int> unlockedIds = _cardUnlockRuleProvider.GetUnlockedCardIds(
            _aiPlayerState.TechCulture.TechLevel,
            _aiPlayerState.TechCulture.CultureLevel
        );
        if (unlockedIds.Count == 0)
        {
            return 0;
        }

        return unlockedIds[_random.Next(unlockedIds.Count)];
    }

    /// <summary>
    /// AI建城
    /// </summary>
    private void AICityGenerator(Vector3 position)
    {
        HexCellData h = _mapDataService.GetCellByWorldPosition(position);

        GameObject g = Instantiate(AICity);
        g.transform.SetParent(GameObject.Find("EnemyBuilding").transform, false);
        g.transform.position = position;
        g.tag = "EnemyBuilding";

        BuildingController buildingController = g.AddComponent<BuildingController>();
        _container.Inject(buildingController);

        BuildingData buildingData = new BuildingData(Enums.BulidingType.City, _buildingDataProvider);
        buildingController.buildingData = buildingData;
        buildingData.controller = buildingController;

        buildingController.bulidingType = Enums.BulidingType.City;
        h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(Enums.BulidingType.City, g);
        h.movementCost = float.MaxValue;

        // 获取当前AI的城市编号（新城市的索引）
        int cityIndex = _enemyModelManager.CityCount.ContainsKey(AIIndex) ? _enemyModelManager.CityCount[AIIndex] : 0;
        var cityKey = new KeyValuePair<int, int>(AIIndex, cityIndex);

        buildingController.Player_City_Index = cityKey;

        // 为该城市创建单独的势力范围字典
        if (!_enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.ContainsKey(cityKey))
        {
            _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[cityKey] = new Dictionary<Vector3, HexCellData>();
        }

        // 单位UI画布
        Canvas canvas = g.GetComponentInChildren<Canvas>();
        if (canvas == null) { return; }
        UIController buildingCanvas = canvas.gameObject.AddComponent<UIController>();
        _container.Inject(buildingCanvas);
        buildingCanvas.UIType = "buildingCanvas";
        _uiConfigProvider.AddRuntimeCanvas(canvas);

        // 单位血条
        GameObject healthBar = canvas.transform.GetChild(0).gameObject;
        UIController healthBarUI = healthBar.AddComponent<UIController>();
        _container.Inject(healthBarUI);
        healthBarUI.UIType = "buildingHealthBar";
        buildingController.uiHealthBar = healthBar.GetComponent<Slider>();
        healthBar.transform.GetChild(2).GetComponent<Image>().color = Color.red;

        // 获取AI的总势力范围字典
        var aiTotalSphere = _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData;
        if (!aiTotalSphere.ContainsKey(AIIndex))
            aiTotalSphere[AIIndex] = new Dictionary<Vector3, HexCellData>();

        // 扩展至总势力范围（同时设置地块归属）
        _playerModelManager.ExpandTheSphereOfInfluence(
            _mapDataService.WorldToHexCoordinate(g.transform.position),
            aiTotalSphere[AIIndex],
            cityKey
        );

        // 扩展至该城市自己的势力范围
        _playerModelManager.ExpandTheSphereOfInfluence(
            _mapDataService.WorldToHexCoordinate(g.transform.position),
            _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[cityKey],
            cityKey
        );

        // 增加该AI的城市计数
        if (!_enemyModelManager.CityCount.ContainsKey(AIIndex))
            _enemyModelManager.CityCount[AIIndex] = 0;
        _enemyModelManager.CityCount[AIIndex]++;

        // 触发地图视觉更新（势力范围边缘线）
        _mapVisualEvent.Raise();

        // 隐藏建筑（初始时不可见，等探索后才显示）
        g.SetActive(false);
    }

    /// <summary>
    /// AI单位生成
    /// </summary>
    private void AIUnitGenerator(int UnitIndex, Vector3 position)
    {
        GameObject g = Instantiate(_unitDataProvider.GetUnitPrefab(UnitIndex));
        g.transform.SetParent(GameObject.Find("EnemyUnit").transform, false);
        g.transform.position = position;
        g.tag = "EnemyUnit";

        g.AddComponent<UnitMovementController>();
        _container.InjectGameObject(g);

        CharacterData characterData = new CharacterData(UnitIndex, g, g.GetComponent<UnitMovementController>(), _unitDataProvider.GetUnitData(UnitIndex));
        g.GetComponent<UnitMovementController>().characterData = characterData;

        // 使用仓库添加敌方单位
        _unitRepository.AddEnemyUnit(AIIndex, g, characterData);

        g.GetComponent<UnitMovementController>().PlayerIndex = AIIndex;

        // 面板数据初始化
        CharacterData.InfoPanelData infoPanelData = new CharacterData.InfoPanelData();
        infoPanelData.sprite = _unitDataProvider.GetUnitIcon(characterData.UnitID);
        infoPanelData.name = characterData.unitData.unitName;
        infoPanelData.skillIcon = _unitDataProvider.GetSkillIcon(characterData.UnitID);
        infoPanelData.InfoDatas = new List<KeyValuePair<KeyValuePair<Sprite, string>, float>>();

        KeyValuePair<Sprite, string> Movement = new KeyValuePair<Sprite, string>(_uiConfigProvider.GetMovementPointsIcon(), "剩余移动力");
        KeyValuePair<Sprite, string> MeleeAttack = new KeyValuePair<Sprite, string>(_uiConfigProvider.GetMeleeAttackPointsIcon(), "攻击力");

        if (characterData.UnitID == 0)
        {
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(Movement, characterData.unitData.MovementPoints));
        }
        else
        {
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(MeleeAttack, characterData.unitData.BasicAttackValue));
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(Movement, characterData.unitData.MovementPoints));
        }
        characterData.infoPanelData = infoPanelData;

        // 单位UI画布
        Canvas canvas = g.GetComponentInChildren<Canvas>();
        UIController unitCanvas = canvas.gameObject.AddComponent<UIController>();
        unitCanvas.UIType = "unitCanvas";
        _uiConfigProvider.AddRuntimeCanvas(canvas);
        _container.Inject(unitCanvas);

        GameObject icon = canvas.transform.GetChild(0).gameObject;
        UIController iconUI = icon.AddComponent<UIController>();
        iconUI.UIType = "unitIcon";
        _container.Inject(iconUI);

        GameObject healthBar = canvas.transform.GetChild(1).gameObject;
        UIController healthBarUI = healthBar.AddComponent<UIController>();
        _container.Inject(healthBarUI);
        healthBarUI.UIType = "healthBar";
        characterData.healthBar = healthBar.GetComponent<Slider>();
        healthBar.transform.GetChild(2).GetComponent<Image>().color = Color.red;

        HexCellData h = _mapDataService.GetCellByWorldPosition(position);
        h.SetHaveUnit(true, g);

        g.SetActive(false);
    }

    /// <summary>
    /// AI建筑生成
    /// </summary>
    private void AIBuildingGenerator(int CardIndex, Vector3 position)
    {
        Vector3 v = _mapDataService.WorldToHexCoordinate(position);
        HexCellData h = _mapDataService.GetCellByWorldPosition(position);

        int bulidingTypeInt = CardIndex - (int)_unitDataProvider.GetUnitIconCount();
        GameObject g = Instantiate(_buildingDataProvider.GetBuildingPrefab(bulidingTypeInt));
        g.transform.SetParent(GameObject.Find("EnemyBuilding").transform, false);
        g.transform.position = position;
        g.tag = "EnemyBuilding";

        BuildingController buildingController = g.AddComponent<BuildingController>();
        _container.Inject(buildingController);

        BuildingData buildingData = new BuildingData((Enums.BulidingType)(bulidingTypeInt + 1), _buildingDataProvider);
        buildingController.buildingData = buildingData;
        buildingData.controller = buildingController;

        buildingController.bulidingType = (Enums.BulidingType)(bulidingTypeInt + 1);
        h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>((Enums.BulidingType)(bulidingTypeInt + 1), g);

        if (bulidingTypeInt == 0 || bulidingTypeInt == 1)
        {
            h.movementCost = float.MaxValue;
        }

        // 与玩家一致：建筑落地后扩展势力范围
        if (h.Player_City_Index.Key == AIIndex &&
            _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.ContainsKey(AIIndex))
        {
            _playerModelManager.ExpandTheSphereOfInfluence(
                v,
                _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[AIIndex],
                h.Player_City_Index
            );

            if (_enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.TryGetValue(h.Player_City_Index, out var citySphere))
            {
                _playerModelManager.ExpandTheSphereOfInfluence(v, citySphere, h.Player_City_Index);
            }
            _mapVisualEvent.Raise();
        }

        // 科技文化建筑：增加每回合科文产量
        if (bulidingTypeInt == 3)
        {
            _aiPlayerState.TechCulture.TechPointsPerTurn += TechCultureBuildingPointsPerTurn;
            _aiPlayerState.TechCulture.CulturePointsPerTurn += TechCultureBuildingPointsPerTurn;
        }

        buildingController.Player_City_Index = h.Player_City_Index;

        // 单位UI画布
        Canvas canvas = g.GetComponentInChildren<Canvas>();
        if (canvas == null) { return; }
        UIController buildingCanvas = canvas.gameObject.AddComponent<UIController>();
        _container.Inject(buildingCanvas);
        buildingCanvas.UIType = "buildingCanvas";
        _uiConfigProvider.AddRuntimeCanvas(canvas);

        GameObject healthBar = canvas.transform.GetChild(0).gameObject;
        UIController healthBarUI = healthBar.AddComponent<UIController>();
        _container.Inject(healthBarUI);
        healthBarUI.UIType = "buildingHealthBar";
        buildingController.uiHealthBar = healthBar.GetComponent<Slider>();
        healthBar.transform.GetChild(2).GetComponent<Image>().color = Color.red;

        g.SetActive(false);
    }

    // ---------------- AI 回合行为 ----------------
    /// <summary>
    /// 在 AI 回合被调用：依次处理每个敌方单位的行动。
    /// </summary>
    public IEnumerator ExecuteAITurn()
    {
        ExecuteAITurnCardPipeline();

        // 从仓库收集所有敌方单位（扁平化）
        List<CharacterData> enemyUnits = new List<CharacterData>();
        foreach (var group in _unitRepository.AllEnemyUnitGroups)
        {
            enemyUnits.AddRange(group.Values);
        }

        // 重置每个单位移动力为待机值
        foreach (var cd in enemyUnits)
        {
            if (cd?.unitMovementController != null)
            {
                cd.unitMovementController.RestoreUnitMovementStandbyParameters();
            }
        }

        if (enemyUnits.Count == 0) yield break;

        // 逐单位执行决策并等待其动作完成
        foreach (var cd in enemyUnits)
        {
            if (cd == null || cd.model == null) continue;
            if (cd.currentHp <= 0) continue;

            yield return StartCoroutine(HandleSingleUnitTurn(cd));

            yield return new WaitForSeconds(0.1f);
        }
    }

    /// <summary>
    /// 处理单个单位的回合：寻找最近玩家 -> 若可见则移动到攻击位置并攻击 -> 否则随机可达移动
    /// </summary>
    private IEnumerator HandleSingleUnitTurn(CharacterData cd)
    {
        var umc = cd.unitMovementController;
        if (umc == null || cd.model == null) yield break;

        TryReapChest(cd);

        if (cd.UnitID == 0)
        {
            yield return HandleSettlerTurn(cd);
            yield break;
        }

        List<Vector3> allPoints = new List<Vector3>(_mapDataService.GetAllHexCoordinates());
        Vector3 startHex = _mapDataService.WorldToHexCoordinate(cd.model.transform.position);

        // 寻找最近目标（玩家单位 + 玩家建筑）
        CharacterData nearestPlayer = null;
        GameObject nearestPlayerBuilding = null;
        float nearestCost = float.MaxValue;
        foreach (var p in _unitRepository.AllPlayerUnits.Values)
        {
            if (p?.model == null) continue;
            Vector3 endHex = _mapDataService.WorldToHexCoordinate(p.model.transform.position);
            if (_movementSystem.CalculateMinMovementCostBetweenTwoHexes(allPoints, startHex, endHex, Enums.MovementPurpose.None, out float cost, out _) && cost < nearestCost)
            {
                nearestCost = cost;
                nearestPlayer = p;
            }
        }

        GameObject[] playerBuildings = GameObject.FindGameObjectsWithTag("PlayerBuilding");
        foreach (var building in playerBuildings)
        {
            if (building == null) continue;
            Vector3 endHex = _mapDataService.WorldToHexCoordinate(building.transform.position);
            if (_movementSystem.CalculateMinMovementCostBetweenTwoHexes(allPoints, startHex, endHex, Enums.MovementPurpose.None, out float cost, out _) && cost < nearestCost)
            {
                nearestCost = cost;
                nearestPlayer = null;
                nearestPlayerBuilding = building;
            }
        }

        if (nearestCost <= umc.currentMovementPoints && (nearestPlayer != null || nearestPlayerBuilding != null))
        {
            if (nearestPlayer != null)
            {
                umc.attackedUnit = nearestPlayer.model;
                umc.attackTarget = "PlayerUnit";
            }
            else
            {
                umc.attackedUnit = nearestPlayerBuilding;
                umc.attackTarget = "PlayerBuilding";
            }

            Vector3 targetHex = _mapDataService.WorldToHexCoordinate(umc.attackedUnit.transform.position);
            umc.MoveTo(targetHex, Enums.MovementPurpose.MoveToAttack);

            while (umc.isMoving || umc.isAttack)
                yield return null;
            yield return new WaitForSeconds(0.2f);
        }
        else
        {
            // 随机移动
            List<Vector3> reachable = _movementSystem.GetAllReachableHexesFromStartHex(allPoints, startHex, umc.currentMovementPoints);
            reachable.RemoveAll(v => v == startHex);
            if (reachable.Count > 0)
            {
                Vector3 chosen = reachable[Random.Range(0, reachable.Count)];
                umc.MoveTo(chosen, Enums.MovementPurpose.None);
                while (umc.isMoving)
                    yield return null;
                yield return new WaitForSeconds(0.05f);
            }
        }
    }

    private void ExecuteAITurnCardPipeline()
    {
        // 与玩家类似，每回合只把“次卡”推进手牌一次
        _aiPlayerState.Card.HasDealtThisTurn = false;
        DealFromNextCardIfPossible();
        ApplyTechCultureProgress();
        PlayAICards();
    }

    private void DealFromNextCardIfPossible()
    {
        if (_aiPlayerState.Card.NextCardId < 0)
        {
            _aiPlayerState.Card.NextCardId = GenerateAICardID();
        }

        if (_aiPlayerState.Card.HasDealtThisTurn) return;
        if (_aiPlayerState.Card.HandCardIds.Count >= AICardState.MaxHandCards) return;
        if (_aiPlayerState.Card.NextCardId < 0) return;

        _aiPlayerState.Card.HandCardIds.Add(_aiPlayerState.Card.NextCardId);
        _aiPlayerState.Card.HasDealtThisTurn = true;
        _aiPlayerState.Card.NextCardId = GenerateAICardID();
    }

    private void ApplyTechCultureProgress()
    {
        // 与玩家 AddPointsPerTurn 一致：累积值按每回合产量推进
        int techMaxLevel = Mathf.Max(0, _techData.TechCost.Count - 1);
        if (_aiPlayerState.TechCulture.TechLevel < _techData.TechCost.Count)
        {
            float techCost = _techData.TechCost[_aiPlayerState.TechCulture.TechLevel];
            if (techCost > 0)
            {
                _aiPlayerState.TechCulture.TechAccumulatedPoints =
                    Mathf.Min(1f, _aiPlayerState.TechCulture.TechAccumulatedPoints + _aiPlayerState.TechCulture.TechPointsPerTurn / techCost);
            }
            if (_aiPlayerState.TechCulture.TechAccumulatedPoints >= 1f && _aiPlayerState.TechCulture.TechLevel < techMaxLevel)
            {
                _aiPlayerState.TechCulture.TechLevel++;
                _aiPlayerState.TechCulture.TechAccumulatedPoints = 0f;
            }
        }

        int cultureMaxLevel = Mathf.Max(0, _cultureData.CultureCost.Count - 1);
        if (_aiPlayerState.TechCulture.CultureLevel < _cultureData.CultureCost.Count)
        {
            float cultureCost = _cultureData.CultureCost[_aiPlayerState.TechCulture.CultureLevel];
            if (cultureCost > 0)
            {
                _aiPlayerState.TechCulture.CultureAccumulatedPoints =
                    Mathf.Min(1f, _aiPlayerState.TechCulture.CultureAccumulatedPoints + _aiPlayerState.TechCulture.CulturePointsPerTurn / cultureCost);
            }
            if (_aiPlayerState.TechCulture.CultureAccumulatedPoints >= 1f && _aiPlayerState.TechCulture.CultureLevel < cultureMaxLevel)
            {
                _aiPlayerState.TechCulture.CultureLevel++;
                _aiPlayerState.TechCulture.CultureAccumulatedPoints = 0f;
            }
        }
    }

    private void PlayAICards()
    {
        if (_aiPlayerState.Card.HandCardIds.Count == 0) return;

        List<int> orderedCards = _aiPlayerState.Card.HandCardIds
            .OrderByDescending(GetCardPriority)
            .ToList();
        List<int> playedCards = new List<int>();

        foreach (int cardId in orderedCards)
        {
            if (TryPlaySingleCard(cardId))
            {
                playedCards.Add(cardId);
            }
        }

        foreach (int cardId in playedCards)
        {
            _aiPlayerState.Card.HandCardIds.Remove(cardId);
        }
    }

    private int GetCardPriority(int cardId)
    {
        int unitCount = (int)_unitDataProvider.GetUnitIconCount();
        bool isUnitCard = cardId < unitCount;
        if (isUnitCard)
        {
            return cardId == 0 ? 100 : 70;
        }

        int buildingId = cardId - unitCount;
        if (buildingId == 3) return 90; // 科技文化建筑优先
        return 60;
    }

    private bool TryPlaySingleCard(int cardId)
    {
        if (cardId == 0 && _settlerCardPlayed)
            return false;

        int unitCount = (int)_unitDataProvider.GetUnitIconCount();
        bool isBuildingCard = cardId >= unitCount;
        if (isBuildingCard && _playedBuildingCardIds.Contains(cardId))
            return false;

        bool played = isBuildingCard
            ? TrySpawnBuildingFromCard(cardId)
            : TrySpawnUnitFromCard(cardId);

        if (played)
        {
            if (isBuildingCard)
                _playedBuildingCardIds.Add(cardId);
            else if (cardId == 0)
                _settlerCardPlayed = true;
        }

        return played;
    }

    private bool TrySpawnUnitFromCard(int unitId)
    {
        List<HexCellData> candidateCells = GetAIOwnedCells()
            .Where(IsValidSpawnCellForUnit)
            .ToList();
        if (candidateCells.Count == 0) return false;

        HexCellData selected = candidateCells[_random.Next(candidateCells.Count)];
        AIUnitGenerator(unitId, selected.RealCenterWorldCoordinate);
        return true;
    }

    private bool TrySpawnBuildingFromCard(int cardId)
    {
        int unitCount = (int)_unitDataProvider.GetUnitIconCount();
        int buildingId = cardId - unitCount;
        if (buildingId < 0 || buildingId >= _buildingDataProvider.GetBuildingCardsCount())
        {
            return false;
        }

        List<HexCellData> candidateCells = GetAIOwnedCells()
            .Where(IsValidSpawnCellForBuilding)
            .ToList();
        if (candidateCells.Count == 0) return false;

        // 科文建筑优先放在已有城市圈地内
        HexCellData selected = candidateCells[_random.Next(candidateCells.Count)];
        AIBuildingGenerator(cardId, selected.RealCenterWorldCoordinate);
        return true;
    }

    private bool IsValidSpawnCellForUnit(HexCellData cell)
    {
        if (cell == null) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.IsHaveUnit()) return false;
        return cell.Player_City_Index.Key == AIIndex;
    }

    private bool IsValidSpawnCellForBuilding(HexCellData cell)
    {
        if (cell == null) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.IsHaveUnit()) return false;
        return cell.Player_City_Index.Key == AIIndex;
    }

    private List<HexCellData> GetAIOwnedCells()
    {
        if (!_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.ContainsKey(AIIndex))
        {
            return new List<HexCellData>();
        }
        return _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[AIIndex]
            .Values
            .Where(c => c != null)
            .ToList();
    }

    private IEnumerator HandleSettlerTurn(CharacterData cd)
    {
        if (TryFoundCityWithSettler(cd))
        {
            yield break;
        }

        var umc = cd.unitMovementController;
        List<Vector3> allPoints = new List<Vector3>(_mapDataService.GetAllHexCoordinates());
        Vector3 startHex = _mapDataService.WorldToHexCoordinate(cd.model.transform.position);
        List<Vector3> reachable = _movementSystem.GetAllReachableHexesFromStartHex(allPoints, startHex, umc.currentMovementPoints);

        List<Vector3> cityTargets = reachable
            .Where(v =>
            {
                HexCellData c = _mapDataService.GetCell(v);
                return IsValidCityCell(c, cd.model) && c.Player_City_Index.Key == -1;
            })
            .ToList();

        if (cityTargets.Count == 0)
        {
            cityTargets = reachable
                .Where(v => IsValidCityCell(_mapDataService.GetCell(v), cd.model))
                .ToList();
        }

        if (cityTargets.Count > 0)
        {
            Vector3 chosen = cityTargets[_random.Next(cityTargets.Count)];
            umc.MoveTo(chosen, Enums.MovementPurpose.None);
            while (umc.isMoving)
            {
                yield return null;
            }
        }

        TryFoundCityWithSettler(cd);
    }

    private bool TryFoundCityWithSettler(CharacterData settlerData)
    {
        if (settlerData == null || settlerData.model == null) return false;

        HexCellData cell = _mapDataService.GetCellByWorldPosition(settlerData.model.transform.position);
        if (!IsValidCityCell(cell, settlerData.model)) return false;

        AICityGenerator(cell.RealCenterWorldCoordinate);
        cell.SetHaveUnit(false, null);
        _unitRepository.RemoveEnemyUnit(settlerData.model);
        Destroy(settlerData.model);
        return true;
    }

    private bool IsValidCityCell(HexCellData cell, GameObject settlerObj)
    {
        if (cell == null) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.Player_City_Index.Key != -1 && cell.Player_City_Index.Key != AIIndex) return false;
        if (!cell.IsHaveUnit()) return true;

        GameObject occupiedUnit = cell.GetUnit();
        return occupiedUnit == settlerObj;
    }

    private void TryReapChest(CharacterData cd)
    {
        if (cd == null || cd.model == null) return;

        HexCellData h = _mapDataService.GetCellByWorldPosition(cd.model.transform.position);
        if (h == null) return;
        if (h.GetResource() != Enums.ResourceType.Chest) return;

        h.ReapResource();
        if (h.resourceModel != null)
        {
            Destroy(h.resourceModel);
            h.resourceModel = null;
        }

        AddInstantTechCulturePoints(ReapChestGain, ReapChestGain);
    }

    private void AddInstantTechCulturePoints(float techPoints, float culturePoints)
    {
        if (_techData.TechCost.Count > 0 && _aiPlayerState.TechCulture.TechLevel < _techData.TechCost.Count)
        {
            float cost = _techData.TechCost[_aiPlayerState.TechCulture.TechLevel];
            if (cost > 0)
            {
                _aiPlayerState.TechCulture.TechAccumulatedPoints =
                    Mathf.Min(1f, _aiPlayerState.TechCulture.TechAccumulatedPoints + techPoints / cost);
            }
        }

        if (_cultureData.CultureCost.Count > 0 && _aiPlayerState.TechCulture.CultureLevel < _cultureData.CultureCost.Count)
        {
            float cost = _cultureData.CultureCost[_aiPlayerState.TechCulture.CultureLevel];
            if (cost > 0)
            {
                _aiPlayerState.TechCulture.CultureAccumulatedPoints =
                    Mathf.Min(1f, _aiPlayerState.TechCulture.CultureAccumulatedPoints + culturePoints / cost);
            }
        }
    }
}