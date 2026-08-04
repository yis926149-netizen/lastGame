using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 方案二：六边形飞盘砸落特效。
/// 流程：地下弹出 → 上抛（抛硬币式水平翻转）→ 滞空 → 下坠撞击 → 势力范围扩散环。
/// 撞击瞬间触发 onImpact 回调（执行领土/收割/奖励），动画结束触发 onComplete（回池）。
/// </summary>
public class ExplorationDiskEffect : MonoBehaviour
{
	[Header("轨迹时间（秒）")]
	[SerializeField] private float _popupTime = 0.05f;   // 地下弹出到地面
	[SerializeField] private float _riseTime = 0.15f;    // 地面继续飞到顶点
	[SerializeField] private float _holdTime = 0.05f;    // 顶点滞空
	[SerializeField] private float _fallTime = 0.15f;    // 下坠到撞击

	[Header("轨迹高度（世界单位）")]
	[SerializeField] private float _undergroundDepth = 1.0f; // 起始埋深
	[SerializeField] private float _throwHeight = 2.5f;      // 顶点相对地面高度

	[Header("缓动曲线")]
	[SerializeField] private AnimationCurve _popupCurve = null; // 出土（OutBack）
	[SerializeField] private AnimationCurve _riseCurve = null;  // 上抛（减速到顶）
	[SerializeField] private AnimationCurve _fallCurve = null;  // 下坠（加速砸落）

	[Header("抛硬币式翻转")]
	[SerializeField] private float _riseFlipSpeed = 360f;  // 上升段翻速（度/秒）
	[SerializeField] private float _fallFlipSpeed = 720f;  // 下坠段翻速（度/秒）

	[Header("撞击嵌入")]
	[SerializeField] private float _embedTime = 0.1f;      // 撞击后盘体缩扁嵌地

	[Header("势力范围扩散环（可选子对象）")]
	[SerializeField] private Transform _ringObject = null; // 子物体：六边形环 mesh
	[SerializeField] private float _ringMaxScale = 3.5f;   // 光环终态缩放倍数
	[SerializeField] private float _ringTime = 0.35f;      // 光环扩散时长
	[SerializeField] private float _ringStartAlpha = 0.6f; // 光环起始不透明度
	[SerializeField] private AnimationCurve _ringScaleCurve = null; // EaseOutCubic

	[Header("撞击特效（可选）")]
	[SerializeField] private ParticleSystem _dustBurst = null;  // 尘土爆发

	private Action _onImpact;
	private Action<ExplorationDiskEffect> _onComplete;
	private Vector3 _baseScale;
	private Material _ringMaterial;
	private Renderer _ringRenderer;

	private void Awake()
	{
		_baseScale = transform.localScale;

		if (_popupCurve == null || _popupCurve.keys.Length == 0)
			_popupCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.7f, 1.1f), new Keyframe(1f, 1f));
		if (_riseCurve == null || _riseCurve.keys.Length == 0)
			_riseCurve = new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f)); // 减速到顶
		if (_fallCurve == null || _fallCurve.keys.Length == 0)
			_fallCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f)); // 加速砸落
		if (_ringScaleCurve == null || _ringScaleCurve.keys.Length == 0)
			_ringScaleCurve = new AnimationCurve(new Keyframe(0f, 0f, 3f, 3f), new Keyframe(1f, 1f, 0f, 0f)); // EaseOut

		if (_ringObject != null)
		{
			_ringRenderer = _ringObject.GetComponent<Renderer>();
			if (_ringRenderer != null)
				_ringMaterial = _ringRenderer.material;
		}
	}

	public void Play(Vector3 cellWorldPos, Action onImpact, Action<ExplorationDiskEffect> onComplete)
	{
		_onImpact = onImpact;
		_onComplete = onComplete;

		transform.localScale = _baseScale;
		transform.rotation = Quaternion.identity;
		gameObject.SetActive(true);
		StartCoroutine(PlaySmashAnimation(cellWorldPos));
	}

	private IEnumerator PlaySmashAnimation(Vector3 cellWorldPos)
	{
		float cellY = cellWorldPos.y;
		float startY = cellY - _undergroundDepth;
		float peakY = cellY + _throwHeight;

		// 隐藏光环
		if (_ringObject != null) _ringObject.gameObject.SetActive(false);

		// --- 1. 地下弹出（startY → cellY），OutBack ---
		transform.position = new Vector3(cellWorldPos.x, startY, cellWorldPos.z);
		float t = 0f;
		while (t < _popupTime)
		{
			t += Time.deltaTime;
			float p = _popupCurve.Evaluate(Mathf.Clamp01(t / _popupTime));
			SetY(cellWorldPos, Mathf.LerpUnclamped(startY, cellY, p));
			Flip(_riseFlipSpeed);
			yield return null;
		}

		// --- 2. 上抛（cellY → peakY），减速到顶 ---
		t = 0f;
		while (t < _riseTime)
		{
			t += Time.deltaTime;
			float p = _riseCurve.Evaluate(Mathf.Clamp01(t / _riseTime));
			SetY(cellWorldPos, Mathf.LerpUnclamped(cellY, peakY, p));
			Flip(_riseFlipSpeed);
			yield return null;
		}

		// --- 3. 滞空 ---
		SetY(cellWorldPos, peakY);
		float hold = 0f;
		while (hold < _holdTime)
		{
			hold += Time.deltaTime;
			Flip(_riseFlipSpeed);
			yield return null;
		}

		// --- 4. 下坠（peakY → cellY），加速砸落 ---
		t = 0f;
		while (t < _fallTime)
		{
			t += Time.deltaTime;
			float p = _fallCurve.Evaluate(Mathf.Clamp01(t / _fallTime));
			SetY(cellWorldPos, Mathf.LerpUnclamped(peakY, cellY, p));
			Flip(_fallFlipSpeed);
			yield return null;
		}

		// --- 5. ★ 撞击瞬间 ★ ---
		SetY(cellWorldPos, cellY);
		transform.rotation = Quaternion.identity; // 骤停，正面朝上

		_onImpact?.Invoke();                       // 领土 + 收割 + 奖励

		if (_dustBurst != null)
		{
			_dustBurst.transform.position = new Vector3(cellWorldPos.x, cellY, cellWorldPos.z);
			_dustBurst.Play();
		}

		StartCoroutine(ExpandRing(cellWorldPos, cellY));

		// --- 6. 盘体嵌入地面（scaleY → 0）---
		t = 0f;
		Vector3 fromScale = _baseScale;
		Vector3 toScale = new Vector3(_baseScale.x, 0f, _baseScale.z);
		while (t < _embedTime)
		{
			t += Time.deltaTime;
			transform.localScale = Vector3.Lerp(fromScale, toScale, Mathf.Clamp01(t / _embedTime));
			yield return null;
		}
		transform.localScale = toScale;

		// --- 7. 等待光环播完后回池 ---
		float wait = Mathf.Max(0f, _ringTime - _embedTime);
		if (wait > 0f) yield return new WaitForSeconds(wait);

		transform.localScale = _baseScale;
		_onComplete?.Invoke(this);
	}

	private IEnumerator ExpandRing(Vector3 cellWorldPos, float cellY)
	{
		if (_ringObject == null) yield break;

		_ringObject.gameObject.SetActive(true);
		_ringObject.position = new Vector3(cellWorldPos.x, cellY + 0.05f, cellWorldPos.z);

		float t = 0f;
		while (t < _ringTime)
		{
			t += Time.deltaTime;
			float p = Mathf.Clamp01(t / _ringTime);
			float eased = _ringScaleCurve.Evaluate(p);
			float scale = Mathf.Lerp(1f, _ringMaxScale, eased);
			_ringObject.localScale = new Vector3(scale, 1f, scale);

			if (_ringMaterial != null && _ringMaterial.HasProperty("_Color"))
			{
				Color c = _ringMaterial.color;
				c.a = Mathf.Lerp(_ringStartAlpha, 0f, p);
				_ringMaterial.color = c;
			}
			yield return null;
		}
		_ringObject.gameObject.SetActive(false);
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
