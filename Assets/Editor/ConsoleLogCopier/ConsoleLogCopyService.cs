using UnityEditor;
using UnityEngine;

internal static class ConsoleLogCopyService
{
    private static bool _hasLoggedCompatibilityWarning;

    public static bool IsAvailable => ConsoleLogEntriesReflector.IsAvailable;
    public static string UnavailableReason => ConsoleLogEntriesReflector.UnavailableReason;

    public static void CopyVisibleLogs(EditorWindow notificationWindow)
    {
        if (!ConsoleLogEntriesReflector.TryGetVisibleEntries(out var entries, out string error))
        {
            ShowNotification(notificationWindow, "复制失败：Console API 不兼容");
            if (!_hasLoggedCompatibilityWarning)
            {
                Debug.LogWarning($"[ConsoleLogCopier] 无法读取控制台日志：{error}");
                _hasLoggedCompatibilityWarning = true;
            }

            return;
        }

        string text = ConsoleLogFormatter.Format(entries);
        if (string.IsNullOrEmpty(text))
        {
            ShowNotification(notificationWindow, "没有可见日志");
            return;
        }

        EditorGUIUtility.systemCopyBuffer = text;
        ShowNotification(notificationWindow, $"已复制 {entries.Count} 条日志");
    }

    private static void ShowNotification(EditorWindow window, string message)
    {
        if (window != null)
        {
            window.ShowNotification(new GUIContent(message));
        }
        else
        {
            Debug.Log($"[ConsoleLogCopier] {message}");
        }
    }
}
