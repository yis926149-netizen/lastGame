using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConfigExporter.Excel;
using ConfigExporter.Validation;
using ConfigExporter.Xlsx;

namespace ConfigExporter.Tests;

public static class CommentRowTests
{
    /// <summary>首列以 // 开头的行整行跳过：夹在数据中间、位于表尾、仅首列有文本均不报错。</summary>
    public static void CommentRowsAreSkipped()
    {
        var doc = TestData.UnitSchema();
        var table = doc.Tables[0];

        var sheet = new XlsxSheet { Name = table.SheetName };
        sheet.Rows.Add(table.Columns.Select(c => (object?)c.Name).ToList());
        sheet.Rows.Add(new List<object?> { SheetMarkers.FieldNoteRow, "兵种策略类型", "生命值", "攻击间隔（秒）" });
        sheet.Rows.Add(new List<object?> { "unit.archer", "Ranged", 60.0, 1.2 });
        // 夹在数据行中间、仅首列有文本——若未跳过会刷出 3 条 required + 1 条 id-pattern
        sheet.Rows.Add(new List<object?> { "// 下面是旧版本遗留，别动", null, null, null });
        sheet.Rows.Add(new List<object?> { "unit.settler", "Settler", 50.0, 0.0 });
        // 表尾注释行，且带前导空白
        sheet.Rows.Add(new List<object?> { "  // 只调黄色部分就行", null, null, null });

        var xlsx = WriteTemp(sheet, "comment-rows.xlsx");
        var result = ValidationEngine.Validate(WorkbookReader.Read(xlsx, doc), doc);

        Check.True(!result.HasErrors, "注释行应被跳过，不产生任何错误：" + Describe(result));
        Check.SequenceEqual(
            new[] { "unit.archer", "unit.settler" },
            result.Tables[0].Rows.Select(r => (string)r.Values[0]!));
    }

    /// <summary>// 只在首列生效：数据行里其他列出现 // 不影响该行被正常读取。</summary>
    public static void SlashesInOtherColumnsAreNotComments()
    {
        var doc = TestData.UnitSchema();
        var table = doc.Tables[0];

        var sheet = new XlsxSheet { Name = table.SheetName };
        sheet.Rows.Add(table.Columns.Select(c => (object?)c.Name).ToList());
        sheet.Rows.Add(new List<object?> { SheetMarkers.FieldNoteRow, "", "", "" });
        sheet.Rows.Add(new List<object?> { "unit.archer", "Ranged", 60.0, 1.2 });
        // 首列是合法 ID，第二列的 // 不构成注释——该行应照常校验（枚举非法 → 报错）
        sheet.Rows.Add(new List<object?> { "unit.swordsman", "// Melee", 100.0, 1.5 });

        var xlsx = WriteTemp(sheet, "slash-elsewhere.xlsx");
        var result = ValidationEngine.Validate(WorkbookReader.Read(xlsx, doc), doc);

        Check.True(result.HasErrors, "非首列的 // 不应被当作注释，该行仍需参与校验");
    }

    private static string WriteTemp(XlsxSheet sheet, string fileName)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "configexporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var path = Path.Combine(tmp, fileName);
        XlsxWriter.Write(path, new List<XlsxSheet> { sheet });
        return path;
    }

    private static string Describe(ValidationResult result) =>
        string.Join("; ", result.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.ToString()));
}
