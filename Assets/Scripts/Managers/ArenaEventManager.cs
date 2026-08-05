using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
// 【竞技场-阶段二】竞技场事件管理器（ArenaEventManager）
// 状态机：Inactive → Reserved（开局预留区标记）→ Activated（突起 + 宝箱）→ Destroyed（宝箱摧毁恢复）。
// 只负责生成 HexCellPatch 与状态编排；地形重建/单位处理/迷雾/标签全部经 MapMutationService。
// 触发时机 = 配置项 ArenaActivateTime（GameTime 自算，不挂 GlobalTimerService 避免冲突；
// 暂停期间 GameTime 冻结天然顺延）。0 = 开局即突起。
//****************************************

public class ArenaEventManager : ITickable
{
    public enum ArenaState
    {
        Inactive,
        Reserved,
        Activated,
        Destroyed
    }

    public const string VisibilitySource = "Arena";
    public const int ArenaRadius = 3;

    private readonly IMapDataService _mapDataService;
    private readonly MapGenerationConfigSO _config;
    private readonly MapMutationService _mutationService;
    private readonly TemporaryVisibilityService _visibilityService;
    private readonly IMapRenderBackend _renderBackend;
    private readonly GameLoop _gameLoop;
    private readonly UnitMovementSystem _movementSystem;
    private readonly DiContainer _container;
    private readonly IUIConfigProvider _uiConfigProvider;
    private readonly MapVisualTransitionService _visualTransition;

    private HexCellData _centerCell;
    private readonly List<HexCellData> _arenaCells = new List<HexCellData>();   // 37 格（中心 + 半径3）
    private readonly List<HexCellData> _innerCells = new List<HexCellData>();   // 内 2 环（19 格）
    private readonly List<HexCellData> _ringCells = new List<HexCellData>();    // 外环（18 格）
    private readonly List<HexCellData> _wallCells = new List<HexCellData>();    // 外环除入口（16 格）
    private HexCellData _entranceA;
    private HexCellData _entranceB;

    private VisibilityLease _lease;
    private CentralChest _chest;

    public ArenaState State { get; private set; } = ArenaState.Inactive;
    public HexCellData CenterCell => _centerCell;
    public IReadOnlyList<HexCellData> ArenaCells => _arenaCells;

    public ArenaEventManager(
        IMapDataService mapDataService,
        MapGenerationConfigSO config,
        MapMutationService mutationService,
        TemporaryVisibilityService visibilityService,
        IMapRenderBackend renderBackend,
        GameLoop gameLoop,
        UnitMovementSystem movementSystem,
        DiContainer container,
        IUIConfigProvider uiConfigProvider,
        MapVisualTransitionService visualTransition)
    {
        _mapDataService = mapDataService;
        _config = config;
        _mutationService = mutationService;
        _visibilityService = visibilityService;
        _renderBackend = renderBackend;
        _gameLoop = gameLoop;
        _movementSystem = movementSystem;
        _container = container;
        _uiConfigProvider = uiConfigProvider;
        _visualTransition = visualTransition;
    }

    // ── 初始化（GameFlowManager 在 MapRender 后、公共建筑/玩家出生前调用）──────────

    /// <summary>
    /// 地图数据落定后调用：缓存 37 格（中心 + 半径3）、标记预留区 IsUnexplorable（禁开发/禁部署/禁占领）。
    /// 预留区地形按普通规则生成（不特殊处理），通行保持。
    /// </summary>
    public void OnMapInitialized()
    {
        if (State != ArenaState.Inactive) return;

        _centerCell = FindMapCenterCell();
        if (_centerCell == null)
        {
            Debug.LogError("[ArenaEventManager] 找不到地图中心格，竞技场预留区初始化失败。");
            return;
        }

        CollectArenaCells();
        SelectEntrances();

        // 预留区：禁探索（复用 IsUnexplorable，无费用标签 → 天然禁部署）
        foreach (HexCellData cell in _arenaCells)
            cell.IsUnexplorable = true;

        State = ArenaState.Reserved;
        Debug.Log($"[ArenaEventManager] 预留区已标记：中心 {_centerCell.HexCoordinate}，共 {_arenaCells.Count} 格（内环 {_innerCells.Count} + 外环 {_ringCells.Count}）。");
    }

    private HexCellData FindMapCenterCell()
    {
        // 地图按行主序生成（z 外层、x 内层），几何中心 = 行列各取中位
        int centerZ = _config.zNumber / 2;
        int centerX = _config.xNumber / 2;
        int order = centerZ * _config.xNumber + centerX;
        HexCellData cell = _mapDataService.GetCell(order);
        if (cell == null)
        {
            // 兜底：取生成顺序中位数
            var all = _mapDataService.GetAllCells();
            if (all != null && all.Count > 0)
                return all[all.Count / 2];
        }
        return cell;
    }

    private void CollectArenaCells()
    {
        _arenaCells.Clear();
        _innerCells.Clear();
        _ringCells.Clear();

        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell == null) continue;
            int distance = CubeDistance(cell.HexCoordinate, _centerCell.HexCoordinate);
            if (distance > ArenaRadius) continue;

            _arenaCells.Add(cell);
            if (distance <= ArenaRadius - 1)
                _innerCells.Add(cell);
            else
                _ringCells.Add(cell);
        }
    }

    /// <summary>入口：外环上沿 E / W 两个相对方向的格（对称，不偏袒出生侧）。</summary>
    private void SelectEntrances()
    {
        _entranceA = StepN(_centerCell, Enums.HexDirection.E, ArenaRadius);
        _entranceB = StepN(_centerCell, Enums.HexDirection.W, ArenaRadius);

        _wallCells.Clear();
        foreach (HexCellData cell in _ringCells)
        {
            if (cell != _entranceA && cell != _entranceB)
                _wallCells.Add(cell);
        }
    }

    private HexCellData StepN(HexCellData from, Enums.HexDirection direction, int steps)
    {
        HexCellData current = from;
        for (int i = 0; i < steps && current != null; i++)
            current = _mapDataService.GetNeighbor(current, direction);
        return current;
    }

    // ── 激活 ─────────────────────────────────────────────────

    public void Tick()
    {
        if (State != ArenaState.Reserved) return;
        if (_config.ArenaActivateTime <= 0f || _gameLoop.GameTime >= _config.ArenaActivateTime)
            Activate();
    }

    /// <summary>调试/测试入口：跳过等待立即突起。</summary>
    public void ActivateNow()
    {
        if (State == ArenaState.Reserved)
            Activate();
    }

    /// <summary>
    /// 突起（阶段二 Duration=0 同步提交；阶段四检测 SupportsAnimatedTransition 后启用 1.2s 中心向外错峰，§14）：
    /// 清空内 2 环 → 边界环不可通行（入口保留）→ 单位弹射/取消由 MapMutationService 处理 →
    /// Arena VisibilityLease 点亮 37 格 → 生成宝箱。
    /// </summary>
    private void Activate()
    {
        if (State != ArenaState.Reserved) return;
        State = ArenaState.Activated;

        float floorHeight = _config.ArenaFloorHeight;
        float wallHeight = _config.ArenaWallHeight;
        bool animated = _renderBackend.SupportsAnimatedTransition;

        _mutationService.BeginTransaction();

        // 内 2 环：平台高度、清河流/地貌/资源、可通行（IsUnexplorable 保持 true → 禁开发/禁部署）
        foreach (HexCellData cell in _innerCells)
        {
            _mutationService.Apply(cell, new HexCellPatch
            {
                HasHeight = true,
                Height = floorHeight,
                HasMovementCost = true,
                MovementCost = 1f,
                ClearRiver = true,
                ClearLandForm = true,
                ClearResource = true
            });
        }

        // 外环：墙高；非入口格 movementCost=MaxValue（边界地块，决策 C）；入口格按普通地块
        foreach (HexCellData cell in _ringCells)
        {
            bool isEntrance = cell == _entranceA || cell == _entranceB;
            _mutationService.Apply(cell, new HexCellPatch
            {
                HasHeight = true,
                Height = isEntrance ? floorHeight : wallHeight,
                HasMovementCost = true,
                MovementCost = isEntrance ? 1f : float.MaxValue,
                HasIsUnexplorable = true,
                IsUnexplorable = !isEntrance,
                ClearRiver = true,
                ClearLandForm = true,
                ClearResource = true
            });
        }

        // 阶段四：后端支持动画 → 1.2s 中心向外错峰（§13.10 第一版范围）；否则同步提交
        var options = new MapTransitionOptions
        {
            Duration = animated ? ArenaRiseDurationSeconds : 0f,
            Stagger = animated ? MapTransitionStagger.CenterToOuter : MapTransitionStagger.Simultaneous,
            StaggerCenter = _centerCell,
            Easing = null,
            LockAffectedCells = true
        };
        _mutationService.Commit(options);

        // 迷雾视觉点亮（不置探索位）：Arena VisibilityLease + 立即刷新（突破 20fps 限频）
        // 【实机修订-2026-08-04】snapCells=_arenaCells：突起帧 37 格瞬间点亮（§18.2），
        // 不再走 2 秒渐变（FogTransitionManager.TransitionSpeed=0.5/s 太慢，观感"迷雾未去除"）
        _lease = _visibilityService.AcquireLease(VisibilitySource, _arenaCells);
        _renderBackend.ForceRefreshFogVisuals(_arenaCells);

        // 中央宝箱（出现即激活，同帧）
        SpawnChest();

        // 阶段四：宝箱作为视觉跟随物随地形升起（§13.2 宝箱模型升起；动画期间注册）
        if (animated && _chest != null && _visualTransition != null)
        {
            _visualTransition.RegisterVisualFollower(_chest.transform, _centerCell);
        }

        // 全单位路径失效（MapMutationService.Commit 已统一处理，此处仅日志）
        Debug.Log($"[ArenaEventManager] 竞技场突起完成：平台 {_innerCells.Count} 格 + 边界 {_wallCells.Count} 格（入口 2）" +
                  (animated ? $"，动画 {ArenaRiseDurationSeconds}s 中心向外错峰。" : "（同步提交）。"));
    }

    /// <summary>阶段四：竞技场突起动画时长（§13.10 第一版锁定 1.2s）。</summary>
    public const float ArenaRiseDurationSeconds = 1.2f;

    // ── 宝箱 ─────────────────────────────────────────────────

    private void SpawnChest()
    {
        if (_config.centralChestModel == null)
        {
            Debug.LogError("[ArenaEventManager] 未配置 centralChestModel（MapGenerationConfigSO），宝箱无法生成。");
            return;
        }

        GameObject model = Object.Instantiate(_config.centralChestModel);
        model.name = "CentralChest";
        // 先挂父（worldPositionStays=false 保留 prefab 原始 local），再设世界坐标：
        // 若先设 position 再 SetParent(false)，local 被保留导致世界坐标叠加父对象偏移
        // （NeutralBuilding 根节点位于 (13.83, 24.35, 55.86)），宝箱会偏出竞技场。
        model.transform.SetParent(GameObject.Find("NeutralBuilding")?.transform, false);
        model.transform.position = _centerCell.RealCenterWorldCoordinate;
        model.tag = "NeutralBuilding";

        CentralChest chest = model.AddComponent<CentralChest>();
        _container.Inject(chest);

        chest.buildingData = new BuildingData(Enums.BulidingType.PublicBuilding, null);
        chest.buildingData.controller = null;
        chest.bulidingType = Enums.BulidingType.PublicBuilding;
        chest.InitializeAsChest(_centerCell, _mapDataService);

        // 宝箱占格不可通行（Initialize 已置 MaxValue）：中心格上若有单位（罕见），弹到最近可通行格
        _movementSystem.EjectUnitsFromImpassableCells(new List<HexCellData> { _centerCell });

        // 血条画布（运行时构造）→ WireBuildingCanvas（注入 UIController + 注册运行时画布）
        BuildChestHealthBar(model);
        SpawnUIWiring.WireBuildingCanvas(model, chest, new Color(1f, 0.85f, 0.2f), _container, _uiConfigProvider);

        chest.ChestDestroyed += OnChestDestroyed;
        _gameLoop.RegisterPublicBuilding(chest);
        _chest = chest;

        Debug.Log($"[ArenaEventManager] 中央宝箱已生成于 {_centerCell.HexCoordinate}（HP={CentralChest.ChestHp}，中立，直接激活）。");
    }

    /// <summary>
    /// 运行时构造宝箱血条：World Space Canvas（scale 0.01，与既有建筑画布一致）+ child0 Slider
    /// （WireBuildingCanvas 约定 canvas.transform.GetChild(0) 为血条）。
    /// </summary>
    private static void BuildChestHealthBar(GameObject root)
    {
        var canvasGo = new GameObject("HealthBarCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        var canvasRT = canvasGo.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(1920f, 1080f);
        canvasRT.localPosition = new Vector3(0f, 2.2f, 0f);
        canvasRT.localScale = Vector3.one * 0.01f;

        var sliderGo = new GameObject("HealthBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
        sliderGo.transform.SetParent(canvasGo.transform, false);
        var sliderRT = sliderGo.GetComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRT.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = Vector2.zero;
        sliderRT.sizeDelta = new Vector2(400f, 60f);

        var background = sliderGo.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.65f);

        var slider = sliderGo.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(sliderGo.transform, false);
        var fillRT = fillGo.GetComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.offsetMin = new Vector2(4f, 4f);
        fillRT.offsetMax = new Vector2(-4f, -4f);
        fillGo.GetComponent<Image>().color = Color.white;
        slider.fillRect = fillRT;
    }

    /// <summary>宝箱摧毁 → 竞技场恢复普通地形（纯规则恢复，零 mesh 重建；高度不回落，决策已锁定）。</summary>
    private void OnChestDestroyed(CentralChest chest)
    {
        if (State != ArenaState.Activated) return;
        State = ArenaState.Destroyed;

        _mutationService.BeginTransaction();
        // 全竞技场解除禁探索（恢复可探索 → 自动恢复可部署/可占领）
        foreach (HexCellData cell in _arenaCells)
        {
            _mutationService.Apply(cell, HexCellPatch.UnexplorablePatch(false));
        }
        // 边界环（含原入口）恢复可通行
        foreach (HexCellData cell in _ringCells)
        {
            _mutationService.Apply(cell, HexCellPatch.MovementCostPatch(1f));
        }
        _mutationService.Commit(new MapTransitionOptions { Duration = 0f });

        // 释放 Arena 临时可见性 → 迷雾重新遮盖（探索位始终未置位，需探索恢复）
        if (_lease != null)
        {
            _lease.Release();
            _lease = null;
        }
        _renderBackend.ForceRefreshFogVisuals();

        // 注销并销毁宝箱。先确定性清格（不等 Destroy 延迟到帧末），基类 OnDestroy 幂等兜底
        if (chest != null)
        {
            // 阶段四：动画未结束时取消跟随（防止销毁后 Transform 被持续写入）
            if (_visualTransition != null)
                _visualTransition.UnregisterVisualFollower(chest.transform);

            HexCellData center = chest.RootHex;
            if (center != null && center.publicBuildingRoot == chest)
            {
                center.publicBuildingRoot = null;
                center.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(
                    Enums.BulidingType.NoBuilding, null);
                center.movementCost = 1f; // 中心格恢复普通平地（可通行）
            }
            chest.ChestDestroyed -= OnChestDestroyed;
            _gameLoop.UnregisterPublicBuilding(chest);
            Object.Destroy(chest.gameObject);
        }
        _chest = null;

        Debug.Log("[ArenaEventManager] 竞技场已恢复普通地形（可探索/可通行/可开发/可部署，迷雾重新遮盖）。");
    }

    // ── 对局结束兜底 ─────────────────────────────────────────

    /// <summary>对局结束时强制收尾：释放 lease、强制完成动画、注销并销毁宝箱（由 EndGame.EndThisGame 调用）。</summary>
    public void Shutdown()
    {
        // 阶段五：分帧提交对局结束兜底（几何立即构建完成，防锁残留/句柄泄漏）
        if (_mutationService.HasSlicedCommitPending)
            _mutationService.ForceCompleteSliced();

        // 阶段四：强制完成活动动画（§13.8 对局结束清理；幂等）
        if (_visualTransition != null && _visualTransition.IsAnimating)
        {
            if (_chest != null)
                _visualTransition.UnregisterVisualFollower(_chest.transform);
            _visualTransition.ForceComplete();
        }

        if (_lease != null)
        {
            _lease.Release();
            _lease = null;
        }
        if (_chest != null)
        {
            _chest.ChestDestroyed -= OnChestDestroyed;
            _gameLoop.UnregisterPublicBuilding(_chest);
            Object.Destroy(_chest.gameObject);
            _chest = null;
        }
        State = ArenaState.Destroyed;
    }

    // ── 出生/生成排除查询 ────────────────────────────────────

    /// <summary>查询地块是否落在预留区（含外扩 extraRings 环）——供主城出生点/公共建筑生成排除。</summary>
    public bool IsNearReservedZone(HexCellData cell, int extraRings)
    {
        if (_centerCell == null || cell == null) return false;
        return CubeDistance(cell.HexCoordinate, _centerCell.HexCoordinate) <= ArenaRadius + extraRings;
    }

    private static int CubeDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = a - b;
        return (int)((Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z)) * 0.5f);
    }}
