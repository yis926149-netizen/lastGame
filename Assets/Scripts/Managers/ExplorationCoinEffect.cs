using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 探索金币奖励特效：探索动画结束后，单枚金币从地块弹出 →
/// 上升（抛硬币式翻转）→ 顶点滞空 → 加速下坠 → 落地缩小消失回池。
/// 纯表现层，无业务副作用；动画由协程 + Time.deltaTime 驱动（与探索特效一致）。
/// </summary>
public class ExplorationCoinEffect : MonoBehaviour
{
	[Header("轨迹时间（秒）")]
	[SerializeField] private float _popupTime = 0.05f;  // 弹出（缩放 0→1，OutBack 过冲）
	[SerializeField] private float _riseTime = 0.6f;    // 地面到顶点（减速）
	[SerializeField] private float _holdTime = 0.15f;   // 顶点滞空
	[SerializeField] private float _fallTime = 0.3f;    // 下坠（加速）
	[SerializeField] private float _shrinkTime = 0.15f; // 落地缩小消失

	[Header("轨迹高度（世界单位）")]
	[SerializeField] private float _throwHeight = 2.0f; // 顶点相对地面高度

	[Header("抛硬币式翻转（度/秒）")]
	[SerializeField] private float _riseFlipSpeed = 540f;
	[SerializeField] private float _fallFlipSpeed = 1080f;

	[Header("缓动曲线")]
	[SerializeField] private AnimationCurve _popupCurve = null; // OutBack 过冲
	[SerializeField] private AnimationCurve _riseCurve = null;  // 减速到顶
	[SerializeField] private AnimationCurve _fallCurve = null;  // 加速下坠

	private Vector3 _baseScale;
	private Quaternion _baseRotation;
	private Action<ExplorationCoinEffect> _onAnimationComplete;

	private void Awake()
	{
		// 组件需手动挂载在金币预制体上；实例化时（对象激活）立即捕获原始 Transform
		_baseScale = transform.localScale;
		_baseRotation = transform.localRotation;

		// 特效实例不参与射线与物理（防止遮挡地块点击）
		Collider col = GetComponent<Collider>();
		if (col != null) col.enabled = false;

		if (_popupCurve == null || _popupCurve.keys.Length == 0)
			_popupCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.7f, 1.15f), new Keyframe(1f, 1f));
		if (_riseCurve == null || _riseCurve.keys.Length == 0)
			_riseCurve = new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
		if (_fallCurve == null || _fallCurve.keys.Length == 0)
			_fallCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
	}

	public void Play(Vector3 cellWorldPos, Action<ExplorationCoinEffect> onComplete)
	{
		_onAnimationComplete = onComplete;
		transform.position = new Vector3(cellWorldPos.x, cellWorldPos.y, cellWorldPos.z);
		transform.localRotation = _baseRotation;
		transform.localScale = Vector3.zero;
		gameObject.SetActive(true);
		StartCoroutine(PlayCoinAnimation(cellWorldPos));
	}

	private IEnumerator PlayCoinAnimation(Vector3 cellWorldPos)
	{
		float cellY = cellWorldPos.y;
		float peakY = cellY + _throwHeight;

		// --- 1. 弹出：缩放 OutBack 0→1 ---
		float t = 0f;
		while (t < _popupTime)
		{
			t += Time.deltaTime;
			float p = _popupCurve.Evaluate(Mathf.Clamp01(t / _popupTime));
			transform.localScale = _baseScale * p;
			yield return null;
		}
		transform.localScale = _baseScale;

		// --- 2. 上升 + 抛硬币式翻转（减速到顶）---
		t = 0f;
		while (t < _riseTime)
		{
			t += Time.deltaTime;
			float p = _riseCurve.Evaluate(Mathf.Clamp01(t / _riseTime));
			SetY(cellWorldPos, Mathf.LerpUnclamped(cellY, peakY, p));
			Flip(_riseFlipSpeed);
			yield return null;
		}

		// --- 3. 顶点滞空（继续缓转，避免骤停感，与飞盘方案一致）---
		SetY(cellWorldPos, peakY);
		float hold = 0f;
		while (hold < _holdTime)
		{
			hold += Time.deltaTime;
			Flip(_riseFlipSpeed);
			yield return null;
		}

		// --- 4. 下坠 + 加速翻转 ---
		t = 0f;
		while (t < _fallTime)
		{
			t += Time.deltaTime;
			float p = _fallCurve.Evaluate(Mathf.Clamp01(t / _fallTime));
			SetY(cellWorldPos, Mathf.LerpUnclamped(peakY, cellY, p));
			Flip(_fallFlipSpeed);
			yield return null;
		}

		// --- 5. 落地：正面朝上，缩小消失 ---
		SetY(cellWorldPos, cellY);
		transform.localRotation = _baseRotation;
		t = 0f;
		while (t < _shrinkTime)
		{
			t += Time.deltaTime;
			float p = Mathf.Clamp01(t / _shrinkTime);
			transform.localScale = _baseScale * (1f - p);
			yield return null;
		}

		transform.localScale = Vector3.zero;
		_onAnimationComplete?.Invoke(this);
	}

	private void SetY(Vector3 cellWorldPos, float y)
	{
		transform.position = new Vector3(cellWorldPos.x, y, cellWorldPos.z);
	}

	private void Flip(float speed)
	{
		// 抛硬币式：绕世界 X 轴翻转
		transform.Rotate(Vector3.right, speed * Time.deltaTime, Space.World);
	}
}
