using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 探索金币奖励漂字特效（方案二 AddCoinsUI）：从地块下方漂升到顶点 →
/// 滞空 → CanvasGroup 淡出消散。Screen Space Overlay 映射：
/// 每帧把世界坐标（地块 + 当前高度偏移）映射为屏幕位置，文字恒定大小。
/// 组件需手动挂载在 AddCoinsUI 预制体上。
/// 动画由 Update 状态机驱动（不依赖协程，避免 inactive 对象启动协程报错）。
/// </summary>
public class ExplorationAddCoinsUIEffect : MonoBehaviour
{
	[Header("漂升（秒/世界单位）")]
	[SerializeField] private float _driftUpTime = 0.6f;   // 漂升时长
	[SerializeField] private float _driftUpHeight = 2.0f; // 顶点相对地块的高度
	[SerializeField] private float _startBelow = 1.0f;    // 起点在地块下方深度

	[Header("滞空（秒）")]
	[SerializeField] private float _holdTime = 0.5f;

	[Header("消散（秒）")]
	[SerializeField] private float _fadeTime = 0.4f;

	[Header("金额文本")]
	[SerializeField] private GameObject _amountTextObject; // 该物体的 Text 组件显示金币数量

	private enum Phase
	{
		Idle,
		DriftUp,
		Hold,
		Fade,
	}

	private RectTransform _rectTransform;
	private CanvasGroup _canvasGroup;
	private Text _amountText;
	private Camera _camera;
	private RectTransform _canvasRect; // 父 Canvas 的 RectTransform，用于屏幕映射
	private Vector3 _worldAnchor;      // 地块世界坐标
	private float _currentYOffset;     // 当前相对地块的高度偏移
	private bool _isActive;            // 是否正在播放（控制 LateUpdate 映射）
	private Action<ExplorationAddCoinsUIEffect> _onAnimationComplete;
	private Phase _phase = Phase.Idle;
	private float _phaseTime;

	private void Awake()
	{
		_rectTransform = GetComponent<RectTransform>();
		_canvasGroup = GetComponent<CanvasGroup>();
		if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
		if (_amountTextObject != null)
		{
			_amountText = _amountTextObject.GetComponent<Text>();
		}
		_camera = Camera.main;
	}

	/// <summary>播放漂字：从地块下方漂升到顶点，滞空后淡出消散。</summary>
	public void Play(Vector3 cellWorldPos, int amount, RectTransform parentCanvasRect, Action<ExplorationAddCoinsUIEffect> onComplete)
	{
		_onAnimationComplete = onComplete;
		_canvasRect = parentCanvasRect;
		if (_amountText != null) _amountText.text = "+" + amount;
		_canvasGroup.alpha = 1f;
		_worldAnchor = cellWorldPos;
		_currentYOffset = -_startBelow;
		_isActive = true;
		_phase = Phase.DriftUp;
		_phaseTime = 0f;
		gameObject.SetActive(true);
	}

	private void Update()
	{
		if (_phase == Phase.Idle) return;

		_phaseTime += Time.deltaTime;

		switch (_phase)
		{
			case Phase.DriftUp:
			{
				float p = Mathf.Clamp01(_phaseTime / _driftUpTime);
				float eased = 1f - (1f - p) * (1f - p); // EaseOutQuad 减速到顶
				_currentYOffset = Mathf.Lerp(-_startBelow, _driftUpHeight, eased);
				if (p >= 1f)
				{
					_currentYOffset = _driftUpHeight;
					_phase = Phase.Hold;
					_phaseTime = 0f;
				}
				break;
			}

			case Phase.Hold:
				if (_phaseTime >= _holdTime)
				{
					_phase = Phase.Fade;
					_phaseTime = 0f;
				}
				break;

			case Phase.Fade:
				_canvasGroup.alpha = 1f - Mathf.Clamp01(_phaseTime / _fadeTime);
				if (_phaseTime >= _fadeTime)
				{
					_canvasGroup.alpha = 0f;
					_phase = Phase.Idle;
					_isActive = false;
					_onAnimationComplete?.Invoke(this);
				}
				break;
		}
	}

	private void LateUpdate()
	{
		if (!_isActive || _camera == null || _canvasRect == null || _rectTransform == null) return;

		Vector3 worldPos = _worldAnchor + Vector3.up * _currentYOffset;
		Vector3 screenPos = _camera.WorldToScreenPoint(worldPos);
		if (screenPos.z < 0) return; // 相机背后不显示

		// Screen Space Overlay：映射时 camera 传 null
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out Vector2 localPos))
		{
			_rectTransform.anchoredPosition = localPos;
		}
	}
}
