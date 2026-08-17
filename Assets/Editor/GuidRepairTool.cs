using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// .meta GUID 修复工具。
///
/// 背景：项目中有 90 个 .meta 的 guid 被改写成了 base64 随机值（从最初提交起就存在），
/// 导致场景/脚本/资产的引用悬空。原始 32 位十六进制 GUID 保留在引用端，可以反推回来。
///
/// 菜单：
///   Tools/游戏配置/生成 .meta 修复计划  —— 只读，生成 GuidRepairPlan.txt
///   Tools/游戏配置/执行 .meta 修复      —— 应用高置信修复，写前备份为 *.guidrepair.bak
///
/// 分类：
///   RESTORE     —— 确定性反推出原始 GUID（ScriptableObject 脚本 + Installer/场景绑定资产；冲突时场景值优先）
///   CONFLICT    —— 场景内同一资产仍有多条不一致引用，需人工裁决（暂不自动处理）
///   REGENERATE  —— 未被引用的 meta（文件夹 / 普通类 / 接口），生成新 GUID
///   REVIEW      —— 图片 / 预制体 / asmdef / dll 等，暂不自动处理
/// </summary>
public static class GuidRepairTool
{
    private const string HexPattern = "^[0-9a-f]{32}$";

    private sealed class RepairEntry
    {
        public string MetaPath;
        public string Action;      // RESTORE / CONFLICT / REGENERATE / REVIEW
        public string TargetGuid;  // RESTORE / REGENERATE 的目标值
        public string Reason;      // 证据或原因
    }

    // ────────────────────────────────────────────────────────────
    // 菜单入口
    // ────────────────────────────────────────────────────────────

    [MenuItem("Tools/游戏配置/生成 .meta 修复计划")]
    public static void GeneratePlan()
    {
        try
        {
            List<RepairEntry> plan = ComputePlan(out _, out _, out _);
            string reportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GuidRepairPlan.txt"));
            File.WriteAllText(reportPath, BuildPlanText(plan), new UTF8Encoding(false));

            int restore = Count(plan, "RESTORE");
            int conflict = Count(plan, "CONFLICT");
            int regenerate = Count(plan, "REGENERATE");
            int review = Count(plan, "REVIEW");
            Debug.Log(
                "[GuidRepairTool] 计划已生成：RESTORE " + restore + "、CONFLICT " + conflict +
                "、REGENERATE " + regenerate + "、REVIEW " + review +
                "。报告：" + reportPath);
        }
        catch (Exception e)
        {
            Debug.LogError("[GuidRepairTool] 生成计划失败：" + e);
        }
    }

    [MenuItem("Tools/游戏配置/执行 .meta 修复")]
    public static void ApplyPlan()
    {
        try
        {
            List<RepairEntry> plan = ComputePlan(out var definedGuids, out var sceneFields, out var installerFields);
            int applied = 0;
            var skipped = new List<string>();

            foreach (RepairEntry entry in plan)
            {
                if (entry.Action != "RESTORE" && entry.Action != "REGENERATE")
                    continue;

                Backup(entry.MetaPath);
                RewriteGuid(entry.MetaPath, entry.TargetGuid);
                applied++;
            }

            // 场景优先：把 Installer 默认引用同步到场景值（仅限被修复的悬空场景值）
            SyncInstallerDefaultReferences(sceneFields, installerFields, definedGuids);

            foreach (RepairEntry entry in plan)
            {
                if (entry.Action == "CONFLICT" || entry.Action == "REVIEW")
                    skipped.Add(entry.MetaPath + "  (" + entry.Action + ": " + entry.Reason + ")");
            }

            Debug.Log("[GuidRepairTool] 已修复 " + applied + " 个 .meta（已备份为 *.guidrepair.bak）。");
            if (skipped.Count > 0)
            {
                Debug.Log("[GuidRepairTool] 以下未自动处理，见 GuidRepairPlan.txt：\n  " + string.Join("\n  ", skipped.ToArray()));
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[GuidRepairTool] 执行失败：" + e);
        }
    }

    // ────────────────────────────────────────────────────────────
    // 计划计算
    // ────────────────────────────────────────────────────────────

    private static List<RepairEntry> ComputePlan(
        out HashSet<string> definedGuids,
        out Dictionary<string, HashSet<string>> sceneFields,
        out Dictionary<string, HashSet<string>> installerFields)
    {
        string assetsRoot = Application.dataPath;

        // 合法 hex guid 集合（存在于某个 .meta 中的 GUID）
        definedGuids = new HashSet<string>();
        // 损坏 meta 路径 -> 当前 base64 值
        var corrupted = new List<KeyValuePair<string, string>>();

        foreach (string meta in EnumerateFiles(assetsRoot, "*.meta"))
        {
            string raw = ReadMetaGuid(meta);
            if (string.IsNullOrEmpty(raw))
            {
                corrupted.Add(new KeyValuePair<string, string>(meta, ""));
                continue;
            }
            if (!Regex.IsMatch(raw, HexPattern, RegexOptions.IgnoreCase))
            {
                corrupted.Add(new KeyValuePair<string, string>(meta, raw));
                continue;
            }
            definedGuids.Add(raw.ToLowerInvariant());
        }

        // 场景字段 / Installer 默认引用字段，分别记录来源（场景优先）
        sceneFields = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        installerFields = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        CollectInstallerFields(assetsRoot, installerFields, sceneFields);

        var plan = new List<RepairEntry>();

        foreach (var kv in corrupted)
        {
            string metaPath = kv.Key;
            string stem = metaPath.Substring(0, metaPath.Length - ".meta".Length);
            string ext = Path.GetExtension(stem).ToLowerInvariant();
            string name = Path.GetFileNameWithoutExtension(stem);

            // 文件夹：生成新 GUID（不被序列化引用）
            if (Directory.Exists(stem))
            {
                plan.Add(Regenerate(metaPath, definedGuids, "文件夹 meta，无序列化引用"));
                continue;
            }

            if (ext == ".cs")
            {
                plan.Add(ClassifyScript(metaPath, name, assetsRoot, definedGuids));
                continue;
            }

            if (ext == ".asset")
            {
                plan.Add(ClassifyAsset(metaPath, name, sceneFields, installerFields, definedGuids));
                continue;
            }

            plan.Add(new RepairEntry { MetaPath = metaPath, Action = "REVIEW", Reason = "未知类型：" + ext });
        }

        return plan;
    }

    private static RepairEntry ClassifyScript(string metaPath, string scriptName, string assetsRoot, HashSet<string> definedGuids)
    {
        // 先按命名约定反推 ScriptableObject 脚本：XSO.cs -> X.asset 的 m_Script。
        // 这一步不依赖 .cs 文件内容（.cs 可能是 GBK/UTF-16 编码，ReadAllText 可能乱码）。
        string soAssetName = scriptName.EndsWith("SO", StringComparison.Ordinal)
            ? scriptName.Substring(0, scriptName.Length - 2) + ".asset"
            : scriptName + ".asset";

        string assetPath = FindAsset(assetsRoot, soAssetName);
        if (assetPath != null)
        {
            string scriptGuid = ReadScriptGuid(assetPath);
            // 该 asset 的 m_Script 悬空 -> 就是这个脚本被改坏前的原始 GUID
            if (scriptGuid != null && !definedGuids.Contains(scriptGuid))
                return new RepairEntry { MetaPath = metaPath, Action = "RESTORE", TargetGuid = scriptGuid, Reason = assetPath + " 的 m_Script" };
            // asset 存在但其 m_Script 已能解析 -> 该 asset 指向的不是本脚本，不能反推
            return new RepairEntry { MetaPath = metaPath, Action = "REVIEW", Reason = "同名 asset 的 m_Script 未悬空，无法反推" };
        }

        // 兜底：读脚本内容，区分 MonoBehaviour 与普通类/接口
        string scriptPath = metaPath.Substring(0, metaPath.Length - ".meta".Length);
        string scriptText = SafeReadAllText(scriptPath);
        if (scriptText != null && scriptText.Contains("MonoBehaviour"))
            return new RepairEntry { MetaPath = metaPath, Action = "REVIEW", Reason = "MonoBehaviour 脚本，需人工核对引用" };

        // 普通类 / 接口 / 结构体：guid 不被序列化引用，安全重建
        return Regenerate(metaPath, definedGuids, "普通类/接口，非 MonoBehaviour/ScriptableObject");
    }

    private static RepairEntry ClassifyAsset(string metaPath, string assetName,
        Dictionary<string, HashSet<string>> sceneFields,
        Dictionary<string, HashSet<string>> installerFields,
        HashSet<string> definedGuids)
    {
        string assetNormalized = Normalize(assetName + "SO");
        string assetPlain = Normalize(assetName);

        var sceneDangling = CollectDangling(sceneFields, assetNormalized, assetPlain, definedGuids);
        var installerDangling = CollectDangling(installerFields, assetNormalized, assetPlain, definedGuids);

        // 优先采用场景值（运行时的权威来源）
        if (sceneDangling.Count == 1)
        {
            string guid = First(sceneDangling);
            string reason = "场景字段优先：" + string.Join(",", MatchingFields(sceneFields, assetNormalized, assetPlain).ToArray());
            if (installerDangling.Count > 0 && !installerDangling.Contains(guid))
                reason += "（Installer 默认引用另存为 " + string.Join(" / ", SortedList(installerDangling).ToArray()) + "，将同步）";
            return new RepairEntry { MetaPath = metaPath, Action = "RESTORE", TargetGuid = guid, Reason = reason };
        }
        if (sceneDangling.Count > 1)
        {
            return new RepairEntry { MetaPath = metaPath, Action = "CONFLICT", Reason = "场景内多引用不一致：" + string.Join(" / ", SortedList(sceneDangling).ToArray()) };
        }

        // 场景没有悬空引用时，回退到 Installer 默认引用
        if (installerDangling.Count == 1)
        {
            string guid = First(installerDangling);
            return new RepairEntry { MetaPath = metaPath, Action = "RESTORE", TargetGuid = guid, Reason = "Installer 字段：" + string.Join(",", MatchingFields(installerFields, assetNormalized, assetPlain).ToArray()) };
        }
        if (installerDangling.Count > 1)
        {
            return new RepairEntry { MetaPath = metaPath, Action = "CONFLICT", Reason = "Installer 多引用不一致：" + string.Join(" / ", SortedList(installerDangling).ToArray()) };
        }
        return new RepairEntry { MetaPath = metaPath, Action = "REVIEW", Reason = "未在场景/Installer 字段中找到悬空引用" };
    }

    private static HashSet<string> CollectDangling(Dictionary<string, HashSet<string>> fields, string assetNormalized, string assetPlain, HashSet<string> definedGuids)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            string n = Normalize(field.Key);
            if (n == assetNormalized || n == assetPlain)
            {
                foreach (string guid in field.Value)
                    if (!definedGuids.Contains(guid))
                        result.Add(guid);
            }
        }
        return result;
    }

    private static List<string> MatchingFields(Dictionary<string, HashSet<string>> fields, string assetNormalized, string assetPlain)
    {
        var list = new List<string>();
        foreach (var field in fields)
        {
            string n = Normalize(field.Key);
            if (n == assetNormalized || n == assetPlain)
                list.Add(field.Key);
        }
        return list;
    }

    private static string First(HashSet<string> set)
    {
        foreach (string s in set) return s;
        return null;
    }

    private static List<string> SortedList(HashSet<string> set)
    {
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static RepairEntry Regenerate(string metaPath, HashSet<string> definedGuids, string reason)
    {
        string guid;
        do { guid = Guid.NewGuid().ToString("N"); } while (definedGuids.Contains(guid));
        return new RepairEntry { MetaPath = metaPath, Action = "REGENERATE", TargetGuid = guid, Reason = reason };
    }

    // ────────────────────────────────────────────────────────────
    // 解析辅助
    // ────────────────────────────────────────────────────────────

    private static void CollectInstallerFields(string assetsRoot,
        Dictionary<string, HashSet<string>> installerFields,
        Dictionary<string, HashSet<string>> sceneFields)
    {
        string installerMeta = Path.Combine(assetsRoot, "Scripts", "Infrastructure", "Installers", "GameInstaller.cs.meta");
        string gameScene = Path.Combine(assetsRoot, "Scenes", "GameScene.unity");

        // GameInstaller.cs.meta 的 defaultReferences 形如：- _field: {fileID:..., guid: <hex>, type: 2}
        var defaultRef = new Regex(@"^\s*-\s*(\w+):\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]{32})", RegexOptions.Multiline);
        // GameScene.unity 形如：  _field: {fileID:..., guid: <hex>, type: 2}
        var sceneRef = new Regex(@"^\s*(\w+):\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]{32})", RegexOptions.Multiline);

        CollectFieldRefs(installerMeta, defaultRef, installerFields);
        CollectFieldRefs(gameScene, sceneRef, sceneFields);
    }

    private static void CollectFieldRefs(string path, Regex regex, Dictionary<string, HashSet<string>> fields)
    {
        if (!File.Exists(path)) return;
        string text = SafeReadAllText(path);
        if (text == null) return;

        foreach (Match m in regex.Matches(text))
        {
            string field = m.Groups[1].Value;
            string guid = m.Groups[2].Value.ToLowerInvariant();
            if (!fields.TryGetValue(field, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                fields[field] = set;
            }
            set.Add(guid);
        }
    }

    private static string ReadScriptGuid(string assetPath)
    {
        string text = SafeReadAllText(assetPath);
        if (text == null) return null;
        Match m = Regex.Match(text, @"m_Script:\s*\{[^{}]*guid:\s*([0-9a-fA-F]{32})");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static string FindAsset(string assetsRoot, string fileName)
    {
        foreach (string f in Directory.EnumerateFiles(assetsRoot, "*.asset", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

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

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        foreach (string f in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            yield return f;
    }

    private static string SafeReadAllText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    // ────────────────────────────────────────────────────────────
    // 同步 Installer 默认引用（场景优先）
    // ────────────────────────────────────────────────────────────

    private static void SyncInstallerDefaultReferences(
        Dictionary<string, HashSet<string>> sceneFields,
        Dictionary<string, HashSet<string>> installerFields,
        HashSet<string> definedGuids)
    {
        string installerMeta = Path.Combine(Application.dataPath, "Scripts", "Infrastructure", "Installers", "GameInstaller.cs.meta");
        if (!File.Exists(installerMeta)) return;

        string content = SafeReadAllText(installerMeta);
        if (content == null) return;

        bool changed = false;
        foreach (var sceneField in sceneFields)
        {
            // 仅当场景值是“悬空（被修复的原始 GUID）”时同步，避免误改有意的合法差异
            if (sceneField.Value.Count != 1) continue;
            string sceneGuid = First(sceneField.Value);
            if (sceneGuid == null || definedGuids.Contains(sceneGuid)) continue;

            if (!installerFields.TryGetValue(sceneField.Key, out var installerSet)) continue;
            if (installerSet.Count == 1 && installerSet.Contains(sceneGuid)) continue; // 已一致

            string pattern = @"(?m)^(\s*-\s*" + Regex.Escape(sceneField.Key) + @":\s*\{fileID:\s*\d+,\s*guid:\s*)[0-9a-fA-F]{32}";
            string updated = Regex.Replace(content, pattern, "${1}" + sceneGuid);
            if (!string.Equals(updated, content, StringComparison.Ordinal))
            {
                content = updated;
                changed = true;
                Debug.Log("[GuidRepairTool] 同步 Installer 默认引用 " + sceneField.Key + " -> " + sceneGuid);
            }
        }

        if (changed)
        {
            Backup(installerMeta);
            File.WriteAllText(installerMeta, content);
        }
    }

    // ────────────────────────────────────────────────────────────
    // 写入
    // ────────────────────────────────────────────────────────────

    private static void Backup(string metaPath)
    {
        string backup = metaPath + ".guidrepair.bak";
        if (!File.Exists(backup))
            File.Copy(metaPath, backup, false);
    }

    private static void RewriteGuid(string metaPath, string newHexGuid)
    {
        string content = File.ReadAllText(metaPath);
        // 只替换 guid 行，保留换行符与其余内容。
        string updated = Regex.Replace(content, @"(?m)^guid:[^\r\n]*", "guid: " + newHexGuid);
        File.WriteAllText(metaPath, updated);
    }

    // ────────────────────────────────────────────────────────────
    // 报告
    // ────────────────────────────────────────────────────────────

    private static int Count(List<RepairEntry> plan, string action)
    {
        int n = 0;
        foreach (RepairEntry e in plan) if (e.Action == action) n++;
        return n;
    }

    private static string BuildPlanText(List<RepairEntry> plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine(".meta GUID 修复计划（只读）");
        sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        AppendSection(sb, plan, "RESTORE", "确定性反推原始 GUID");
        AppendSection(sb, plan, "CONFLICT", "多引用不一致，需人工裁决");
        AppendSection(sb, plan, "REGENERATE", "未被引用，重建新 GUID");
        AppendSection(sb, plan, "REVIEW", "暂不自动处理");
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, List<RepairEntry> plan, string action, string title)
    {
        sb.AppendLine("## " + title + "（" + action + "）");
        sb.AppendLine();
        bool any = false;
        foreach (RepairEntry e in plan)
        {
            if (e.Action != action) continue;
            any = true;
            sb.AppendLine("  " + e.MetaPath);
            if (!string.IsNullOrEmpty(e.TargetGuid))
                sb.AppendLine("      -> " + e.TargetGuid);
            if (!string.IsNullOrEmpty(e.Reason))
                sb.AppendLine("      (" + e.Reason + ")");
        }
        if (!any) sb.AppendLine("  （无）");
        sb.AppendLine();
    }
}
