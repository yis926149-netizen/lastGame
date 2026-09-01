using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 飞行元素视图契约：基类驱动动画所需的最小 UI 访问面。
/// 任何飞行元素（金币、碎片、图标…）都可实现；实现类需暴露 RectTransform 与 CanvasGroup。
/// </summary>
public interface IFlyingItemView
{
	/// <summary>元素 RectTransform（每帧写入 anchoredPosition / localScale）。</summary>
	RectTransform Rect { get; }

	/// <summary>元素 CanvasGroup（淡入淡出由基类写入 alpha）。</summary>
	CanvasGroup CanvasGroup { get; }

	/// <summary>切换整枚元素 UI 的显隐（池化复用时调用）。</summary>
	void SetActive(bool active);
}

/// <summary>
/// UI 飞入表现基类（纯表现层）：
/// 封装「一个 UI 元素从任意屏幕点飞向目标 UI」的通用机制——
/// 对象池、全局活跃上限、待生成队列、集中 Update 驱动（GameLoop.GameTime 暂停语义）、
/// 世界坐标 → 屏幕坐标 → 飞行层局部坐标三层换算、目标落点每帧刷新、
/// 确定性散开、两段式出现+飞行曲线（可整段重写）、落地 punch（共享工具）。
///
/// 调用方只触发「从哪飞」：FlyFromWorld / FlyFromScreen。
/// 「飞几枚、每枚怎么排、什么触发」由派生类决定——探索金币按广播载荷批量，
/// 其他用途可单次触发或自行批处理。
///
/// 职责边界（严格表现层）：不结算、不入账、不消费业务快照、不发布任何业务事件。
/// 业务数据（如钱包金额）与表现解耦，由派生类自行决定是否做「显示层延迟」。
///
/// 默认两段曲线（阶段A出现：上浮+放大+淡入；阶段B飞行：贝塞尔+缩放）由 TickItem 提供，
/// 派生类可整段重写 TickItem 以使用不同曲线/节奏。
/// </summary>
public abstract class FlyingItemFlyerBase : MonoBehaviour
{
	[Header("UI 引用")]
	[Tooltip("所有飞行元素 UI 的父节点")]
	[SerializeField] private RectTransform _flyLayer;
	[Tooltip("飞行元素最终飞向的 UI")]
	[SerializeField] private RectTransform _targetUI;
	[Tooltip("飞行元素 UI 预制体")]
	[SerializeField] private GameObject _itemPrefab;

	[Header("对象池")]
	[Tooltip("对象池初始容量，须 ≥ 全局活跃上限 _globalActiveCap")]
	[SerializeField] private int _initialPoolSize = 40;
	[Tooltip("全局活跃上限，超出部分进入待生成队列")]
	[SerializeField] private int _globalActiveCap = 40;

	[Header("生成与散开")]
	[Tooltip("每枚飞行元素的发射起始间隔（秒）")]
	[SerializeField] private float _spawnInterval = 0.04f;
	[Tooltip("出现阶段散开半径")]
	[SerializeField] private float _scatterRadius = 80f;

	[Header("阶段A：出现")]
	[Tooltip("出现阶段持续时长（秒）")]
	[SerializeField] private float _appearDuration = 0.25f;
	[Tooltip("出现阶段起始缩放")]
	[SerializeField] private float _appearStartScale = 0.25f;
	[Tooltip("出现阶段结束缩放")]
	[SerializeField] private float _appearEndScale = 1f;
	[Tooltip("出现阶段上浮高度")]
	[SerializeField] private float _liftHeight = 60f;

	[Header("阶段B：飞向目标")]
	[Tooltip("飞向目标阶段持续时长（秒）")]
	[SerializeField] private float _flyDuration = 0.35f;
	[Tooltip("飞行贝塞尔曲线弧线高度")]
	[SerializeField] private float _arcHeight = 100f;
	[Tooltip("落地时缩放")]
	[SerializeField] private float _landScale = 0.75f;

	[Header("阶段曲线（AnimationCurve，输入归一化进度 0..1）")]
	[Tooltip("阶段A 位置曲线：0=起点，1=起浮终点。默认 EaseOutQuad 形状")]
	[SerializeField] private AnimationCurve _appearPositionCurve = BuildDefaultCurve(EaseOutQuad);
	[Tooltip("阶段A 缩放曲线：0=_appearStartScale，1=_appearEndScale。默认 EaseOutBack 形状（带过冲）")]
	[SerializeField] private AnimationCurve _appearScaleCurve = BuildDefaultCurve(EaseOutBack);
	[Tooltip("阶段B 位置曲线：0=阶段A终点，1=目标点。默认 EaseInCubic 形状")]
	[SerializeField] private AnimationCurve _flyPositionCurve = BuildDefaultCurve(EaseInCubic);
	[Tooltip("阶段B 缩放曲线：0=_appearEndScale，1=_landScale。默认线性")]
	[SerializeField] private AnimationCurve _flyScaleCurve = BuildDefaultCurve(t => t);

	[Header("落点反馈")]
	[Tooltip("飞行元素落地时是否对目标 UI 做 punch 缩放反馈")]
	[SerializeField] private bool _punchTargetOnArrival = true;
	[Tooltip("目标 UI punch 缩放强度（每枚落地都是这个振幅）")]
	[SerializeField] private float _targetPunchScale = 0.15f;
	[Tooltip("单次 punch 时长（秒）。期间有新元素落地会打断、归位并重新开始")]
	[SerializeField] private float _punchDuration = 0.3f;
	[Tooltip("punch 振动次数")]
	[SerializeField] private int _punchVibrato = 8;
	[Tooltip("punch 弹性（0~1）")]
	[SerializeField] private float _punchElasticity = 0.5f;

	/// <summary>GameLoop（提供暂停语义）。可由 Zenject 注入派生类后赋值，或派生类自行注入。</summary>
	protected GameLoop GameLoop;

	// ── 对象池 ────────────────────────────────────────────
	private readonly Queue<IFlyingItemView> _pool = new Queue<IFlyingItemView>();
	private readonly HashSet<IFlyingItemView> _pooled = new HashSet<IFlyingItemView>();

	// ── 活跃飞行元素（集中驱动） ─────────────────────────────
	private readonly List<ItemState> _active = new List<ItemState>();

	// ── 待生成批次（超出全局活跃上限时排队，旧批完成即释放容量） ──
	private readonly Queue<PendingBatch> _pendingBatches = new Queue<PendingBatch>();

	// 飞行层 Canvas 信息（换算相机一律由 _flyLayer 所在 Canvas 推导）。
	private Canvas _flyCanvas;

	// 目标落点缓存：每帧换算成功则更新；失败时沿用上次值（目标被销毁/换算异常时平滑兜底）。
	private Vector2 _cachedTargetLocal = Vector2.zero;

	// 目标 UI 基准缩放：punch 前后归位用（DOPunchScale 被 Kill 时不会自动回退，需显式还原）。
	private Vector3 _targetBaseScale;

	// 目标 UI 缩放 punch 共享工具（每枚落地强制重启）：见 FlyerPunchTarget。
	private FlyerPunchTarget _punch;

	/// <summary>单枚飞行元素的动画状态（由本类集中持有并驱动）。</summary>
	protected sealed class ItemState
	{
		public IFlyingItemView View;
		public float StartGameTime;   // 出现阶段起点（含逐枚 stagger 延迟，GameTime 语义）
		public Vector2 AppearStart;   // 阶段A起点（含散开偏移）
		public Vector2 AppearEnd;     // 阶段A终点 = 阶段B起点（上浮后）
	}

	/// <summary>一批待生成的飞行元素（按 batch 内逐枚 stagger 发射）。</summary>
	protected sealed class PendingBatch
	{
		public Vector2 ScreenPos;      // 起始屏幕坐标
		public int Amount;
		public int NextIndex;
		public float SpawnStartGameTime;
	}

	/// <summary>当前活跃飞行元素数量（派生类可据此做预算/显示层延迟）。</summary>
	protected int ActiveCount => _active.Count;

	/// <summary>出现阶段起始缩放（派生类 ConfigureForSpawn 设置初始视觉缩放时读取）。</summary>
	protected float AppearStartScale => _appearStartScale;

	/// <summary>目标落点（飞行层局部坐标，每帧刷新）。派生类整段重写 TickItem 时需要它作为阶段B终点。</summary>
	protected Vector2 CachedTargetLocal => _cachedTargetLocal;

	/// <summary>飞行元素最终飞向的目标 UI。默认取 Inspector 里的 _targetUI；派生类可覆写以提供自定义目标节点。</summary>
	protected virtual RectTransform TargetUI => _targetUI;

	// ── 公开入口 ──────────────────────────────────────────

	/// <summary>
	/// 从世界坐标触发一次飞行。调用方只给「从哪飞」，数量/触发方式由派生类决定。
	/// 返回 true 表示被接受（进入生成/排队流程）；false 表示相机背后或换算失败。
	/// </summary>
	public bool FlyFromWorld(Vector3 worldPos)
	{
		if (Camera.main == null) return false;

		Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
		if (screenPos.z < 0f)
		{
			return false; // 相机背后：不播放，避免从屏幕反侧冒出。
		}

		const float margin = 40f;
		screenPos.x = Mathf.Clamp(screenPos.x, margin, Screen.width - margin);
		screenPos.y = Mathf.Clamp(screenPos.y, margin, Screen.height - margin);

		return FlyFromScreen(screenPos);
	}

	/// <summary>
	/// 从屏幕坐标触发一次飞行（起点已在屏幕空间的场景，如纯 UI→UI）。
	/// </summary>
	public bool FlyFromScreen(Vector2 screenPos)
	{
		if (_flyLayer == null || TargetUI == null) return false;
		return EnqueueBatch(screenPos, 1);
	}

	/// <summary>
	/// 从世界坐标触发一批飞行（数量由派生类决定）。换算失败/相机背后返回 false。
	/// </summary>
	protected bool FlyBatchFromWorld(Vector3 worldPos, int amount)
	{
		if (amount <= 0) return false;

		if (Camera.main == null) return false;
		Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
		if (screenPos.z < 0f)
		{
			return false;
		}

		const float margin = 40f;
		screenPos.x = Mathf.Clamp(screenPos.x, margin, Screen.width - margin);
		screenPos.y = Mathf.Clamp(screenPos.y, margin, Screen.height - margin);

		return FlyBatchFromScreen(screenPos, amount);
	}

	/// <summary>
	/// 从屏幕坐标触发一批飞行（数量由派生类决定）。
	/// </summary>
	protected bool FlyBatchFromScreen(Vector2 screenPos, int amount)
	{
		if (_flyLayer == null || TargetUI == null) return false;
		return EnqueueBatch(screenPos, amount);
	}

	// ── 生命周期 ──────────────────────────────────────────

	private void Start()
	{
		if (_flyLayer == null || TargetUI == null)
		{
			Debug.LogError($"[{GetType().Name}] _flyLayer / _targetUI 未赋值！请在 Inspector 中拖入对应引用。");
			return;
		}

		_targetBaseScale = TargetUI.localScale;
		_punch = new FlyerPunchTarget(
			TargetUI,
			_targetBaseScale,
			_targetPunchScale,
			_punchDuration,
			_punchVibrato,
			_punchElasticity);
		CacheFlyCanvas();
		EnsureDefaultCurves();

		// 预热对象池：保证单批不跨批借用，池容量 ≥ 全局活跃上限。
		// 若初始容量 > 0 但预制体缺失，InstantiateView 返回 null，此处跳过空槽。
		int prewarmed = 0;
		for (int i = 0; i < _initialPoolSize; i++)
		{
			IFlyingItemView view = InstantiateView();
			if (view != null)
			{
				ReturnToPool(view);
				prewarmed++;
			}
		}
		if (prewarmed == 0)
		{
			Debug.LogError($"[{GetType().Name}] _itemPrefab 未正确实例化任何视图！请检查预制体与 View 组件。");
		}

		SubscribeSource();
	}

	private void OnDestroy()
	{
		UnsubscribeSource();

		_punch?.Dispose();
		_punch = null;

		_active.Clear();
		_pendingBatches.Clear();
		_pool.Clear();
		_pooled.Clear();

		OnDestroyed(); // 基类清理完成后，给派生类一次清自身状态的机会。
	}

	// ── 派生类钩子 ──────────────────────────────────────────

	/// <summary>订阅触发源（如广播）。初始化时调用一次。</summary>
	protected abstract void SubscribeSource();

	/// <summary>取消订阅触发源。销毁时调用一次。</summary>
	protected virtual void UnsubscribeSource() { }

	/// <summary>复用配置：激活并设置该飞行元素为该次飞行的初始状态。</summary>
	protected abstract void ConfigureForSpawn(IFlyingItemView view, Vector2 startLocal);

	/// <summary>该飞行元素落地完成、回池前调用。默认做目标 punch 反馈。</summary>
	protected virtual void OnItemArrived(ItemState state)
	{
		PunchTarget();
	}

	/// <summary>活跃数变化钩子（如 HUD 延迟显示）。默认无操作。</summary>
	protected virtual void OnActiveCountChanged(int count) { }

	/// <summary>某枚飞行元素生成时换算失败被跳过（未播动画）。默认无操作；派生类可据此做计数追平。</summary>
	protected virtual void OnItemSkipped() { }

	/// <summary>本组件销毁时调用（基类清理完成后）。清派生类状态用。</summary>
	protected virtual void OnDestroyed() { }

	/// <summary>实例化池化元素所用预制体。默认 _itemPrefab；派生类可覆写以保留自身的序列化字段名。</summary>
	protected virtual GameObject ItemPrefab => _itemPrefab;

	/// <summary>
	/// 默认两段曲线：阶段A出现（上浮+放大+淡入），阶段B沿贝塞尔飞向目标（缩放至落地）。
	/// 曲线形状由 Inspector 中的 AnimationCurve 字段控制（见 _appearPositionCurve 等），
	/// 缺省时回退到硬编码缓动形状，行为不变。
	/// 派生类可整段重写此方法以使用不同曲线/节奏。
	/// 返回 true 表示动画完成（调用方负责回池并触发 OnItemArrived）。
	/// </summary>
	protected virtual bool TickItem(ItemState state, float elapsed)
	{
		RectTransform rect = state.View.Rect;
		CanvasGroup canvasGroup = state.View.CanvasGroup;

		if (elapsed < _appearDuration)
		{
			// 阶段A：上浮 + 放大 + 淡入（位置/缩放走曲线，淡入保持快速线性）。
			float p = Mathf.Clamp01(elapsed / _appearDuration);
			rect.anchoredPosition = Vector2.Lerp(state.AppearStart, state.AppearEnd, SampleCurve(_appearPositionCurve, p, EaseOutQuad));
			rect.localScale = Vector3.one * Mathf.Lerp(_appearStartScale, _appearEndScale, SampleCurve(_appearScaleCurve, p, EaseOutBack));
			if (canvasGroup != null)
			{
				canvasGroup.alpha = Mathf.Clamp01(p * 5f);
			}
			return false;
		}

		// 阶段B：沿二次贝塞尔曲线飞向目标（控制点 = 中点 + 上抬弧线）。
		float flyP = Mathf.Clamp01((elapsed - _appearDuration) / _flyDuration);
		float eased = SampleCurve(_flyPositionCurve, flyP, EaseInCubic);
		Vector2 control = (state.AppearEnd + _cachedTargetLocal) * 0.5f + new Vector2(0f, _arcHeight);
		rect.anchoredPosition = QuadraticBezier(state.AppearEnd, control, _cachedTargetLocal, eased);
		rect.localScale = Vector3.one * Mathf.Lerp(_appearEndScale, _landScale, SampleCurve(_flyScaleCurve, flyP, t => t));
		if (canvasGroup != null)
		{
			canvasGroup.alpha = 1f;
		}

		return flyP >= 1f;
	}

	/// <summary>入队一批飞行元素；返回是否入队成功。</summary>
	protected bool EnqueueBatch(Vector2 screenPos, int amount)
	{
		if (amount <= 0) return false;
		_pendingBatches.Enqueue(new PendingBatch
		{
			ScreenPos = screenPos,
			Amount = amount,
		});
		return true;
	}

	// ── 集中驱动 ──────────────────────────────────────────

	private void Update()
	{
		if (_active.Count == 0 && _pendingBatches.Count == 0)
		{
			return;
		}

		SpawnPendingWithinBudget();

		float now = Now();
		if (TryComputeTargetLocal(out Vector2 targetLocal))
		{
			_cachedTargetLocal = targetLocal;
		}

		for (int i = _active.Count - 1; i >= 0; i--)
		{
			ItemState state = _active[i];

			// 活跃实例可能已被销毁（场景切换等），逐个判空剔除，避免 MissingReferenceException。
			if (state == null || state.View == null)
			{
				_active.RemoveAt(i);
				continue;
			}

			float elapsed = now - state.StartGameTime;
			if (elapsed < 0f)
			{
				continue; // 逐枚 stagger 尚未到点，保持初始状态。
			}

			if (TickItem(state, elapsed))
			{
				OnItemArrived(state);
				ReturnToPool(state.View);
				_active.RemoveAt(i);
				OnActiveCountChanged(_active.Count);
			}
		}
	}

	private void SpawnPendingWithinBudget()
	{
		// 排查用：本帧有待生成批次、却因活跃预算耗尽而一枚都发不出时说明原因。
		// _globalActiveCap 被误设为 0 时这里会永久静默，是「入队成功但什么都不出现」的典型原因。
		if (_pendingBatches.Count > 0 && _active.Count >= _globalActiveCap)
		{
			Debug.LogWarning($"[{GetType().Name}] 待生成批次 {_pendingBatches.Count} 个，但活跃数 {_active.Count} 已达上限 _globalActiveCap={_globalActiveCap}，本帧不生成。", this);
		}

		while (_pendingBatches.Count > 0 && _active.Count < _globalActiveCap)
		{
			PendingBatch batch = _pendingBatches.Peek();
			if (batch == null || batch.Amount <= 0 || batch.NextIndex >= batch.Amount)
			{
				_pendingBatches.Dequeue();
				continue;
			}

			IFlyingItemView item = GetItem();
			if (item == null)
			{
				// 池空且实例化失败：预制体缺失 / 未实现 IFlyingItemView（InstantiateView 已打过 Error）。
				Debug.LogError($"[{GetType().Name}] 取不到可用视图（池空且实例化失败），本帧中止补发。请检查预制体是否挂了 IFlyingItemView 实现。", this);
				break;
			}

			if (batch.NextIndex == 0)
			{
				batch.SpawnStartGameTime = Now();
			}

			Camera uiCamera = GetUICamera();
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_flyLayer, batch.ScreenPos, uiCamera, out Vector2 cellLocal))
			{
				Debug.LogWarning($"[{GetType().Name}] 屏幕坐标 {batch.ScreenPos} → 飞行层局部坐标换算失败，该枚跳过（未播动画）。" +
								 $"flyLayer={_flyLayer.name} uiCamera={(uiCamera == null ? "null(Overlay)" : uiCamera.name)}", this);
				ReturnToPool(item);
				OnItemSkipped(); // 该枚未播动画即被跳过：通知派生类做计数追平。
				batch.NextIndex++;
				if (batch.NextIndex >= batch.Amount) _pendingBatches.Dequeue();
				continue;
			}

			Vector2 scatter = ComputeScatterOffset(batch.ScreenPos, batch.NextIndex);
			Vector2 appearStart = cellLocal + scatter;
			Vector2 appearEnd = cellLocal + scatter + new Vector2(0f, _liftHeight);

			ConfigureForSpawn(item, appearStart);

			_active.Add(new ItemState
			{
				View = item,
				StartGameTime = batch.SpawnStartGameTime + batch.NextIndex * _spawnInterval,
				AppearStart = appearStart,
				AppearEnd = appearEnd,
			});

			batch.NextIndex++;
			if (batch.NextIndex >= batch.Amount)
			{
				_pendingBatches.Dequeue();
			}
		}
	}

	// ── 换算与散开 ────────────────────────────────────────

	/// <summary>目标 UI → 飞行层局部坐标；失败返回 false（调用方沿用缓存落点，纯表现无结算义务）。</summary>
	private bool TryComputeTargetLocal(out Vector2 targetLocal)
	{
		targetLocal = default;
		if (_flyLayer == null || TargetUI == null) return false;

		Camera uiCamera = GetUICamera();
		// RectTransformUtility.WorldToScreenPoint(cam==null) 直接返回 (x,y)，恰为 Overlay 正确行为。
		Vector2 targetScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, TargetUI.position);
		return RectTransformUtility.ScreenPointToLocalPointInRectangle(_flyLayer, targetScreen, uiCamera, out targetLocal);
	}

	/// <summary>确定性散开偏移（圆形区域均匀分布）。种子由屏幕坐标量化 + 枚序号合成。</summary>
	private Vector2 ComputeScatterOffset(Vector2 screenPos, int index)
	{
		int seed = Mathf.RoundToInt(screenPos.x) * 73856093
				 ^ Mathf.RoundToInt(screenPos.y) * 19349663
				 ^ index;
		float angle = Hash01(seed) * Mathf.PI * 2f;
		float radius = Mathf.Sqrt(Hash01(seed + 17)) * _scatterRadius; // sqrt 保证圆盘内均匀
		return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
	}

	private void PunchTarget()
	{
		if (!_punchTargetOnArrival || TargetUI == null || _punch == null) return;

		// 每枚落地都强制重启一次缩放：缩放期间有新元素进入则打断、归位、重新开始
		// （见 FlyerPunchTarget）。只 Kill 自己那条 Sequence，不用 _targetUI.DOKill()——
		// 后者会把目标 UI 上无关的 tween 一并杀掉。
		_punch.RequestPunch();
	}

	// ── 对象池 ────────────────────────────────────────────

	private IFlyingItemView GetItem()
	{
		if (_pool.Count > 0)
		{
			var view = _pool.Dequeue();
			_pooled.Remove(view);
			return view;
		}
		return InstantiateView();
	}

	private IFlyingItemView InstantiateView()
	{
		GameObject prefab = ItemPrefab;
		if (prefab == null)
		{
			Debug.LogError($"[{GetType().Name}] 预制体字段为空（ItemPrefab == null）！请在 Inspector 中拖入飞行元素预制体。", this);
			return null;
		}

		Transform parent = _flyLayer != null ? _flyLayer : transform;
		GameObject go = Instantiate(prefab, parent);
		var view = go.GetComponent<IFlyingItemView>();
		if (view == null)
		{
			Debug.LogError($"[{GetType().Name}] _itemPrefab 未挂载 IFlyingItemView 接口！请确保预制体顶节点实现该接口。");
			Destroy(go);
			return null;
		}
		go.SetActive(false);
		return view;
	}

	private void ReturnToPool(IFlyingItemView view)
	{
		if (view == null || !_pooled.Add(view)) return;
		view.SetActive(false);
		_pool.Enqueue(view);
	}

	// ── 缓存与换算辅助 ────────────────────────────────────

	/// <summary>换算相机一律由飞行层所在 Canvas 推导，不是目标 UI 所在 Canvas。</summary>
	private Camera GetUICamera()
	{
		if (_flyCanvas == null)
		{
			CacheFlyCanvas();
		}
		if (_flyCanvas != null && _flyCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
		{
			return _flyCanvas.worldCamera;
		}
		return null; // Overlay：camera 传 null 即正确行为。
	}

	private void CacheFlyCanvas()
	{
		_flyCanvas = _flyLayer != null ? _flyLayer.GetComponentInParent<Canvas>() : null;
	}

	private float Now()
	{
		// 暂停时 GameTime 不累加 → 飞行元素冻结；若未绑定 GameLoop，Time.time 为防御性回退。
		return GameLoop != null ? GameLoop.GameTime : Time.time;
	}

	// ── 曲线辅助 ───────────────────────────────────────────

	/// <summary>
	/// 采样一条进度曲线。运行期若某条曲线字段为 null（Inspector 里被清空），
	/// 回退到该字段对应的默认形状，保证始终有一条可用曲线。
	/// 注意：不限制输出范围——让过冲类曲线（如 EaseOutBack 默认缩放曲线）保留超出 0..1 的峰值。
	/// </summary>
	private float SampleCurve(AnimationCurve curve, float t, Func<float, float> fallback)
	{
		if (curve == null)
		{
			return fallback(t);
		}
		return curve.Evaluate(Mathf.Clamp01(t));
	}

	/// <summary>把闭式缓动函数采样成一条折线曲线；采样点足够多以保留过冲形状。</summary>
	private static AnimationCurve BuildDefaultCurve(Func<float, float> ease)
	{
		const int samples = 16;
		Keyframe[] keys = new Keyframe[samples + 1];
		for (int i = 0; i <= samples; i++)
		{
			float t = i / (float)samples;
			keys[i] = new Keyframe(t, ease(t));
		}
		return new AnimationCurve(keys);
	}

	/// <summary>
	/// 运行期保证四条阶段曲线均已赋值；Inspector 里缺省（首次挂载 / 被清空）时，
	/// 以其默认闭式缓动形状填充，使当前行为与硬编码阶段完全一致。
	/// </summary>
	private void EnsureDefaultCurves()
	{
		if (_appearPositionCurve == null) _appearPositionCurve = BuildDefaultCurve(EaseOutQuad);
		if (_appearScaleCurve == null) _appearScaleCurve = BuildDefaultCurve(EaseOutBack);
		if (_flyPositionCurve == null) _flyPositionCurve = BuildDefaultCurve(EaseInCubic);
		if (_flyScaleCurve == null) _flyScaleCurve = BuildDefaultCurve(t => t);
	}

	// ── 缓动与曲线 ─────────────────────────────────────────

	protected static float EaseOutQuad(float t)
	{
		return 1f - (1f - t) * (1f - t);
	}

	protected static float EaseInCubic(float t)
	{
		return t * t * t;
	}

	protected static float EaseOutBack(float t)
	{
		const float c1 = 1.70158f;
		const float c3 = c1 + 1f;
		return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
	}

	protected static Vector2 QuadraticBezier(Vector2 a, Vector2 b, Vector2 c, float t)
	{
		float u = 1f - t;
		return u * u * a + 2f * u * t * b + t * t * c;
	}

	/// <summary>确定性伪随机 [0,1)。</summary>
	protected static float Hash01(int n)
	{
		n = (n ^ 61) ^ (n >> 16);
		n = n + (n << 3);
		n = n ^ (n >> 4);
		n = n * 0x27d4eb2d;
		n = n ^ (n >> 15);
		return (n & 0x7fffffff) / (float)0x7fffffff;
	}
}
