using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ConfigExporter.Schema;
using ConfigExporter.Xlsx;

namespace ConfigExporter.Init;

/// <summary>根据 schema 生成模板工作簿（表头 + seed 示例行）。</summary>
public static class WorkbookInitializer
{
    public static void Init(SchemaDocument doc, string outputPath)
    {
        var sheets = new List<XlsxSheet>();
        foreach (var table in doc.Tables)
        {
            var sheet = new XlsxSheet { Name = table.SheetName };
            // 第1行：英文表头（列名，reader 依此精确匹配）
            sheet.Rows.Add(table.Columns.Select(c => (object?)c.Name).ToList());
            // 第2行：字段中文说明（首列为标记，reader 据此跳过）
            sheet.Rows.Add(BuildNoteRow(table));

            foreach (var seed in table.Seed)
            {
                var row = new List<object?>();
                foreach (var col in table.Columns)
                {
                    var raw = seed.TryGetValue(col.Name, out var v) ? ToClr(v) : null;
                    row.Add(Normalize(col.Type, raw));
                }
                sheet.Rows.Add(row);
            }

            sheets.Add(sheet);
        }

        XlsxWriter.Write(outputPath, sheets);
    }

    /// <summary>生成第2行"字段中文说明"：首列标记 + 各列 note（与表头严格对齐）。</summary>
    private static List<object?> BuildNoteRow(TableSchema table)
    {
        var row = new List<object?>();
        var keyNote = string.IsNullOrWhiteSpace(table.Columns[0].Note)
            ? ""
            : "　" + table.Columns[0].Note;
        row.Add(SheetMarkers.FieldNoteRow + keyNote);

        for (var i = 1; i < table.Columns.Count; i++)
            row.Add(string.IsNullOrWhiteSpace(table.Columns[i].Note) ? "" : table.Columns[i].Note);

        return row;
    }

    private static object? Normalize(string type, object? raw)
    {
        if (raw is null)
            return null;
        switch (type)
        {
            case "int":
                return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            case "float":
                return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            case "bool":
                return Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
            default:
                return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
        }
    }

    private static object? ToClr(object? o) => o switch
    {
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => e.ToString(),
        },
        _ => o,
    };
}
