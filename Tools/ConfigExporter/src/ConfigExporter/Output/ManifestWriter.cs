using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using ConfigExporter.Schema;
using ConfigExporter.Validation;

namespace ConfigExporter.Output;

public static class ManifestWriter
{
    public static string Write(
        SchemaDocument doc,
        List<ParsedTable> tables,
        string inputXlsx,
        string outputDir,
        List<string> csvPaths,
        string jsonPath,
        bool stamp,
        string exporterVersion)
    {
        var path = Path.Combine(outputDir, "game-config.manifest.json");
        var workbookSha = Sha256File(inputXlsx);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new Utf8JsonWriter(fs, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        w.WriteStartObject();
        w.WriteString("schemaVersion", doc.SchemaVersion);
        w.WriteString("exporterVersion", exporterVersion);
        w.WriteString("workbook", Path.GetFileName(inputXlsx));
        w.WriteString("workbookSha256", workbookSha);
        if (stamp)
            w.WriteString("generatedAtUtc", DateTime.UtcNow.ToString("o"));

        w.WritePropertyName("tables");
        w.WriteStartArray();
        foreach (var t in tables)
        {
            w.WriteStartObject();
            w.WriteString("sheetName", t.Schema.SheetName);
            w.WriteNumber("rowCount", t.Rows.Count);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WritePropertyName("outputs");
        w.WriteStartObject();
        foreach (var csv in csvPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            w.WritePropertyName(Rel(outputDir, csv));
            w.WriteStartObject();
            w.WriteString("sha256", Sha256File(csv));
            w.WriteEndObject();
        }
        w.WritePropertyName(Rel(outputDir, jsonPath));
        w.WriteStartObject();
        w.WriteString("sha256", Sha256File(jsonPath));
        w.WriteEndObject();
        w.WriteEndObject();

        w.WriteEndObject();
        w.Flush();
        return path;
    }

    private static string Rel(string outputDir, string p) =>
        Path.GetRelativePath(outputDir, p).Replace('\\', '/');

    private static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}
