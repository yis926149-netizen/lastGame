using UnityEngine;

/// <summary>
/// UI 弧光拖尾 · 参数容器（ScriptableObject）。
///
/// 双重收益：
///   1) 策划可在 Assets 里存预设复用（不同特效各存一份）；
///   2) 它同时是拖尾 Renderer 的合批分组键——同一 profile 实例的所有 Emitter
///      共享一个 Renderer 节点（§3.1）。颜色差异走顶点色，因此同 profile 下
///      每条尾巴仍可独立着色（tint）。
///
/// 注意：lifetime / minSampleDistance / maxPoints 等采样参数在 Emitter 侧消费；
///       material / texture / widthCurve / colorGradient / 动态参数在 Renderer 侧消费。
/// </summary>
[CreateAssetMenu(fileName = "UITrailProfile", menuName = "Game/UI/UITrail Profile")]
public class UITrailProfile : ScriptableObject
{
    /// <summary>Renderer 自动创建材质时使用的 Shader 名（Shader.Find 用）。</summary>
    public const string DefaultShaderName = "Custom/UITrailGlow";

    [Header("渲染")]
    [Tooltip("可选：自定义材质。留空时 Renderer 运行时按 DefaultShaderName 自动创建（推荐）。")]
    public Material material;

    [Tooltip("软边光带贴图（中心过曝白芯 → 暖黄扩散 → 边缘 alpha 归零）。留空时用纯白贴图（无软边，仅调试）。")]
    public Texture2D texture;

    [Header("宽度 / 颜色（按尾巴归一化位置采样：0=尾端最旧，1=头端最新）")]
    [Tooltip("半宽曲线（画布单位）。头部更宽更亮、尾部收窄。")]
    public AnimationCurve widthCurve = CreateDefaultWidthCurve();

    [Tooltip("颜色渐变：rgb 为色调，a 为强度（尾端 a 归零实现淡出）。")]
    public Gradient colorGradient = CreateDefaultGradient();

    [Header("采样与老化")]
    [Tooltip("单点存活时长（秒）。超出自动移除，停止移动时尾巴自然淡出。")]
    public float lifetime = 0.6f;

    [Tooltip("位移超过该距离才追加新采样点（画布单位）。")]
    public float minSampleDistance = 8f;

    [Tooltip("单条尾巴采样点上限（环形缓冲复用，2 顶点/点）。")]
    [Range(4, 128)] public int maxPoints = 32;

    [Header("动态感（_Time.y 驱动，CPU 每帧零开销）")]
    [Tooltip("整体动画开关。关闭后为静态拖尾。")]
    public bool animate = true;

    [Tooltip("流动速度（能量沿尾巴奔跑）。")]
    public float flowSpeed = 1.6f;

    [Tooltip("流动强度。")]
    public float flowStrength = 0.35f;

    [Tooltip("呼吸速度。")]
    public float breathSpeed = 2f;

    [Tooltip("呼吸幅度（整体亮度缓慢起伏）。")]
    public float breathAmount = 0.22f;

    private void Reset()
    {
        // CreateAssetMenu / Inspector Reset 时调用，把全部参数拉回默认。
        ApplyDefaults();
    }

    /// <summary>填充所有默认参数（CreateAssetMenu 与编辑器生成脚本共用，保持单一事实源）。</summary>
    public void ApplyDefaults()
    {
        lifetime = 0.6f;
        minSampleDistance = 8f;
        maxPoints = 32;
        animate = true;
        flowSpeed = 1.6f;
        flowStrength = 0.35f;
        breathSpeed = 2f;
        breathAmount = 0.22f;

        widthCurve = CreateDefaultWidthCurve();
        colorGradient = CreateDefaultGradient();
    }

    private static AnimationCurve CreateDefaultWidthCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 2f),
            new Keyframe(0.5f, 8f),
            new Keyframe(1f, 12f));
    }

    private static Gradient CreateDefaultGradient()
    {
        Gradient g = new Gradient();
        g.mode = GradientMode.Blend;
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.84f, 0.28f), 0f),
                new GradientColorKey(new Color(1f, 0.84f, 0.28f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.9f, 0.5f),
                new GradientAlphaKey(1f, 1f),
            });
        return g;
    }
}
