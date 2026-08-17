using System;
using System.IO;
using System.Linq;
using ConfigExporter.Excel;
using ConfigExporter.Init;
using ConfigExporter.Output;
using ConfigExporter.Schema;
using ConfigExporter.Validation;

namespace ConfigExporter;

public static class Program
{
    public const string Version = "0.1.0";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            return args[0] switch
            {
                "init" => RunInit(args),
                "help" or "--help" or "-h" => Help(),
                _ => RunExport(args),
            };
        }
        catch (ConfigExportException ex)
        {
            Console.Error.WriteLine("错误: " + ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("未预期错误: " + ex);
            return 1;
        }
    }

    private static int Help()
    {
        PrintUsage();
        return 0;
    }

    private static int RunInit(string[] args)
    {
        var schema = RequireValue(args, "--schema");
        var output = RequireValue(args, "--output");
        var doc = SchemaLoader.Load(schema);
        WorkbookInitializer.Init(doc, output);
        Console.WriteLine("已生成模板工作簿: " + output);
        return 0;
    }

    private static int RunExport(string[] args)
    {
        var input = RequireValue(args, "--input");
        var schema = RequireValue(args, "--schema");
        var output = RequireValue(args, "--output");
        var strict = HasFlag(args, "--strict");
        var stamp = HasFlag(args, "--stamp");

        var doc = SchemaLoader.Load(schema);
        var sheets = WorkbookReader.Read(input, doc);
        var result = ValidationEngine.Validate(sheets, doc);

        foreach (var issue in result.Issues)
            Console.Error.WriteLine(issue.ToString());

        var fatal = result.HasErrors ||
                    (strict && result.Issues.Any(i => i.Severity == IssueSeverity.Warning));
        if (fatal)
        {
            var errors = result.Issues.Count(i => i.Severity == IssueSeverity.Error);
            var warnings = result.Issues.Count(i => i.Severity == IssueSeverity.Warning);
            Console.Error.WriteLine($"校验未通过：{errors} 处错误，{warnings} 处警告。未生成任何输出。");
            return 1;
        }

        Exporter.Write(doc, result, input, output, stamp);
        Console.WriteLine("导出完成 -> " + output);
        return 0;
    }

    private static string RequireValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        throw new ConfigExportException($"缺少参数 {name}");
    }

    private static bool HasFlag(string[] args, string name) => args.Contains(name);

    private static void PrintUsage()
    {
        Console.WriteLine("ConfigExporter " + Version);
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  从 schema 生成模板工作簿:");
        Console.WriteLine("    dotnet run -- init --schema <schema.json> --output <out.xlsx>");
        Console.WriteLine();
        Console.WriteLine("  导出（默认命令）:");
        Console.WriteLine("    dotnet run -- --input <in.xlsx> --schema <schema.json> --output <outDir> [--strict] [--stamp]");
        Console.WriteLine();
        Console.WriteLine("  参数:");
        Console.WriteLine("    --input   源 Excel 工作簿路径");
        Console.WriteLine("    --schema  schema JSON 路径");
        Console.WriteLine("    --output  输出目录（导出）或目标 xlsx（init）");
        Console.WriteLine("    --strict  将警告升级为错误");
        Console.WriteLine("    --stamp   在 manifest 中写入生成时间（默认关闭以保持确定性）");
    }
}
