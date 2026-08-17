using ConfigExporter.Schema;

namespace ConfigExporter.Tests;

public static class TestData
{
    public const string UnitSchemaJson = """
    {"schemaVersion":"1.0.0","tables":[{"sheetName":"单位","key":"unitId","columns":[
    {"name":"unitId","type":"string","required":true,"unique":true,"idPattern":"^[a-z0-9_]+(\\.[a-z0-9_]+)*$"},
    {"name":"strategyType","type":"enum","required":true,"enum":["Melee","Ranged","Settler"]},
    {"name":"hp","type":"float","required":true,"min":0,"minExclusive":true},
    {"name":"attackIntervalSeconds","type":"float","required":true,"min":0}
    ],"rules":[{"id":"ai-positive","type":"conditionalMinExclusive","field":"attackIntervalSeconds","whenField":"strategyType","whenNotIn":["Settler"],"min":0,"description":"非移民攻击间隔>0"}]}]}
    """;

    public static SchemaDocument UnitSchema() => SchemaLoader.Parse(UnitSchemaJson);
}
