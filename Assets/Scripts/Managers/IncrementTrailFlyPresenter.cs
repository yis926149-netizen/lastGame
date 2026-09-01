using System.Collections.Generic;
using UnityEngine;
using Zenject;

/// <summary>
/// 增量拖尾飞入表现（纯表现层）——FlyingItemFlyerBase 的第二个特化：
/// 与探索金币飞入共用同一套通用机制（对象池、活跃上限、待生成队列、集中 Update 驱动、
/// 三层坐标换算、暂停语义、落地 punch），但视觉与曲线完全不同：
///
///   · 视觉：元素本体不可见，**飞行途中拉出的那条 UITrail 拖尾就是全部表现**
///     （见 IncrementTrailFlyingUI）。因此不需要基类那套「出现阶段上浮 + 放大 + 淡入」
///     ——本类整段重写 TickItem，用单段贝塞尔直接起飞，让拖尾从起点一路拉到目标 UI。
///   · 收尾：到达目标后不立刻回池（那会一帧切断整条光带，见 UITrail.OnDisable →
///     Renderer.Unregister）。而是关闭 emitting、原地停留 _trailSettleDuration 秒，
///     让 UITrail.AgePoints 按 lifetime 逐点淘汰——光带从尾部渐渐收进落点后再回池。
///
/// 触发方式有两条，互不干扰：
///   · 业务侧直接调用 <see cref="FlyToTargetFromWorld"/>，参数是「某个物体的世界空间坐标」；
///   · 订阅探索广播，在「玩家探索到的奖励建筑是金矿」时自动以该地块世界坐标触发。
/// 最终都飞向 Inspector 里挂的 _incrementTargetUI 节点。
///
/// 职责边界（严格表现层）：不结算、不入账、不消费业务快照、不发布任何业务事件。
/// </summary>
public class IncrementTrailFlyPresenter : FlyingItemFlyerBase
{
	[Header("增量拖尾专属")]
	[Tooltip("拖尾飞行元素预制体（顶节点需挂 IncrementTrailFlyingUI + UITrail）")]
	[SerializeField] private GameObject _trailItemPrefab;
	[Tooltip("拖尾最终飞向的 UI 节点。留空则回退到基类 Inspector 里的目标 UI")]
	[SerializeField] private RectTransform _incrementTargetUI;

	[Header("拖尾飞行曲线")]
	[Tooltip("单段飞行时长（秒）")]
	[SerializeField] private float _trailFlyDuration = 0.6f;
	[Tooltip("贝塞尔弧线高度：正值向上拱，负值向下垂")]
	[SerializeField] private float _trailArcHeight = 160f;
	[Tooltip("飞行进度曲线（输入/输出均为 0..1）。默认先慢后快，拖尾在起点聚拢、临近目标拉长")]
	[SerializeField] private AnimationCurve _trailFlyCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

	[Header("拖尾收尾")]
	[Tooltip("到达目标后保持存活、仅停止发射的时长（秒）。应 ≥ UITrailProfile.lifetime，否则光带会被提前切断")]
	[SerializeField] private float _trailSettleDuration = 0.35f;

	[Header("调试")]
	[Tooltip("打开后在整条触发链路上打日志（订阅→广播过滤→起飞→生成→到达），用于排查「不出特效」")]
	[SerializeField] private bool _verboseLog = true;

	// 每枚元素的收尾起始时刻（GameTime 语义）。仅在「已到达、正在等拖尾淡出」期间存在。
	private readonly Dictionary<IFlyingItemView, float> _settleStart = new Dictionary<IFlyingItemView, float>();

	private IExplorationBroadcastSource _broadcastSource;

	/// <summary>覆写为使用本类自己的预制体字段，避免与基类 _itemPrefab 的场景引用互相干扰。</summary>
	protected override GameObject ItemPrefab => _trailItemPrefab;

	/// <summary>覆写目标节点：优先用本类暴露的 UI 节点，未赋值时回退基类字段。</summary>
	protected override RectTransform TargetUI => _incrementTargetUI != null ? _incrementTargetUI : base.TargetUI;

	[Inject]
	public void Construct(IExplorationBroadcastSource broadcastSource, GameLoop gameLoop)
	{
		GameLoop = gameLoop;
		_broadcastSource = broadcastSource;
	}

	// ── 对外触发接口 ──────────────────────────────────────

	/// <summary>
	/// 从某个物体的世界空间坐标触发一次拖尾飞行，飞向本组件暴露的目标 UI 节点。
	/// 返回 false 表示相机背后/引用缺失，本次不播放。
	/// </summary>
	public bool FlyToTargetFromWorld(Vector3 worldPos)
	{
		bool accepted = FlyFromWorld(worldPos);
		if (_verboseLog)
		{
			// false 的三种可能（基类 FlyFromWorld / FlyFromScreen）：Camera.main 为空、
			// 世界点在相机背后（screenPos.z < 0）、_flyLayer 或 TargetUI 未赋值。
			Debug.Log($"[TrailFly] FlyToTargetFromWorld world={worldPos} accepted={accepted} " +
					  $"mainCam={(Camera.main == null ? "NULL" : Camera.main.name)} target={(TargetUI == null ? "NULL" : TargetUI.name)}", this);
		}
		return accepted;
	}

	/// <summary>从某个 Transform 的世界坐标触发一次拖尾飞行（空引用安全）。</summary>
	public bool FlyToTargetFrom(Transform source)
	{
		if (source == null) return false;
		return FlyToTargetFromWorld(source.position);
	}

	// ── 基类钩子 ──────────────────────────────────────────

	/// <summary>
	/// 除直接调用外，另订阅探索广播：玩家探索到的建筑奖励为金矿时自动触发。
	/// </summary>
	protected override void SubscribeSource()
	{
		if (_incrementTargetUI == null)
		{
			Debug.LogWarning("[IncrementTrailFlyPresenter] _incrementTargetUI 未赋值，将回退到基类目标 UI。", this);
		}
		if (_trailItemPrefab == null)
		{
			Debug.LogError("[IncrementTrailFlyPresenter] _trailItemPrefab 未赋值！拖尾飞入不会有任何表现。", this);
		}

		if (_broadcastSource == null)
		{
			Debug.LogError("[IncrementTrailFlyPresenter] IExplorationBroadcastSource 未注入！金矿奖励不会自动触发拖尾。", this);
			return;
		}
		_broadcastSource.Broadcast += OnBroadcast;
		if (_verboseLog)
		{
			Debug.Log($"[TrailFly] 已订阅探索广播。prefab={(_trailItemPrefab == null ? "NULL" : _trailItemPrefab.name)} " +
					  $"target={(TargetUI == null ? "NULL" : TargetUI.name)} settle={_trailSettleDuration}s", this);
		}
	}

	protected override void UnsubscribeSource()
	{
		if (_broadcastSource != null)
		{
			_broadcastSource.Broadcast -= OnBroadcast;
		}
	}

	/// <summary>
	/// 只认「玩家 + Building 结算 + 建筑类型为金矿」这一种情况，用该地块的世界坐标起飞。
	///
	/// 阶段取 RewardPoint 而非 Settled：与金币飞入保持一致，等探索动画走到奖励表现点再播，
	/// 否则拖尾会在小人还没走到时就凭空出现。注意建筑奖励若因格子不合格/生成失败而降级，
	/// SettledRewardType 会变成 Gold（见 ExplorationRewardSystem.SettleBuilding），
	/// 此处判等 Building 天然把降级情形排除在外——降级时该由金币飞入负责表现。
	/// </summary>
	private void OnBroadcast(ExplorationAcquisition acquisition)
	{
		if (acquisition == null) return;

		// 每条广播都打一行原始信息：先确认「金矿到底以什么阶段/什么结算类型」到达本组件，
		// 再逐条对照下面的过滤条件——不出特效时，看它卡在哪一条上。
		if (_verboseLog)
		{
			BuildingConfigSO b = acquisition.BuildingConfig;
			Debug.Log($"[TrailFly] 广播 phase={acquisition.Phase} faction={acquisition.FactionId} " +
					  $"settledType={acquisition.SettledRewardType} originalType={acquisition.OriginalRewardType} " +
					  $"building={(b == null ? "NULL" : $"{b.name}(type={b.buildingType})")} cell={acquisition.Cell?.HexCoordinate}", this);
		}

		if (acquisition.FactionId != 0)
		{
			return; // 非玩家阵营，静默（AI 探索每回合都刷，打日志会淹没 Console）。
		}
		if (acquisition.Phase != ExplorationBroadcastPhase.RewardPoint)
		{
			return; // Explored/Settled 阶段不是本组件的触发点，静默。
		}
		if (acquisition.SettledRewardType != ExplorationRewardConfigSO.ExplorationRewardType.Building)
		{
			if (_verboseLog) Debug.Log($"[TrailFly] 跳过：RewardPoint 的 settledType={acquisition.SettledRewardType} 不是 Building（建筑降级为金币时即为此）", this);
			return;
		}

		BuildingConfigSO building = acquisition.BuildingConfig;
		if (building == null)
		{
			if (_verboseLog) Debug.Log("[TrailFly] 跳过：settledType=Building 但 BuildingConfig 为空", this);
			return;
		}
		if (building.buildingType != Enums.BulidingType.GoldMine)
		{
			if (_verboseLog) Debug.Log($"[TrailFly] 跳过：建筑 {building.name} 的 buildingType={building.buildingType}，不是 GoldMine", this);
			return;
		}

		if (_verboseLog) Debug.Log($"[TrailFly] 命中金矿！cell={acquisition.Cell.HexCoordinate} world={acquisition.Cell.RealCenterWorldCoordinate}", this);
		FlyToTargetFromWorld(acquisition.Cell.RealCenterWorldCoordinate);
	}

	protected override void OnDestroyed()
	{
		_settleStart.Clear();
	}

	/// <summary>
	/// 复用配置：激活元素并放到起点。本元素无图形，alpha 只为契约完整性写满；
	/// 拖尾的清空由 IncrementTrailFlyingUI.SetActive(true) 负责（必须在激活之后 Clear）。
	/// </summary>
	protected override void ConfigureForSpawn(IFlyingItemView view, Vector2 startLocal)
	{
		view.SetActive(true);

		_settleStart.Remove(view); // 复用前清掉上一轮的收尾状态。

		if (view is IncrementTrailFlyingUI trailView && trailView.Trail != null)
		{
			trailView.Trail.emitting = true; // 上一轮收尾时被关掉，复用时必须重新打开。
		}

		view.Rect.anchorMin = new Vector2(0.5f, 0.5f);
		view.Rect.anchorMax = new Vector2(0.5f, 0.5f);
		view.Rect.pivot = new Vector2(0.5f, 0.5f);
		view.Rect.anchoredPosition = startLocal;
		view.Rect.localScale = Vector3.one;
		if (view.CanvasGroup != null)
		{
			view.CanvasGroup.alpha = 1f;
		}
		view.Rect.SetAsLastSibling();

		if (_verboseLog)
		{
			var tv = view as IncrementTrailFlyingUI;
			Debug.Log($"[TrailFly] 生成元素 startLocal={startLocal} target={CachedTargetLocal} " +
					  $"trail={(tv == null ? "视图类型不是 IncrementTrailFlyingUI" : (tv.Trail == null ? "UITrail 缺失！" : $"profile={(tv.Trail.profile == null ? "NULL" : tv.Trail.profile.name)} layer={tv.Trail.layer}"))} " +
					  $"活跃数={ActiveCount}", this);
		}
	}

	/// <summary>
	/// 整段重写飞行曲线：跳过基类的出现阶段，单段贝塞尔从起点直飞目标；
	/// 到达后进入「收尾」——关闭 emitting、原地停留，让拖尾按 lifetime 自行淘汰采样点，
	/// 停留满 _trailSettleDuration 才返回 true 交由基类回池。
	/// </summary>
	protected override bool TickItem(ItemState state, float elapsed)
	{
		RectTransform rect = state.View.Rect;

		// 收尾阶段：位置锁在落点，只等拖尾散尽。
		if (_settleStart.TryGetValue(state.View, out float settleStart))
		{
			rect.anchoredPosition = CachedTargetLocal;
			return NowGameTime() - settleStart >= _trailSettleDuration;
		}

		float duration = Mathf.Max(0.01f, _trailFlyDuration);
		float p = Mathf.Clamp01(elapsed / duration);
		float eased = _trailFlyCurve != null ? _trailFlyCurve.Evaluate(p) : p;

		Vector2 control = (state.AppearStart + CachedTargetLocal) * 0.5f + new Vector2(0f, _trailArcHeight);
		rect.anchoredPosition = QuadraticBezier(state.AppearStart, control, CachedTargetLocal, eased);

		if (p < 1f)
		{
			return false;
		}

		// 刚到达：停止发射新采样点，转入收尾等待（此帧不回池，否则整条光带被一帧切断）。
		if (state.View is IncrementTrailFlyingUI trailView && trailView.Trail != null)
		{
			trailView.Trail.emitting = false;
			if (_verboseLog)
			{
				// PointCount == 0/1 意味着整段飞行没采到点——多半是位移不足 minSampleDistance，
				// 或起点与目标几乎重合，此时光带本身就不会有任何几何。
				Debug.Log($"[TrailFly] 到达目标，转入收尾。采样点数={trailView.Trail.PointCount} 落点={CachedTargetLocal}", this);
			}
		}
		_settleStart[state.View] = NowGameTime();

		// 落地反馈在「视觉到位」的这一刻给，而不是等拖尾散尽后才蹦。
		PunchTargetNow();
		return false;
	}

	/// <summary>拖尾散尽回池时不再重复 punch——反馈已在到达瞬间给过。</summary>
	protected override void OnItemArrived(ItemState state)
	{
		_settleStart.Remove(state.View);
	}

	// ── 辅助 ──────────────────────────────────────────────

	/// <summary>与基类一致的时间语义：暂停时不累加，未绑定 GameLoop 时回退 Time.time。</summary>
	private float NowGameTime()
	{
		return GameLoop != null ? GameLoop.GameTime : Time.time;
	}

	/// <summary>借基类默认实现在到达瞬间触发一次目标 punch（每枚强制重启由 FlyerPunchTarget 负责）。</summary>
	private void PunchTargetNow()
	{
		base.OnItemArrived(null);
	}
}
