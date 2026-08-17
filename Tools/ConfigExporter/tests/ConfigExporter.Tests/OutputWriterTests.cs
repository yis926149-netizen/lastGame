using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ConfigExporter.Excel;
using ConfigExporter.Init;
using ConfigExporter.Output;
using ConfigExporter.Schema;
using ConfigExporter.Validation;
using ConfigExporter.Xlsx;

namespace ConfigExporter.Tests;

public static class OutputWriterTests
{
    private static ParsedTable Table()
    {
        var schema = TestData.UnitSchema().Tables[0];
        return new ParsedTable
        {
            Schema = schema,
            Rows = new List<ParsedRow>
            {
                new() { Values = new List<object?> { "unit.archer", "Ranged", 60.0, 1.2 } },
                new() { Values = new List<object?> { "unit.settler", "Settler", 50.0, 0.0 } },
            },
        };
    }

    public static void CsvIsDeterministic()
    {
        var dir1 = TempDir();
        var dir2 = TempDir();
        CsvWriter.WriteAll(new List<ParsedTable> { Table() }, dir1);
        CsvWriter.WriteAll(new List<ParsedTable> { Table() }, dir2);

        var b1 = File.ReadAllBytes(Path.Combine(dir1, "单位.csv"));
        var b2 = File.ReadAllBytes(Path.Combine(dir2, "单位.csv"));
        Check.SequenceEqual(b1, b2);
    }

    public static void JsonFieldOrderDeterministic()
    {
        var dir1 = TempDir();
        var dir2 = TempDir();
        var doc = TestData.UnitSchema();
        JsonWriter.Write(new List<ParsedTable> { Table() }, doc, dir1);
        JsonWriter.Write(new List<ParsedTable> { Table() }, doc, dir2);

        var text1 = File.ReadAllText(Path.Combine(dir1, "game-config.json"), Encoding.UTF8);
        var text2 = File.ReadAllText(Path.Combine(dir2, "game-config.json"), Encoding.UTF8);
        Check.Equal(text1, text2);

        Check.Contains(text1, "\"sheetName\"");
        Check.Contains(text1, "\"columns\"");
        Check.Contains(text1, "\"values\"");
        var unitIdIndex = text1.IndexOf("\"unitId\"", StringComparison.Ordinal);
        var hpIndex = text1.IndexOf("\"hp\"", StringComparison.Ordinal);
        Check.True(unitIdIndex >= 0 && hpIndex > unitIdIndex, "columns 数组应遵循 schema 列顺序");
    }

    public static void ManifestWritesHashes()
    {
        var tmp = TempDir();
        var doc = TestData.UnitSchema();
        var xlsx = Path.Combine(tmp, "seed.xlsx");
        WorkbookInitializer.Init(doc, xlsx);

        var sheets = WorkbookReader.Read(xlsx, doc);
        var result = ValidationEngine.Validate(sheets, doc);
        Check.True(!result.HasErrors, "seed 数据应校验通过");

        var outDir = Path.Combine(tmp, "out");
        Exporter.Write(doc, result, xlsx, outDir, stamp: false);

        var manifestPath = Path.Combine(outDir, "game-config.manifest.json");
        Check.True(File.Exists(manifestPath), "manifest 应已生成");
        var manifest = File.ReadAllText(manifestPath, Encoding.UTF8);
        Check.Contains(manifest, "workbookSha256");
        Check.Contains(manifest, "game-config.json");
        Check.True(File.Exists(Path.Combine(outDir, "game-config.json")), "game-config.json 应已生成");
        Check.True(File.Exists(Path.Combine(outDir, "Csv", "单位.csv")), "CSV 应已生成");
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "configexporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
