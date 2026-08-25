using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 刘海屏安全区适配：将所在 RectTransform 限制在 Screen.safeArea 内。
///
/// 重要：本组件必须挂在一个「全屏子面板」上（例如 Canvas 下的 SafeArea 物体），
/// 不要挂在根 Canvas 上。原因：根 Canvas 的 Rect 被改小后，CanvasScaler 会用缩小后的
/// 尺寸重新计算 scaleFactor，导致整个 UI 的尺寸一起变小。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform _panel;
    private Rect _lastSafeArea;

    private void Awake()
    {
        _panel = GetComponent<RectTransform>();

        // 挂错位置的即时提醒：根 Canvas 上一定有 Canvas 组件（且通常有 CanvasScaler）。
        if (GetComponent<Canvas>() != null || GetComponent<CanvasScaler>() != null)
        {
            Debug.LogError("[SafeAreaFitter] 不要挂在根 Canvas 上，否则 CanvasScaler 会把 UI 整体缩小。请改挂到 Canvas 下的全屏子面板。", this);
        }

        ApplySafeArea();
    }

    private void Update()
    {
        if (Screen.safeArea != _lastSafeArea)
        {
            ApplySafeArea();
        }
    }

    private void ApplySafeArea()
    {
        Rect area = Screen.safeArea;
        if (area.width <= 0f || area.height <= 0f)
        {
            return;
        }

        Vector2 min = area.position;
        Vector2 max = area.position + area.size;
        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        _panel.anchorMin = min;
        _panel.anchorMax = max;
        _panel.offsetMin = Vector2.zero;
        _panel.offsetMax = Vector2.zero;
        _lastSafeArea = area;
    }
}
