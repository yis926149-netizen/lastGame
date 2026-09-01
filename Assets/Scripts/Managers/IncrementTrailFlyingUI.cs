using UnityEngine;

/// <summary>
/// 增量拖尾飞行元素：单个"纯拖尾"飞行元素的引用持有器。
///
/// 与 ExplorationFlyingCoinUI 的区别：本元素**没有任何图形组件**（无 Image、无文本）——
/// 视觉全部来自挂在同节点上的 UITrail：元素本体不可见，飞行途中拉出的那条弧光拖尾就是全部表现。
/// 因此 CanvasGroup 仅为满足 IFlyingItemView 契约而存在（基类会写 alpha），不影响拖尾可见性
/// （UITrail 走自己的 Renderer 与顶点色，不受本节点 CanvasGroup 影响）。
///
/// 池化要点：UITrail 的采样点是「留在运动路径上」的历史轨迹，对象池取出复用时若不清空，
/// 会从上一次的落点拉出一条横穿屏幕的光带。故 SetActive(true) 时必须 Clear()。
/// 不自行 Update、不保存动画状态——动画由 IncrementTrailFlyPresenter 集中驱动。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class IncrementTrailFlyingUI : MonoBehaviour, IFlyingItemView
{
	/// <summary>RectTransform（由 Presenter 每帧写入 anchoredPosition / localScale）。</summary>
	public RectTransform Rect { get; private set; }

	/// <summary>CanvasGroup（基类会写 alpha；本元素无图形，仅为契约完整性存在，缺失时自动补挂）。</summary>
	public CanvasGroup CanvasGroup { get; private set; }

	/// <summary>拖尾组件——本元素唯一的视觉来源。</summary>
	public UITrail Trail { get; private set; }

	private void Awake()
	{
		Rect = transform as RectTransform;
		Trail = GetComponent<UITrail>();
		if (Trail == null)
		{
			Debug.LogError($"[IncrementTrailFlyingUI] '{gameObject.name}' 未挂载 UITrail！本元素视觉完全依赖拖尾，缺失将什么都看不到。", this);
		}

		CanvasGroup = GetComponent<CanvasGroup>();
		if (CanvasGroup == null)
		{
			CanvasGroup = gameObject.AddComponent<CanvasGroup>();
		}

		// 纯表现：不参与射线、不参与交互，防止遮挡地块/按钮点击。
		CanvasGroup.interactable = false;
		CanvasGroup.blocksRaycasts = false;
	}

	/// <summary>
	/// 切换显隐（池化复用时调用）。取出复用时清空拖尾历史采样点，
	/// 否则会从上一次落点拉出一条横穿屏幕的光带（见 UITrail.Clear 注释）。
	/// </summary>
	public void SetActive(bool active)
	{
		if (gameObject.activeSelf != active)
		{
			gameObject.SetActive(active);
		}

		if (active && Trail != null)
		{
			// 注意：Clear 必须在 SetActive(true) 之后——UITrail.OnEnable 会重新绑定 Renderer，
			// 在此之前调用 Clear 拿不到 Renderer，脏标记不会生效。
			Trail.Clear();
		}
	}
}
