using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 【P0-2 Shader 变体预热】在开局前预热预录制的 Shader 变体集，消除 WebGL / 微信小游戏
/// 运行期 shader 首次进入画面时的同步编译卡顿（累计卡顿约 11.7s 中除长帧外的主要来源）。
///
/// 变体集资产由编辑器工具生成：
///   Tools/微信小游戏性能优化/生成 Shader 变体预热集
/// （对应 <c>Assets/Editor/ShaderVariantPreloadTool.cs</c>），产物为
/// <c>Assets/Resources/PreloadedShaders.shadervariants</c>，并注册进 Graphics Settings 的
/// 「Preloaded Shaders」列表——这一步保证变体不被 Stripping 裁掉，是 WarmUp 能真正生效的前提。
///
/// 本类只负责「何时预热」：启动后、首场景（StartScene）加载完成后立即同步预热。
/// StartScene 只有一枚开始按钮（延迟 3s 才可点击），预热发生在玩家可操作之前，
/// 因此把这笔编译开销从 GameScene 开局前移到了菜单阶段。
/// </summary>
public static class ShaderPreloader
{
    /// <summary>Resources 下的变体集资源名（不含扩展名、不含目录）。</summary>
    private const string ResourcePath = "PreloadedShaders";

    /// <summary>变体集是否已预热（WarmUp 幂等，重复调用为 no-op，但保留开关便于埋点）。</summary>
    public static bool IsWarmedUp { get; private set; }

    /// <summary>上一次 WarmUp 实际耗时（毫秒），供真机对照累计卡顿变化。</summary>
    public static double LastWarmUpMs { get; private set; }

    /// <summary>上一次预热命中的变体数（0 表示未找到资产）。</summary>
    public static int LastVariantCount { get; private set; }

    /// <summary>
    /// 启动钩子：首场景加载完成后自动预热。
    /// 使用 AfterSceneLoad 而非 BeforeSceneLoad：图形设备此时已确定就绪，语义上等价于
    /// 计划中的「StartScene 内」预热；如需更早（尽量藏进微信 loading 遮罩）可改回
    /// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoWarmUp()
    {
#if UNITY_EDITOR
        // 编辑器内每次进 PlayMode 都自动预热会拖慢迭代，且对 WebGL 卡顿无意义；
        // 需要验证时可从任意脚本手动调用 ShaderPreloader.WarmUpAll()。
        return;
#else
        WarmUpAll();
#endif
    }

    /// <summary>
    /// 加载并预热 PreloadedShaders 变体集。资产不存在时仅打日志、不抛异常，
    /// 避免在未生成变体集的旧工程上阻断启动。
    /// </summary>
    public static void WarmUpAll()
    {
        if (IsWarmedUp) return;

        ShaderVariantCollection collection = null;
        try
        {
            collection = Resources.Load<ShaderVariantCollection>(ResourcePath);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ShaderPreloader] 加载 {ResourcePath} 失败：{e.Message}");
        }

        if (collection == null)
        {
            Debug.LogWarning("[ShaderPreloader] 未找到 Resources/PreloadedShaders.shadervariants，跳过预热。" +
                             "请在 Unity 中运行 Tools/微信小游戏性能优化/生成 Shader 变体预热集。");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            collection.WarmUp();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ShaderPreloader] WarmUp 抛异常：{e.Message}");
        }
        sw.Stop();

        LastWarmUpMs = sw.Elapsed.TotalMilliseconds;
        LastVariantCount = collection.variantCount;
        IsWarmedUp = true;

        Debug.Log($"[ShaderPreloader] 预热完成：{collection.variantCount} 个变体，耗时 {LastWarmUpMs:F1} ms");
    }
}
