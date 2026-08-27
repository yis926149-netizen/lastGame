using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// 探索金币奖励表现层：订阅统一广播的 RewardPoint 阶段，
/// 只处理玩家阵营且实际结算为金币（SettledRewardType == Gold）的载荷，
/// 使用 SettledGoldAmount 播放金币表现（含建筑降级金币，不漏播、不误播）。
/// 表现方式由 Inspector 下拉框切换（CoinModel=金币模型弹跳 / AddCoinsUI=漂字）。
/// </summary>
public class ExplorationCoinPresenter : MonoBehaviour
{
	/// <summary>金币奖励表现方式。</summary>
	public enum CoinRewardEffectStyle
	{
		CoinModel,
		AddCoinsUI,
	}

	[Header("特效方案选择")]
	[SerializeField] private CoinRewardEffectStyle _effectStyle = CoinRewardEffectStyle.CoinModel;

	[Header("方案一：金币模型弹跳")]
	[SerializeField] private GameObject _coinPrefab;

	[Header("方案二：AddCoinsUI 漂字")]
	[SerializeField] private GameObject _addCoinsUIPrefab;

	[Header("对象池配置")]
	[SerializeField] private int _initialPoolSize = 4;

	private IExplorationBroadcastSource _broadcastSource;
	private readonly Queue<ExplorationCoinEffect> _coinPool = new Queue<ExplorationCoinEffect>();
	private readonly HashSet<ExplorationCoinEffect> _pooledCoins = new HashSet<ExplorationCoinEffect>();
	private readonly Queue<ExplorationAddCoinsUIEffect> _uiPool = new Queue<ExplorationAddCoinsUIEffect>();
	private readonly HashSet<ExplorationAddCoinsUIEffect> _pooledUIs = new HashSet<ExplorationAddCoinsUIEffect>();

	[Inject]
	public void Construct(IExplorationBroadcastSource broadcastSource)
	{
		_broadcastSource = broadcastSource;
	}

	private void Start()
	{
		if (_broadcastSource == null)
		{
			Debug.LogError("[CoinPresenter] IExplorationBroadcastSource 未注入！Zenject 可能未找到该组件。");
			return;
		}
		if (_coinPrefab == null && _addCoinsUIPrefab == null)
		{
			Debug.LogError("[CoinPresenter] 金币 Prefab 与 AddCoinsUI Prefab 均未赋值！请在 Inspector 中拖入对应 Prefab。");
			return;
		}

		for (int i = 0; i < _initialPoolSize; i++)
		{
			if (_coinPrefab != null) ReturnCoinToPool(InstantiateCoin());
			if (_addCoinsUIPrefab != null) ReturnUIToPool(InstantiateUI());
		}

		_broadcastSource.Broadcast += OnBroadcast;
	}

	private void OnDestroy()
	{
		if (_broadcastSource != null)
		{
			_broadcastSource.Broadcast -= OnBroadcast;
		}
	}

	/// <summary>只处理玩家 RewardPoint 且实际结算为金币的载荷。</summary>
	private void OnBroadcast(ExplorationAcquisition acquisition)
	{
		if (acquisition == null) return;
		if (acquisition.Phase != ExplorationBroadcastPhase.RewardPoint) return;
		if (acquisition.FactionId != 0) return;
		if (acquisition.SettledRewardType != ExplorationRewardConfigSO.ExplorationRewardType.Gold) return;
		if (acquisition.Cell == null || acquisition.SettledGoldAmount <= 0) return;

		PlayCoinNow(acquisition.Cell, acquisition.SettledGoldAmount);
	}

	/// <summary>立即播放金币表现（使用对象池，按 Inspector 选择方案）。</summary>
	private void PlayCoinNow(HexCellData cell, int amount)
	{
		switch (_effectStyle)
		{
			case CoinRewardEffectStyle.CoinModel:
				var coin = GetCoin();
				if (coin != null) coin.Play(cell.RealCenterWorldCoordinate, ReturnCoinToPool);
				break;

			case CoinRewardEffectStyle.AddCoinsUI:
				var ui = GetUI();
				if (ui != null)
				{
					Canvas overlayCanvas = FindActiveOverlayCanvas();
					if (overlayCanvas == null)
					{
						Debug.LogError("[CoinPresenter] 未找到激活的 Screen Space Overlay Canvas，AddCoinsUI 无法显示。");
						break;
					}
					// 若实例父级已失活，重新挂到当前激活的 Canvas 下
					if (ui.transform.parent == null || !ui.transform.parent.gameObject.activeInHierarchy)
					{
						ui.transform.SetParent(overlayCanvas.transform, false);
					}
					ui.Play(cell.RealCenterWorldCoordinate, amount, (RectTransform)overlayCanvas.transform, ReturnUIToPool);
				}
				break;
		}
	}

	/// <summary>动态查找当前激活的 Screen Space Overlay Canvas（场景中存在多个 World Space Canvas，不可缓存）。</summary>
	private Canvas FindActiveOverlayCanvas()
	{
		foreach (var canvas in FindObjectsOfType<Canvas>())
		{
			if (canvas.renderMode == UnityEngine.RenderMode.ScreenSpaceOverlay && canvas.isActiveAndEnabled)
				return canvas;
		}
		return null;
	}

	// ── 方案一：金币模型对象池 ────────────────

	private ExplorationCoinEffect GetCoin()
	{
		if (_coinPool.Count > 0)
		{
			var coin = _coinPool.Dequeue();
			_pooledCoins.Remove(coin);
			return coin;
		}
		return InstantiateCoin();
	}

	private ExplorationCoinEffect InstantiateCoin()
	{
		if (_coinPrefab == null) return null;
		GameObject go = Instantiate(_coinPrefab, transform);
		var coin = go.GetComponent<ExplorationCoinEffect>();
		if (coin == null)
		{
			Debug.LogError("[CoinPresenter] 金币 Prefab 上未挂载 ExplorationCoinEffect！请手动将脚本拖到预制体上。");
			Destroy(go);
			return null;
		}
		go.SetActive(false);
		return coin;
	}

	private void ReturnCoinToPool(ExplorationCoinEffect coin)
	{
		if (coin == null || !_pooledCoins.Add(coin)) return;
		coin.gameObject.SetActive(false);
		_coinPool.Enqueue(coin);
	}

	// ── 方案二：AddCoinsUI 对象池 ────────────

	private ExplorationAddCoinsUIEffect GetUI()
	{
		if (_uiPool.Count > 0)
		{
			var ui = _uiPool.Dequeue();
			_pooledUIs.Remove(ui);
			return ui;
		}
		return InstantiateUI();
	}

	private ExplorationAddCoinsUIEffect InstantiateUI()
	{
		if (_addCoinsUIPrefab == null) return null;
		Canvas overlayCanvas = FindActiveOverlayCanvas();
		Transform parent = overlayCanvas != null ? overlayCanvas.transform : transform;
		GameObject go = Instantiate(_addCoinsUIPrefab, parent);
		var ui = go.GetComponent<ExplorationAddCoinsUIEffect>();
		if (ui == null)
		{
			Debug.LogError("[CoinPresenter] AddCoinsUI Prefab 上未挂载 ExplorationAddCoinsUIEffect！请手动将脚本拖到预制体上。");
			Destroy(go);
			return null;
		}
		go.SetActive(false);
		return ui;
	}

	private void ReturnUIToPool(ExplorationAddCoinsUIEffect ui)
	{
		if (ui == null || !_pooledUIs.Add(ui)) return;
		ui.gameObject.SetActive(false);
		_uiPool.Enqueue(ui);
	}
}
