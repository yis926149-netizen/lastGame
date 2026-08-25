using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 屏幕适配工具：UI 位移/缩放一律使用 Canvas 参考单位，不直接使用设备像素。
/// 本项目 CanvasScaler 基准为 1080x1920，且按高度匹配，因此参考高度为 1920。
/// </summary>
public static class UIScreenHelper
{
    /// <summary>与场景 CanvasScaler 的 ReferenceResolution.y 保持一致。</summary>
    public const float ReferenceHeight = 1920f;

    /// <summary>读取所在 Canvas 的参考高度，找不到时回退到本项目基准高度。</summary>
    public static float CanvasHeight(Component component)
    {
        if (component != null)
        {
            var scaler = component.GetComponentInParent<CanvasScaler>();
            if (scaler != null) return scaler.referenceResolution.y;
        }

        return ReferenceHeight;
    }
}
