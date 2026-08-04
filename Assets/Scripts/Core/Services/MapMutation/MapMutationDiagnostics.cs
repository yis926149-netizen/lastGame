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

    /// <summary>脏 Chunk 高亮停留时长（秒），到时自动清除。</summary>
    public static float HighlightDurationSeconds = 5f;

    /// <summary>脏 Chunk 高亮颜色（品红，区别于卡牌/可达/选中通道）。</summary>
    public static Color DirtyChunkHighlightColor = new Color(1f, 0f, 1f, 0.5f);

    /// <summary>格式化脏位标记为可读字符串（调试日志用）。</summary>
    public static string FormatDirtyFlags(MapDirtyFlags flags)
    {
        if (flags == MapDirtyFlags.None) return "None";
        return flags.ToString();
    }
}
