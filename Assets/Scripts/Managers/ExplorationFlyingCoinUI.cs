using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 探索金币飞入表现：单枚金币 UI 的引用持有器。
/// 不自行 Update、不保存动画状态——动画状态与进度由 ExplorationCoinFlyPresenter 集中驱动
/// （暂停语义统一走 GameLoop.GameTime，见 ExplorationCoinFlyPresenter）。
/// 组件需手动挂载在 CoinUIPrefab 上（RectTransform + Image 为必需，CanvasGroup 缺失时自动补挂）。
/// </summary>
public class ExplorationFlyingCoinUI : MonoBehaviour, IFlyingItemView
{
	/// <summary>金币 UI 的 RectTransform（由 Presenter 每帧写入 anchoredPosition / localScale）。</summary>
	public RectTransform Rect { get; private set; }

	/// <summary>金币 UI 的 Image（仅用于禁用 raycastTarget，避免阻挡点击）。</summary>
	public Image Image { get; private set; }

	/// <summary>金币 UI 的 CanvasGroup（淡入淡出由 Presenter 写入 alpha）。</summary>
	public CanvasGroup CanvasGroup { get; private set; }

	private void Awake()
	{
		Rect = transform as RectTransform;
		Image = GetComponent<Image>();
		CanvasGroup = GetComponent<CanvasGroup>();
		if (CanvasGroup == null)
		{
			CanvasGroup = gameObject.AddComponent<CanvasGroup>();
		}

		// 纯表现：不参与射线、不参与交互，防止遮挡地块/按钮点击。
		CanvasGroup.interactable = false;
		CanvasGroup.blocksRaycasts = false;
		if (Image != null)
		{
			Image.raycastTarget = false;
		}
	}

	/// <summary>切换整枚金币 UI 的显隐（池化复用时调用）。</summary>
	public void SetActive(bool active)
	{
		if (gameObject.activeSelf != active)
		{
			gameObject.SetActive(active);
		}
	}
}
