using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace UI.PlacementMask
{
    //****************************************
    // 提起态·不可放置（红）+ 可放置（绿）双遮罩 · 主控组件（屏幕空间 UI 遮罩）。
    //
    // 【路线三·拓扑级并集轮廓】流程（红/绿各走一遍，共享同一刷新触发与全部拟合参数；
    //  仅配色与「立体感阴影线」参数红绿各持一份）：
    //   1) 读 PlayerInputHandler.RaisedUnplaceableCells / RaisedPlaceableCells（不重写放置判定）。
    //      不可放置快照已把整片山脉算入：CanBuildOnCell/CanSpawnUnitOnCell 拒绝非清除山格，
    //      故山脉自然落入不可放置集，这里**不再过滤山格**。
    //      （可放置与不可放置是同一二分的互补：一次遍历同时重算，值互斥、并集=全图。）
    //   2) PlacementMaskTopology.Build：立方坐标角点身份 → 去重顶点 + 边界闭环；
    //   3) 每条闭环：世界空间简化 + 圆角 → 投屏 → 去重，得到一批「处理后闭环」；
    //   4) 填充层与描边层**都吃这同一批闭环**：填充走扫描线偶奇填充，描边走羽化缎带。
    //
    // 【填充与描边必须同源】
    // 早期填充走的是 topo 的原始六边形角点逐格扇形三角化，与描边那条「简化+圆角」后的路径
    // 是两套几何：凹口处描边被切到填充之外 → 线内侧露白；凸角处圆角切角 → 填充溢出线外。
    // 偏移量可达大半个格，远超描边半宽，调粗线盖不住。故填充改为共用处理后闭环
    //（PlacementMaskFill），两层逐点重合，任何拟合参数下都严丝合缝。
    //
    // 视觉分工：红为全图口径（582/598 格几乎铺满屏），内部填充压得淡、高 alpha 会压抑
    // （真机标定后取 0.55），对比度全押在边界描边上 → 读作「圈出一个区域」而非「盖了层脏色」。
    // 绿为可放置格（一般是地图上的孤岛/散块，格数少），配色略亮但同属「低底 + 强调边」口径。
    //
    // 刷新策略：透视相机，卡引用 / 快照数量 / 相机变换任一变化才重建。
    // 两套遮罩共用同一次重建：快照与触发完全同源，避免两个组件各维护一套脏检测而错帧。
    //****************************************
    public sealed class PlacementRangeMaskUI : MonoBehaviour
    {
        [Inject] private PlayerInputHandler _inputHandler;
        [Inject] private MapGenerationConfigSO _config;
        [Inject(Id = "TargetUICanvas")] private Canvas _targetCanvas;

        // ---- 表现参数（真机可再标定，红值已标定保持不动）----
        // 全部参数收进 PlacementRangeMaskSettings，由 GameInstaller 的序列化字段注入：
        // 本组件是运行时新建对象，自身 Inspector 不随场景保存、也无法在 Play 之前调。
        // 未注入时（单测 / 手动 AddComponent）回落到 Settings 的默认值 = 收编前的原常量值。
        private PlacementRangeMaskSettings _settings = new PlacementRangeMaskSettings();

        /// <summary>
        /// 表现参数（配色 / 描边形状 / 重建阈值 / 红绿显示开关）。
        /// 赋值即时生效：立刻重建当前遮罩，不必等相机移动触发脏检测。
        /// 由 GameInstaller 在组件实例化后注入；运行时也可再改。
        /// </summary>
        public PlacementRangeMaskSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value ?? new PlacementRangeMaskSettings();
                if (_hasActiveMask) Rebuild();
            }
        }

        /// <summary>【红·不可放置遮罩】显示开关。赋值即时生效（立刻清 mesh / 重建）。</summary>
        public bool ShowUnplaceableMask
        {
            get => _settings.ShowUnplaceableMask;
            set => SetMaskVisible(ref _settings.ShowUnplaceableMask, value);
        }

        /// <summary>【绿·可放置遮罩】显示开关。语义同 <see cref="ShowUnplaceableMask"/>。</summary>
        public bool ShowPlaceableMask
        {
            get => _settings.ShowPlaceableMask;
            set => SetMaskVisible(ref _settings.ShowPlaceableMask, value);
        }

        private void SetMaskVisible(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            // 关 → 立刻清掉该层已有 mesh；开 → 下一次 Rebuild 补上。
            // 直接重建而非等脏检测：脏标记只看卡引用/快照数/相机，开关变化不在其中。
            if (_hasActiveMask) Rebuild();
        }

        private Camera _camera;
        private RectTransform _maskRoot;
        private PlacementRangeMaskGraphic _fillGraphic;
        private PlacementRangeMaskGraphic _strokeGraphic;
        private PlacementRangeMaskGraphic _placeableFillGraphic;
        private PlacementRangeMaskGraphic _placeableStrokeGraphic;

        // 提起态 / 相机变化的重建脏标记来源
        private object _lastRaisedCardRef;
        private int _lastUnplaceableCount = -1;
        private Vector3 _lastCamPos;
        private Quaternion _lastCamRot;
        private bool _hasActiveMask;

        // 复用缓冲，避免每次重建产生 GC（提起态下相机每动就重建一次）。
        // 红/绿两次 BuildMaskForSet 依次复用：SetMesh 内部拷贝顶点，缓冲用完即弃，安全。
        private readonly List<Vector3> _loopWorld = new List<Vector3>();
        private readonly List<Vector3> _loopSimplified = new List<Vector3>();
        private readonly List<Vector3> _loopRounded = new List<Vector3>();
        private readonly List<Vector2> _loopLocal = new List<Vector2>();
        private readonly List<Vector2> _strokeVerts = new List<Vector2>();
        private readonly List<Color32> _strokeColors = new List<Color32>();
        private readonly List<int> _strokeTris = new List<int>();
        private readonly List<Vector2> _fillVerts = new List<Vector2>();
        private readonly List<int> _fillTris = new List<int>();
        // 阴影线的平移副本：每条环临时填一次，喂给 BuildRibbon 后即弃（复用避免每环分配）。
        private readonly List<Vector2> _shadowLoop = new List<Vector2>();

        // 处理后闭环（简化+圆角+投屏+去重）：填充与描边的共同输入。
        // 池化复用内层 List，避免每帧为每条环各分配一个 List。
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

            CardController raised = CardController.ActiveRaisedCard;

            // 无提起卡 → 清空并复位脏标记
            if (raised == null)
            {
                if (_hasActiveMask)
                {
                    ClearGraphics();
                    _hasActiveMask = false;
                }
                _lastRaisedCardRef = null;
                _lastUnplaceableCount = -1;
                return;
            }

            // PlayerInputHandler（ITickable）与本组件（MonoBehaviour.Update）同帧顺序不保证：
            // 提起卡变化那一帧本组件可能先跑，读到上一帧的空快照。因此脏检测不能只看卡引用，
            // 必须叠加「快照数量变化」，让 0→N 的那一帧补上重建。
            IReadOnlyList<HexCellData> snapshot = _inputHandler?.RaisedUnplaceableCells;
            int unplaceableCount = snapshot?.Count ?? 0;

            bool raisedChanged = !ReferenceEquals(raised, _lastRaisedCardRef);
            bool snapshotChanged = unplaceableCount != _lastUnplaceableCount;
            bool cameraChanged = CameraMovedBeyondThreshold();

            if (raisedChanged || snapshotChanged || cameraChanged || !_hasActiveMask)
            {
                _lastRaisedCardRef = raised;
                _lastUnplaceableCount = unplaceableCount;
                CacheCameraTransform();
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _hasActiveMask = true;

            if (_camera == null || _targetCanvas == null)
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

            // 红（不可放置）与绿（可放置）是同一快照二分的互补，走同一套拓扑/拟合/投屏流水线，
            // 只有配色不同。快照本身已是「不可放置」全图口径（含非清除山脉），这里不再过滤山格，
            // 让整片山脉被红色遮罩覆盖（扑灭/清除山后该格回到可放置集，自然不会出现）。
            // 开关关闭 → 传 null，BuildMaskForSet 走清空分支（该层清 mesh 且跳过拓扑/投屏开销）。
            BuildMaskForSet(
                _settings.ShowUnplaceableMask ? _inputHandler?.RaisedUnplaceableCells : null,
                _fillGraphic, _strokeGraphic,
                _settings.UnplaceableFillColor, _settings.UnplaceableStrokeColor,
                _settings.UnplaceableShadow,
                outerRadius, elevationStep, canvasRect, uiCam);
            BuildMaskForSet(
                _settings.ShowPlaceableMask ? _inputHandler?.RaisedPlaceableCells : null,
                _placeableFillGraphic, _placeableStrokeGraphic,
                _settings.PlaceableFillColor, _settings.PlaceableStrokeColor,
                _settings.PlaceableShadow,
                outerRadius, elevationStep, canvasRect, uiCam);
        }

        /// <summary>
        /// 对一组格（不可放置 / 可放置各一份）构建填充 + 描边两层。
        /// 先把边界闭环拟合并投屏一次，再喂给两层——两层几何同源是本方案的关键（见类注释）。
        /// 红/绿两次调用依次复用同一批缓冲是安全的（SetMesh 内部拷贝）。
        /// </summary>
        private void BuildMaskForSet(
            IReadOnlyList<HexCellData> cells,
            PlacementRangeMaskGraphic fillGraphic, PlacementRangeMaskGraphic strokeGraphic,
            Color fillColor, Color strokeColor, PlacementMaskShadowSettings shadow,
            float outerRadius, float elevationStep,
            RectTransform canvasRect, Camera uiCam)
        {
            if (cells == null || cells.Count == 0)
            {
                fillGraphic.ClearMesh();
                strokeGraphic.ClearMesh();
                return;
            }

            PlacementMaskTopology.Topology topo =
                PlacementMaskTopology.Build(cells, outerRadius, elevationStep);

            PrepareLoops(topo, outerRadius, canvasRect, uiCam);

            BuildFillLayer(fillGraphic, fillColor);
            BuildStrokeLayer(strokeGraphic, strokeColor, shadow);
        }

        /// <summary>
        /// 每条边界闭环 → 世界空间简化 + 圆角 → 投屏 → 去重，结果存进 _loops 供两层共用。
        ///
        /// 简化/圆角刻意放在**投屏前的世界空间**：容差以 R 为单位有稳定几何含义，
        /// 且推拉相机时轮廓形状不变（屏幕空间做的话，缩放会改变简化力度）。
        /// </summary>
        private void PrepareLoops(
            PlacementMaskTopology.Topology topo, float outerRadius,
            RectTransform canvasRect, Camera uiCam)
        {
            ReleaseLoops();

            float epsilon = _settings.SimplifyEpsilonInR * outerRadius;
            float cornerRadius = _settings.CornerRadiusInR * outerRadius;

            foreach (List<int> loop in topo.Loops)
            {
                _loopWorld.Clear();
                for (int i = 0; i < loop.Count; i++)
                    _loopWorld.Add(topo.CornerWorld[loop[i]]);
                if (_loopWorld.Count < 3) continue;

                PlacementMaskOutline.SimplifyClosed(_loopWorld, epsilon, _loopSimplified);
                PlacementMaskOutline.RoundCorners(
                    _loopSimplified, cornerRadius, _settings.CornerSegments, _loopRounded);

                // 环上任一点投屏失败就整环作废：拆环会连出跨屏的错误线段，
                // 填充那边还会因残缺环把嵌套判定算错。
                _loopLocal.Clear();
                bool ok = true;
                for (int i = 0; i < _loopRounded.Count; i++)
                {
                    if (!TryProjectLocal(_loopRounded[i], canvasRect, uiCam, out Vector2 local))
                    { ok = false; break; }
                    _loopLocal.Add(local);
                }
                if (!ok || _loopLocal.Count < 3) continue;

                // 圆角相接处（切点被夹到边中点时）会产生重合点：缎带在那里退化出尖刺，
                // 故必须去重。（填充侧的扫描线对重复点免疫，这条只为描边。）
                List<Vector2> dedup = RentLoop();
                PlacementMaskOutline.DedupClosed(_loopLocal, _settings.MergeEpsilonLocal, dedup);
                if (dedup.Count < 3) { _loopPool.Add(dedup); continue; }
                _loops.Add(dedup);
            }
        }

        /// <summary>
        /// 填充层：与描边同一批闭环，扫描线 + 偶奇规则。洞（被包围的异色孤岛）真的挖空。
        /// 边界与描边中线逐点重合，故描边内侧不会再露白、也不会有填充溢出到线外。
        /// </summary>
        private void BuildFillLayer(PlacementRangeMaskGraphic fillGraphic, Color fillColor)
        {
            _fill.Triangulate(_loops, _fillVerts, _fillTris);
            fillGraphic.color = fillColor;
            fillGraphic.SetMesh(_fillVerts, _fillTris);
        }

        /// <summary>
        /// 描边层：每条闭环一条羽化缎带，所有环合并进一张 mesh。
        ///
        /// 【立体感·方案 A】shadow.Offset > 0 时，先把同一批路径整体平移一段距离、用深色画一遍，
        /// 再画主线。两者共用**同一张 mesh**：UGUI 单张 mesh 内三角按提交顺序绘制、无深度测试，
        /// 故先提交的阴影自然压在主线下面 —— 不需要额外的 Graphic，drawcall 不变。
        ///
        /// 阴影参数由调用方按红/绿分别传入（PlacementMaskShadowSettings），不再共用一份：
        /// 红是连片大区域、要厚重感，绿多是零散小岛、同样偏移会把小块压暗。
        ///
        /// ⚠️ 平移是**整体**的：朝下的边界外侧探出深带（想要的厚度感），而朝上的边界会在
        /// 区域**内侧**同样探出一条深带（物理上说不通的重影）。偏移小时读作厚度，给大值必然穿帮。
        /// 真需要明显的「墙」时应改为按 dot(外法线, 光照方向) 调制墙带宽度的方案。
        /// </summary>
        private void BuildStrokeLayer(
            PlacementRangeMaskGraphic strokeGraphic, Color strokeColor,
            PlacementMaskShadowSettings shadow)
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
                        _shadowLoop, _settings.StrokeHalfWidth, shadow.Tint,
                        _strokeVerts, _strokeColors, _strokeTris,
                        _settings.StrokeCoreRatio);
                }
            }

            for (int i = 0; i < _loops.Count; i++)
            {
                PlacementMaskOutline.BuildRibbon(
                    _loops[i], _settings.StrokeHalfWidth, strokeColor,
                    _strokeVerts, _strokeColors, _strokeTris,
                    _settings.StrokeCoreRatio);
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

        private bool CameraMovedBeyondThreshold()
        {
            if (_camera == null) return false;
            Transform t = _camera.transform;
            if ((t.position - _lastCamPos).sqrMagnitude > _settings.CamMoveSqrThreshold) return true;
            if (Quaternion.Angle(t.rotation, _lastCamRot) > _settings.CamRotThresholdDeg) return true;
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
            var go = new GameObject("PlacementRangeMaskRoot");
            _maskRoot = go.AddComponent<RectTransform>();
            go.transform.SetParent(_targetCanvas != null ? _targetCanvas.transform : null, false);
            StretchFull(_maskRoot);
            // 排在业务 UI 之下、地图之上：作为第一个 sibling（渲染顺序靠前 = 被后续 UI 覆盖）。
            _maskRoot.SetAsFirstSibling();

            // 填充先建、描边后建：sibling 顺序即渲染顺序，描边必须压在填充之上。
            // 红（不可放置）先、绿（可放置）后：两者是快照二分互补、互不重叠，顺序只影响 z 序。
            _fillGraphic = CreateGraphic("PlacementMask_Fill");
            _strokeGraphic = CreateGraphic("PlacementMask_Stroke");
            _placeableFillGraphic = CreateGraphic("PlacementMask_Fill_Green");
            _placeableStrokeGraphic = CreateGraphic("PlacementMask_Stroke_Green");
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
            g.raycastTarget = false; // 遮罩不吞点击，放置/收起卡牌交互不受影响
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
            if (_placeableFillGraphic != null) _placeableFillGraphic.ClearMesh();
            if (_placeableStrokeGraphic != null) _placeableStrokeGraphic.ClearMesh();
        }
    }
}
