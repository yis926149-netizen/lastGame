using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using ConfigExporter.Schema;
using ConfigExporter.Validation;

namespace ConfigExporter.Output;

/// <summary>
/// 确定性 JSON 输出。采用 JsonUtility 可解析的通用结构：
/// 每张表由 sheetName + columns（列名数组）+ rows（每行为 { values: string[] }）组成。
/// 数值统一以字符串表示（Formatting.Display），类型由 schema 定义；
/// CSV 是带类型的人类审查产物，此 JSON 面向 Unity Editor 导入器（机器格式）。
/// </summary>
public static class JsonWriter
{
    public static string Write(List<ParsedTable> tables, SchemaDocument doc, string outputDir)
    {
        var path = Path.Combine(outputDir, "game-config.json");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        writer.WriteStartObject();
        writer.WriteString("schemaVersion", doc.SchemaVersion);
        writer.WritePropertyName("tables");
        writer.WriteStartArray();
        foreach (var table in tables)
        {
            writer.WriteStartObject();
            writer.WriteString("sheetName", table.Schema.SheetName);

            writer.WritePropertyName("columns");
            writer.WriteStartArray();
            foreach (var col in table.Schema.Columns)
                writer.WriteStringValue(col.Name);
            writer.WriteEndArray();

            writer.WritePropertyName("rows");
            writer.WriteStartArray();
            foreach (var row in table.Rows)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("values");
                writer.WriteStartArray();
                foreach (var v in row.Values)
                {
                    if (v is null)
                        writer.WriteNullValue();
                    else
                        writer.WriteStringValue(Formatting.Display(v));
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return path;
    }
}
