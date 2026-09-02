using DG.Tweening;
using UnityEngine;
using Zenject;

/// <summary>
/// 卡牌拖拽世界空间预览控制器（改造计划 §4.3）。
/// 持握阶段：实例由主相机直接渲染，逐帧（ITickable）射线地形、吸附到命中点上方固定高度；
/// 落位阶段：从释放悬停位置向生成后的实际位置播放纯视觉落位补间（unscaledTime 驱动，暂停也播完）。
/// 生命周期：Begin 持握 → Follow 更新触点 → ReleaseOwnership 交出实例（不销毁）→
/// PlayLanding 落位补间 → Cancel 销毁未交出实例；Dispose 收尾全部状态。
/// 所有方法按 token 校验，拒绝上一张卡的迟到回调。
/// </summary>
public class CardDragWorldPreviewController : ITickable, System.IDisposable
{
    /// <summary>最大缩放下屏幕上缘射线到地面约 160 世界单位，显式传 300 保证全屏可用。
    /// 【落点图标与连线计划 §3.2】统一使用 CardController.CardDragRaycastMaxDistance，
    /// 与高亮（PlayerInputHandler）和落点判定（CardController.OnEndDrag）同一射程。</summary>
    private const float RaycastMaxDistance = CardController.CardDragRaycastMaxDistance;

    private readonly IMapRaycastService _mapRaycast;
    private readonly GameLoop _gameLoop;

    // ── 持握状态 ──
    private GameObject _instance;
    private object _token;
    private Vector2 _lastPointer;
    private Vector3 _lastHoverPosition;
    private bool _hasPointer;

    private Transform _dragRoot;
    private Vector3 _originalLocalScale;
    private Animator[] _animators;

    // ── 拎起 scale-in（GameTime 驱动）──
    private float _appearDuration;
    private float _appearElapsed;

    // ── 落位补间（GameTime 驱动）──
    private Tween _landingDriver;
    private GameObject _landingInstance;
    private UnitBrainBase _landingBrain;
    private Vector3 _landingFrom;
    private Vector3 _landingTo;
    private float _landingDuration;
    private float _landingStartTime;
    private System.Action _landingOnComplete;

    // ── 落位拉伸（Squash & Stretch，方案 B：缩放视觉子节点而非 root）──
    private Transform _stretchVisualRoot;   // 视觉子节点（ResolveVisualRoot 定位；null = 不拉伸）
    private Vector3 _stretchBaseScale;      // 视觉子节点基线 localScale
    private float _stretchBaseLocalY;       // 视觉子节点基线 localPosition.y
    private float _stretchBottomLocalY;     // 视觉子节点局部空间底部 Y（底锚点，仅子节点时使用）
    private bool _stretchAnchorEnabled;     // 视觉根是实例子节点时做顶锚点补偿；等于实例根时仅缩放

    private bool _isDisposed;

    public CardDragWorldPreviewController(IMapRaycastService mapRaycast, GameLoop gameLoop)
    {
        _mapRaycast = mapRaycast;
        _gameLoop = gameLoop;
    }

    /// <summary>当前是否有活动持握（供调试与断言使用）。</summary>
    public bool IsActive => _instance != null && _token != null;

    /// <summary>
    /// 开始持握：把已 PrepareForDrag 的实例挂到拖拽根节点下，记录 prefab 原缩放，
    /// 立即按当前指针做一次初始吸附（未命中则置于地图下方的隐藏位），随后由 Tick 逐帧跟随。
    /// </summary>
    public void Begin(GameObject instance, object token)
    {
        if (_isDisposed || instance == null) return;

        // 上一段持握若未清理（异常路径），先销毁旧实例再开新的。
        if (_instance != null) DestroyHeldInstance();

        _token = token;
        _instance = instance;

        // 保持 prefab 自身的 root scale / rotation，不做任何覆盖
        // （建筑根缩放为 2.5 / (1.5,2,1.5)，禁止写 localScale = Vector3.one）。
        _originalLocalScale = instance.transform.localScale;

        EnsureDragRoot();
        instance.transform.SetParent(_dragRoot, false);

        _animators = instance.GetComponentsInChildren<Animator>(false);

        // 拎起 scale-in（0 = 不做，直接原缩放）。
        _appearDuration = Mathf.Max(0f, FeelConfigProvider.CardDragPreviewAppearDuration);
        _appearElapsed = 0f;
        if (_appearDuration <= 0f)
            instance.transform.localScale = _originalLocalScale;
        else
            instance.transform.localScale = Vector3.zero;

        // 初始位置：立即按当前指针吸附一次，避免实例在第一帧出现在根节点原点。
        _lastHoverPosition = new Vector3(0f, -500f, 0f);
        Follow(Input.mousePosition, token);
        TrySnapToTerrain();
    }

    /// <summary>更新最近触点（原始屏幕像素坐标）；射线由 Tick 每帧重算，内部统一走逻辑坐标。</summary>
    public void Follow(Vector2 pointerPosition, object token)
    {
        if (_isDisposed || _instance == null) return;
        if (token != null && !ReferenceEquals(token, _token)) return;

        _lastPointer = pointerPosition;
        _hasPointer = true;
    }

    /// <summary>
    /// 交出实例所有权（不销毁）：停止逐帧跟随并清空持握状态，返回实例供 SpawnUnit/SpawnBuilding 接线。
    /// token 不匹配或未持握时返回 null。
    /// </summary>
    public GameObject ReleaseOwnership(object token)
    {
        if (_isDisposed || _instance == null) return null;
        if (token != null && !ReferenceEquals(token, _token)) return null;

        GameObject instance = _instance;
        _instance = null;
        _token = null;
        _animators = null;

        // 拎起 scale-in 若未播完：直接回到 prefab 原缩放，落位后不再改缩放。
        if (instance != null)
            instance.transform.localScale = _originalLocalScale;

        return instance;
    }

    /// <summary>
    /// 播放落位补间（纯视觉）：终点取当前实际位置（生成后读，单位可能已被站位槽二次吸附）。
    /// 起点取终点正上方（X/Z 对齐终点、Y = 终点 Y + 悬停高度 + 延长落差），做纯垂直下落，
    /// 避免松手触点与目标槽位不在同一条竖线上造成的水平位移。
    /// 补间期间冻结单位 brain，补间结束恢复为 GameLoop.IsPaused（不无条件写 false，防止暂停中落地被解冻）。
    /// </summary>
    public void PlayLanding(GameObject instance, Vector3 fromPos, System.Action onComplete)
    {
        if (_isDisposed || instance == null) return;

        KillLandingDriver();

        // 方案一：纯垂直下落。起点取终点正上方，忽略松手触点 X/Z（屏幕空间触点不在地块正上方，
        // 用它会带进水平位移）；起始高度 = 终点 Y + 基础悬停高度 + 延长落差。
        // 终点仍读自「生成后的实际位置」，最终落点不变，只有下落距离被拉长。
        Vector3 endPos = instance.transform.position;
        float hoverHeight = Mathf.Max(0f, FeelConfigProvider.CardDragPreviewHoverHeight);
        Vector3 startPos = new Vector3(endPos.x, endPos.y + hoverHeight + LandingDropHeight, endPos.z);
        instance.transform.position = startPos;

        // 单位落地即注册了 brain，下一帧就可能决策并改写 transform.position；
        // 补间期间冻结，恢复时机见 FinishLanding。
        UnitBrainBase brain = instance.GetComponentInChildren<UnitBrainBase>(true);
        if (brain != null) brain.IsPaused = true;

        PrepareLandingStretch(instance);

        _landingInstance = instance;
        _landingBrain = brain;
        _landingFrom = startPos;
        _landingTo = endPos;
        _landingDuration = LandingDuration;
        _landingStartTime = Time.unscaledTime;
        _landingOnComplete = onComplete;

        // 进度由 unscaledTime 增量驱动（不受暂停/速度档位影响，暂停出牌也播完落位）；
        // driver 只承载每帧 OnUpdate；SetLoops(-1) 永不自然结束。
        _landingDriver = DOTween.To(() => 0f, _ => { }, 1f, LandingDuration)
            .SetEase(Ease.Linear)
            .SetLoops(-1)
            .OnUpdate(UpdateLanding);
    }

    /// <summary>销毁尚未交出所有权的实例（松手取消/无效/失焦路径）；token 不匹配时拒绝。</summary>
    public void Cancel(object token)
    {
        if (_isDisposed || _instance == null) return;
        if (token != null && !ReferenceEquals(token, _token)) return;

        DestroyHeldInstance();
    }

    /// <summary>
    /// 拖拽成功结束的收尾入口：幂等且不销毁。成功路径下实例所有权已在
    /// HandleCardDragEnd 内经 ReleaseOwnership 交出、落位补间继续由控制器驱动；
    /// 失败路径的销毁由随后的 Cancel 完成。仅保留迟到回调的 token 防护。
    /// </summary>
    public void End(object token)
    {
        // 无状态变更（幂等）：见类注释「生命周期」。
    }

    /// <summary>容器销毁：销毁未落位实例、终止补间 driver、清理拖拽根节点。</summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        DestroyHeldInstance();
        KillLandingDriver();

        if (_dragRoot != null)
        {
            Object.Destroy(_dragRoot);
            _dragRoot = null;
        }
    }

    /// <summary>
    /// 逐帧驱动（不受暂停影响，与摄像机、卡牌拖拽 UI 交互一致）：
    /// 持握实例每帧重算射线跟随地形（相机键盘/边缘平移时指针不动也能跟随）；
    /// 拎起 scale-in 与待机动画按暂停语义冻结；补间期间持续冻结 brain，
    /// 抵消「补间中点恢复」时 GameLoop.SetPaused(false) 的广播解冻。
    /// </summary>
    public void Tick()
    {
        if (_isDisposed) return;

        if (_instance != null && _token != null)
        {
            if (_hasPointer) TrySnapToTerrain();

            if (_appearElapsed < _appearDuration)
            {
                _appearElapsed = Mathf.Min(_appearDuration, _appearElapsed + Time.unscaledDeltaTime);
                float progress = Mathf.Clamp01(_appearElapsed / Mathf.Max(0.0001f, _appearDuration));
                _instance.transform.localScale = _originalLocalScale * SmoothStep01(progress);
            }

            if (_animators != null)
            {
                bool paused = _gameLoop != null && _gameLoop.IsPaused;
                foreach (Animator animator in _animators)
                    if (animator != null) animator.speed = paused ? 0f : 1f;
            }
        }

        if (_landingInstance != null && _landingBrain != null)
            _landingBrain.IsPaused = true;
    }

    /// <summary>
    /// 射线吸附：屏幕坐标统一走 CardController.GetCardDragLogicPosition（触点 + Screen.height × 偏移比例），
    /// 与高亮（PlayerInputHandler）和落点判定（CardController.OnEndDrag）同一坐标，保证
    /// 模型悬停格 = 高亮格 = 实际落地格。命中时吸附到 hit.point 上方固定高度；未命中保持上一位置。
    /// </summary>
    private void TrySnapToTerrain()
    {
        if (_instance == null || _mapRaycast == null) return;

        Vector2 logicPosition = CardController.GetCardDragLogicPosition(_lastPointer);
        if (_mapRaycast.RaycastMap(logicPosition, out RaycastHit hit, RaycastMaxDistance))
        {
            _lastHoverPosition = hit.point + Vector3.up * Mathf.Max(0f, FeelConfigProvider.CardDragPreviewHoverHeight);

            // 建筑统一吸附到所属地块中心：无论射线命中地块内哪个位置（槽位），
            // 预览都钉在格心，松手后也落在格心（与 SpawnBuilding 的 targetCell.RealCenterWorldCoordinate 一致）。
            ICardView cardView = _token as ICardView;
            if (cardView != null && cardView.Data?.NormalCardConfig is BuildingConfigSO)
            {
                HexCellData cell = _mapRaycast.GetCellByWorldPosition(hit.point);
                if (cell != null)
                {
                    _lastHoverPosition = cell.RealCenterWorldCoordinate
                        + Vector3.up * Mathf.Max(0f, FeelConfigProvider.CardDragPreviewHoverHeight);
                }
            }
        }

        _instance.transform.position = _lastHoverPosition;
    }

    private void UpdateLanding()
    {
        if (_landingInstance == null)
        {
            FinishLanding();
            return;
        }

        float current = Time.unscaledTime;
        float elapsed = current - _landingStartTime;
        if (elapsed < 0f) return;

        // 滞空段：停在起始高度不动，也不施加拉伸（进度按 0 计算，包络两端均为 0）。
        // 下坠计时从滞空结束才开始，故整段总时长 = LandingHangTime + LandingDuration。
        elapsed -= LandingHangTime;
        if (elapsed < 0f)
        {
            _landingInstance.transform.position = _landingFrom;
            ApplyLandingStretch(0f);
            return;
        }

        float progress = Mathf.Clamp01(elapsed / _landingDuration);
        _landingInstance.transform.position = Vector3.LerpUnclamped(_landingFrom, _landingTo, FallCurve(progress));
        ApplyLandingStretch(progress);

        if (progress >= 1f) FinishLanding();
    }

    private void FinishLanding()
    {
        if (_landingDriver != null)
        {
            _landingDriver.Kill();
            _landingDriver = null;
        }

        GameObject instance = _landingInstance;
        UnitBrainBase brain = _landingBrain;
        System.Action onComplete = _landingOnComplete;

        _landingInstance = null;
        _landingBrain = null;
        _landingOnComplete = null;

        if (instance != null)
            instance.transform.position = _landingTo;

        ResetLandingStretch();

        // 补间结束：恢复为全局暂停状态（不能无条件写 false，否则暂停中落地会被解冻）。
        if (brain != null && _gameLoop != null)
            brain.IsPaused = _gameLoop.IsPaused;

        onComplete?.Invoke();
    }

    private void KillLandingDriver()
    {
        if (_landingDriver != null)
        {
            _landingDriver.Kill();
            _landingDriver = null;
        }
        _landingInstance = null;
        _landingBrain = null;
        _landingOnComplete = null;

        ResetLandingStretch();
    }

    private void DestroyHeldInstance()
    {
        if (_instance != null)
        {
            Object.Destroy(_instance);
            _instance = null;
        }
        _token = null;
        _animators = null;
    }

    private void EnsureDragRoot()
    {
        if (_dragRoot != null) return;
        _dragRoot = new GameObject("CardDragWorldPreviewRoot").transform;
    }

    private static float SmoothStep01(float t)
    {
        return t * t * (3f - 2f * t);
    }

    // ── 落位拉伸（Squash & Stretch，方案 B：只缩放视觉子节点，不碰血条 Canvas 与 root 位移补间）──

    /// <summary>落位开始时解析视觉子节点并缓存拉伸基线。</summary>
    private void PrepareLandingStretch(GameObject instance)
    {
        _stretchVisualRoot = ResolveVisualRoot(instance);
        _stretchAnchorEnabled = false;

        if (_stretchVisualRoot == null) return;

        _stretchBaseScale = _stretchVisualRoot.localScale;
        _stretchBaseLocalY = _stretchVisualRoot.localPosition.y;
        _stretchAnchorEnabled = _stretchVisualRoot != instance.transform;
        _stretchBottomLocalY = ComputeVisualBottomLocalY(_stretchVisualRoot);
    }

    /// <summary>
    /// 定位「视觉子节点」：网格在根上（当前建筑）→ 根；否则取「非 UI 且子树含 3D 网格」的直接子节点（单位：model）。
    /// 不靠名字（实测 model/Canvas/默认名不一致），按结构启发式。
    /// </summary>
    private static Transform ResolveVisualRoot(GameObject instance)
    {
        Transform root = instance != null ? instance.transform : null;
        if (root == null) return null;

        if (HasOwnMeshRenderer(root)) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null) continue;
            if (ContainsCanvas(child)) continue;
            if (HasMeshInSubtree(child)) return child;
        }

        return root;
    }

    private static bool HasOwnMeshRenderer(Transform t)
    {
        if (t == null) return false;
        return t.GetComponent<MeshRenderer>() != null || t.GetComponent<SkinnedMeshRenderer>() != null;
    }

    private static bool HasMeshInSubtree(Transform t)
    {
        if (t == null) return false;
        if (HasOwnMeshRenderer(t)) return true;
        if (t.GetComponentInChildren<MeshRenderer>(true) != null) return true;
        if (t.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return true;
        return false;
    }

    private static bool ContainsCanvas(Transform t)
    {
        if (t == null) return false;
        return t.GetComponent<Canvas>() != null || t.GetComponentInChildren<Canvas>(true) != null;
    }

    /// <summary>视觉子节点局部空间底部 Y（底锚点）：由 3D 网格世界包围盒底反算，自动吸收嵌套缩放。</summary>
    private static float ComputeVisualBottomLocalY(Transform visualRoot)
    {
        if (visualRoot == null) return 0f;

        bool hasBounds = false;
        Bounds combined = default;
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue; // 排除粒子/拖尾/UI
            if (!hasBounds) { combined = r.bounds; hasBounds = true; }
            else combined.Encapsulate(r.bounds);
        }

        if (!hasBounds) return 0f;

        Vector3 bottomWorld = new Vector3(visualRoot.position.x, combined.min.y, visualRoot.position.z);
        return visualRoot.InverseTransformPoint(bottomWorld).y;
    }

    /// <summary>
    /// 按落位进度施加「先拉长 → 落地压扁 → 弹回」包络：
    /// 拉伸为钟形（峰值位置可调），压扁 + 弹回为尾部阻尼振荡；Y 与 XZ 按体积守恒反向缩放。
    /// 以脚底为锚：拉伸只往上长、压扁只往下塌，视觉根永不被推到地面以下。
    /// （延长落差已由 PlayLanding 抬高视觉起点实现，不在此处逐帧叠加。）
    /// </summary>
    private void ApplyLandingStretch(float progress)
    {
        if (_stretchVisualRoot == null) return;

        float t = Mathf.Clamp01(progress);
        float stretchY = 1f + LandingStretchAmp * StretchBump(t, LandingStretchPeak);
        float squashY = 1f - LandingSquashAmp * SquashBounce(t);
        float k = stretchY * squashY;
        float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.0001f, k));

        _stretchVisualRoot.localScale = new Vector3(
            _stretchBaseScale.x * sxz,
            _stretchBaseScale.y * k,
            _stretchBaseScale.z * sxz);

        if (_stretchAnchorEnabled)
        {
            // 底锚点：脚底始终钉在原地。缩放 k 会把底部推到 bottomLocalY * k，
            // 反向补偿 bottomLocalY * (1 - k) 抵消它——拉伸只往上长，压扁只往下塌，绝不入地。
            Vector3 lp = _stretchVisualRoot.localPosition;
            lp.y = _stretchBaseLocalY + _stretchBottomLocalY * (1f - k);
            _stretchVisualRoot.localPosition = lp;
        }
    }

    /// <summary>拉伸钟形包络：峰值位置 peak 可调，两端为 0、峰值为 1。</summary>
    private static float StretchBump(float t, float peak)
    {
        float u = t <= peak
            ? 0.5f * (t / peak)
            : 0.5f + 0.5f * ((t - peak) / (1f - peak));
        return Mathf.Sin(Mathf.PI * u);
    }

    /// <summary>落地撞击时刻（progress）。下落与压扁共用，保证「贴地」与「压扁」同帧发生。</summary>
    private const float LandingImpactProgress = 0.7f;

    // ── 落位手感（硬编码，不走配置表）────────────────────────────
    // 表现配置表缺列，这几项此前一直在吃 GameConfigImporter 的硬编码默认值，
    // 语义上从未真正可配；索性显式写在这里，改手感只改这一处。
    // hoverHeight 不在此列——它同时被持握吸附与落点图标读取，仍走 FeelConfigProvider。

    /// <summary>额外下落高度（世界单位）。总落差 = hoverHeight + 本值。</summary>
    private const float LandingDropHeight = 3f;

    /// <summary>落位补间时长（秒）。与总落差配套：落差变则须同调，否则撞击速度失真。</summary>
    private const float LandingDuration = 0.38f;

    /// <summary>松手后的滞空时长（秒）。模型悬在起始高度不动，随后才开始下坠。</summary>
    private const float LandingHangTime = 0.3f;

    /// <summary>拉伸峰值位置（progress 0~1）。紧贴 LandingImpactProgress 之前，速度最大处最长。</summary>
    private const float LandingStretchPeak = 0.6f;

    /// <summary>落位拉伸幅度（Y 方向最大拉长比例）。</summary>
    private const float LandingStretchAmp = 0.3f;

    /// <summary>落地压扁幅度（撞击瞬间 Y 方向最大压缩比例）。</summary>
    private const float LandingSquashAmp = 0.22f;

    /// <summary>落地压扁 + 弹回：撞击后阻尼振荡（压扁 → 轻微过冲弹回 → 归位）。</summary>
    private static float SquashBounce(float t)
    {
        const float damp = 2f;       // 阻尼，越大弹回越快、过冲越小
        float w = (t - LandingImpactProgress) / (1f - LandingImpactProgress);
        if (w <= 0f) return 0f;
        if (w >= 1f) w = 1f;
        return Mathf.Sin(Mathf.PI * 2f * w) * Mathf.Exp(-damp * w);
    }

    /// <summary>复位拉伸到 prefab 原样；底锚点补偿仅当视觉根是子节点时回写（等于实例根时只还原缩放，不碰位移）。</summary>
    private void ResetLandingStretch()
    {
        if (_stretchVisualRoot != null)
        {
            _stretchVisualRoot.localScale = _stretchBaseScale;
            if (_stretchAnchorEnabled)
            {
                Vector3 lp = _stretchVisualRoot.localPosition;
                lp.y = _stretchBaseLocalY;
                _stretchVisualRoot.localPosition = lp;
            }
        }

        _stretchVisualRoot = null;
        _stretchAnchorEnabled = false;
    }

    /// <summary>
    /// 下落位移曲线（返回 0→1 的行程比例，允许 &gt;1 的弹起过冲）：
    /// [0, LandingImpactProgress] 重力加速段 t²（先慢后快，落地瞬间速度最大）；
    /// 之后落点已到，做一次快速衰减的小弹跳（向上为负超出，故用 1 - 微小正弦）。
    /// 与 SquashBounce 共用同一撞击时刻，画面上「贴地 = 压扁 = 弹起」发生在同一帧。
    /// </summary>
    private static float FallCurve(float t)
    {
        if (t <= LandingImpactProgress)
        {
            float u = t / LandingImpactProgress;
            return u * u;
        }

        const float bounceHeight = 0.06f;   // 弹起高度（占总落差比例）
        const float damp = 3.5f;
        float w = (t - LandingImpactProgress) / (1f - LandingImpactProgress);
        return 1f - bounceHeight * Mathf.Sin(Mathf.PI * w) * Mathf.Exp(-damp * w);
    }
}
