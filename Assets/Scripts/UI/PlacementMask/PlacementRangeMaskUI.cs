using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace UI.PlacementMask
{
    //****************************************
    // 不可放置区域红色遮罩 · 主控组件（屏幕空间 UI 遮罩，实现方案 §4/§5）。
    //
    // 流程（每次「提起卡变化」或「相机变换」触发重建）：
    //   1) 读取 PlayerInputHandler.RaisedUnplaceableCells（不重写放置判定）；
    //   2) 过滤有效山格（MountainCellRule.IsEffectiveMountainCell，对齐现有高亮门禁）；
    //   3) 按六方向邻接分连通区域（方向索引 0..5，不含 None）；
    //   4) 每区域用邻接边界求世界空间外轮廓 → Catmull-Rom 平滑 → 去自交预警 → Ear Clipping；
    //   5) 轮廓顶点 Camera.main.WorldToScreenPoint（剔除 z<0 背面点）→ Canvas 本地坐标；
    //   6) 每区域一张 PlacementRangeMaskGraphic 半透明红多边形。
    //
    // 刷新策略：透视相机，监听 Camera.main.transform（position + rotation）超阈值才重建，
    //   避免每帧重投影；卡牌落下/拖拽/无提起卡时清空。
    //****************************************
    public sealed class PlacementRangeMaskUI : MonoBehaviour
    {
        [Inject] private PlayerInputHandler _inputHandler;
        [Inject] private IMapDataService _mapData;
        [Inject] private MapGenerationConfigSO _config;
        [Inject(Id = "TargetUICanvas")] private Canvas _targetCanvas;

        // ---- 诊断日志开关（定位遮罩不显示问题；排查完可关）----
        private const bool VerboseLog = true;

        // ---- 表现参数（真机可再标定，见方案 §4.2/§7）----
        private static readonly Color MaskColor = new Color(1.0f, 0.15f, 0.12f, 0.30f);

        // 相机变换刷新阈值：位移平方阈值 + 旋转角度阈值。
        private const float CamMoveSqrThreshold = 0.01f;
        private const float CamRotThresholdDeg = 0.1f;

        private Camera _camera;
        private RectTransform _maskRoot;
        private readonly List<PlacementRangeMaskGraphic> _pool = new List<PlacementRangeMaskGraphic>();

        // 提起态 / 相机变化的重建脏标记来源
        private object _lastRaisedCardRef;
        private int _lastUnplaceableCount = -1; // 上次快照的不可放置格数量（内容变化检测，规避 Tick 顺序早一帧读空快照）
        private Vector3 _lastCamPos;
        private Quaternion _lastCamRot;
        private bool _hasActiveMask;

        private void Awake()
        {
            BuildRoot();
            if (VerboseLog)
                Debug.Log($"[遮罩] Awake：组件已创建。inputHandler={( _inputHandler != null ? "有" : "NULL")} " +
                          $"mapData={(_mapData != null ? "有" : "NULL")} config={(_config != null ? "有" : "NULL")} " +
                          $"targetCanvas={(_targetCanvas != null ? _targetCanvas.name : "NULL")} " +
                          $"canvasRenderMode={(_targetCanvas != null ? _targetCanvas.renderMode.ToString() : "?")}", this);
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
                    if (VerboseLog) Debug.Log("[遮罩] Update：无提起卡 → 清空遮罩。", this);
                    ClearAllGraphics();
                    _hasActiveMask = false;
                }
                _lastRaisedCardRef = null;
                _lastUnplaceableCount = -1;
                return;
            }

            // 当前快照的不可放置格数量（PlayerInputHandler 的快照可能晚本组件一帧才填上，
            // 因此不能只靠"卡引用变化"触发——改用"引用变化 OR 快照数量变化"，
            // 保证快照从 0 变非 0 的那一帧能补上重建）。
            IReadOnlyList<HexCellData> snapshot = _inputHandler?.RaisedUnplaceableCells;
            int unplaceableCount = snapshot?.Count ?? 0;

            bool raisedChanged = !ReferenceEquals(raised, _lastRaisedCardRef);
            bool snapshotChanged = unplaceableCount != _lastUnplaceableCount;
            bool cameraChanged = CameraMovedBeyondThreshold();

            if (raisedChanged || snapshotChanged || cameraChanged || !_hasActiveMask)
            {
                if (VerboseLog)
                    Debug.Log($"[遮罩] Update：触发重建。raisedChanged={raisedChanged} snapshotChanged={snapshotChanged}" +
                              $"(count {_lastUnplaceableCount}→{unplaceableCount}) cameraChanged={cameraChanged} " +
                              $"hasActiveMask={_hasActiveMask} camera={(_camera != null ? _camera.name : "NULL")}", this);
                _lastRaisedCardRef = raised;
                _lastUnplaceableCount = unplaceableCount;
                CacheCameraTransform();
                Rebuild();
            }
        }

        private void Rebuild()
        {
            ClearAllGraphics();
            _hasActiveMask = true;

            if (_camera == null || _targetCanvas == null)
            {
                if (VerboseLog) Debug.LogWarning($"[遮罩] Rebuild 早退：camera={(_camera != null ? "有" : "NULL")} " +
                                                 $"targetCanvas={(_targetCanvas != null ? "有" : "NULL")}", this);
                return;
            }

            IReadOnlyList<HexCellData> unplaceable = _inputHandler?.RaisedUnplaceableCells;
            if (unplaceable == null || unplaceable.Count == 0)
            {
                if (VerboseLog) Debug.LogWarning($"[遮罩] Rebuild 早退：RaisedUnplaceableCells " +
                                                 $"{(unplaceable == null ? "为 NULL" : "为空(count=0)")}。" +
                                                 $"（是否真的处于提起态？PlayerInputHandler 是否本帧已算过快照？）", this);
                return;
            }

            // 2) 过滤有效山格（对齐现有高亮门禁；RaisedUnplaceableCells 本身不过滤）。
            var filtered = new List<HexCellData>(unplaceable.Count);
            foreach (HexCellData cell in unplaceable)
            {
                if (cell == null) continue;
                if (MountainCellRule.IsEffectiveMountainCell(cell)) continue;
                filtered.Add(cell);
            }
            if (VerboseLog) Debug.Log($"[遮罩] Rebuild：不可放置格 原始={unplaceable.Count} 过滤山格后={filtered.Count}", this);
            if (filtered.Count == 0)
            {
                if (VerboseLog) Debug.LogWarning("[遮罩] Rebuild 早退：过滤山格后无剩余格。", this);
                return;
            }

            // 3) 连通分组
            System.Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf =
                (c, d) => _mapData.GetNeighbor(c, d);
            List<PlacementMaskGeometry.Region> regions =
                PlacementMaskGeometry.GroupIntoRegions(filtered, neighborOf);

            float outerRadius = _config != null ? _config.OuterRadius : 3f;

            RectTransform canvasRect = _targetCanvas.transform as RectTransform;
            Camera uiCam = _targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _targetCanvas.worldCamera;

            if (VerboseLog) Debug.Log($"[遮罩] Rebuild：分组数={regions.Count} outerRadius={outerRadius} " +
                                      $"uiCam={(uiCam != null ? uiCam.name : "null(Overlay)")}", this);

            int graphicIndex = 0;
            int totalCells = 0, totalTris = 0, dropBackface = 0, committedRegions = 0;

            var ring = new List<Vector3>(6);

            foreach (var region in regions)
            {
                // 【逐格扇形直填】每格 6 个三角形（中心 + 相邻两角点），投屏后合并进本区域一张 mesh。
                // 放弃脆弱的「求整体外轮廓」——大面积/有洞/T型交叉时串环易碎（35 碎环全三角化失败）。
                // 相邻格三角形自然拼满过渡区；重叠区半透明同色叠加不影响观感。
                var localVerts = new List<Vector2>(region.Cells.Count * 7);
                var tris = new List<int>(region.Cells.Count * 18);

                foreach (HexCellData cell in region.Cells)
                {
                    totalCells++;
                    PlacementMaskGeometry.GetCellRingWorld(cell, outerRadius, ring);

                    // 中心点 + 6 角点投屏
                    if (!ProjectPoint(cell.RealCenterWorldCoordinate, canvasRect, uiCam, out Vector2 centerLocal))
                    { dropBackface++; continue; }

                    // 投影 6 个角点；任一背面则整格跳过（单格小，跳过无碍）
                    bool ok = true;
                    Vector2[] corners = new Vector2[6];
                    for (int i = 0; i < 6; i++)
                    {
                        if (!ProjectPoint(ring[i], canvasRect, uiCam, out corners[i])) { ok = false; break; }
                    }
                    if (!ok) { dropBackface++; continue; }

                    int baseIdx = localVerts.Count;
                    localVerts.Add(centerLocal);       // baseIdx + 0 = 中心
                    for (int i = 0; i < 6; i++)
                        localVerts.Add(corners[i]);    // baseIdx + 1..6 = 角点

                    for (int i = 0; i < 6; i++)
                    {
                        int a = baseIdx + 1 + i;
                        int b = baseIdx + 1 + (i + 1) % 6;
                        tris.Add(baseIdx); // 中心
                        tris.Add(a);
                        tris.Add(b);
                        totalTris++;
                    }
                }

                if (localVerts.Count >= 3 && tris.Count >= 3)
                {
                    PlacementRangeMaskGraphic g = GetOrCreateGraphic(graphicIndex++);
                    g.SetMesh(localVerts, tris);
                    committedRegions++;
                }
            }

            if (VerboseLog)
                Debug.Log($"[遮罩] Rebuild 完成：区域={regions.Count} 提交区域={committedRegions} " +
                          $"格数={totalCells} 三角形={totalTris} 背面跳过格={dropBackface} " +
                          $"| 图形对象={_pool.Count}", this);

            // 回收多余图形
            for (int i = graphicIndex; i < _pool.Count; i++)
                _pool[i].ClearMesh();
        }

        // ---------------- 投影与坐标换算 ----------------

        /// <summary>单个世界点 → Canvas 本地坐标。相机背后(z<0)返回 false。</summary>
        private bool ProjectPoint(Vector3 world, RectTransform canvasRect, Camera uiCam, out Vector2 local)
        {
            local = default;
            Vector3 screen = _camera.WorldToScreenPoint(world);
            if (screen.z < 0f) return false; // 背面点
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screen, uiCam, out local);
        }

        // ---------------- 相机脏检测 ----------------

        private bool CameraMovedBeyondThreshold()
        {
            if (_camera == null) return false;
            Transform t = _camera.transform;
            if ((t.position - _lastCamPos).sqrMagnitude > CamMoveSqrThreshold) return true;
            if (Quaternion.Angle(t.rotation, _lastCamRot) > CamRotThresholdDeg) return true;
            return false;
        }

        private void CacheCameraTransform()
        {
            if (_camera == null) return;
            _lastCamPos = _camera.transform.position;
            _lastCamRot = _camera.transform.rotation;
        }

        // ---------------- 图形对象池 ----------------

        private void BuildRoot()
        {
            var go = new GameObject("PlacementRangeMaskRoot");
            _maskRoot = go.AddComponent<RectTransform>();
            go.transform.SetParent(_targetCanvas != null ? _targetCanvas.transform : null, false);
            _maskRoot.anchorMin = Vector2.zero;
            _maskRoot.anchorMax = Vector2.one;
            _maskRoot.offsetMin = Vector2.zero;
            _maskRoot.offsetMax = Vector2.zero;
            _maskRoot.pivot = new Vector2(0.5f, 0.5f);
            // 排在业务 UI 之下、地图之上：作为第一个 sibling（渲染顺序靠前 = 被后续 UI 覆盖）。
            _maskRoot.SetAsFirstSibling();

            if (VerboseLog)
                Debug.Log($"[遮罩] BuildRoot：PlacementRangeMaskRoot 已挂到 " +
                          $"{(_targetCanvas != null ? _targetCanvas.name : "NULL(未挂到任何 Canvas!)")}，" +
                          $"siblingIndex={_maskRoot.GetSiblingIndex()}", this);
        }

        private PlacementRangeMaskGraphic GetOrCreateGraphic(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject($"RegionMask_{_pool.Count}");
                go.transform.SetParent(_maskRoot, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                // 显式补 CanvasRenderer（本工程 Graphic 子类不隐式补，见 Graphic 组件注释）。
                go.AddComponent<CanvasRenderer>();
                var g = go.AddComponent<PlacementRangeMaskGraphic>();
                g.color = MaskColor;
                g.raycastTarget = false; // 遮罩不吞点击（放置/收起卡牌交互不受影响）
                _pool.Add(g);
                if (VerboseLog)
                    Debug.Log($"[遮罩] 新建图形对象 {go.name}：canvasRenderer={(g.GetComponent<CanvasRenderer>() != null ? "有" : "无(会永不出mesh!)")} " +
                              $"color={g.color}", this);
            }
            return _pool[index];
        }

        private void ClearAllGraphics()
        {
            for (int i = 0; i < _pool.Count; i++)
                _pool[i].ClearMesh();
        }
    }
}
