using System.Collections.Generic;
using System.IO;
using ConfigExporter.Schema;
using ConfigExporter.Validation;

namespace ConfigExporter.Output;

/// <summary>导出编排：校验通过后写出 CSV、JSON 与 manifest。</summary>
public static class Exporter
{
    public static void Write(
        SchemaDocument doc, ValidationResult result, string inputXlsx, string outputDir, bool stamp)
    {
        Directory.CreateDirectory(outputDir);
        var csvDir = Path.Combine(outputDir, "Csv");
        Directory.CreateDirectory(csvDir);

        var csvPaths = CsvWriter.WriteAll(result.Tables, csvDir);
        var jsonPath = JsonWriter.Write(result.Tables, doc, outputDir);
        ManifestWriter.Write(
            doc, result.Tables, inputXlsx, outputDir, csvPaths, jsonPath, stamp, Program.Version);
    }
}
