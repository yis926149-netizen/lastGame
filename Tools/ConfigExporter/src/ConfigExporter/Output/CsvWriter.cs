using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ConfigExporter.Validation;

namespace ConfigExporter.Output;

public static class CsvWriter
{
    public static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>为每张表写一份 CSV，返回写出路径。</summary>
    public static List<string> WriteAll(List<ParsedTable> tables, string csvDir)
    {
        var paths = new List<string>();
        foreach (var table in tables)
        {
            var path = Path.Combine(csvDir, table.Schema.SheetName + ".csv");
            WriteTable(table, path);
            paths.Add(path);
        }
        return paths;
    }

    private static void WriteTable(ParsedTable table, string path)
    {
        using var sw = new StreamWriter(path, false, Utf8NoBom) { NewLine = "\n" };
        sw.WriteLine(string.Join(",", table.Schema.Columns.Select(c => Escape(c.Name))));
        foreach (var row in table.Rows)
            sw.WriteLine(string.Join(",", row.Values.Select(EscapeValue)));
    }

    private static string EscapeValue(object? v) => Escape(Formatting.Display(v));

    private static string Escape(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
