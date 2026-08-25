using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【P3 - GPU Instancing】编辑器侧工具：批量开启材质 GPU Instancing，并检查材质 Shader 引用。
///
/// 背景：地貌/资源/单位/建筑 prefab 数量大、材质少，同一 mesh + 同一材质被大量实例复用。
/// 给这些材质开启 GPU Instancing 后，Unity 会把同 mesh 同材质的实例合并进同一批 DrawCall，
/// 显著降低渲染批次与 CPU 提交开销。
///
/// 注意：
///  1. GPU Instancing 只对「重复 mesh + 重复材质」的 MeshRenderer 生效；Chunk 地形是
///     逐 Chunk 程序生成的独立 mesh（每 Chunk 一张唯一网格），不受 Instancing 影响。
///  2. 粒子（Lana Studio / VFXPACK）与 UI 材质不参与 Instancing，本工具默认不扫描这些目录。
///  3. 设置材质标记本身无害：若 shader 不支持 Instancing，Unity 只是忽略该标记，不会报错。
/// </summary>
public static class GPUInstancingTool
{
    // 扫描范围：模型类材质目录（地形 + 商店模型包）。VFX/UI 目录刻意排除。
    private static readonly string[] ModelMaterialFolders =
    {
        "Assets/Materials",
        "Assets/KayKit",
        "Assets/Toon_RTS",
    };

    private const string MissingShaderName = "Hidden/InternalErrorShader";

    // ─────────────────────────────────────────────────────────────
    // 菜单 1：批量开启材质 GPU Instancing
    // ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/微信小游戏性能优化/批量开启材质 GPU Instancing")]
    public static void EnableInstancingOnMaterials()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", ModelMaterialFolders);
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[GPUInstancingTool] 扫描目录内未找到任何材质。");
            return;
        }

        int enabled = 0;
        int skipped = 0;
        var enabledList = new List<string>(guids.Length);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                skipped++;
                continue;
            }

            if (mat.enableInstancing)
            {
                skipped++;
                continue;
            }

            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            enabledList.Add($"{path}  [{(mat.shader != null ? mat.shader.name : "<null shader>")}]");
            enabled++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var sb = new StringBuilder();
        sb.AppendLine($"[GPUInstancingTool] 完成：新开启 {enabled} 个材质，跳过 {skipped} 个（已开启或读取失败）。");
        sb.AppendLine("新开启列表：");
        foreach (string line in enabledList)
            sb.AppendLine("  - " + line);
        Debug.Log(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────
    // 菜单 2：检查材质 Shader 引用（缺失 shader 告警）
    // ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/微信小游戏性能优化/检查材质 Shader 引用")]
    public static void CheckMaterialShaderReferences()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", ModelMaterialFolders);
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[GPUInstancingTool] 扫描目录内未找到任何材质。");
            return;
        }

        var shaderUsage = new Dictionary<string, int>();
        var brokenMaterials = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            string shaderName = mat.shader != null ? mat.shader.name : "<null>";
            if (string.IsNullOrEmpty(shaderName)) shaderName = "<null>";

            shaderUsage.TryGetValue(shaderName, out int count);
            shaderUsage[shaderName] = count + 1;

            if (shaderName == MissingShaderName || shaderName == "<null>")
                brokenMaterials.Add(path);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[GPUInstancingTool] 材质 Shader 引用统计（共 {guids.Length} 个材质）：");
        foreach (KeyValuePair<string, int> kv in shaderUsage)
            sb.AppendLine($"  {kv.Value,4}  ×  {kv.Key}");

        if (brokenMaterials.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"[GPUInstancingTool] ⚠ 发现 {brokenMaterials.Count} 个材质引用缺失 shader（渲染为紫红色错误着色器），需修复：");
            foreach (string path in brokenMaterials)
                sb.AppendLine("  - " + path);
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("[GPUInstancingTool] 未发现缺失 shader 的材质。");
        }

        Debug.Log(sb.ToString());
    }
}
