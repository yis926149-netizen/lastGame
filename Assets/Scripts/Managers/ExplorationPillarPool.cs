using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class ExplorationPillarPool : MonoBehaviour
{
	[SerializeField] private GameObject _pillarPrefab;
	[SerializeField] private int _initialPoolSize = 5;

	private IExplorationService _explorationService;
	private Queue<ExplorationPillarEffect> _pool = new Queue<ExplorationPillarEffect>();

	private void Awake()
	{
		Debug.Log($"[PillarPool] Awake: enabled={enabled}, activeInHierarchy={gameObject.activeInHierarchy}");
	}

	private void OnEnable()
	{
		Debug.Log($"[PillarPool] OnEnable");
	}

	[Inject]
	public void Construct(IExplorationService explorationService)
	{
		_explorationService = explorationService;
		Debug.Log($"[PillarPool] Construct: explorationService = {explorationService}");
	}

	private void Start()
	{
		Debug.Log($"[PillarPool] Start: _pillarPrefab = {_pillarPrefab}, _explorationService = {_explorationService}");

		if (_pillarPrefab == null)
		{
			Debug.LogError("[PillarPool] _pillarPrefab 未赋值！请在 Inspector 中拖入 Prefab。");
			return;
		}
		if (_explorationService == null)
		{
			Debug.LogError("[PillarPool] _explorationService 未注入！Zenject 可能未找到该组件。");
			return;
		}

		for (int i = 0; i < _initialPoolSize; i++)
		{
			InstantiateToPool();
		}
		Debug.Log($"[PillarPool] 预热完成，池中数量: {_pool.Count}");

		_explorationService.CellExplored += OnCellExplored;
		Debug.Log("[PillarPool] 已订阅 CellExplored 事件");
	}

	private void OnDestroy()
	{
		if (_explorationService != null)
		{
			_explorationService.CellExplored -= OnCellExplored;
		}
	}

	private void OnCellExplored(HexCellData cell)
	{
		Debug.Log($"================================================");
		Debug.Log($"[PillarPool] ★ 收到 CellExplored 事件 ★");
		Debug.Log($"[PillarPool] Hex坐标: {cell?.HexCoordinate}");
		Debug.Log($"[PillarPool] CenterWorld: {cell?.CenterWorldCoordinate}");
		Debug.Log($"[PillarPool] RealCenterWorld (扰动后): {cell?.RealCenterWorldCoordinate}");
		Debug.Log($"[PillarPool] Height: {cell?.Height}, IsExplored: {cell?.IsExplored}");
		Debug.Log($"================================================");
		if (cell == null) return;
		var pillar = Get();
		Debug.Log($"[PillarPool] 取到pillar实例: {pillar?.name}, activeSelf={pillar?.gameObject?.activeSelf}");

		// 柱体动画完成后 → 执行领土拓展、收割、视觉刷新 → 然后回池
		var capturedCell = cell;
		pillar.Play(cell.RealCenterWorldCoordinate,
			onDissolveStart: () =>
			{
				Debug.Log($"[PillarPool] 溶解开始，执行 CompleteExploration for {capturedCell.HexCoordinate}");
				_explorationService.CompleteExploration(capturedCell);
			},
			onComplete: (effect) =>
			{
				ReturnToPool(effect);
			});
	}

	private ExplorationPillarEffect Get()
	{
		if (_pool.Count > 0)
		{
			return _pool.Dequeue();
		}
		return InstantiateToPool();
	}

	private ExplorationPillarEffect InstantiateToPool()
	{
		GameObject go = Instantiate(_pillarPrefab, transform);
		Debug.Log($"[PillarPool] 实例化: {go.name}");
		go.SetActive(false);
		var effect = go.GetComponent<ExplorationPillarEffect>();
		if (effect == null)
		{
			Debug.LogWarning($"[PillarPool] Prefab 上没有 ExplorationPillarEffect，自动添加");
			effect = go.AddComponent<ExplorationPillarEffect>();
		}
		_pool.Enqueue(effect);
		return effect;
	}

	private void ReturnToPool(ExplorationPillarEffect effect)
	{
		if (effect == null) return;
		Debug.Log($"[PillarPool] 返回对象池: {effect.gameObject.name}");
		effect.gameObject.SetActive(false);
		_pool.Enqueue(effect);
	}
}
