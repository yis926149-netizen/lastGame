using System.Collections.Generic;

namespace ConfigExporter.Schema;

/// <summary>配置表结构文档（Config/Schema/配置表结构.json）。</summary>
public sealed class SchemaDocument
{
    public string SchemaVersion { get; set; } = "";
    public string Workbook { get; set; } = "";
    public List<TableSchema> Tables { get; set; } = new();
}

/// <summary>一张业务表对应 Excel 中的一个工作表。</summary>
public sealed class TableSchema
{
    public string SheetName { get; set; } = "";
    public string Key { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ColumnSchema> Columns { get; set; } = new();
    public List<TableRule> Rules { get; set; } = new();
    public List<Dictionary<string, object?>> Seed { get; set; } = new();
}

/// <summary>一列字段的定义与约束。</summary>
public sealed class ColumnSchema
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public bool Unique { get; set; }

    /// <summary>可选字段的空值默认值，以字符串形式给出，按列类型解析。</summary>
    public string? Default { get; set; }

    /// <summary>字符串 ID 的正则约束。</summary>
    public string? IdPattern { get; set; }

    /// <summary>enum 列的合法取值。</summary>
    public List<string>? Enum { get; set; }

    public double? Min { get; set; }
    public double? Max { get; set; }
    public bool MinExclusive { get; set; }
    public string? Note { get; set; }
}

/// <summary>表级跨字段校验规则。</summary>
public sealed class TableRule
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Field { get; set; } = "";
    public string WhenField { get; set; } = "";
    public List<string> WhenNotIn { get; set; } = new();
    public double Min { get; set; }
    public string Description { get; set; } = "";
}
