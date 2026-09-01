namespace ConfigExporter.Xlsx;

/// <summary>导出器与 Excel 工作簿之间的结构约定标记。</summary>
public static class SheetMarkers
{
    /// <summary>
    /// 字段中文说明行的首列标记。说明行位于表头行（第1行）正下方（第2行），
    /// 首列为该标记，其余列依次是该列的中文解释；读取时按此标记整行跳过。
    /// </summary>
    public const string FieldNoteRow = "#字段说明";

    /// <summary>
    /// 注释行的首列前缀。首列以此开头的行整行跳过，供策划在表中随手写备注，
    /// 可出现在表内任意位置（含数据行之间与表尾）。
    /// </summary>
    public const string CommentRow = "//";
}
