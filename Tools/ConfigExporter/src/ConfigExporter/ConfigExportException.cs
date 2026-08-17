using System;

namespace ConfigExporter;

/// <summary>导出器预期的可读错误，用于在退出码与日志中直接呈现。</summary>
public sealed class ConfigExportException : Exception
{
    public ConfigExportException(string message) : base(message)
    {
    }
}
