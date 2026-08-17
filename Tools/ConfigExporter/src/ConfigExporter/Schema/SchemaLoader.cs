using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ConfigExporter.Schema;

public static class SchemaLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static SchemaDocument Load(string path)
    {
        if (!File.Exists(path))
            throw new ConfigExportException($"schema 文件不存在: {path}");
        return Parse(File.ReadAllText(path), path);
    }

    public static SchemaDocument Parse(string json, string sourceName = "<schema>")
    {
        SchemaDocument doc;
        try
        {
            doc = JsonSerializer.Deserialize<SchemaDocument>(json, Options)
                  ?? throw new ConfigExportException("schema 反序列化结果为空。");
        }
        catch (ConfigExportException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new ConfigExportException($"schema 解析失败（{sourceName}）: {ex.Message}");
        }

        SchemaValidator.Validate(doc, sourceName);
        return doc;
    }
}

/// <summary>校验 schema 本身是否自洽，尽早暴露结构错误。</summary>
public static class SchemaValidator
{
    private static readonly string[] KnownTypes = { "string", "int", "float", "bool", "enum" };

    public static void Validate(SchemaDocument doc, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(doc.SchemaVersion))
            throw new ConfigExportException($"schema 缺少 schemaVersion（{sourceName}）");

        foreach (var table in doc.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.SheetName))
                throw new ConfigExportException($"schema 存在缺少 sheetName 的表（{sourceName}）");
            if (table.Columns.Count == 0)
                throw new ConfigExportException($"表 [{table.SheetName}] 未声明任何列");

            var names = table.Columns.Select(c => c.Name).ToList();
            if (names.Distinct().Count() != names.Count)
                throw new ConfigExportException($"表 [{table.SheetName}] 存在重复列名");

            foreach (var col in table.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.Name))
                    throw new ConfigExportException($"表 [{table.SheetName}] 存在空列名");
                if (!KnownTypes.Contains(col.Type))
                    throw new ConfigExportException(
                        $"表 [{table.SheetName}] 列 [{col.Name}] 类型 [{col.Type}] 不合法，应为 {string.Join("/", KnownTypes)}");
                if (col.Type == "enum" && (col.Enum is null || col.Enum.Count == 0))
                    throw new ConfigExportException(
                        $"表 [{table.SheetName}] 列 [{col.Name}] 为 enum 但未声明枚举值");
            }

            if (!string.IsNullOrWhiteSpace(table.Key) && !names.Contains(table.Key))
                throw new ConfigExportException($"表 [{table.SheetName}] 主键 [{table.Key}] 不是已声明列");
        }
    }
}
