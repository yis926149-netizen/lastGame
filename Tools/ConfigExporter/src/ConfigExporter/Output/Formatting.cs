using System;
using System.Globalization;

namespace ConfigExporter.Output;

/// <summary>确定性格式化：固定 culture、固定小数表示。</summary>
public static class Formatting
{
    public static string Number(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static string Int(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool v) => v ? "true" : "false";

    /// <summary>将解析值转为展示/排序用字符串。</summary>
    public static string Display(object? v) => v switch
    {
        null => "",
        bool b => Bool(b),
        long l => Int(l),
        double d => Number(d),
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
