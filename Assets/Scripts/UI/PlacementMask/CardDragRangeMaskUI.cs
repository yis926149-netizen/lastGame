using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace UI.PlacementMask
{
    //****************************************
    // 卡牌拖拽范围遮罩 · 主控组件（屏幕空间 UI 遮罩，拖拽态）。
    //
    // 一套渲染管线服务两类范围来源：
    //   · 战术卡：effectRadius（TacticalCardPresenter.GetEffectRadius，与结算同一份值）
    //   · 箭塔建造卡：预览射程（ArrowTowerShooter.GetPreviewRange，基础 + 高地加成；
    //     迷雾格不判高地、统一按平地口径，防「拖卡探迷雾下高地」作弊）
    // 两者都只在卡牌拖拽时显示、以触点指向格为中心圈 n 环（含中心格）、中心格非法时仍显示并压暗。
    //
    // 复用放置遮罩（红/绿）的整套技术路线：拓扑 → 拟合 → 投屏 → 填充 + 描边。
    // 但与 PlacementRangeMaskUI 有三处本质不同：
    //
    // 【1. 触发态是拖拽态，不是提起态】
    // 读 PlayerInputHandler.DraggingHoveredCell（触点当前指向的地块，每帧由
    // HighlightGridOnMouseHover 维护）。范围跟随触点指向的地块实时变化。
    // 生命周期锚定在 _isDraggingCard 上：按下拖起出现、松手即消失，
    // 与落点图标 / 单格高亮共存亡。
    //
    // 【2. 脏键不能抄「快照数量变化」】
    // n 固定时格数恒为 3n²+3n+1，触点在地图内部移格时格数一动不动 → 数量脏键 100% 失效。
    // 正确脏键：(拖拽卡引用, 触点格 HexCoordinate, 半径, 中心格合法性, 激活的 settings, 相机变换)。
    // 触点指向格的坐标是核心。
    //
    // 【3. 范围层独立成组件，不塞进 PlacementRangeMaskUI.Rebuild()】
    // 两层刷新频率差一个数量级（红/绿随提起卡+相机，本层随触点移格），
    // 塞一起会让触点每移一格都连带把 598 格红层整个重建一遍（R2）。
    //****************************************
    public sealed class CardDragRangeMaskUI : MonoBehaviour
    {
        [Inject] private PlayerInputHandler _inputHandler;
        [Inject] private IMapDataService _mapData;
        [Inject] private MapGenerationConfigSO _config;
        [Inject] private IBuildingDataProvider _buildingData;
        [Inject(Optional = true)] private IMapVisibilityResolver _visibilityResolver;
        [Inject(Id = "TargetUICanvas")] private Canvas _targetCanvas;

        // ---- 表现参数（真机可再标定）----
        // 全部参数收进 CardRangeMaskSettings 的两个子类，由 GameInstaller 的序列化字段注入
        //（本组件是运行时新建对象，自身 Inspector 不随场景保存、也无法在 Play 之前调）。
        private TacticalRangeMaskSettings _tacticalSettings = new TacticalRangeMaskSettings();
        private ArrowTowerRangeMaskSettings _arrowTowerSettings = new ArrowTowerRangeMaskSettings();

        /// <summary>战术卡范围表现参数。赋值即时生效。</summary>
        public TacticalRangeMaskSettings Settings
        {
            get => _tacticalSettings;
            set
            {
                _tacticalSettings = value ?? new TacticalRangeMaskSettings();
                if (_hasActiveMask) Rebuild();
            }
        }

        /// <summary>箭塔范围表现参数（默认精确贴合六边形）。赋值即时生效。</summary>
        public ArrowTowerRangeMaskSettings ArrowTowerSettings
        {
            get => _arrowTowerSettings;
            set
            {
                _arrowTowerSettings = value ?? new ArrowTowerRangeMaskSettings();
                if (_hasActiveMask) Rebuild();
            }
        }

        private Camera _camera;
        private RectTransform _maskRoot;
        private PlacementRangeMaskGraphic _fillGraphic;
        private PlacementRangeMaskGraphic _strokeGraphic;

        // 当前激活的 settings（战术卡 或 箭塔）；未激活为 null。渲染代码一律从它取形状/配色参数。
        private CardRangeMaskSettings _active;

        // 脏检测键（见类注释【2】）
        private CardData _lastCardRef;            // 拖拽卡引用（卡切换时重建）
        private Vector3 _lastHoverHex = NoHex;    // 触点指向格的 HexCoordinate
        private int _lastRadius = -1;
        private bool _lastValid;
        private Vector3 _lastCamPos;
        private Quaternion _lastCamRot;
        private bool _hasActiveMask;

        private static readonly Vector3 NoHex = new Vector3(999999f, 999999f, 999999f);
        private const int PlayerFactionId = 0; // 玩家阵营（与 ChunkMapRenderer.PlayerViewerFactionId 一致）

        // 复用缓冲：触点移格高频重建，避免每帧 GC（语义同 PlacementRangeMaskUI）。
        private readonly List<HexCellData> _rangeCells = new List<HexCellData>();
        private readonly List<Vector3> _loopWorld = new List<Vector3>();
        private readonly List<Vector3> _loopSimplified = new List<Vector3>();
        private readonly List<Vector3> _loopRounded = new List<Vector3>();
        private readonly List<Vector2> _loopLocal = new List<Vector2>();
        private readonly List<Vector2> _strokeVerts = new List<Vector2>();
        private readonly List<Color32> _strokeColors = new List<Color32>();
        private readonly List<int> _strokeTris = new List<int>();
        private readonly List<Vector2> _fillVerts = new List<Vector2>();
        private readonly List<int> _fillTris = new List<int>();
        private readonly List<Vector2> _shadowLoop = new List<Vector2>();
        private readonly List<List<Vector2>> _loops = new List<List<Vector2>>();
        private readonly List<List<Vector2>> _loopPool = new List<List<Vector2>>();

        private readonly PlacementMaskFill _fill = new PlacementMaskFill();

        private void Awake()
        {
            BuildRoot();
        }

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;

            if (!TryResolve(out CardRangeMaskSettings settings, out int radius, out bool valid))
            {
                if (_hasActiveMask)
                {
                    ClearGraphics();
                    _hasActiveMask = false;
                }
                ResetDirty();
                _active = null;
                return;
            }

            HexCellData hovered = _inputHandler.DraggingHoveredCell;
            CardData cardData = _inputHandler.DraggingCardData;
            Vector3 hex = hovered.HexCoordinate;

            bool cardChanged = !ReferenceEquals(cardData, _lastCardRef);
            bool cellChanged = hex != _lastHoverHex;
            bool radiusChanged = radius != _lastRadius;
            bool validChanged = valid != _lastValid;
            bool settingsChanged = !ReferenceEquals(settings, _active);
            bool cameraChanged = CameraMovedBeyondThreshold(settings);

            if (!cardChanged && !cellChanged && !radiusChanged && !validChanged
                && !settingsChanged && !cameraChanged && _hasActiveMask)
                return;

            _lastCardRef = cardData;
            _lastHoverHex = hex;
            _lastRadius = radius;
            _lastValid = valid;
            _active = settings;
            CacheCameraTransform();
            Rebuild();
        }

        // ------------------------------------------------ 范围来源解析 ------------------------------------------------

        /// <summary>
        /// 解析当前帧「该不该画、画多大、合不合法、用哪套参数」。
        /// 无范围（非拖拽 / 非战术卡与箭塔卡 / 开关关闭 / 半径 ≤ 0）返回 false。
        /// </summary>
        private bool TryResolve(out CardRangeMaskSettings settings, out int radius, out bool valid)
        {
            settings = null;
            radius = 0;
            valid = false;

            HexCellData hovered = _inputHandler?.DraggingHoveredCell;
            ICardDropHandler handler = _inputHandler?.DraggingDropHandler;
            CardData cardData = _inputHandler?.DraggingCardData;

            if (hovered == null || handler == null) return false;

            // 战术卡：effectRadius 与结算读同一份（R1）。
            if (handler is TacticalCardPresenter tactical)
            {
                settings = _tacticalSettings;
                if (!settings.ShowRangeMask) return false;
                radius = tactical.GetEffectRadius(cardData);
                if (radius <= 0) return false; // 决策 8：n=0 仅中心格，不走区域遮罩
                valid = handler.CanDeployTo(cardData, hovered);
                return true;
            }

            // 箭塔建造卡：有效射程（基础 + 高地加成）与战斗索敌读同一份（R1）。
            if (TryResolveArrowTower(handler, cardData, hovered, out radius))
            {
                settings = _arrowTowerSettings;
                if (!settings.ShowRangeMask) return false;
                if (radius <= 0) return false;
                valid = handler.CanDeployTo(cardData, hovered);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断当前拖拽的是否为「箭塔建造卡」，并给出该悬停格上的有效射程。
        /// 建筑类型以 Excel 平衡库为唯一主源（GetBuildingType），与部署 switch 一致。
        /// </summary>
        private bool TryResolveArrowTower(
            ICardDropHandler handler, CardData cardData, HexCellData hovered, out int radius)
        {
            radius = 0;
            if (!(handler is CardPresenter) || cardData == null || cardData.IsUnit) return false;
            if (!(cardData.NormalCardConfig is BuildingConfigSO buildingConfig)) return false;
            if (_buildingData == null) return false;

            Enums.BulidingType type;
            try
            {
                type = _buildingData.GetBuildingType(buildingConfig.buildingId);
            }
            catch (System.Exception)
            {
                // 视觉层绝不因数值库缺失/ID 未命中而崩溃：降级为「不画范围」。
                return false;
            }

            if (type != Enums.BulidingType.ArrowTower) return false;
            // 迷雾格不判高地、统一按平地口径（否则拖卡探迷雾下高地 = 变相作弊）。
            radius = ArrowTowerShooter.GetPreviewRange(hovered, IsVisibleToPlayer(hovered));
            return true;
        }

        /// <summary>
        /// 悬停格对玩家（阵营 0）当前是否可见（无迷雾）。走统一可见性链：
        /// 有效山格永久可见 → 临时 lease → 后勤连通 → 探索位回落；解析器缺失时回落到 IsExplored。
        /// </summary>
        private bool IsVisibleToPlayer(HexCellData cell)
        {
            if (cell == null) return false;
            if (_visibilityResolver != null) return _visibilityResolver.IsVisibleToFaction(cell, PlayerFactionId);
            return cell.IsExplored;
        }

        // ------------------------------------------------ 重建 ------------------------------------------------

        private void ResetDirty()
        {
            _lastCardRef = null;
            _lastHoverHex = NoHex;
            _lastRadius = -1;
            _lastValid = false;
        }

        private void Rebuild()
        {
            _hasActiveMask = true;

            if (_camera == null || _targetCanvas == null || _mapData == null)
            {
                ClearGraphics();
                return;
            }

            if (!TryResolve(out CardRangeMaskSettings settings, out int radius, out bool valid))
            {
                ClearGraphics();
                return;
            }
            _active = settings;

            HexCellData hovered = _inputHandler.DraggingHoveredCell;
            if (hovered == null)
            {
                ClearGraphics();
                return;
            }

            float outerRadius = _config != null ? _config.OuterRadius : 3f;
            float elevationStep = _config != null ? _config.elevationStep : 3f;

            RectTransform canvasRect = _targetCanvas.transform as RectTransform;
            Camera uiCam = _targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _targetCanvas.worldCamera;

            // n 环枚举：与结算/索敌共用同一份 HexRange.CollectInRange + 同一份半径。
            HexRange.CollectInRange(_mapData, hovered, radius, _rangeCells);
            if (_rangeCells.Count == 0)
            {
                ClearGraphics();
                return;
            }

            Color fill = valid ? settings.FillColor : settings.InvalidFillColor;
            Color stroke = valid ? settings.StrokeColor : settings.InvalidStrokeColor;

            BuildMask(
                _rangeCells, _fillGraphic, _strokeGraphic, fill, stroke, settings.Shadow,
                outerRadius, elevationStep, canvasRect, uiCam, settings);
        }

        /// <summary>对一组格构建填充 + 描边两层（几何同源，见 PlacementRangeMaskUI 类注释）。</summary>
        private void BuildMask(
            List<HexCellData> cells,
            PlacementRangeMaskGraphic fillGraphic, PlacementRangeMaskGraphic strokeGraphic,
            Color fillColor, Color strokeColor, PlacementMaskShadowSettings shadow,
            float outerRadius, float elevationStep,
            RectTransform canvasRect, Camera uiCam, CardRangeMaskSettings settings)
        {
            if (cells == null || cells.Count == 0)
            {
                fillGraphic.ClearMesh();
                strokeGraphic.ClearMesh();
                return;
            }

            PlacementMaskTopology.Topology topo =
                PlacementMaskTopology.Build(cells, outerRadius, elevationStep);

            PrepareLoops(topo, outerRadius, canvasRect, uiCam, settings);

            BuildFillLayer(fillGraphic, fillColor);
            BuildStrokeLayer(strokeGraphic, strokeColor, shadow, settings);
        }

        /// <summary>每条边界闭环 → 世界空间简化 + 圆角 → 投屏 → 去重，供两层共用。</summary>
        private void PrepareLoops(
            PlacementMaskTopology.Topology topo, float outerRadius,
            RectTransform canvasRect, Camera uiCam, CardRangeMaskSettings settings)
        {
            ReleaseLoops();

            float epsilon = settings.SimplifyEpsilonInR * outerRadius;
            float cornerRadius = settings.CornerRadiusInR * outerRadius;

            foreach (List<int> loop in topo.Loops)
            {
                _loopWorld.Clear();
                for (int i = 0; i < loop.Count; i++)
                    _loopWorld.Add(topo.CornerWorld[loop[i]]);
                if (_loopWorld.Count < 3) continue;

                PlacementMaskOutline.SimplifyClosed(_loopWorld, epsilon, _loopSimplified);
                PlacementMaskOutline.RoundCorners(
                    _loopSimplified, cornerRadius, settings.CornerSegments, _loopRounded);

                _loopLocal.Clear();
                bool ok = true;
                for (int i = 0; i < _loopRounded.Count; i++)
                {
                    if (!TryProjectLocal(_loopRounded[i], canvasRect, uiCam, out Vector2 local))
                    { ok = false; break; }
                    _loopLocal.Add(local);
                }
                if (!ok || _loopLocal.Count < 3) continue;

                List<Vector2> dedup = RentLoop();
                PlacementMaskOutline.DedupClosed(_loopLocal, settings.MergeEpsilonLocal, dedup);
                if (dedup.Count < 3) { _loopPool.Add(dedup); continue; }
                _loops.Add(dedup);
            }
        }

        private void BuildFillLayer(PlacementRangeMaskGraphic fillGraphic, Color fillColor)
        {
            _fill.Triangulate(_loops, _fillVerts, _fillTris);
            fillGraphic.color = fillColor;
            fillGraphic.SetMesh(_fillVerts, _fillTris);
        }

        private void BuildStrokeLayer(
            PlacementRangeMaskGraphic strokeGraphic, Color strokeColor,
            PlacementMaskShadowSettings shadow, CardRangeMaskSettings settings)
        {
            _strokeVerts.Clear();
            _strokeColors.Clear();
            _strokeTris.Clear();

            if (shadow != null && shadow.Offset > 0f)
            {
                float rad = shadow.DirDeg * Mathf.Deg2Rad;
                Vector2 shift = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * shadow.Offset;

                for (int i = 0; i < _loops.Count; i++)
                {
                    List<Vector2> src = _loops[i];
                    _shadowLoop.Clear();
                    for (int k = 0; k < src.Count; k++) _shadowLoop.Add(src[k] + shift);

                    PlacementMaskOutline.BuildRibbon(
                        _shadowLoop, settings.StrokeHalfWidth, shadow.Tint,
                        _strokeVerts, _strokeColors, _strokeTris,
                        settings.StrokeCoreRatio);
                }
            }

            for (int i = 0; i < _loops.Count; i++)
            {
                PlacementMaskOutline.BuildRibbon(
                    _loops[i], settings.StrokeHalfWidth, strokeColor,
                    _strokeVerts, _strokeColors, _strokeTris,
                    settings.StrokeCoreRatio);
            }

            strokeGraphic.color = Color.white; // 逐顶点色生效时不参与，留白避免二次染色
            strokeGraphic.SetMesh(_strokeVerts, _strokeTris, _strokeColors);
        }

        private List<Vector2> RentLoop()
        {
            int last = _loopPool.Count - 1;
            if (last < 0) return new List<Vector2>();
            List<Vector2> l = _loopPool[last];
            _loopPool.RemoveAt(last);
            l.Clear();
            return l;
        }

        private void ReleaseLoops()
        {
            for (int i = 0; i < _loops.Count; i++) _loopPool.Add(_loops[i]);
            _loops.Clear();
        }

        // ---------------- 投影与坐标换算 ----------------

        private bool TryProjectLocal(
            Vector3 world, RectTransform canvasRect, Camera uiCam, out Vector2 local)
        {
            Vector3 screen = _camera.WorldToScreenPoint(world);
            if (screen.z < 0f) { local = default; return false; }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screen, uiCam, out local);
        }

        // ---------------- 相机脏检测 ----------------

        private bool CameraMovedBeyondThreshold(CardRangeMaskSettings settings)
        {
            if (_camera == null) return false;
            Transform t = _camera.transform;
            if ((t.position - _lastCamPos).sqrMagnitude > settings.CamMoveSqrThreshold) return true;
            if (Quaternion.Angle(t.rotation, _lastCamRot) > settings.CamRotThresholdDeg) return true;
            return false;
        }

        private void CacheCameraTransform()
        {
            if (_camera == null) return;
            _lastCamPos = _camera.transform.position;
            _lastCamRot = _camera.transform.rotation;
        }

        // ---------------- 图形层 ----------------

        private void BuildRoot()
        {
            var go = new GameObject("CardDragRangeMaskRoot");
            _maskRoot = go.AddComponent<RectTransform>();
            Transform parent = _targetCanvas != null ? _targetCanvas.transform : null;
            go.transform.SetParent(parent, false);
            StretchFull(_maskRoot);

            // 渲染顺序：排在放置遮罩（红/绿）之后 = sibling 更靠后 = 渲染在其上。
            // 放置遮罩把自己 SetAsFirstSibling（最底），本层找它并按名插到它后面。
            // 两者通常不同屏（拖拽态 vs 提起态互斥），顺序只为将来两态被打通时确定性。
            Transform placement = parent != null ? parent.Find("PlacementRangeMaskRoot") : null;
            if (placement != null)
                _maskRoot.SetSiblingIndex(placement.GetSiblingIndex() + 1);
            else
                _maskRoot.SetAsFirstSibling();

            // 填充先建、描边后建：sibling 顺序即渲染顺序，描边必须压在填充之上。
            _fillGraphic = CreateGraphic("CardDragRangeMask_Fill");
            _strokeGraphic = CreateGraphic("CardDragRangeMask_Stroke");
        }

        private PlacementRangeMaskGraphic CreateGraphic(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_maskRoot, false);
            StretchFull(go.AddComponent<RectTransform>());
            // 显式补 CanvasRenderer：本工程（Unity 2022.3）AddComponent 建 Graphic 子类不隐式补，
            // 缺了它 Graphic.Rebuild() 首行即早退、永不出 mesh 且不报错（见 UITrailRenderer.cs:269-276）。
            go.AddComponent<CanvasRenderer>();
            var g = go.AddComponent<PlacementRangeMaskGraphic>();
            g.raycastTarget = false; // 遮罩绝不能吞指针事件，否则松手放置直接失效
            return g;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private void ClearGraphics()
        {
            if (_fillGraphic != null) _fillGraphic.ClearMesh();
            if (_strokeGraphic != null) _strokeGraphic.ClearMesh();
        }
    }
}
