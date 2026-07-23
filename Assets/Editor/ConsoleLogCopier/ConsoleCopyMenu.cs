using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class ConsoleCopyMenu
{
    private const string MenuPath = "Tools/Console/复制可见日志";

    [MenuItem(MenuPath)]
    private static void CopyVisibleLogs()
    {
        ConsoleLogCopyService.CopyVisibleLogs(GetOpenConsoleWindow());
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateCopyVisibleLogs()
    {
        return ConsoleLogCopyService.IsAvailable;
    }

    private static EditorWindow GetOpenConsoleWindow()
    {
        Type consoleWindowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEditor.ConsoleWindow", false))
            .FirstOrDefault(type => type != null);

        return consoleWindowType == null
            ? null
            : Resources.FindObjectsOfTypeAll(consoleWindowType).OfType<EditorWindow>().FirstOrDefault();
    }
}
