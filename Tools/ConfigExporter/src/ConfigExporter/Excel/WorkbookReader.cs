using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConfigExporter.Schema;
using ConfigExporter.Xlsx;

namespace ConfigExporter.Excel;

/// <summary>原始工作表读取结果，不含业务校验。</summary>
public sealed class RawSheet
{
    public string SheetName = "";
    public List<string> Headers = new();
    public List<RawRow> Rows = new();
    public bool HasMergedCells = false;
    public List<int> HiddenDataRowNumbers = new();
}

/// <summary>一行原始单元格快照，按 schema 列顺序对齐。</summary>
public sealed class RawRow
{
    public int RowNumber = 0;
    public List<CellSnapshot> Cells = new();
}

/// <summary>单个单元格的原始快照。</summary>
public sealed class CellSnapshot
{
    public string Address = "";
    public bool IsEmpty = false;
    public bool HasFormula = false;

    /// <summary>typed value: string / double / bool / DateTime，空单元格为 null。</summary>
    public object? Value = null;
}

public static class WorkbookReader
{
    /// <summary>按 schema 逐表读取工作簿，缺失工作表即抛出错误。</summary>
    public static List<RawSheet> Read(string path, SchemaDocument doc)
    {
        if (!File.Exists(path))
            throw new ConfigExportException($"输入工作簿不存在: {path}");

        var workbook = XlsxReader.Read(path);
        var sheets = new List<RawSheet>();
        foreach (var table in doc.Tables)
        {
            var sheetData = workbook.GetSheet(table.SheetName)
                            ?? throw new ConfigExportException($"工作簿缺少工作表 [{table.SheetName}]");
            sheets.Add(MapSheet(sheetData, table));
        }
        return sheets;
    }

    private static RawSheet MapSheet(XlsxSheetData sheetData, TableSchema table)
    {
        var sheet = new RawSheet { SheetName = table.SheetName };

        var headerRow = sheetData.Rows.FirstOrDefault(r => r.RowNumber == 1);
        for (var c = 1; c <= table.Columns.Count; c++)
        {
            if (headerRow is not null && headerRow.Cells.TryGetValue(c, out var cell))
                sheet.Headers.Add(cell.Value is string s ? s.Trim() : (cell.Value?.ToString() ?? "").Trim());
            else
                sheet.Headers.Add("");
        }

        sheet.HasMergedCells = sheetData.HasMergedCells;

        foreach (var xrow in sheetData.Rows.Where(r => r.RowNumber >= 2).OrderBy(r => r.RowNumber))
        {
            if (xrow.Hidden)
            {
                sheet.HiddenDataRowNumbers.Add(xrow.RowNumber);
                continue;
            }

            // 跳过"字段中文说明"行（首列标记为 #字段说明）
            if (IsFieldNoteRow(xrow))
                continue;

            // 跳过注释行（首列以 // 开头）
            if (IsCommentRow(xrow))
                continue;

            var row = new RawRow { RowNumber = xrow.RowNumber };
            var hasAnyContent = false;
            for (var c = 1; c <= table.Columns.Count; c++)
            {
                if (xrow.Cells.TryGetValue(c, out var cell))
                {
                    var snap = new CellSnapshot
                    {
                        Address = CellRef.ToReference(xrow.RowNumber, c),
                        IsEmpty = cell.Value is null && !cell.HasFormula,
                        HasFormula = cell.HasFormula,
                        Value = cell.Value,
                    };
                    if (!snap.IsEmpty || snap.HasFormula)
                        hasAnyContent = true;
                    row.Cells.Add(snap);
                }
                else
                {
                    row.Cells.Add(new CellSnapshot
                    {
                        Address = CellRef.ToReference(xrow.RowNumber, c),
                        IsEmpty = true,
                        Value = null,
                    });
                }
            }

            if (hasAnyContent)
                sheet.Rows.Add(row);
        }

        return sheet;
    }

    /// <summary>判断某行是否为"字段中文说明"行（首列以 #字段说明 开头）。</summary>
    private static bool IsFieldNoteRow(XlsxRow row)
    {
        if (row.Cells.TryGetValue(1, out var first) && first.Value is string s)
            return s.TrimStart().StartsWith(SheetMarkers.FieldNoteRow, System.StringComparison.Ordinal);
        return false;
    }

    /// <summary>判断某行是否为注释行（首列以 // 开头）。</summary>
    private static bool IsCommentRow(XlsxRow row)
    {
        if (row.Cells.TryGetValue(1, out var first) && first.Value is string s)
            return s.TrimStart().StartsWith(SheetMarkers.CommentRow, System.StringComparison.Ordinal);
        return false;
    }
}
