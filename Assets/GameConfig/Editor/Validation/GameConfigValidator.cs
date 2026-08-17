using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace GameConfig.Editor
{
    /// <summary>
    /// 校验 game-config.json 的 schema 版本与基本结构（骨架）。
    /// 阶段2 起在此叠加：schema 版本比对、源工作簿哈希比对、跨表引用与资源校验。
    /// </summary>
    public static class GameConfigValidator
    {
        public static string Validate(string jsonPath)
        {
            var sb = new StringBuilder();
            if (!File.Exists(jsonPath))
                return $"找不到 {jsonPath}";

            GameConfigFile file;
            try
            {
                file = JsonUtility.FromJson<GameConfigFile>(File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                return "JSON 解析失败: " + ex.Message;
            }

            if (file is null || file.tables is null)
                return "game-config.json 结构为空或非法";

            sb.AppendLine("schemaVersion: " + file.schemaVersion);
            sb.AppendLine("表数量: " + file.tables.Length);
            foreach (var table in file.tables)
                sb.AppendLine("- " + table.sheetName + ": " + (table.rows?.Length ?? 0) + " 行");

            return sb.ToString();
        }
    }
}
