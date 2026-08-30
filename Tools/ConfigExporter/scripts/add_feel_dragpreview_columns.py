"""向「表现配置」工作表追加卡牌拖拽世界空间预览的 3 个数值列（一次性迁移脚本）。

背景
----
Config/Excel/游戏数值配置.xlsx 是配置唯一主源，但为二进制；exporter 的 `init`
命令会按 schema 重建整个工作簿，会丢弃已手工录入的数据。因此这里做「外科手术式」
注入：只在包内两个部件上做文本级最小改动——

  * xl/sharedStrings.xml       追加 6 条字符串（3 个列名 + 3 条中文说明）
  * xl/worksheets/sheetNN.xml  在表头行/说明行/数据行末尾各追加 3 个单元格

其余条目（样式、主题、docProps、其他工作表）按原字节复制，不做任何解析或重排，
避免 XML 往返丢失命名空间声明与 WPS 私有扩展。

用法（幂等：列已存在时直接返回，不做修改）
    python Tools/ConfigExporter/scripts/add_feel_dragpreview_columns.py

之后必须重跑导出（Config/Generated 由工具生成，禁止手改）：
    dotnet run --project Tools/ConfigExporter/src/ConfigExporter -- ^
        --input Config/Excel/游戏数值配置.xlsx ^
        --schema Config/Schema/配置表结构.json ^
        --output Config/Generated
"""

import os
import re
import shutil
import sys
import zipfile
import xml.etree.ElementTree as ET

MAIN = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
PKG_RELS = "http://schemas.openxmlformats.org/package/2006/relationships"
DOC_RELS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
M = "{%s}" % MAIN

SHEET_NAME = "表现配置"

# (列名, 中文说明, 值)；顺序必须与 Config/Schema/配置表结构.json 的列顺序一致。
NEW_COLUMNS = [
    ("cardDragPreviewHoverHeight", "持握悬停高度（世界单位，模型悬停在命中点上方的高度）", "1.0"),
    ("cardDragPreviewSnapDuration", "落位补间时长（秒）", "0.2"),
    ("cardDragPreviewAppearDuration", "拎起 scale-in 时长（秒，0=不做）", "0.08"),
]

HEADER_ROW, NOTE_ROW, DATA_ROW = 1, 2, 3

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
WORKBOOK = os.path.join(REPO, "Config", "Excel", "游戏数值配置.xlsx")


def col_letters(index):
    """1 基列号 -> Excel 列字母（1 -> A，27 -> AA）。"""
    name = ""
    while index > 0:
        index, rem = divmod(index - 1, 26)
        name = chr(ord("A") + rem) + name
    return name


def letters_to_index(ref):
    """单元格引用（如 "AB12"）-> 列号（1 基）。"""
    index = 0
    for ch in ref:
        if not ch.isalpha():
            break
        index = index * 26 + (ord(ch.upper()) - ord("A") + 1)
    return index


def xml_escape(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def find_sheet_part(zf):
    """在包内定位 SHEET_NAME 对应的 worksheet 部件路径。"""
    wb = ET.fromstring(zf.read("xl/workbook.xml"))
    rels = ET.fromstring(zf.read("xl/_rels/workbook.xml.rels"))
    rel_map = {e.get("Id"): e.get("Target") for e in rels.findall("{%s}Relationship" % PKG_RELS)}
    for sheet in wb.iter(M + "sheet"):
        if sheet.get("name") == SHEET_NAME:
            return "xl/" + rel_map[sheet.get("{%s}id" % DOC_RELS)].lstrip("/")
    raise SystemExit("工作簿中找不到工作表 [%s]" % SHEET_NAME)


def read_shared_strings(text):
    """按出现顺序返回共享字符串；本工作簿全部为无富文本的 <si><t>。"""
    return [
        m.group(1)
        for m in re.finditer(r"<si><t(?:\s[^>]*)?>(.*?)</t></si>", text, re.S)
    ]


def row_span(text, row_number):
    """取出 <row r="N" ...>...</row> 的 (起始下标, 结束下标, 整段文本)。"""
    pattern = r'<row r="%d"[^>]*>.*?</row>' % row_number
    match = re.search(pattern, text, re.S)
    if match is None:
        raise SystemExit("[%s] 缺少第 %d 行" % (SHEET_NAME, row_number))
    return match.start(), match.end(), match.group(0)


def main():
    if not os.path.isfile(WORKBOOK):
        raise SystemExit("找不到工作簿: %s" % WORKBOOK)

    with zipfile.ZipFile(WORKBOOK) as zf:
        entries = [(info, zf.read(info.filename)) for info in zf.infolist()]
        sheet_part = find_sheet_part(zf)

    parts = {info.filename: data for info, data in entries}
    shared_text = parts["xl/sharedStrings.xml"].decode("utf-8")
    sheet_text = parts[sheet_part].decode("utf-8")

    shared = read_shared_strings(shared_text)
    string_index = {text: i for i, text in enumerate(shared)}

    # 幂等检查：表头已含新列则直接返回；只含部分则拒绝半量写入。
    _, _, header_text = row_span(sheet_text, HEADER_ROW)
    header_names = {
        shared[int(v)]
        for v in re.findall(r'<c[^>]*t="s"[^>]*><v>(\d+)</v></c>', header_text)
        if int(v) < len(shared)
    }
    present = [c for c in NEW_COLUMNS if c[0] in header_names]
    if len(present) == len(NEW_COLUMNS):
        print("列已存在，无需修改：%s" % WORKBOOK)
        return 0
    if present:
        raise SystemExit(
            "检测到 %d/%d 列已存在，拒绝半量写入，请人工核对表头。"
            % (len(present), len(NEW_COLUMNS))
        )

    # 追加位置 = 表头现有最右列 + 1。
    first_col = max(
        letters_to_index(ref) for ref in re.findall(r'<c r="([A-Z]+)\d+"', header_text)
    ) + 1

    # 新增共享字符串（列名 + 说明），复用已存在的同文本。
    appended = []

    def intern(text):
        if text not in string_index:
            string_index[text] = len(shared)
            shared.append(text)
            appended.append(text)
        return string_index[text]

    header_cells, note_cells, data_cells = [], [], []
    for offset, (name, note, value) in enumerate(NEW_COLUMNS):
        letter = col_letters(first_col + offset)
        header_cells.append(
            '<c r="%s%d" t="s"><v>%d</v></c>' % (letter, HEADER_ROW, intern(name)))
        note_cells.append(
            '<c r="%s%d" t="s"><v>%d</v></c>' % (letter, NOTE_ROW, intern(note)))
        data_cells.append('<c r="%s%d"><v>%s</v></c>' % (letter, DATA_ROW, value))

    last_col = first_col + len(NEW_COLUMNS) - 1
    span = "1:%d" % last_col

    # 逐行注入：在 </row> 之前追加单元格，并同步 spans。
    for row_number, cells in (
        (HEADER_ROW, header_cells), (NOTE_ROW, note_cells), (DATA_ROW, data_cells)
    ):
        start, end, row_text = row_span(sheet_text, row_number)
        new_row = row_text[: -len("</row>")] + "".join(cells) + "</row>"
        new_row = re.sub(r'(<row r="%d"[^>]*?)\sspans="[^"]*"' % row_number, r"\1", new_row, count=1)
        new_row = new_row.replace(
            '<row r="%d"' % row_number, '<row r="%d" spans="%s"' % (row_number, span), 1)
        sheet_text = sheet_text[:start] + new_row + sheet_text[end:]

    sheet_text = re.sub(
        r'<dimension ref="[^"]*"/>',
        '<dimension ref="A1:%s%d"/>' % (col_letters(last_col), DATA_ROW),
        sheet_text, count=1)

    # sst：追加 <si> 并同步 count（全簿 t="s" 单元格总数）/ uniqueCount。
    new_si = "".join("<si><t>%s</t></si>" % xml_escape(t) for t in appended)
    shared_text = shared_text.replace("</sst>", new_si + "</sst>", 1)

    # count = 全簿 t="s" 单元格总数（含重复引用），uniqueCount = 去重字符串数。
    # 原始值由 Excel 维护，直接加增量，避免我的扫描口径与 Excel 不一致。
    sst_match = re.search(r'<sst[^>]*?count="(\d+)"[^>]*?uniqueCount="(\d+)"', shared_text)
    declared_count, declared_unique = int(sst_match.group(1)), int(sst_match.group(2))
    new_string_cells = 2 * len(NEW_COLUMNS)   # 表头 3 + 说明行 3
    shared_text = shared_text.replace(
        'count="%d" uniqueCount="%d"' % (declared_count, declared_unique),
        'count="%d" uniqueCount="%d"' % (declared_count + new_string_cells, declared_unique + len(appended)),
        1)

    backup = WORKBOOK + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(WORKBOOK, backup)
        print("已备份原工作簿 -> %s" % backup)

    tmp = WORKBOOK + ".tmp"
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as out:
        for info, data in entries:
            if info.filename == sheet_part:
                data = sheet_text.encode("utf-8")
            elif info.filename == "xl/sharedStrings.xml":
                data = shared_text.encode("utf-8")
            out.writestr(info, data)
    os.replace(tmp, WORKBOOK)

    print("已向 [%s] 追加 %d 列（%s..%s）：%s" % (
        SHEET_NAME, len(NEW_COLUMNS),
        col_letters(first_col), col_letters(last_col),
        ", ".join(c[0] for c in NEW_COLUMNS)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
