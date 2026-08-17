using ConfigExporter.Schema;

namespace ConfigExporter.Tests;

public static class SchemaLoaderTests
{
    public static void ValidSchemaReadsColumnsAndRules()
    {
        var doc = TestData.UnitSchema();
        Check.Equal("1.0.0", doc.SchemaVersion);
        var table = doc.Tables[0];
        Check.Equal("单位", table.SheetName);
        Check.Equal("unitId", table.Key);
        Check.Equal(4, table.Columns.Count);
        Check.Equal(1, table.Rules.Count);
    }

    public static void UnknownColumnTypeThrows()
    {
        const string json = """
        {"schemaVersion":"1","tables":[{"sheetName":"t","columns":[{"name":"a","type":"nope"}]}]}
        """;
        Check.Throws<ConfigExportException>(() => SchemaLoader.Parse(json));
    }

    public static void KeyNotAColumnThrows()
    {
        const string json = """
        {"schemaVersion":"1","tables":[{"sheetName":"t","key":"zzz","columns":[{"name":"a","type":"string"}]}]}
        """;
        Check.Throws<ConfigExportException>(() => SchemaLoader.Parse(json));
    }
}
