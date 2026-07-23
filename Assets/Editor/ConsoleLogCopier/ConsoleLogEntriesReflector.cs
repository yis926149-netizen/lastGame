using System;
using System.Collections.Generic;
using System.Reflection;

internal static class ConsoleLogEntriesReflector
{
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static MethodInfo _startGettingEntries;
    private static MethodInfo _endGettingEntries;
    private static MethodInfo _getEntryInternal;
    private static FieldInfo _messageField;
    private static FieldInfo _modeField;
    private static Type _logEntryType;
    private static string _unavailableReason;

    static ConsoleLogEntriesReflector()
    {
        Initialize();
    }

    public static bool IsAvailable => string.IsNullOrEmpty(_unavailableReason);
    public static string UnavailableReason => _unavailableReason;

    public static bool TryGetVisibleEntries(out List<ConsoleLogEntry> entries, out string error)
    {
        entries = new List<ConsoleLogEntry>();
        error = null;

        if (!IsAvailable)
        {
            error = _unavailableReason;
            return false;
        }

        bool started = false;
        try
        {
            int count = (int)_startGettingEntries.Invoke(null, null);
            started = true;

            for (int row = 0; row < count; row++)
            {
                object logEntry = Activator.CreateInstance(_logEntryType);
                object[] arguments = { row, logEntry };
                bool found = (bool)_getEntryInternal.Invoke(null, arguments);
                if (!found)
                {
                    continue;
                }

                logEntry = arguments[1];
                string message = _messageField.GetValue(logEntry) as string ?? string.Empty;

                // GetEntryInternal already fills LogEntry.message with both the log text and its
                // call stack (ConsoleWindow itself renders the detail pane straight from message).
                int mode = (int)_modeField.GetValue(logEntry);
                entries.Add(new ConsoleLogEntry(message, mode));
            }

            return true;
        }
        catch (Exception exception)
        {
            error = GetInnermostMessage(exception);
            entries.Clear();
            return false;
        }
        finally
        {
            if (started)
            {
                try
                {
                    _endGettingEntries.Invoke(null, null);
                }
                catch
                {
                    // Do not hide the original read error if the internal cleanup also fails.
                }
            }
        }
    }

    private static void Initialize()
    {
        try
        {
            // Tuanjie/Unity 2022.3 exposes these as UnityEditor.LogEntries/LogEntry; older editors
            // used the UnityEditorInternal namespace, so both are attempted for forward safety.
            Type logEntriesType = FindType("UnityEditor.LogEntries", "UnityEditorInternal.LogEntries");
            _logEntryType = FindType("UnityEditor.LogEntry", "UnityEditorInternal.LogEntry");

            if (logEntriesType == null || _logEntryType == null)
            {
                _unavailableReason = "未找到 LogEntries/LogEntry 内部类型。";
                return;
            }

            _startGettingEntries = logEntriesType.GetMethod("StartGettingEntries", StaticFlags, null, Type.EmptyTypes, null);
            _endGettingEntries = logEntriesType.GetMethod("EndGettingEntries", StaticFlags, null, Type.EmptyTypes, null);
            _getEntryInternal = logEntriesType.GetMethod(
                "GetEntryInternal",
                StaticFlags,
                null,
                new[] { typeof(int), _logEntryType },
                null);
            _messageField = _logEntryType.GetField("message", InstanceFlags);
            _modeField = _logEntryType.GetField("mode", InstanceFlags);

            if (_startGettingEntries == null || _endGettingEntries == null || _getEntryInternal == null ||
                _messageField == null || _modeField == null)
            {
                _unavailableReason = "Console 内部 API 与预期的 2022.3 结构不一致。";
            }
        }
        catch (Exception exception)
        {
            _unavailableReason = GetInnermostMessage(exception);
        }
    }

    private static Type FindType(params string[] candidateNames)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (string fullName in candidateNames)
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static string GetInnermostMessage(Exception exception)
    {
        while (exception.InnerException != null)
        {
            exception = exception.InnerException;
        }

        return exception.Message;
    }
}
