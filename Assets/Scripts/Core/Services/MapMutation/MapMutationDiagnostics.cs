using UnityEngine;

//****************************************
// 【动态地图-阶段五】地图变化诊断开关（MapMutationDiagnostics）
// 开发辅助能力（§阶段五-诊断扩展）：批量提交日志 + 脏 Chunk 高亮可视化。
// 默认全关（不刷屏、不影响性能）；调试时经 execute_script / 测试打开。
//****************************************

public static class MapMutationDiagnostics
{
    /// <summary>提交完成后打印批量日志摘要（CommitId/补丁数/脏格数/脏 Chunk 数/耗时）。</summary>
    public static bool EnableCommitLogging = false;

    /// <summary>提交完成后用 HexHighlightRenderer 高亮脏格（DebugDirtyChunk 通道）。</summary>
    public static bool EnableDirtyChunkHighlight = false;

    /// <summary>脏 Chunk 高亮颜色（品红，区别于卡牌/可达/选中通道）。</summary>
    public static Color DirtyChunkHighlightColor = new Color(1f, 0f, 1f, 0.5f);

    /// <summary>
    /// 【波浪测试-2026-08-05】禁用 keep-below clip 顶出（§13.2/§20-10）。
    /// 全图波浪变化混有"未参与动画的低格/水域格"：clip 平面按参与格最低 startY 起算，
    /// 上升时会裁掉同 Chunk 内不参与动画的更低格。由 MapWaveTestController 在测试期间
    /// 临时开启、动画结束（含异常/取消）恢复为 false。
    /// </summary>
    public static bool DisableKeepBelowClip = false;

    /// <summary>格式化脏位标记为可读字符串（调试日志用）。</summary>
    public static string FormatDirtyFlags(MapDirtyFlags flags)
    {
        if (flags == MapDirtyFlags.None) return "None";
        return flags.ToString();
    }
}
