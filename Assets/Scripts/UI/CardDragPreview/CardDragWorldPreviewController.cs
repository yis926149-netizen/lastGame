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
    /// 播放落位补间（纯视觉）：终点取当前实际位置（生成后读，单位可能已被站位槽二次吸附），
    /// 把位置拨回释放悬停点后按 GameTime 增量补间回去。补间期间冻结单位 brain，
    /// 补间结束恢复为 GameLoop.IsPaused（不无条件写 false，防止暂停中落地被解冻）。
    /// </summary>
    public void PlayLanding(GameObject instance, Vector3 fromPos, System.Action onComplete)
    {
        if (_isDisposed || instance == null) return;

        KillLandingDriver();

        Vector3 endPos = instance.transform.position;
        instance.transform.position = fromPos;

        // 单位落地即注册了 brain，下一帧就可能决策并改写 transform.position；
        // 补间期间冻结，恢复时机见 FinishLanding。
        UnitBrainBase brain = instance.GetComponentInChildren<UnitBrainBase>(true);
        if (brain != null) brain.IsPaused = true;

        float duration = Mathf.Max(0.0001f, FeelConfigProvider.CardDragPreviewSnapDuration);

        _landingInstance = instance;
        _landingBrain = brain;
        _landingFrom = fromPos;
        _landingTo = endPos;
        _landingDuration = duration;
        _landingStartTime = Time.unscaledTime;
        _landingOnComplete = onComplete;

        // 进度由 unscaledTime 增量驱动（不受暂停/速度档位影响，暂停出牌也播完落位）；
        // driver 只承载每帧 OnUpdate；SetLoops(-1) 永不自然结束。
        _landingDriver = DOTween.To(() => 0f, _ => { }, 1f, duration)
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

        float progress = Mathf.Clamp01(elapsed / _landingDuration);
        _landingInstance.transform.position = Vector3.Lerp(_landingFrom, _landingTo, EaseOutCubic(progress));

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

    private static float EaseOutCubic(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u;
    }
}
