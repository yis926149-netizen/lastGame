using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using static ConfigExporter.Xlsx.XlsxNamespaces;

namespace ConfigExporter.Xlsx;

/// <summary>一张待写入的工作表。</summary>
public sealed class XlsxSheet
{
    public string Name = "";

    /// <summary>行集合；每行按列顺序存放 cell 值（string/long/double/bool，null 表示空单元格）。</summary>
    public List<List<object?>> Rows = new();
}

/// <summary>最小依赖的 .xlsx 写入器：仅产出本工具与 Excel 都能识别的结构。</summary>
public static class XlsxWriter
{
    public static void Write(string path, List<XlsxSheet> sheets)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // ZipFile.Open(path, ZipArchiveMode.Create) 底层用 FileMode.CreateNew，目标已存在会抛 IOException；
        // 直接用 FileMode.Create 打开目标文件流交给 ZipArchive，实现截断覆盖（避开"删除+重命名"的沙箱限制）。
        using var fs = new FileStream(full, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            WriteEntry(zip, "[Content_Types].xml", BuildContentTypes(sheets));
            WriteEntry(zip, "_rels/.rels", BuildRootRels());
            WriteEntry(zip, "xl/workbook.xml", BuildWorkbook(sheets));
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets));
            for (var i = 0; i < sheets.Count; i++)
                WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", BuildWorksheet(sheets[i]));
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private static byte[] Serialize(XDocument doc)
    {
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            Indent = false,
        };
        using (var xw = XmlWriter.Create(ms, settings))
            doc.Save(xw);
        return ms.ToArray();
    }

    private static byte[] BuildContentTypes(List<XlsxSheet> sheets)
    {
        var root = new XElement(ContentTypes + "Types",
            new XElement(ContentTypes + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypes + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypes + "Override",
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")));

        for (var i = 0; i < sheets.Count; i++)
            root.Add(new XElement(ContentTypes + "Override",
                new XAttribute("PartName", $"/xl/worksheets/sheet{i + 1}.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));

        return Serialize(new XDocument(new XDeclaration("1.0", "UTF-8", null), root));
    }

    private static byte[] BuildRootRels()
    {
        var root = new XElement(PackageRels + "Relationships",
            new XElement(PackageRels + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml")));
        return Serialize(new XDocument(new XDeclaration("1.0", "UTF-8", null), root));
    }

    private static byte[] BuildWorkbook(List<XlsxSheet> sheets)
    {
        var sheetsEl = new XElement(Main + "sheets");
        for (var i = 0; i < sheets.Count; i++)
            sheetsEl.Add(new XElement(Main + "sheet",
                new XAttribute("name", sheets[i].Name),
                new XAttribute("sheetId", i + 1),
                new XAttribute(OfficeDocRels + "id", $"rId{i + 1}")));

        var root = new XElement(Main + "workbook", sheetsEl);
        return Serialize(new XDocument(new XDeclaration("1.0", "UTF-8", null), root));
    }

    private static byte[] BuildWorkbookRels(List<XlsxSheet> sheets)
    {
        var root = new XElement(PackageRels + "Relationships");
        for (var i = 0; i < sheets.Count; i++)
            root.Add(new XElement(PackageRels + "Relationship",
                new XAttribute("Id", $"rId{i + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{i + 1}.xml")));
        return Serialize(new XDocument(new XDeclaration("1.0", "UTF-8", null), root));
    }

    private static byte[] BuildWorksheet(XlsxSheet sheet)
    {
        var sheetData = new XElement(Main + "sheetData");
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var rowEl = new XElement(Main + "row", new XAttribute("r", r + 1));
            for (var c = 0; c < sheet.Rows[r].Count; c++)
            {
                var cell = BuildCell(r + 1, c + 1, sheet.Rows[r][c]);
                if (cell is not null)
                    rowEl.Add(cell);
            }
            sheetData.Add(rowEl);
        }

        var root = new XElement(Main + "worksheet", sheetData);
        return Serialize(new XDocument(new XDeclaration("1.0", "UTF-8", null), root));
    }

    private static XElement? BuildCell(int row, int col, object? value)
    {
        var reference = CellRef.ToReference(row, col);
        switch (value)
        {
            case string s:
                return new XElement(Main + "c",
                    new XAttribute("r", reference),
                    new XAttribute("t", "inlineStr"),
                    new XElement(Main + "is",
                        new XElement(Main + "t",
                            new XAttribute(Xml + "space", "preserve"), s)));
            case bool b:
                return new XElement(Main + "c",
                    new XAttribute("r", reference),
                    new XAttribute("t", "b"),
                    new XElement(Main + "v", b ? "1" : "0"));
            case long l:
                return new XElement(Main + "c",
                    new XAttribute("r", reference),
                    new XElement(Main + "v", l.ToString(CultureInfo.InvariantCulture)));
            case int i:
                return BuildCell(row, col, (long)i);
            case double d:
                return new XElement(Main + "c",
                    new XAttribute("r", reference),
                    new XElement(Main + "v", d.ToString("R", CultureInfo.InvariantCulture)));
            default:
                return null; // 空单元格省略
        }
    }
}
