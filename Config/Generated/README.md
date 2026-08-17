# Config/Generated —— 自动生成目录（禁止手改）

本目录由 `Tools/ConfigExporter` 全量重建或稳定覆盖，**不允许人工修改任何文件**。

| 文件/目录 | 说明 |
| --- | --- |
| `Csv/` | 每张业务表一份规范化 UTF-8 CSV（LF 换行、无 BOM），便于 Git 比较 |
| `game-config.json` | 完整中间数据，供 Unity Editor 导入器读取；采用 `JsonUtility` 可解析的通用结构（`sheetName` + `columns` + `rows[{values[]}]`，数值为字符串） |
| `game-config.manifest.json` | 记录 schema 版本、工作簿 SHA-256、各表行数与输出哈希 |

约定：

- 所有文件使用 `UTF-8 无 BOM` + `LF` 换行，字段顺序与 schema 一致，键表按稳定 ID 升序排序。
- `game-config.json` 与 CSV 为**确定性输出**：相同 Excel 输入生成字节级一致的中间数据。
- `game-config.manifest.json` 默认不含时间戳（保证确定性）；仅在导出时加 `--stamp` 才写入 `generatedAtUtc`。
- 需要修改数值时，请编辑 `Config/Excel/游戏数值配置.xlsx`，然后重新运行导出器，不要直接改本目录内容。
