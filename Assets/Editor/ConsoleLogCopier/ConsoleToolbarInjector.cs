using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
internal static class ConsoleToolbarInjector
{
    internal const string ButtonName = "console-log-copier-copy-visible-button";

    private const double PollIntervalSeconds = 0.5d;
    private const float ButtonLeft = 345f;
    private const float ButtonWidth = 108f;
    private const float MinimumWindowWidth = 570f;

    private static readonly Type ConsoleWindowType;
    private static double _nextPollTime;

    static ConsoleToolbarInjector()
    {
        ConsoleWindowType = FindConsoleWindowType();
        EditorApplication.update += OnEditorUpdate;
        AssemblyReloadEvents.beforeAssemblyReload += RemoveFromAll;
    }

    private static void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup < _nextPollTime)
        {
            return;
        }

        _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;
        foreach (EditorWindow window in GetOpenConsoleWindows())
        {
            EnsureInjected(window);
        }
    }

    internal static void EnsureInjected(EditorWindow consoleWindow)
    {
        if (consoleWindow == null || consoleWindow.rootVisualElement.Q<Button>(ButtonName) != null)
        {
            return;
        }

        var button = new ToolbarButton(() => ConsoleLogCopyService.CopyVisibleLogs(consoleWindow))
        {
            name = ButtonName,
            text = "复制可见日志",
            tooltip = "复制当前筛选后可见的日志及调用堆栈"
        };

        button.SetEnabled(ConsoleLogCopyService.IsAvailable);
        if (!ConsoleLogCopyService.IsAvailable)
        {
            button.tooltip = $"当前版本不兼容：{ConsoleLogCopyService.UnavailableReason}";
        }

        button.style.position = Position.Absolute;
        button.style.left = ButtonLeft;
        button.style.top = 1f;
        button.style.width = ButtonWidth;
        button.style.height = 19f;

        consoleWindow.rootVisualElement.Add(button);
        UpdateVisibility(button, consoleWindow.rootVisualElement.resolvedStyle.width);
        consoleWindow.rootVisualElement.RegisterCallback<GeometryChangedEvent>(evt =>
            UpdateVisibility(button, evt.newRect.width));
    }

    internal static void RemoveFromAll()
    {
        foreach (EditorWindow window in GetOpenConsoleWindows())
        {
            VisualElement button = window.rootVisualElement.Q(ButtonName);
            button?.RemoveFromHierarchy();
        }
    }

    private static void UpdateVisibility(VisualElement button, float windowWidth)
    {
        button.style.display = windowWidth >= MinimumWindowWidth ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static EditorWindow[] GetOpenConsoleWindows()
    {
        return ConsoleWindowType == null
            ? Array.Empty<EditorWindow>()
            : Resources.FindObjectsOfTypeAll(ConsoleWindowType).OfType<EditorWindow>().ToArray();
    }

    private static Type FindConsoleWindowType()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEditor.ConsoleWindow", false))
            .FirstOrDefault(type => type != null && typeof(EditorWindow).IsAssignableFrom(type));
    }
}
