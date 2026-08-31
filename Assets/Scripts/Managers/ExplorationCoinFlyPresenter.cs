using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using Zenject;

/// <summary>
/// 探索金币奖励“飞入”表现接收者（纯表现层）：
/// 订阅统一广播的 RewardPoint 阶段，只在「玩家 + SettledRewardType == Gold」时，
/// 按 SettledGoldAmount 生成对应个数的金币 UI（含建筑降级金币），
/// 先在地块屏幕位置出现（上浮 + 散开 + 放大），再逐枚沿二次贝塞尔曲线飞向 targetUI。
///
/// 职责边界（严格表现层）：不结算、不入账、不消费奖励快照、不发布阶段。
/// 钱包金额在 Settled 已到账，本组件只负责飞币表现，绝不调用 GoldWallet.AddGold。
/// 动画由 GameLoop.GameTime 驱动，暂停即冻结；目标 UI 位置每帧换算，支持目标移动/动画/分辨率变化。
/// </summary>
public class ExplorationCoinFlyPresenter : MonoBehaviour
{
	[Header("UI 引用")]
	[Tooltip("所有飞币 UI 的父节点")]
	[SerializeField] private RectTransform _flyLayer;
	[Tooltip("金币最终飞向的 UI")]
	[SerializeField] private RectTransform _targetUI;
	[Tooltip("金币 UI 预制体")]
	[SerializeField] private GameObject _coinUIPrefab;
	[Tooltip("金币贴图，运行时写到每枚飞币的 Image")]
	[SerializeField] private Sprite _coinSprite;

	[Header("对象池")]
	[Tooltip("对象池初始容量，须 ≥ 全局活跃上限 _globalActiveCap")]
	[SerializeField] private int _initialPoolSize = 40;
	[Tooltip("全局活跃金币上限，超出部分进入待生成队列")]
	[SerializeField] private int _globalActiveCap = 40;

	[Header("生成与散开")]
	[Tooltip("每枚飞币的发射起始间隔（秒）")]
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

	[Header("落点反馈")]
	[Tooltip("金币落地时是否对目标 UI 做 punch 缩放反馈")]
	[SerializeField] private bool _punchTargetOnArrival = true;
	[Tooltip("目标 UI punch 缩放强度")]
	[SerializeField] private float _targetPunchScale = 0.15f;

	private IExplorationBroadcastSource _broadcastSource;
	private GameLoop _gameLoop;

	// ── 对象池 ────────────────────────────────────────────
	private readonly Queue<ExplorationFlyingCoinUI> _pool = new Queue<ExplorationFlyingCoinUI>();
	private readonly HashSet<ExplorationFlyingCoinUI> _pooled = new HashSet<ExplorationFlyingCoinUI>();

	// ── 活跃飞币（集中驱动） ─────────────────────────────
	private readonly List<CoinState> _active = new List<CoinState>();

	// ── 待生成批次（超出全局活跃上限时排队，旧批完成即释放容量） ──
	private readonly Queue<PendingBatch> _pendingBatches = new Queue<PendingBatch>();

	// 飞行层 Canvas 信息（换算相机一律由 _flyLayer 所在 Canvas 推导，见 §4.6）。
	private Canvas _flyCanvas;

	// 目标落点缓存：每帧换算成功则更新；失败时沿用上次值（目标被销毁/换算异常时平滑兜底，纯表现无结算义务）。
	private Vector2 _cachedTargetLocal = Vector2.zero;

	// 目标 UI 基准缩放：punch 前后归位用（DOPunchScale 被 Kill 时不会自动回退，需显式还原）。
	private Vector3 _targetBaseScale;

	// ── 方案 B：显示层延迟 ────────────────────────────────
	// 飞行中金币数：真实钱包已在 Settled 入账、但 HUD 尚未显示的部分（每枚飞币落地/跳过时 -1）。
	// HUD 显示值 = 真实钱包 - InFlightGold，飞币落地逐枚追平。
	public int InFlightGold { get; private set; }

	/// <summary>飞行中金币数变化时触发（HUD 据此刷新显示值）。</summary>
	public event System.Action InFlightChanged;

	/// <summary>单枚飞币的动画状态（由 Presenter 集中持有并驱动）。</summary>
	private sealed class CoinState
	{
		public ExplorationFlyingCoinUI View;
		public float StartGameTime;   // 出现阶段起点（含逐枚 stagger 延迟，GameTime 语义）
		public Vector2 AppearStart;   // 阶段A起点（地块局部坐标 + 散开偏移）
		public Vector2 AppearEnd;     // 阶段A终点 = 阶段B起点（上浮后）
	}

	/// <summary>一批待生成的飞币（按 batch 内逐枚 stagger 发射）。</summary>
	private sealed class PendingBatch
	{
		public HexCellData Cell;
		public int Amount;
		public int NextIndex;
		public float SpawnStartGameTime;
	}

	[Inject]
	public void Construct(IExplorationBroadcastSource broadcastSource, GameLoop gameLoop)
	{
		_broadcastSource = broadcastSource;
		_gameLoop = gameLoop;
	}

	private void Start()
	{
		if (_broadcastSource == null)
		{
			Debug.LogError("[CoinFlyPresenter] IExplorationBroadcastSource 未注入！Zenject 可能未找到该组件。");
			return;
		}
		if (_coinUIPrefab == null || _flyLayer == null || _targetUI == null)
		{
			Debug.LogError("[CoinFlyPresenter] _coinUIPrefab / _flyLayer / _targetUI 未赋值！请在 Inspector 中拖入对应引用。");
			return;
		}
		if (_coinSprite == null)
		{
			Debug.LogWarning("[CoinFlyPresenter] _coinSprite 未赋值！请在 Inspector 中拖入金币贴图，否则飞币将显示为空白。");
		}

		_targetBaseScale = _targetUI.localScale;
		CacheFlyCanvas();

		// 预热对象池：保证 ≤25 枚单批不跨批借用，池容量 ≥ 全局活跃上限（见 §4.3/§4.8）。
		for (int i = 0; i < _initialPoolSize; i++)
		{
			ReturnToPool(InstantiateCoin());
		}

		_broadcastSource.Broadcast += OnBroadcast;
	}

	private void OnDestroy()
	{
		if (_broadcastSource != null)
		{
			_broadcastSource.Broadcast -= OnBroadcast;
		}

		_active.Clear();
		_pendingBatches.Clear();
		_pool.Clear();
		_pooled.Clear();
		InFlightGold = 0;
	}

	private void Update()
	{
		if (_active.Count == 0 && _pendingBatches.Count == 0)
		{
			return;
		}

		// 先按容量从待生成队列补发，再统一驱动（旧批完成即释放容量，支持批次重叠）。
		SpawnPendingWithinBudget();

		float now = Now();
		if (TryComputeTargetLocal(out Vector2 targetLocal))
		{
			_cachedTargetLocal = targetLocal;
		}

		for (int i = _active.Count - 1; i >= 0; i--)
		{
			CoinState state = _active[i];

			// 活跃实例可能已被销毁（场景切换等），逐个判空剔除，避免 MissingReferenceException（§4.7）。
			if (state == null || state.View == null)
			{
				_active.RemoveAt(i);
				continue;
			}

			float elapsed = now - state.StartGameTime;
			if (elapsed < 0f)
			{
				continue; // 逐枚 stagger 尚未到点，保持初始状态（透明、小尺寸）。
			}

			if (TickCoin(state, elapsed))
			{
				ReturnToPool(state.View);
				_active.RemoveAt(i);
				DecrementInFlight(); // 一枚飞币落地：显示值 +1
			}
		}
	}

	/// <summary>处理玩家金币奖励：Settled 记录飞行中金币数（HUD 延迟显示）；RewardPoint 生成飞币。</summary>
	private void OnBroadcast(ExplorationAcquisition acquisition)
	{
		if (acquisition == null || acquisition.FactionId != 0) return;
		if (acquisition.SettledRewardType != ExplorationRewardConfigSO.ExplorationRewardType.Gold) return;
		if (acquisition.SettledGoldAmount <= 0) return;

		// 方案 B：Settled 阶段就计入「飞行中金币数」。真实钱包此刻已入账，
		// HUD 立即回退到入账前值（与入账同帧完成，无闪烁），飞币落地再逐枚 +1。
		if (acquisition.Phase == ExplorationBroadcastPhase.Settled)
		{
			InFlightGold += acquisition.SettledGoldAmount;
			InFlightChanged?.Invoke();
			return;
		}

		if (acquisition.Phase != ExplorationBroadcastPhase.RewardPoint) return;
		if (acquisition.Cell == null) return;

		_pendingBatches.Enqueue(new PendingBatch
		{
			Cell = acquisition.Cell,
			Amount = acquisition.SettledGoldAmount,
		});
	}

	/// <summary>在当前全局活跃上限内，从待生成队列逐枚补发飞币。</summary>
	private void SpawnPendingWithinBudget()
	{
		while (_pendingBatches.Count > 0 && _active.Count < _globalActiveCap)
		{
			PendingBatch batch = _pendingBatches.Peek();
			if (batch == null || batch.Cell == null || batch.NextIndex >= batch.Amount)
			{
				_pendingBatches.Dequeue();
				continue;
			}

			var coin = GetCoin();
			if (coin == null)
			{
				// 池容量 < 全局活跃上限的矛盾不应发生（§4.3 池 ≥ 上限）；此处防御性中止本帧补发。
				break;
			}

			if (batch.NextIndex == 0)
			{
				batch.SpawnStartGameTime = Now();
			}

			if (!TryComputeAppearPoints(batch.Cell, batch.NextIndex, out Vector2 appearStart, out Vector2 appearEnd))
			{
				// 相机背后 / 相机缺失 / 换算失败：该枚直接回池，不播动画（纯表现，无兜底结算义务，§4.6）。
				ReturnToPool(coin);
				DecrementInFlight(); // 被跳过的枚同样视为「已到账」，显示值立即 +1，避免永久少显示。
				batch.NextIndex++;
				if (batch.NextIndex >= batch.Amount) _pendingBatches.Dequeue();
				continue;
			}

			ConfigureCoinView(coin, appearStart);

			_active.Add(new CoinState
			{
				View = coin,
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

	/// <summary>驱动单枚飞币一帧；返回 true 表示动画完成（调用方负责回池）。</summary>
	private bool TickCoin(CoinState state, float elapsed)
	{
		if (elapsed < _appearDuration)
		{
			// 阶段A：上浮 + 散开已由起点固化；此处上浮 + 放大 + 淡入。
			float p = Mathf.Clamp01(elapsed / _appearDuration);
			state.View.Rect.anchoredPosition = Vector2.Lerp(state.AppearStart, state.AppearEnd, EaseOutQuad(p));
			state.View.Rect.localScale = Vector3.one * Mathf.Lerp(_appearStartScale, _appearEndScale, EaseOutBack(p));
			state.View.CanvasGroup.alpha = Mathf.Clamp01(p * 5f);
			return false;
		}

		// 阶段B：沿二次贝塞尔曲线飞向目标（控制点 = 中点 + 上抬弧线，仿战术卡第二段但方向为上抛）。
		float flyP = Mathf.Clamp01((elapsed - _appearDuration) / _flyDuration);
		float eased = EaseInCubic(flyP);
		Vector2 control = (state.AppearEnd + _cachedTargetLocal) * 0.5f + new Vector2(0f, _arcHeight);
		state.View.Rect.anchoredPosition = QuadraticBezier(state.AppearEnd, control, _cachedTargetLocal, eased);
		state.View.Rect.localScale = Vector3.one * Mathf.Lerp(_appearEndScale, _landScale, flyP);
		state.View.CanvasGroup.alpha = 1f;

		if (flyP >= 1f)
		{
			PunchTarget();
			return true;
		}
		return false;
	}

	/// <summary>阶段A起终点换算：地块世界坐标 → 屏幕坐标（含相机背后/安全区）→ 飞行层局部坐标 + 确定性散开。</summary>
	private bool TryComputeAppearPoints(HexCellData cell, int index, out Vector2 appearStart, out Vector2 appearEnd)
	{
		appearStart = default;
		appearEnd = default;
		if (cell == null) return false;

		// (a) 地块世界坐标 → 屏幕坐标。参考 TacticalCardPresenter，但补上相机背后（z<0）处理（§4.6a）。
		if (Camera.main == null) return false;
		Vector3 screenPos = Camera.main.WorldToScreenPoint(cell.RealCenterWorldCoordinate);
		if (screenPos.z < 0f)
		{
			return false; // 相机背后：不播，避免从屏幕反侧冒币。
		}

		const float margin = 40f;
		screenPos.x = Mathf.Clamp(screenPos.x, margin, Screen.width - margin);
		screenPos.y = Mathf.Clamp(screenPos.y, margin, Screen.height - margin);

		// (b) 屏幕坐标 → 飞行层局部坐标（相机由飞行层所在 Canvas 推导，见 §4.6b）。
		Camera uiCamera = GetUICamera();
		if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_flyLayer, screenPos, uiCamera, out Vector2 cellLocal))
		{
			return false;
		}

		Vector2 scatter = ComputeScatterOffset(cell, index);
		appearStart = cellLocal + scatter;
		appearEnd = cellLocal + scatter + new Vector2(0f, _liftHeight);
		return true;
	}

	/// <summary>目标 UI → 飞行层局部坐标；失败返回 false（调用方沿用缓存落点，纯表现无结算义务）。</summary>
	private bool TryComputeTargetLocal(out Vector2 targetLocal)
	{
		targetLocal = default;
		if (_flyLayer == null || _targetUI == null) return false;

		Camera uiCamera = GetUICamera();
		// RectTransformUtility.WorldToScreenPoint(cam==null) 直接返回 (x,y)，恰为 Overlay 正确行为（§4.6c）。
		Vector2 targetScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, _targetUI.position);
		return RectTransformUtility.ScreenPointToLocalPointInRectangle(_flyLayer, targetScreen, uiCamera, out targetLocal);
	}

	/// <summary>确定性散开偏移（圆形区域均匀分布）。种子由地块 HexCoordinate 量化 + 枚序号合成（§4.4）。</summary>
	private Vector2 ComputeScatterOffset(HexCellData cell, int index)
	{
		int seed = Mathf.RoundToInt(cell.HexCoordinate.x) * 73856093
				 ^ Mathf.RoundToInt(cell.HexCoordinate.z) * 19349663
				 ^ index;
		float angle = Hash01(seed) * Mathf.PI * 2f;
		float radius = Mathf.Sqrt(Hash01(seed + 17)) * _scatterRadius; // sqrt 保证圆盘内均匀
		return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
	}

	private void ConfigureCoinView(ExplorationFlyingCoinUI coin, Vector2 startLocal)
	{
		coin.SetActive(true);
		if (coin.Image != null && _coinSprite != null)
		{
			coin.Image.sprite = _coinSprite; // 贴图由 Inspector 赋值，不写死在预制体里。
		}
		coin.Rect.anchorMin = new Vector2(0.5f, 0.5f);
		coin.Rect.anchorMax = new Vector2(0.5f, 0.5f);
		coin.Rect.pivot = new Vector2(0.5f, 0.5f);
		coin.Rect.anchoredPosition = startLocal;
		coin.Rect.localScale = Vector3.one * _appearStartScale;
		coin.CanvasGroup.alpha = 0f;
		coin.transform.SetAsLastSibling();
	}

	private void PunchTarget()
	{
		if (!_punchTargetOnArrival || _targetUI == null) return;

		// 每枚金币落地都触发一次缩放；若上一次缩放尚未结束（新金币又进入），
		// 先 Kill 并归位到基准大小再重新开始，避免 DOPunchScale 被 Kill 不归位导致的缩放累积。
		_targetUI.DOKill();
		_targetUI.localScale = _targetBaseScale;

		_targetUI.DOPunchScale(Vector3.one * _targetPunchScale, 0.3f, 6, 0.5f)
			.OnComplete(() =>
			{
				// 双保险：punch 自然结束也显式归位，杜绝残留缩放。
				if (_targetUI != null)
				{
					_targetUI.localScale = _targetBaseScale;
				}
			});
	}

	/// <summary>一枚飞币落地/被跳过：飞行中金币数 -1 并通知 HUD 刷新（显示值 +1）。</summary>
	private void DecrementInFlight()
	{
		if (InFlightGold <= 0) return;
		InFlightGold--;
		InFlightChanged?.Invoke();
	}

	// ── 对象池 ────────────────────────────────────────────

	private ExplorationFlyingCoinUI GetCoin()
	{
		if (_pool.Count > 0)
		{
			var coin = _pool.Dequeue();
			_pooled.Remove(coin);
			return coin;
		}
		return InstantiateCoin();
	}

	private ExplorationFlyingCoinUI InstantiateCoin()
	{
		if (_coinUIPrefab == null) return null;

		Transform parent = _flyLayer != null ? _flyLayer : transform;
		GameObject go = Instantiate(_coinUIPrefab, parent);
		var coin = go.GetComponent<ExplorationFlyingCoinUI>();
		if (coin == null)
		{
			Debug.LogError("[CoinFlyPresenter] _coinUIPrefab 上未挂载 ExplorationFlyingCoinUI！请手动将脚本拖到预制体上。");
			Destroy(go);
			return null;
		}
		go.SetActive(false);
		return coin;
	}

	private void ReturnToPool(ExplorationFlyingCoinUI coin)
	{
		if (coin == null || !_pooled.Add(coin)) return;
		coin.SetActive(false);
		_pool.Enqueue(coin);
	}

	// ── 缓存与换算辅助 ────────────────────────────────────

	/// <summary>换算相机一律由飞行层所在 Canvas 推导，不是目标 UI 所在 Canvas（§4.6）。</summary>
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
		// 暂停时 GameTime 不累加 → 飞币冻结；GameLoop 已必然绑定，Time.time 仅为防御性回退。
		return _gameLoop != null ? _gameLoop.GameTime : Time.time;
	}

	// ── 缓动与曲线（仿 TacticalCardPresenter） ─────────────

	private static float EaseOutQuad(float t)
	{
		return 1f - (1f - t) * (1f - t);
	}

	private static float EaseInCubic(float t)
	{
		return t * t * t;
	}

	private static float EaseOutBack(float t)
	{
		const float c1 = 1.70158f;
		const float c3 = c1 + 1f;
		return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
	}

	private static Vector2 QuadraticBezier(Vector2 a, Vector2 b, Vector2 c, float t)
	{
		float u = 1f - t;
		return u * u * a + 2f * u * t * b + t * t * c;
	}

	/// <summary>确定性伪随机 [0,1)。</summary>
	private static float Hash01(int n)
	{
		n = (n ^ 61) ^ (n >> 16);
		n = n + (n << 3);
		n = n ^ (n >> 4);
		n = n * 0x27d4eb2d;
		n = n ^ (n >> 15);
		return (n & 0x7fffffff) / (float)0x7fffffff;
	}
}
