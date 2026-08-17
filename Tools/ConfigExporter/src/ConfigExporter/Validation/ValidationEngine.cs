using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ConfigExporter.Excel;
using ConfigExporter.Output;
using ConfigExporter.Schema;

namespace ConfigExporter.Validation;

/// <summary>解析并校验后的业务表。</summary>
public sealed class ParsedRow
{
    public List<object?> Values = new();
}

/// <summary>与 schema 对齐、已排序的解析结果。</summary>
public sealed class ParsedTable
{
    public TableSchema Schema = null!;
    public List<ParsedRow> Rows = new();
}

public sealed class ValidationResult
{
    public List<ParsedTable> Tables = new();
    public List<ValidationIssue> Issues = new();
    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
}

public static class ValidationEngine
{
    public static ValidationResult Validate(List<RawSheet> sheets, SchemaDocument doc)
    {
        var result = new ValidationResult();

        foreach (var table in doc.Tables)
        {
            var sheet = sheets.FirstOrDefault(s => s.SheetName == table.SheetName);
            if (sheet is null)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Sheet = table.SheetName, Rule = "sheet-missing", Message = "工作簿缺少该工作表",
                });
                continue;
            }

            result.Tables.Add(ValidateTable(sheet, table, result.Issues));
        }

        return result;
    }

    private static ParsedTable ValidateTable(RawSheet sheet, TableSchema table, List<ValidationIssue> issues)
    {
        var parsed = new ParsedTable { Schema = table };
        var cols = table.Columns;

        // 表头必须与 schema 列顺序、名称完全一致
        var expected = cols.Select(c => c.Name).ToList();
        if (!expected.SequenceEqual(sheet.Headers))
        {
            issues.Add(new ValidationIssue
            {
                Sheet = table.SheetName, Rule = "header-mismatch",
                Message = $"表头不匹配。期望: [{string.Join(", ", expected)}]，实际: [{string.Join(", ", sheet.Headers)}]",
            });
            return parsed;
        }

        if (sheet.HasMergedCells)
            issues.Add(new ValidationIssue
            {
                Sheet = table.SheetName, Rule = "merged-cells", Message = "业务表禁止合并单元格",
            });

        foreach (var hidden in sheet.HiddenDataRowNumbers)
            issues.Add(new ValidationIssue
            {
                Sheet = table.SheetName, Row = hidden, Rule = "hidden-row", Message = "禁止隐藏业务数据行",
            });

        var keyIndex = string.IsNullOrWhiteSpace(table.Key)
            ? -1
            : cols.FindIndex(c => c.Name == table.Key);

        foreach (var rawRow in sheet.Rows)
        {
            var row = new ParsedRow();
            for (var j = 0; j < cols.Count; j++)
            {
                var col = cols[j];
                var snap = rawRow.Cells[j];
                var value = ValidateCell(sheet.SheetName, rawRow.RowNumber, col, snap, issues);
                row.Values.Add(value);
            }
            parsed.Rows.Add(row);
        }

        // 主键唯一
        if (keyIndex >= 0)
        {
            var groups = parsed.Rows
                .Select((r, i) => (value: r.Values[keyIndex], rowNumber: sheet.Rows[i].RowNumber))
                .GroupBy(x => x.value, x => x.rowNumber)
                .Where(g => g.Key is not null && g.Count() > 1);
            foreach (var g in groups)
                issues.Add(new ValidationIssue
                {
                    Sheet = table.SheetName, Column = table.Key, Rule = "duplicate-key",
                    Value = Formatting.Display(g.Key),
                    Message = $"主键重复（出现于行 {string.Join(", ", g)}）",
                });
        }

        ApplyTableRules(table, parsed, issues);

        // 按稳定 ID 排序，避免行顺序造成无意义 diff
        if (keyIndex >= 0)
            parsed.Rows = parsed.Rows
                .OrderBy(r => Formatting.Display(r.Values[keyIndex]), StringComparer.Ordinal)
                .ToList();

        return parsed;
    }

    private static object? ValidateCell(
        string sheetName, int rowNumber, ColumnSchema col, CellSnapshot snap, List<ValidationIssue> issues)
    {
        void Add(string rule, string message, string? value) =>
            issues.Add(new ValidationIssue
            {
                Sheet = sheetName, Row = rowNumber, Column = col.Name, Rule = rule,
                Value = value, Message = message,
            });

        if (snap.HasFormula)
            Add("no-formula", "禁止使用公式作为运行值", snap.Address);

        if (snap.IsEmpty)
        {
            if (col.Required)
            {
                Add("required", "必填字段为空", null);
                return null;
            }

            if (col.Default is not null)
                return Parse(col, col.Default, snap.Address, Add);
            return null;
        }

        return Parse(col, snap.Value, snap.Address, Add);
    }

    private static object? Parse(
        ColumnSchema col, object? raw, string address, Action<string, string, string?> add)
    {
        switch (col.Type)
        {
            case "string":
            {
                if (raw is string s)
                {
                    s = s.Trim();
                    if (col.IdPattern is not null && !Regex.IsMatch(s, col.IdPattern))
                        add("id-pattern", $"ID [{s}] 不符合规范 [{col.IdPattern}]", s);
                    return s;
                }

                add("type", "字符串列要求文本单元格", Formatting.Display(raw));
                return null;
            }
            case "int":
            {
                if (!TryAsLong(raw, out var l))
                {
                    add("type", "整数列要求整数单元格", Formatting.Display(raw));
                    return null;
                }
                CheckRange(col, l, add);
                return l;
            }
            case "float":
            {
                if (!TryAsDouble(raw, out var d))
                {
                    add("type", "浮点列要求数值单元格", Formatting.Display(raw));
                    return null;
                }
                CheckRange(col, d, add);
                return d;
            }
            case "bool":
            {
                if (!TryAsBool(raw, out var b))
                {
                    add("type", "布尔列要求 TRUE/FALSE", Formatting.Display(raw));
                    return null;
                }
                return b;
            }
            case "enum":
            {
                if (raw is string s)
                {
                    s = s.Trim();
                    if (col.Enum is not null && !col.Enum.Contains(s, StringComparer.Ordinal))
                        add("enum", $"枚举值 [{s}] 不在 {string.Join("/", col.Enum)} 中", s);
                    return s;
                }

                add("type", "枚举列要求文本单元格", Formatting.Display(raw));
                return null;
            }
            default:
                add("type", $"未知列类型 {col.Type}", null);
                return null;
        }
    }

    private static void CheckRange(ColumnSchema col, double value, Action<string, string, string?> add)
    {
        if (col.Min is not null)
        {
            if (col.MinExclusive && value <= col.Min)
                add("range", $"数值必须大于 {col.Min}", Formatting.Number(value));
            else if (!col.MinExclusive && value < col.Min)
                add("range", $"数值必须不小于 {col.Min}", Formatting.Number(value));
        }

        if (col.Max is not null && value > col.Max)
            add("range", $"数值必须不大于 {col.Max}", Formatting.Number(value));
    }

    private static void ApplyTableRules(TableSchema table, ParsedTable parsed, List<ValidationIssue> issues)
    {
        var cols = table.Columns;
        foreach (var rule in table.Rules)
        {
            switch (rule.Type)
            {
                case "conditionalMinExclusive":
                {
                    var fieldIndex = cols.FindIndex(c => c.Name == rule.Field);
                    var whenIndex = cols.FindIndex(c => c.Name == rule.WhenField);
                    if (fieldIndex < 0 || whenIndex < 0)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Sheet = table.SheetName, Rule = rule.Id, Message = "规则引用了不存在的字段",
                        });
                        continue;
                    }

                    foreach (var row in parsed.Rows)
                    {
                        var when = Formatting.Display(row.Values[whenIndex]);
                        if (rule.WhenNotIn.Contains(when, StringComparer.Ordinal))
                            continue;
                        if (row.Values[fieldIndex] is double d && d <= rule.Min)
                            issues.Add(new ValidationIssue
                            {
                                Sheet = table.SheetName, Column = rule.Field, Rule = rule.Id,
                                Value = Formatting.Number(d), Message = rule.Description,
                            });
                    }
                    break;
                }
                default:
                    issues.Add(new ValidationIssue
                    {
                        Sheet = table.SheetName, Rule = rule.Id, Message = $"未知规则类型 {rule.Type}",
                    });
                    break;
            }
        }
    }

    private static bool TryAsLong(object? raw, out long value)
    {
        value = 0;
        switch (raw)
        {
            case double d:
                if (Math.Abs(d - Math.Round(d)) > 1e-9 || d < long.MinValue || d > long.MaxValue)
                    return false;
                value = (long)d;
                return true;
            case string s when long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l):
                value = l;
                return true;
            default:
                return false;
        }
    }

    private static bool TryAsDouble(object? raw, out double value)
    {
        value = 0;
        switch (raw)
        {
            case double d:
                value = d;
                return true;
            case string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d):
                value = d;
                return true;
            default:
                return false;
        }
    }

    private static bool TryAsBool(object? raw, out bool value)
    {
        value = false;
        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case double d when Math.Abs(d - 0) < 1e-9:
                value = false;
                return true;
            case double d when Math.Abs(d - 1) < 1e-9:
                value = true;
                return true;
            case string s:
                s = s.Trim().ToLowerInvariant();
                switch (s)
                {
                    case "true" or "1":
                        value = true;
                        return true;
                    case "false" or "0":
                        value = false;
                        return true;
                    default:
                        return false;
                }
            default:
                return false;
        }
    }
}
