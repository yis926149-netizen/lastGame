using System.Collections.Generic;

namespace ConfigExporter.Validation;

public enum IssueSeverity
{
    Error,
    Warning,
}

public sealed class ValidationIssue
{
    public IssueSeverity Severity = IssueSeverity.Error;
    public string Sheet = "";
    public int Row = 0;
    public string? Column = null;
    public string? Value = null;
    public string Rule = "";
    public string Message = "";

    public override string ToString()
    {
        var location = Row > 0
            ? $"{Sheet}!{Column ?? "?"}（第{Row}行）"
            : $"{Sheet}（整表）";
        return $"[{Severity}] {location}: {Message} | 值: {Value ?? "<空>"} | 规则: {Rule}";
    }
}
