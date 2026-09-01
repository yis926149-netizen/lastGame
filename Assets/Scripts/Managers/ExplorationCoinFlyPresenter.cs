using UnityEngine;
using Zenject;

/// <summary>
/// 探索金币奖励“飞入”表现接收者（纯表现层）——继承 FlyingItemFlyerBase 的金币特化：
/// 订阅统一广播的 RewardPoint 阶段，只在「玩家 + SettledRewardType == Gold」时，
/// 按 SettledGoldAmount 生成对应个数的金币 UI（含建筑降级金币），
/// 先在地块屏幕位置出现（上浮 + 散开 + 放大），再逐枚沿二次贝塞尔曲线飞向 targetUI。
///
/// 通用机制（对象池、活跃上限、待生成队列、集中 Update 驱动、三层坐标换算、散开、
/// 两段曲线、落地 punch、暂停语义）全部由基类 FlyingItemFlyerBase 承担；
/// 本类只保留金币专属：广播订阅与 Gold 过滤、飞行中金币数（方案 B 显示层延迟）、
/// 金币贴图写回、以及触发计数与落地/跳过计数的追平。
///
/// 职责边界（严格表现层）：不结算、不入账、不消费奖励快照、不发布阶段。
/// 钱包金额在 Settled 已到账，本组件只负责飞币表现，绝不调用 GoldWallet.AddGold。
/// 动画由 GameLoop.GameTime 驱动，暂停即冻结；目标 UI 位置每帧换算，支持目标移动/动画/分辨率变化。
/// </summary>
public class ExplorationCoinFlyPresenter : FlyingItemFlyerBase
{
	[Header("金币专属")]
	[Tooltip("金币 UI 预制体")]
	[SerializeField] private GameObject _coinUIPrefab;
	[Tooltip("金币贴图，运行时写到每枚飞币的 Image")]
	[SerializeField] private Sprite _coinSprite;

	private IExplorationBroadcastSource _broadcastSource;

	// ── 方案 B：显示层延迟 ────────────────────────────────
	// 飞行中金币数：真实钱包已在 Settled 入账、但 HUD 尚未显示的部分（每枚飞币落地/跳过时 -1）。
	// HUD 显示值 = 真实钱包 - InFlightGold，飞币落地逐枚追平。
	public int InFlightGold { get; private set; }

	/// <summary>飞行中金币数变化时触发（HUD 据此刷新显示值）。</summary>
	public event System.Action InFlightChanged;

	/// <summary>覆写为保留原有序列化字段名 _coinUIPrefab（不迁移到基类 _itemPrefab），避免破坏场景 Inspector 引用。</summary>
	protected override GameObject ItemPrefab => _coinUIPrefab;

	[Inject]
	public void Construct(IExplorationBroadcastSource broadcastSource, GameLoop gameLoop)
	{
		GameLoop = gameLoop;
		_broadcastSource = broadcastSource;
	}

	/// <summary>订阅探索奖励广播。基类 Start 末尾调用。</summary>
	protected override void SubscribeSource()
	{
		if (_broadcastSource == null)
		{
			Debug.LogError("[CoinFlyPresenter] IExplorationBroadcastSource 未注入！Zenject 可能未找到该组件。");
			return;
		}

		if (_coinSprite == null)
		{
			Debug.LogWarning("[CoinFlyPresenter] _coinSprite 未赋值！请在 Inspector 中拖入金币贴图，否则飞币将显示为空白。");
		}

		_broadcastSource.Broadcast += OnBroadcast;
	}

	/// <summary>取消订阅。基类 OnDestroy 开头调用。</summary>
	protected override void UnsubscribeSource()
	{
		if (_broadcastSource != null)
		{
			_broadcastSource.Broadcast -= OnBroadcast;
		}
	}

	/// <summary>基类清理完成后清自身状态。</summary>
	protected override void OnDestroyed()
	{
		InFlightGold = 0;
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

		// 整批入队前预检相机：若相机背后/缺失导致整批被 FlyBatchFromWorld 拒绝，
		// 则这 amount 枚一枚都不会播，需立即按整批数量追平「飞行中金币数」，避免显示值永久少算。
		// （基类逐枚换算失败会走 OnItemSkipped() 单独追平，这是整批层面的追平。）
		if (Camera.main == null)
		{
			DecrementInFlightBy(acquisition.SettledGoldAmount);
			return;
		}

		Vector3 screenPos = Camera.main.WorldToScreenPoint(acquisition.Cell.RealCenterWorldCoordinate);
		if (screenPos.z < 0f)
		{
			DecrementInFlightBy(acquisition.SettledGoldAmount);
			return;
		}

		if (!FlyBatchFromWorld(acquisition.Cell.RealCenterWorldCoordinate, acquisition.SettledGoldAmount))
		{
			DecrementInFlightBy(acquisition.SettledGoldAmount);
		}
	}

	/// <summary>某枚飞币生成时换算失败被跳过：按枚追平「飞行中金币数」。</summary>
	protected override void OnItemSkipped()
	{
		DecrementInFlight();
	}

	/// <summary>一枚飞币落地完成：按枚追平「飞行中金币数」。基类默认做目标 punch 反馈。</summary>
	protected override void OnItemArrived(ItemState state)
	{
		DecrementInFlight();
		base.OnItemArrived(state);
	}

	/// <summary>复用配置：激活飞币并写回金币贴图、设置初始状态（锚点/枢轴居中、起点、初始缩放、透明、置顶）。</summary>
	protected override void ConfigureForSpawn(IFlyingItemView view, Vector2 startLocal)
	{
		view.SetActive(true);

		// 类型安全：本类池化元素一定是 ExplorationFlyingCoinUI（ItemPrefab 决定）；防御性判断。
		if (view is ExplorationFlyingCoinUI coin && coin.Image != null && _coinSprite != null)
		{
			coin.Image.sprite = _coinSprite; // 贴图由 Inspector 赋值，不写死在预制体里。
		}

		view.Rect.anchorMin = new Vector2(0.5f, 0.5f);
		view.Rect.anchorMax = new Vector2(0.5f, 0.5f);
		view.Rect.pivot = new Vector2(0.5f, 0.5f);
		view.Rect.anchoredPosition = startLocal;
		view.Rect.localScale = Vector3.one * AppearStartScale;
		if (view.CanvasGroup != null)
		{
			view.CanvasGroup.alpha = 0f;
		}
		view.Rect.SetAsLastSibling();
	}

	/// <summary>一整批飞币被拒绝（相机背后/缺失/入队失败）：整批追平「飞行中金币数」。</summary>
	private void DecrementInFlightBy(int amount)
	{
		if (amount <= 0) return;
		if (InFlightGold <= 0) return;
		InFlightGold -= amount;
		if (InFlightGold < 0) InFlightGold = 0;
		InFlightChanged?.Invoke();
	}

	/// <summary>一枚飞币落地/被跳过：飞行中金币数 -1 并通知 HUD 刷新（显示值 +1）。</summary>
	private void DecrementInFlight()
	{
		if (InFlightGold <= 0) return;
		InFlightGold--;
		InFlightChanged?.Invoke();
	}
}
