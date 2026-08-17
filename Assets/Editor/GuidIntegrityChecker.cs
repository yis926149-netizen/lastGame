using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GUID 与引用完整性诊断工具（只读，不改任何文件）。
/// 菜单：Tools/游戏配置/GUID与引用完整性检查。
///
/// 输出：
/// 1. 非标准 GUID 的 .meta 文件列表；
/// 2. 悬空引用（被引用但找不到对应 .meta 的 32 位十六进制 GUID）；
/// 3. Missing Script 风险（m_Script 指向悬空 GUID 的资产）；
/// 4. 原始 GUID 恢复线索（悬空 GUID 通常就是被改坏 .meta 的原始值）。
/// 报告写入工程根目录 GuidIntegrityReport.txt。
/// </summary>
public static class GuidIntegrityChecker
{
    private const string HexGuidPattern = "^[0-9a-f]{32}$";

    [MenuItem("Tools/游戏配置/GUID与引用完整性检查")]
    public static void Run()
    {
        string assetsRoot = Application.dataPath;

        // guid(小写) -> 定义它的 .meta 路径（仅合法的 32 位十六进制 GUID）。
        var definedGuids = new Dictionary<string, string>();
        // 非标准 GUID 的 .meta 文件（含缺失 guid 行）。
        var corruptedMetas = new List<string>();
        // guid(小写) -> 引用它的 "文件(行号): 行内容" 列表。
        var references = new Dictionary<string, List<string>>();
        // 资产文件 -> 其 m_Script 指向的悬空 GUID 列表。
        var missingScripts = new List<string>();

        try
        {
            // 第一遍：收集所有 .meta 定义的 GUID。
            foreach (string meta in EnumerateFiles(assetsRoot, "*.meta"))
            {
                string rawGuid = ReadMetaGuid(meta);
                if (string.IsNullOrEmpty(rawGuid))
                {
                    corruptedMetas.Add(meta + "  (缺失 guid 行)");
                    continue;
                }

                if (!Regex.IsMatch(rawGuid, HexGuidPattern, RegexOptions.IgnoreCase))
                {
                    corruptedMetas.Add(meta + "  当前值: " + rawGuid);
                    continue;
                }

                string guid = rawGuid.ToLowerInvariant();
                if (!definedGuids.ContainsKey(guid))
                    definedGuids[guid] = meta;
            }

            // 第二遍：收集所有引用（场景、预制体、资产、材质、动画、meta 的 defaultReferences 等）。
            foreach (string file in EnumerateReferenceFiles(assetsRoot))
            {
                CollectReferences(file, references);
                CollectMissingScript(file, definedGuids, missingScripts);
            }

            // 计算悬空引用：被引用但没有合法 .meta 定义。
            var dangling = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kv in references)
            {
                if (!definedGuids.ContainsKey(kv.Key))
                    dangling[kv.Key] = kv.Value;
            }

            WriteReport(assetsRoot, corruptedMetas, dangling, missingScripts);
        }
        catch (Exception e)
        {
            Debug.LogError("[GuidIntegrityChecker] 扫描失败：" + e);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            yield return file;
    }

    private static readonly HashSet<string> ReferenceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".unity", ".prefab", ".asset", ".mat", ".anim", ".controller",
        ".overrideController", ".playable", ".mixer", ".spriteatlas", ".preset",
        ".meta" // 读取 .meta 中的 defaultReferences / externalObjects
    };

    private static IEnumerable<string> EnumerateReferenceFiles(string root)
    {
        foreach (string file in EnumerateFiles(root, "*"))
        {
            if (ReferenceExtensions.Contains(Path.GetExtension(file)))
                yield return file;
        }
    }

    /// <summary>读取 .meta 顶部的 guid 行；没有则返回 null。</summary>
    private static string ReadMetaGuid(string metaPath)
    {
        foreach (string line in File.ReadLines(metaPath))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("guid:", StringComparison.Ordinal))
                return trimmed.Substring("guid:".Length).Trim();
        }
        return null;
    }

    private static readonly Regex GuidReferenceRegex = new Regex(
        @"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

    private static void CollectReferences(string file, Dictionary<string, List<string>> references)
    {
        int lineNo = 0;
        foreach (string rawLine in File.ReadLines(file))
        {
            lineNo++;
            foreach (Match m in GuidReferenceRegex.Matches(rawLine))
            {
                string guid = m.Groups[1].Value.ToLowerInvariant();
                if (!references.TryGetValue(guid, out var list))
                {
                    list = new List<string>();
                    references[guid] = list;
                }
                list.Add($"{file}({lineNo}): {rawLine.Trim()}");
            }
        }
    }

    private static readonly Regex ScriptReferenceRegex = new Regex(
        @"m_Script:\s*\{[^{}]*guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

    private static void CollectMissingScript(string file, Dictionary<string, string> definedGuids, List<string> missingScripts)
    {
        string ext = Path.GetExtension(file);
        if (ext != ".unity" && ext != ".prefab" && ext != ".asset" && ext != ".mat")
            return;

        int lineNo = 0;
        foreach (string rawLine in File.ReadLines(file))
        {
            lineNo++;
            Match m = ScriptReferenceRegex.Match(rawLine);
            if (!m.Success)
                continue;

            string guid = m.Groups[1].Value.ToLowerInvariant();
            if (!definedGuids.ContainsKey(guid))
                missingScripts.Add($"{file}({lineNo}): m_Script -> {guid}");
        }
    }

    private static void WriteReport(
        string assetsRoot,
        List<string> corruptedMetas,
        SortedDictionary<string, List<string>> dangling,
        List<string> missingScripts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GUID 与引用完整性诊断报告（只读，未修改任何文件）");
        sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Assets 目录: " + assetsRoot);
        sb.AppendLine();
        sb.AppendLine("================================================================");
        sb.AppendLine();

        sb.AppendLine("# 1. 非标准 GUID 的 .meta 文件（共 " + corruptedMetas.Count + " 个）");
        sb.AppendLine();
        if (corruptedMetas.Count == 0)
        {
            sb.AppendLine("  （无）");
        }
        else
        {
            foreach (string meta in corruptedMetas)
                sb.AppendLine("  " + meta);
        }
        sb.AppendLine();

        sb.AppendLine("# 2. 悬空引用：被引用但找不到合法 .meta 定义（共 " + dangling.Count + " 个）");
        sb.AppendLine();
        if (dangling.Count == 0)
        {
            sb.AppendLine("  （无）");
        }
        else
        {
            foreach (var kv in dangling)
            {
                sb.AppendLine("  [" + kv.Key + "]");
                foreach (string reference in kv.Value)
                    sb.AppendLine("      " + reference);
            }
        }
        sb.AppendLine();

        sb.AppendLine("# 3. Missing Script 风险：m_Script 指向悬空 GUID（共 " + missingScripts.Count + " 处）");
        sb.AppendLine();
        if (missingScripts.Count == 0)
        {
            sb.AppendLine("  （无）");
        }
        else
        {
            foreach (string entry in missingScripts)
                sb.AppendLine("  " + entry);
        }
        sb.AppendLine();

        sb.AppendLine("# 4. 原始 GUID 恢复线索");
        sb.AppendLine();
        sb.AppendLine("  悬空 GUID 通常就是被改坏 .meta 文件改坏前的原始 GUID。");
        sb.AppendLine("  修复时优先把这些悬空值写回同名 .meta，而不是让 Unity 重新生成新 GUID。");
        sb.AppendLine();

        string reportPath = Path.GetFullPath(Path.Combine(assetsRoot, "..", "GuidIntegrityReport.txt"));
        File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));

        Debug.Log(
            "[GuidIntegrityChecker] 检查完成：非标准 meta " + corruptedMetas.Count +
            " 个，悬空引用 " + dangling.Count + " 个，Missing Script 风险 " + missingScripts.Count +
            " 处。报告已写入：" + reportPath);
    }
}
