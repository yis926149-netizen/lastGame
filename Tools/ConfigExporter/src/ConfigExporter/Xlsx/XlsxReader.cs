using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using static ConfigExporter.Xlsx.XlsxNamespaces;

namespace ConfigExporter.Xlsx;

public sealed class XlsxCell
{
    public object? Value = null;    // string / double / bool / DateTime / "#ERROR"
    public bool HasFormula = false;
}

public sealed class XlsxRow
{
    public int RowNumber = 0;
    public bool Hidden = false;
    public Dictionary<int, XlsxCell> Cells = new(); // 列号(1基) -> cell
}

public sealed class XlsxSheetData
{
    public string Name = "";
    public bool HasMergedCells = false;
    public List<XlsxRow> Rows = new();
}

public sealed class XlsxWorkbook
{
    public List<XlsxSheetData> Sheets = new();

    public XlsxSheetData? GetSheet(string name) =>
        Sheets.FirstOrDefault(s => s.Name == name);
}

/// <summary>最小依赖的 .xlsx 读取器：支持内联字符串、共享字符串、数值、布尔、日期、隐藏行与合并单元格检测。</summary>
public static class XlsxReader
{
    public static XlsxWorkbook Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var wb = new XlsxWorkbook();

        var sharedStrings = ReadSharedStrings(zip);
        var workbookDoc = XDocument.Parse(ReadEntryText(zip, "xl/workbook.xml"));
        var relsDoc = XDocument.Parse(ReadEntryText(zip, "xl/_rels/workbook.xml.rels"));

        var rels = relsDoc.Root!
            .Elements(PackageRels + "Relationship")
            .ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);

        foreach (var sheetEl in workbookDoc.Root!.Elements(Main + "sheets").Elements(Main + "sheet"))
        {
            var name = (string?)sheetEl.Attribute("name") ?? "";
            var relId = (string?)sheetEl.Attribute(OfficeDocRels + "id") ?? "";
            var target = rels.TryGetValue(relId, out var t) ? t : "";
            var sheetPath = "xl/" + target;

            var sheetData = new XlsxSheetData { Name = name };
            var sheetDoc = XDocument.Parse(ReadEntryText(zip, sheetPath));
            sheetData.HasMergedCells = sheetDoc.Root!.Element(Main + "mergeCells") is not null;

            var sheetDataEl = sheetDoc.Root.Element(Main + "sheetData");
            if (sheetDataEl is not null)
            {
                foreach (var rowEl in sheetDataEl.Elements(Main + "row"))
                {
                    var row = new XlsxRow
                    {
                        RowNumber = (int?)rowEl.Attribute("r") ?? 0,
                        Hidden = (string?)rowEl.Attribute("hidden") is "1" or "true",
                    };
                    foreach (var cellEl in rowEl.Elements(Main + "c"))
                    {
                        var reference = (string?)cellEl.Attribute("r") ?? "";
                        var (_, col) = CellRef.Parse(reference);
                        row.Cells[col] = new XlsxCell
                        {
                            HasFormula = cellEl.Element(Main + "f") is not null,
                            Value = ReadCellValue(cellEl, sharedStrings),
                        };
                    }
                    sheetData.Rows.Add(row);
                }
            }

            wb.Sheets.Add(sheetData);
        }

        return wb;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var result = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return result;

        var doc = XDocument.Parse(ReadEntryText(zip, "xl/sharedStrings.xml"));
        foreach (var si in doc.Root!.Elements(Main + "si"))
        {
            var text = string.Concat(si.Descendants(Main + "t").Select(t => t.Value));
            result.Add(text);
        }
        return result;
    }

    private static object? ReadCellValue(XElement cellEl, List<string> sharedStrings)
    {
        var type = (string?)cellEl.Attribute("t");
        var v = cellEl.Element(Main + "v")?.Value;

        switch (type)
        {
            case "s":
                var idx = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
                return idx >= 0 && idx < sharedStrings.Count ? sharedStrings[idx] : "";
            case "inlineStr":
            case "str":
                var isEl = cellEl.Element(Main + "is");
                return isEl is null ? "" : string.Concat(isEl.Descendants(Main + "t").Select(t => t.Value));
            case "b":
                return v == "1";
            case "e":
                return "#ERROR";
            case "d":
                return DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                    ? dt
                    : v;
            default:
                if (v is null)
                    return null;
                return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? d
                    : v;
        }
    }

    private static string ReadEntryText(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name)
                    ?? throw new ConfigExportException($"xlsx 缺少必需部件 [{name}]");
        using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
        return sr.ReadToEnd();
    }
}
