using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// 探索金币奖励表现层：奖励系统在金币到账时登记"待播"（PlayCoinAt），
/// 探索动画到达奖励触发点（ExplorationPillarPool.ExplorationRewardPoint，
/// 石柱=溶解30%，飞盘=撞击）时播放。
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

	/// <summary>待播登记的超时上限：超过该时长仍无动画奖励点事件则视为异常中断，清理登记。</summary>
	private const float PendingExpirySeconds = 10f;

	private struct PendingCoin
	{
		public float TimeStamp;
		public int Amount;

		public PendingCoin(float timeStamp, int amount)
		{
			TimeStamp = timeStamp;
			Amount = amount;
		}
	}

	private ExplorationPillarPool _pillarPool;
	private readonly Queue<ExplorationCoinEffect> _coinPool = new Queue<ExplorationCoinEffect>();
	private readonly HashSet<ExplorationCoinEffect> _pooledCoins = new HashSet<ExplorationCoinEffect>();
	private readonly Queue<ExplorationAddCoinsUIEffect> _uiPool = new Queue<ExplorationAddCoinsUIEffect>();
	private readonly HashSet<ExplorationAddCoinsUIEffect> _pooledUIs = new HashSet<ExplorationAddCoinsUIEffect>();
	private readonly Dictionary<Vector3, PendingCoin> _pendingCoins = new Dictionary<Vector3, PendingCoin>();

	[Inject]
	public void Construct(ExplorationPillarPool pillarPool)
	{
		_pillarPool = pillarPool;
	}

	private void Start()
	{
		if (_pillarPool == null)
		{
			Debug.LogError("[CoinPresenter] ExplorationPillarPool 未注入！Zenject 可能未找到该组件。");
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

		_pillarPool.ExplorationRewardPoint += OnExplorationRewardPoint;
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

	private void OnDestroy()
	{
		if (_pillarPool != null)
		{
			_pillarPool.ExplorationRewardPoint -= OnExplorationRewardPoint;
		}
		_pendingCoins.Clear();
	}

	/// <summary>由探索奖励系统调用：登记该地块待播金币表现（动画奖励触发点到达时播放）。</summary>
	public void PlayCoinAt(HexCellData cell, int amount)
	{
		if (cell == null) return;
		PruneExpiredPendings();
		_pendingCoins[cell.HexCoordinate] = new PendingCoin(Time.realtimeSinceStartup, amount);
	}

	private void OnExplorationRewardPoint(HexCellData cell)
	{
		if (cell == null) return;
		PruneExpiredPendings();
		if (!_pendingCoins.TryGetValue(cell.HexCoordinate, out PendingCoin pending)) return;
		_pendingCoins.Remove(cell.HexCoordinate);

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
					ui.Play(cell.RealCenterWorldCoordinate, pending.Amount, (RectTransform)overlayCanvas.transform, ReturnUIToPool);
				}
				break;
		}
	}

	/// <summary>清理超时未消费的待播登记（探索动画异常中断时避免条目泄漏）。</summary>
	private void PruneExpiredPendings()
	{
		if (_pendingCoins.Count == 0) return;
		var expired = new List<Vector3>();
		foreach (var kv in _pendingCoins)
		{
			if (Time.realtimeSinceStartup - kv.Value.TimeStamp > PendingExpirySeconds)
				expired.Add(kv.Key);
		}
		foreach (var coord in expired)
		{
			_pendingCoins.Remove(coord);
		}
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
