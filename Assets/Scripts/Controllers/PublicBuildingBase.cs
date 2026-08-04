using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
// 【公共建筑系统-决策#18/#21/#31/#32/#33】公共建筑基类
// 职责：两阶段HP管理、多格坐标管理、攻击转发、势力范围扩展、易主逻辑
// 死亡 = 易主而非销毁，由 GameLoop 驱动检测
//
// 继承关系：BuildingBase → PublicBuildingBase → 具体公共建筑子类
//****************************************

public abstract class PublicBuildingBase : BuildingBase
{
    [Inject] private GameLoop _gameLoop;
    [Inject] private PublicBuildingMarkerManager _markerManager;
    [Inject] private ExplorationPillarPool _explorationEffectPool;
    [Inject] private ILogisticsService _logisticsService;
    // 【地图资源配置化】资源统一消费服务（替代原本地收割 switch + _goldWallet 直接发币）
    [Inject] private MapResourceCollectionService _collectionService;

    public enum DiscoveryState
    {
        Hidden,
        Revealed
    }

    /// <summary>公共建筑被占领事件（参数为占领方的 PlayerIndex）</summary>
    public static event System.Action<int> OnPublicBuildingCaptured;

    // ── 【动态地图-阶段二】基类保护入口（CentralChest 用）──────────────────
    /// <summary>触发占领/摧毁事件（C# 事件只能由声明类型内部 Invoke，宝箱 OnDeath 经此触发海克斯）。</summary>
    protected void RaiseCapturedEvent(int factionId)
    {
        OnPublicBuildingCaptured?.Invoke(factionId);
    }

    /// <summary>不经探索直接置为已发现（宝箱生成即激活；不写探索位，迷雾由 VisibilityLease 覆盖）。</summary>
    protected void MarkRevealedWithoutExploration()
    {
        CurrentDiscoveryState = DiscoveryState.Revealed;
    }

    // ── 多格坐标（决策#34/#4）──────────────────────────
    /// <summary>根格（持有 HP 和 Controller 的中心格）</summary>
    public HexCellData RootHex { get; private set; }

    /// <summary>子格偏移方向列表（相对根格，共3个方向，生成时由 PublicBuildingGenerator 设置）</summary>
    public Enums.HexDirection[] SubHexDirections { get; private set; }

    /// <summary>全部占位格（根格 + 3 个子格），生成后缓存</summary>
    public List<HexCellData> OccupiedHexes { get; private set; } = new List<HexCellData>();

    // ── PlayerIndex（伪AI阵营，决策#22/#23）──────────────
    /// <summary>本公共建筑占用的 PlayerIndex（由 GameFlowManager 动态分配）</summary>
    public int PlayerIndex { get; private set; } = -1;

    /// <summary>初始中立态 PlayerIndex（用于判断首次夺取，决策#32）</summary>
    private int _initialNeutralPlayerIndex = -1;

    /// <summary>是否已完成首次夺取（首次夺取后 hp 切换为 defenseHp）</summary>
    private bool _hasBecomeOwned = false;

    /// <summary>公共建筑是否已经被任意单位发现。</summary>
    public DiscoveryState CurrentDiscoveryState { get; private set; } = DiscoveryState.Hidden;

    private HashSet<HexCellData> _discoveryArea;
    private readonly HashSet<HexCellData> _pendingCaptureRewards = new HashSet<HexCellData>();

    // ── 初始化（由 PublicBuildingGenerator 调用）──────────
    /// <summary>
    /// 初始化公共建筑的格子归属、PlayerIndex 和两阶段 HP。
    /// 必须在实例化后立即调用。
    /// </summary>
    public void Initialize(
        HexCellData rootHex,
        Enums.HexDirection[] subHexDirections,
        int assignedPlayerIndex,
        float captureHp,
        float defenseHp,
        IMapDataService mapDataService)
    {
        RootHex = rootHex;
        SubHexDirections = subHexDirections;
        PlayerIndex = assignedPlayerIndex;
        _initialNeutralPlayerIndex = assignedPlayerIndex;

        // 设置两阶段 HP（决策#31）：初始使用 captureHp
        buildingData.captureHp = captureHp;
        buildingData.defenseHp = defenseHp;
        buildingData.hp = captureHp;
        buildingData.currentHp = captureHp;
        SyncHealthBar();

        // 计算并缓存全部占位格
        OccupiedHexes.Clear();
        OccupiedHexes.Add(rootHex);

        foreach (var dir in subHexDirections)
        {
            HexCellData subHex = mapDataService.GetNeighbor(rootHex, dir);
            if (subHex != null)
            {
                OccupiedHexes.Add(subHex);
            }
            else
            {
                Debug.LogWarning($"[PublicBuildingBase] Initialize: sub hex direction {dir} from root {rootHex.HexCoordinate} is out of bounds, skipped.");
            }
        }

        // 在所有占位格上写入根格引用（决策#29/#19）
        foreach (var hex in OccupiedHexes)
        {
            hex.publicBuildingRoot = this;
            hex.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(
                Enums.BulidingType.PublicBuilding, gameObject);
            hex.movementCost = float.MaxValue; // 占位格不可通行
        }

        // 设置归属（中立：Player_City_Index 使用 (playerIndex, 0)）
        Player_City_Index = new KeyValuePair<int, int>(assignedPlayerIndex, 0);

        // 初始血条颜色为中立色（白色）
        UITool.TrySetSliderFillColor(uiHealthBar, Color.white);

        CacheDiscoveryArea();
    }

    /// <summary>
    /// 由 GameLoop 调用。任意单位进入建筑占位格外一环时，全局发现该公共建筑。
    /// 【动态地图-阶段二】改 virtual：CentralChest 覆写为空（生成即激活，不参与发现）。
    /// </summary>
    public virtual void TickDiscovery()
    {
        if (CurrentDiscoveryState == DiscoveryState.Revealed) return;

        CacheDiscoveryArea();
        foreach (var hex in _discoveryArea)
        {
            if (hex != null && (hex.HasOccupant() || hex.IsHaveUnit()))
            {
                Reveal();
                return;
            }
        }
    }

    /// <summary>显示建筑并自动探索占位格及其外一环，不改变地块归属。</summary>
    public void Reveal()
    {
        if (CurrentDiscoveryState == DiscoveryState.Revealed) return;

        CurrentDiscoveryState = DiscoveryState.Revealed;
        CacheDiscoveryArea();

        foreach (var hex in _discoveryArea)
        {
            if (hex == null) continue;

            _explorationEffectPool?.PlayRevealEffect(hex);

            if (!hex.IsExploredBy(0))
            {
                _pendingCaptureRewards.Add(hex);
                hex.ExploreBy(0);
            }
            if (!hex.IsExploredBy(1))
            {
                hex.ExploreBy(1);
            }

            hex.IsUnexplorable = false;
            if (hex.resourceModel != null)
                hex.resourceModel.SetActive(true);
        }

        gameObject.SetActive(true);
        _markerManager.RemoveMarker(this);
        _gameLoop.InvalidateAllBrainPaths();
        _mapVisualEvent.Raise();

        Debug.Log($"[PublicBuildingBase] Revealed at {RootHex?.HexCoordinate}");
    }

    private void CacheDiscoveryArea()
    {
        if (_discoveryArea != null) return;

        _discoveryArea = GetInfluenceRingHexes();
        foreach (var hex in OccupiedHexes)
            _discoveryArea.Add(hex);
    }

    // ── 受击入口（覆写，支持多格攻击转发）────────────
    public override void BuildingAttacked(GameObject enemyAttacker)
    {
        // 公共建筑的受击逻辑与普通建筑相同，但此方法也是子格转发后的接收端
        base.BuildingAttacked(enemyAttacker);
    }

    // ── 死亡 / 易主（决策#9/#32）──────────────────────
    /// <summary>
    /// GameLoop 检测到 HP≤0 后调用。
    /// 公共建筑死亡 = 易主，不销毁。
    /// </summary>
    public override void OnDeath()
    {
        if (_isDestroyed) return;
        if (Attacker == null)
        {
            Debug.LogWarning($"[PublicBuildingBase] OnDeath called but Attacker is null, skipping capture.");
            return;
        }

        var attackerController = Attacker.GetComponent<UnitMovementController>();
        if (attackerController == null)
        {
            Debug.LogWarning($"[PublicBuildingBase] OnDeath: Attacker has no UnitMovementController, skipping.");
            return;
        }

        OnCaptured(attackerController.PlayerIndex);
    }

    // ── 易主流程（决策#32）────────────────────────────
    /// <summary>
    /// 易主流程：移除旧势力范围 → 切换 PlayerIndex → 切换血量阶段（首次时）→ 回满 → 扩展新势力范围 → 更新视觉
    /// 【断供方案-阶段3】triggerRecalculate=false 供区域吞并批量调用（吞并后统一一次重算，见 AnnexationService）。
    /// </summary>
    public void OnCaptured(int newOwnerPlayerIndex, bool triggerRecalculate = true)
    {
        int oldPlayerIndex = PlayerIndex;

        // 1. 移除旧势力范围
        RemoveSphereOfInfluence(oldPlayerIndex);

        // 2. 切换 PlayerIndex 和归属
        PlayerIndex = newOwnerPlayerIndex;
        Player_City_Index = new KeyValuePair<int, int>(newOwnerPlayerIndex, 0);

        // 3. 切换血量阶段（首次夺取时，决策#31/#13）
        if (!_hasBecomeOwned)
        {
            _hasBecomeOwned = true;
            buildingData.hp = buildingData.defenseHp;
        }
        // 之后每次易主 hp 已是 defenseHp，无需再切换

        // 4. 回满血量
        buildingData.currentHp = buildingData.hp;
        SyncHealthBar();

        // 5. 扩展新势力范围（决策#20/#33）
        ExpandSphereOfInfluence(newOwnerPlayerIndex);
        // 【断供方案-阶段3】区域吞并批量调用时抑制逐格重算（AnnexationService 统一一次重算）
        if (triggerRecalculate)
            _logisticsService.RecalculateAll();

        // 6. 更新视觉（血条颜色、tag）
        UpdateVisual(newOwnerPlayerIndex);

        // 7. 触发地图刷新
        _mapVisualEvent.Raise();

        Debug.Log($"[PublicBuildingBase] Captured by PlayerIndex={newOwnerPlayerIndex} at {RootHex?.HexCoordinate}");

        OnPublicBuildingCaptured?.Invoke(newOwnerPlayerIndex);
    }

    // ── 势力范围：移除旧主人的范围（决策#20）────────
    // 【断供方案-阶段1/§4.3】不再读写单城字典——公共建筑不伪装为城市条目，
    // 领地字典统一由 LogisticsService.RecalculateAll 从地块归属重建。
    private void RemoveSphereOfInfluence(int oldPlayerIndex)
    {
        if (oldPlayerIndex < 0) return;

        // 【防 NRE】依赖未注入或已失效时跳过（例如旧场景残留实例、注入失败的边缘情况）
        if (_enemyModelManager == null || _playerModelManager == null)
        {
            Debug.LogWarning($"[PublicBuildingBase] {name}: 依赖未注入 (enemy={_enemyModelManager != null}, player={_playerModelManager != null})，跳过势力范围移除。");
            return;
        }

        if (oldPlayerIndex == 0)
        {
            foreach (var hex in OccupiedHexes)
            {
                _playerModelManager.SphereOfInfluence_HexC_HexCellData.Remove(hex.HexCoordinate);
            }
            foreach (var hex in GetInfluenceRingHexes())
            {
                _playerModelManager.SphereOfInfluence_HexC_HexCellData.Remove(hex.HexCoordinate);
            }
        }
        else
        {
            if (_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.TryGetValue(oldPlayerIndex, out var totalSphere))
            {
                foreach (var hex in OccupiedHexes)
                {
                    totalSphere.Remove(hex.HexCoordinate);
                    hex.Player_City_Index = new KeyValuePair<int, int>(-1, -1);
                }
                foreach (var hex in GetInfluenceRingHexes())
                {
                    totalSphere.Remove(hex.HexCoordinate);
                    if (hex.Player_City_Index.Key == oldPlayerIndex)
                        hex.Player_City_Index = new KeyValuePair<int, int>(-1, -1);
                }
            }
        }
    }

    // ── 势力范围：扩展新主人的范围（决策#7/#20/#33）──
    private void ExpandSphereOfInfluence(int newOwnerPlayerIndex)
    {
        // 【防 NRE】依赖未注入或已失效时跳过（避免吞并/易主流程逐帧刷屏）
        if (_mapDataService == null || _enemyModelManager == null || _playerModelManager == null || _collectionService == null)
        {
            Debug.LogWarning($"[PublicBuildingBase] {name}: 依赖未注入 (map={_mapDataService != null}, enemy={_enemyModelManager != null}, player={_playerModelManager != null}, collection={_collectionService != null})，跳过势力范围扩展。");
            return;
        }

        var newCityKey = new KeyValuePair<int, int>(newOwnerPlayerIndex, 0);
        var owner = newCityKey;

        // 收集：4格各自外圈一环，用 HashSet 去重（决策#33）
        var allInfluenceHexes = new HashSet<HexCellData>();
        foreach (var hex in OccupiedHexes)
        {
            allInfluenceHexes.Add(hex); // 占位格本身
            for (int i = 0; i < 6; i++)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(hex, (Enums.HexDirection)i);
                if (neighbor != null && neighbor.HexType != Enums.HexType.LakeOrSea)
                {
                    allInfluenceHexes.Add(neighbor);
                }
            }
        }

        if (newOwnerPlayerIndex == 0)
        {
            foreach (var hex in allInfluenceHexes)
            {
                if (!hex.IsExploredBy(0) || _pendingCaptureRewards.Remove(hex))
                {
                    _collectionService.HarvestForGold(hex, newOwnerPlayerIndex);
                }

                hex.Player_City_Index = owner;
                hex.ExploreBy(0);
                _playerModelManager.SphereOfInfluence_HexC_HexCellData[hex.HexCoordinate] = hex;
            }
        }
        else
        {
            if (!_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.ContainsKey(newOwnerPlayerIndex))
                _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[newOwnerPlayerIndex] = new Dictionary<Vector3, HexCellData>();

            foreach (var hex in allInfluenceHexes)
            {
                if (!hex.IsExploredBy(newOwnerPlayerIndex) || _pendingCaptureRewards.Remove(hex))
                {
                    _collectionService.HarvestForGold(hex, newOwnerPlayerIndex);
                }

                hex.Player_City_Index = owner;
                hex.ExploreBy(newOwnerPlayerIndex);
                _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[newOwnerPlayerIndex][hex.HexCoordinate] = hex;
            }
        }
    }

    // ── 获取势力范围外圈（各占位格外一环，去重）────
    private HashSet<HexCellData> GetInfluenceRingHexes()
    {
        var ring = new HashSet<HexCellData>();
        foreach (var hex in OccupiedHexes)
        {
            for (int i = 0; i < 6; i++)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(hex, (Enums.HexDirection)i);
                if (neighbor != null && !OccupiedHexes.Contains(neighbor))
                {
                    ring.Add(neighbor);
                }
            }
        }
        return ring;
    }

    // ── 视觉更新（血条颜色 / tag，决策#24）──────────
    protected virtual void UpdateVisual(int ownerPlayerIndex)
    {
        Color barColor;
        string buildingTag;

        if (ownerPlayerIndex == 0)
        {
            barColor = Color.green;
            buildingTag = "PlayerBuilding";
        }
        else if (_enemyModelManager.IsPublicBuilding(ownerPlayerIndex))
        {
            // 公共建筑伪AI——理论上易主后归真实玩家/AI，此分支用于未来扩展
            barColor = Color.white;
            buildingTag = "NeutralBuilding";
        }
        else
        {
            barColor = Color.red;
            buildingTag = "EnemyBuilding";
        }

        gameObject.tag = buildingTag;
        UITool.TrySetSliderFillColor(uiHealthBar, barColor);
    }

    // ── CheckDeath 覆写（去掉 isCityChangeOwner 的限制）
    public override bool CheckDeath()
    {
        return !_isDestroyed && buildingData != null && buildingData.currentHp <= 0;
    }

    // ── MonoBehaviour 生命周期 ────────────────────────
    protected virtual void OnDestroy()
    {
        base.OnDestroy(); // 【断供方案-阶段5】退订血条可见性事件

        _markerManager?.RemoveMarker(this);

        // 清空占位格引用，防止野指针
        foreach (var hex in OccupiedHexes)
        {
            if (hex != null && hex.publicBuildingRoot == this)
            {
                hex.publicBuildingRoot = null;
                hex.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(
                    Enums.BulidingType.NoBuilding, null);
                hex.movementCost = 1f;
            }
        }
    }
}
