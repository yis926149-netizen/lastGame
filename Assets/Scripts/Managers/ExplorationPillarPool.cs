using System.Collections.Generic;
using UnityEngine;
using Zenject;

/// <summary>
/// 探索动画表现（石柱升起 / 飞盘砸落）。
/// 【探索结果纯广播】订阅统一广播的 Explored（仅玩家阵营）播放动画；
/// 动画奖励触发点（石柱溶解30% / 飞盘撞击）回调 _explorationService.SignalRewardPoint(cell)，
/// 不再自行暴露 ExplorationRewardPoint 事件。
/// </summary>
public class ExplorationPillarPool : MonoBehaviour
{
	[Header("特效方案选择")]
	[SerializeField] private ExplorationEffectStyle _effectStyle = ExplorationEffectStyle.PillarRise;

	[Header("方案一：石柱升起")]
	[SerializeField] private GameObject _pillarPrefab;

	[Header("方案二：飞盘砸落")]
	[SerializeField] private GameObject _diskPrefab;

	[Header("对象池配置")]
	[SerializeField] private int _initialPoolSize = 5;

	private IExplorationService _explorationService;
	private IExplorationBroadcastSource _broadcastSource;
	private Queue<ExplorationPillarEffect> _pillarPool = new Queue<ExplorationPillarEffect>();
	private Queue<ExplorationDiskEffect> _diskPool = new Queue<ExplorationDiskEffect>();
	private readonly HashSet<ExplorationPillarEffect> _pooledPillars = new HashSet<ExplorationPillarEffect>();
	private readonly HashSet<ExplorationDiskEffect> _pooledDisks = new HashSet<ExplorationDiskEffect>();

	private void Awake()
	{
	}

	private void OnEnable()
	{
	}

	[Inject]
	public void Construct(IExplorationService explorationService, IExplorationBroadcastSource broadcastSource)
	{
		_explorationService = explorationService;
		_broadcastSource = broadcastSource;
	}

	private void Start()
	{
		if (_explorationService == null)
		{
			Debug.LogError("[PillarPool] _explorationService 未注入！Zenject 可能未找到该组件。");
			return;
		}
		if (_broadcastSource == null)
		{
			Debug.LogError("[PillarPool] _broadcastSource 未注入！Zenject 可能未找到该组件。");
			return;
		}

		switch (_effectStyle)
		{
			case ExplorationEffectStyle.PillarRise:
				if (_pillarPrefab == null)
				{
					Debug.LogError("[PillarPool] _pillarPrefab 未赋值！请在 Inspector 中拖入 Prefab。");
					return;
				}
				for (int i = 0; i < _initialPoolSize; i++)
					ReturnPillarToPool(InstantiatePillar());
				break;

			case ExplorationEffectStyle.DiskSmash:
				if (_diskPrefab == null)
				{
					Debug.LogError("[PillarPool] _diskPrefab 未赋值！请在 Inspector 中拖入 Prefab。");
					return;
				}
				for (int i = 0; i < _initialPoolSize; i++)
					ReturnDiskToPool(InstantiateDisk());
				break;
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

	private void OnBroadcast(ExplorationAcquisition acquisition)
	{
		if (acquisition == null || acquisition.FactionId != 0)
			return;
		if (acquisition.Phase != ExplorationBroadcastPhase.Explored)
			return;

		HexCellData cell = acquisition.Cell;
		if (cell == null) return;

		switch (_effectStyle)
		{
			case ExplorationEffectStyle.PillarRise:
				PlayPillarEffect(cell);
				break;

			case ExplorationEffectStyle.DiskSmash:
				PlayDiskEffect(cell);
				break;

			case ExplorationEffectStyle.None:
				_explorationService.SignalRewardPoint(cell);
				break;
		}
	}

	/// <summary>仅播放发现表现，不触发探索服务的占领、收割或奖励流程。</summary>
	public void PlayRevealEffect(HexCellData cell)
	{
		if (cell == null) return;

		switch (_effectStyle)
		{
			case ExplorationEffectStyle.PillarRise:
				if (_pillarPrefab == null) return;
				var pillar = GetPillar();
				pillar.Play(cell.RealCenterWorldCoordinate,
					onDissolveStart: null,
					onComplete: ReturnPillarToPool);
				break;

			case ExplorationEffectStyle.DiskSmash:
				if (_diskPrefab == null) return;
				var disk = GetDisk();
				disk.Play(cell.RealCenterWorldCoordinate,
					onImpact: null,
					onComplete: ReturnDiskToPool);
				break;
		}
	}

	private void PlayPillarEffect(HexCellData cell)
	{
		var pillar = GetPillar();
		pillar.Play(cell.RealCenterWorldCoordinate,
			onDissolveStart: () =>
			{
				_explorationService.SignalRewardPoint(cell);
			},
			onComplete: ReturnPillarToPool);
	}

	private void PlayDiskEffect(HexCellData cell)
	{
		var disk = GetDisk();
		disk.Play(cell.RealCenterWorldCoordinate,
			onImpact: () =>
			{
				_explorationService.SignalRewardPoint(cell);
			},
			onComplete: ReturnDiskToPool);
	}

	private ExplorationPillarEffect GetPillar()
	{
		if (_pillarPool.Count > 0)
		{
			var effect = _pillarPool.Dequeue();
			_pooledPillars.Remove(effect);
			return effect;
		}
		return InstantiatePillar();
	}

	private ExplorationDiskEffect GetDisk()
	{
		if (_diskPool.Count > 0)
		{
			var effect = _diskPool.Dequeue();
			_pooledDisks.Remove(effect);
			return effect;
		}
		return InstantiateDisk();
	}

	private ExplorationPillarEffect InstantiatePillar()
	{
		GameObject go = Instantiate(_pillarPrefab, transform);
		go.SetActive(false);
		var effect = go.GetComponent<ExplorationPillarEffect>();
		if (effect == null)
		{
			Debug.LogWarning($"[PillarPool] Prefab 上没有 ExplorationPillarEffect，自动添加");
			effect = go.AddComponent<ExplorationPillarEffect>();
		}
		return effect;
	}

	private ExplorationDiskEffect InstantiateDisk()
	{
		GameObject go = Instantiate(_diskPrefab, transform);
		go.SetActive(false);
		var effect = go.GetComponent<ExplorationDiskEffect>();
		if (effect == null)
		{
			Debug.LogWarning($"[PillarPool] Prefab 上没有 ExplorationDiskEffect，自动添加");
			effect = go.AddComponent<ExplorationDiskEffect>();
		}
		return effect;
	}

	private void ReturnPillarToPool(ExplorationPillarEffect effect)
	{
		if (effect == null || !_pooledPillars.Add(effect)) return;
		effect.gameObject.SetActive(false);
		_pillarPool.Enqueue(effect);
	}

	private void ReturnDiskToPool(ExplorationDiskEffect effect)
	{
		if (effect == null || !_pooledDisks.Add(effect)) return;
		effect.gameObject.SetActive(false);
		_diskPool.Enqueue(effect);
	}
}
