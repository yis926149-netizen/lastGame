using System;
using UnityEngine;

public class ExplorationPillarEffect : MonoBehaviour
{
	[SerializeField] private AnimationCurve _riseCurve = null;
	[SerializeField] private float _riseTime = 1.0f;
	[SerializeField] private float _holdTime = 0.5f;
	[SerializeField] private float _dissolveTime = 2.0f;

	private Material _material;
	private ExplorationPillarMesh _pillarMesh;
	private float _pillarRadius;
	private float _pillarHeight;
	private Action _onDissolveStart;
	private Action<ExplorationPillarEffect> _onAnimationComplete;

	private static readonly int DissolveProgressId = Shader.PropertyToID("_DissolveProgress");
	private static readonly int PillarBottomYId = Shader.PropertyToID("_PillarBottomY");
	private static readonly int PillarHeightId = Shader.PropertyToID("_PillarHeight");

	private void Awake()
	{
		_pillarMesh = GetComponent<ExplorationPillarMesh>();
		_pillarHeight = _pillarMesh != null ? _pillarMesh.Height : 1.8f;
		_pillarRadius = _pillarMesh != null ? _pillarMesh.OuterRadius : 2.1f;
		var renderer = GetComponent<MeshRenderer>();
		_material = renderer != null ? renderer.material : null;
		Debug.Log($"[PillarEffect] Awake: pillarMesh={_pillarMesh}, radius={_pillarRadius}, height={_pillarHeight}, renderer={renderer}, material={_material}, shader={_material?.shader?.name}");
		Debug.Log($"[PillarEffect] 动画参数: riseTime={_riseTime}, holdTime={_holdTime}, dissolveTime={_dissolveTime}, totalTime={_riseTime + _holdTime + _dissolveTime}");

		if (_riseCurve == null || _riseCurve.keys.Length == 0)
		{
			_riseCurve = new AnimationCurve(
				new Keyframe(0f, 0f, 0f, 0f),
				new Keyframe(0.6f, 1.15f, 0f, 0f),
				new Keyframe(1f, 1f, -3f, 0f)
			);
		}
	}

	public void Play(Vector3 cellWorldPos, Action onDissolveStart, Action<ExplorationPillarEffect> onComplete)
	{
		_onDissolveStart = onDissolveStart;
		_onAnimationComplete = onComplete;
		if (_material == null)
		{
			Debug.LogError("[PillarEffect] Play: _material 为 null，无法播放动画！");
			return;
		}

		Debug.Log($"========================================");
		Debug.Log($"[PillarEffect] === 开始播放柱体特效 ===");
		Debug.Log($"[PillarEffect] 地块世界坐标: {cellWorldPos}");
		Debug.Log($"[PillarEffect] 柱体半径: {_pillarRadius}, 高度: {_pillarHeight}");
		Debug.Log($"[PillarEffect] 柱体底部Y: {cellWorldPos.y - _pillarHeight}, 顶部Y: {cellWorldPos.y}");
		Debug.Log($"[PillarEffect] Shader: {_material.shader.name}");
		Debug.Log($"========================================");

		_material.SetFloat(DissolveProgressId, 0f);
		_material.SetFloat(PillarBottomYId, cellWorldPos.y);
		_material.SetFloat(PillarHeightId, _pillarHeight);
		gameObject.SetActive(true);
		StartCoroutine(PlayRevealAnimation(cellWorldPos));
	}

	private System.Collections.IEnumerator PlayRevealAnimation(Vector3 cellWorldPos)
	{
		Vector3 startPos = cellWorldPos + Vector3.down * _pillarHeight;
		transform.position = startPos;
		Debug.Log($"[Pillar] [上升] 起始位置(地下): {startPos}");

		float t = 0f;
		int frame = 0;
		while (t < _riseTime)
		{
			yield return null;
			t += Time.deltaTime;
			frame++;
			float progress = _riseCurve.Evaluate(Mathf.Clamp01(t / _riseTime));
			transform.position = Vector3.Lerp(startPos, cellWorldPos, progress);
			Debug.Log($"[Pillar] [上升] frame={frame}, t={t:F3}/{_riseTime}, progress={progress:F3}, pos={transform.position}");
		}

		transform.position = cellWorldPos;
		Debug.Log($"[Pillar] [上升] 完成! 最终位置: {transform.position}, 共 {frame} 帧");
		Debug.Log($"[Pillar] [停顿] 等待 {_holdTime}s...");

		yield return new WaitForSeconds(_holdTime);

		Debug.Log($"[Pillar] [溶解] 开始, dissolveTime={_dissolveTime}");
		t = 0f;
		frame = 0;
		bool dissolveCallbackFired = false;
		while (t < _dissolveTime)
		{
			yield return null;
			t += Time.deltaTime;
			frame++;
			float progress = Mathf.Clamp01(t / _dissolveTime);
			_material.SetFloat(DissolveProgressId, progress);

			if (!dissolveCallbackFired && progress >= 0.3f)
			{
				dissolveCallbackFired = true;
				Debug.Log($"[Pillar] [溶解] progress={progress:F3} >= 0.5, 触发领土拓展回调");
				_onDissolveStart?.Invoke();
			}

			Debug.Log($"[Pillar] [溶解] frame={frame}, t={t:F3}/{_dissolveTime}, progress={progress:F3}, pos={transform.position}");
		}

		_material.SetFloat(DissolveProgressId, 1f);
		Debug.Log($"[Pillar] [溶解] 完成! 共 {frame} 帧, 即将回池");
		_onAnimationComplete?.Invoke(this);
	}
}
