using System.Collections.Generic;
using System.Linq;
using ConfigExporter.Excel;
using ConfigExporter.Validation;

namespace ConfigExporter.Tests;

public static class ValidationEngineTests
{
    private static List<RawSheet> Wrap(RawSheet s) => new() { s };

    private static RawSheet Sheet(List<object?[]> rows)
    {
        var s = new RawSheet
        {
            SheetName = "单位",
            Headers = new List<string> { "unitId", "strategyType", "hp", "attackIntervalSeconds" },
        };
        for (var i = 0; i < rows.Count; i++)
        {
            var rr = new RawRow { RowNumber = i + 2 };
            foreach (var v in rows[i])
                rr.Cells.Add(new CellSnapshot { IsEmpty = v is null, Value = v });
            s.Rows.Add(rr);
        }
        return s;
    }

    public static void ValidRowsNoErrors()
    {
        var sheet = Sheet(new List<object?[]>
        {
            new object?[] { "unit.archer", "Ranged", 60.0, 1.2 },
            new object?[] { "unit.settler", "Settler", 50.0, 0.0 },
        });
        var result = ValidationEngine.Validate(Wrap(sheet), TestData.UnitSchema());
        Check.True(!result.HasErrors, "有效数据不应产生错误");
    }

    public static void HeaderMismatchError()
    {
        var sheet = Sheet(new List<object?[]>());
        sheet.Headers[0] = "wrong";
        var result = ValidationEngine.Validate(Wrap(sheet), TestData.UnitSchema());
        Check.True(result.Issues.Any(i => i.Rule == "header-mismatch"));
    }

    public static void DuplicateKeyError()
    {
        var sheet = Sheet(new List<object?[]>
        {
            new object?[] { "unit.archer", "Ranged", 60.0, 1.2 },
            new object?[] { "unit.archer", "Melee", 100.0, 1.5 },
        });
        var result = ValidationEngine.Validate(Wrap(sheet), TestData.UnitSchema());
        Check.True(result.Issues.Any(i => i.Rule == "duplicate-key"));
    }

    public static void BadEnumError()
    {
        var sheet = Sheet(new List<object?[]>
        {
            new object?[] { "unit.archer", "Flying", 60.0, 1.2 },
        });
        var result = ValidationEngine.Validate(Wrap(sheet), TestData.UnitSchema());
        Check.True(result.Issues.Any(i => i.Rule == "enum"));
    }

    public static void ZeroHpError()
    {
        var sheet = Sheet(new List<object?[]>
        {
            new object?[] { "unit.archer", "Ranged", 0.0, 1.2 },
        });
        var result = ValidationEngine.Validate(Wrap(sheet), TestData.UnitSchema());
        Check.True(result.Issues.Any(i => i.Rule == "range"));
    }

    public static void NonSettlerZeroAttackIntervalError()
    {
        var sheet = Sheet(new List<object?[]>
        {
            new object?[] { "unit.archer", "Ranged", 60.0, 0.0 },
        });
        var result = ValidationEngine.Validate(Wrap(sheet), TestData.UnitSchema());
        Check.True(result.Issues.Any(i => i.Rule == "ai-positive"));
    }

    public static void SettlerZeroAttackIntervalAllowed()
    {
        var sheet = Sheet(new List<object?[]>
        {
            new object?[] { "unit.settler", "Settler", 50.0, 0.0 },
        });
        var result = ValidationEngine.Validate(Wrap(sheet), TestData.UnitSchema());
        Check.True(!result.Issues.Any(i => i.Rule == "ai-positive"));
    }
}
