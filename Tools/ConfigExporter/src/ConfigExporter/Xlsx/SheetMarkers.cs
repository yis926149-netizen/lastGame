namespace ConfigExporter.Xlsx;

/// <summary>导出器与 Excel 工作簿之间的结构约定标记。</summary>
public static class SheetMarkers
{
    /// <summary>
    /// 字段中文说明行的首列标记。说明行位于表头行（第1行）正下方（第2行），
    /// 首列为该标记，其余列依次是该列的中文解释；读取时按此标记整行跳过。
    /// </summary>
    public const string FieldNoteRow = "#字段说明";
}
