using System;
using System.Xml.Linq;

namespace ConfigExporter.Xlsx;

internal static class XlsxNamespaces
{
    public static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static readonly XNamespace PackageRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace OfficeDocRels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    public static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";
}

/// <summary>单元格地址 A1 记法与行列号（1 基）互转。</summary>
public static class CellRef
{
    public static string ToReference(int row, int col) => ColumnName(col) + row;

    public static string ColumnName(int col)
    {
        var s = "";
        while (col > 0)
        {
            col--;
            s = (char)('A' + col % 26) + s;
            col /= 26;
        }
        return s;
    }

    public static (int row, int col) Parse(string reference)
    {
        var i = 0;
        while (i < reference.Length && char.IsLetter(reference[i]))
            i++;
        if (i == 0 || i == reference.Length)
            throw new FormatException($"非法单元格地址: {reference}");

        var col = 0;
        for (var j = 0; j < i; j++)
            col = col * 26 + (char.ToUpperInvariant(reference[j]) - 'A' + 1);

        var row = int.Parse(reference.AsSpan(i));
        return (row, col);
    }
}
