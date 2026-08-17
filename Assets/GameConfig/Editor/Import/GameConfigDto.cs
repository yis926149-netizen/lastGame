using System;

namespace GameConfig.Editor
{
    /// <summary>与 game-config.json 对齐的 JsonUtility DTO（只供编辑器导入用，不进运行期）。</summary>
    [Serializable]
    public sealed class GameConfigFile
    {
        public string schemaVersion;
        public TableEntry[] tables;
    }

    [Serializable]
    public sealed class TableEntry
    {
        public string sheetName;
        public string[] columns;
        public RowEntry[] rows;
    }

    [Serializable]
    public sealed class RowEntry
    {
        public string[] values;
    }
}
