using System;
using System.IO;
using System.Linq;
using ConfigExporter.Excel;
using ConfigExporter.Init;
using ConfigExporter.Validation;

namespace ConfigExporter.Tests;

public static class WorkbookRoundTripTests
{
    public static void InitThenReadThenValidate()
    {
        var doc = TestData.UnitSchema();
        var tmp = Path.Combine(Path.GetTempPath(), "configexporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var xlsx = Path.Combine(tmp, "roundtrip.xlsx");

        // 使用真实 seed 数据（TestData 的 schema 无 seed，这里手动补 3 行）
        var seededDoc = TestData.UnitSchema();
        var table = seededDoc.Tables[0];
        table.Seed = new()
        {
            new() { ["unitId"] = "unit.settler", ["strategyType"] = "Settler", ["hp"] = 50.0, ["attackIntervalSeconds"] = 0.0 },
            new() { ["unitId"] = "unit.swordsman", ["strategyType"] = "Melee", ["hp"] = 100.0, ["attackIntervalSeconds"] = 1.5 },
            new() { ["unitId"] = "unit.archer", ["strategyType"] = "Ranged", ["hp"] = 60.0, ["attackIntervalSeconds"] = 1.2 },
        };

        WorkbookInitializer.Init(seededDoc, xlsx);

        var sheets = WorkbookReader.Read(xlsx, seededDoc);
        var result = ValidationEngine.Validate(sheets, seededDoc);
        Check.True(!result.HasErrors, "往返数据应校验通过");

        var parsed = result.Tables[0];
        Check.Equal(3, parsed.Rows.Count);
        // 按稳定 ID 升序：unit.archer < unit.settler < unit.swordsman
        var ids = parsed.Rows.Select(r => (string)r.Values[0]!).ToArray();
        Check.SequenceEqual(new[] { "unit.archer", "unit.settler", "unit.swordsman" }, ids);
    }
}
