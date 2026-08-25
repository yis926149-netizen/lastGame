using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 【P0-2 Shader 变体预热】编辑器侧工具：自动收集工程用到的 shader 及其变体，
/// 生成 ShaderVariantCollection 并注册进 Graphics Settings 的「Preloaded Shaders」列表。
///
/// 运行时配合 <c>Assets/Scripts/Infrastructure/ShaderPreloader.cs</c> 完成预热。
///
/// 为什么必须注册进 Preloaded Shaders 而不只是放进 Resources：
///   仅仅把 .shadervariants 放在 Resources 里不会被 Shader Stripping 视为引用，
///   其变体仍可能在出包时被裁掉，导致运行时 WarmUp 变 no-op。注册进
///   Graphics Settings → Preloaded Shaders 列表后，Unity 会把这些变体强制保留进包，
///   并在启动时（微信 loading 遮罩阶段）自动预热。本工具的「手动预热」与其互补、幂等。
/// </summary>
public static class ShaderVariantPreloadTool
{
    private const string OutputPath = "Assets/Resources/PreloadedShaders.shadervariants";
    private const string ResourcesFolder = "Assets/Resources";
    private const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";

    // 生成变体集时的扫描范围：工程自有 shader 目录 + 工程自有材质目录 + TextMesh Pro（UI 文本）。
    private static readonly string[] ShaderScanFolders = { "Assets/Shader" };
    private static readonly string[] MaterialScanFolders = { "Assets/Materials", "Assets/TextMesh Pro" };

    // 已知未使用的商店资源包（对齐实施计划 §六 资源瘦身的裁剪候选），扫描全工程引用时排除，
    // 避免把整包 VFX / 示例场景的 shader 变体塞进首包。
    private static readonly string[] ExcludedPrefixes =
    {
        "Assets/Toon_RTS", "Assets/KayKit", "Assets/VFXPACK_",
        "Assets/Lana Studio", "Assets/Scenes/demo"
    };

    // ─────────────────────────────────────────────────────────────
    // 菜单 1：生成 + 注册
    // ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/微信小游戏性能优化/生成 Shader 变体预热集")]
    public static void GeneratePreloadCollection()
    {
        EnsureResourcesFolder();

        List<Shader> shaders = CollectShaders();
        if (shaders.Count == 0)
        {
            Debug.LogWarning("[ShaderVariantPreloadTool] 未收集到任何 shader，未生成变体集。");
            return;
        }

        ShaderVariantCollection collection = new ShaderVariantCollection();
        collection.name = "PreloadedShaders";

        int exactCount = 0;
        foreach (Shader shader in shaders)
        {
            int before = collection.variantCount;
            if (!TryAddExactVariants(collection, shader))
            {
                AddVariantsByKeywordEnumeration(collection, shader);
                Debug.Log($"[ShaderVariantPreloadTool] {shader.name}：精确枚举不可用，已按关键字枚举回退。");
            }
            else
            {
                exactCount++;
            }
            Debug.Log($"[ShaderVariantPreloadTool] {shader.name}：+{collection.variantCount - before} 个变体");
        }

        AssetDatabase.CreateAsset(collection, OutputPath);
        AssetDatabase.SaveAssets();

        RegisterInGraphicsSettings(collection);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ShaderVariantPreloadTool] 已生成 {OutputPath}：共 {shaders.Count} 个 shader / " +
                  $"{collection.variantCount} 个变体（精确枚举 {exactCount} 个 shader），" +
                  "并已注册进 Graphics Settings → Preloaded Shaders。");
    }

    // ─────────────────────────────────────────────────────────────
    // 菜单 2：分析（Always Included 清理依据 + Fog.mat 等变体量核对）
    // ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/微信小游戏性能优化/分析 Shader 变体与 Always Included")]
    public static void Analyze()
    {
        var usedShaders = CollectAllMaterialShaders(out int materialCount);
        var alwaysIncluded = LoadAlwaysIncludedShaders();

        Debug.Log($"[ShaderVariantPreloadTool] ==== Shader 使用分析 ====");
        Debug.Log($"[ShaderVariantPreloadTool] 扫描材质 {materialCount} 个，其中引用到的 shader {usedShaders.Count} 个。");

        Debug.Log($"[ShaderVariantPreloadTool] ---- Always Included Shaders（{alwaysIncluded.Count} 个）----");
        foreach (Shader shader in alwaysIncluded)
        {
            bool usedByMaterial = usedShaders.Contains(shader);
            string path = AssetDatabase.GetAssetPath(shader);
            string name = string.IsNullOrEmpty(path) ? shader.name : path;
            if (usedByMaterial)
                Debug.Log($"[ShaderVariantPreloadTool]   [保留] {name}");
            else
                Debug.Log($"[ShaderVariantPreloadTool]   [候选移除] {name} —— 未被任何材质引用（注意：prefab/代码直接引用不在本次扫描内，请人工确认后再删）");
        }

        // 变体量核对：读取当前 PreloadedShaders 资产，逐 shader 打印变体数。
        ShaderVariantCollection existing = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(OutputPath);
        if (existing == null)
        {
            Debug.Log("[ShaderVariantPreloadTool] 尚未生成 PreloadedShaders.shadervariants（先运行「生成 Shader 变体预热集」）。");
        }
        else
        {
            Debug.Log($"[ShaderVariantPreloadTool] ---- 已生成变体集（共 {existing.variantCount} 变体）----");
            LogVariantCountsByShader(existing);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 收集
    // ─────────────────────────────────────────────────────────────

    /// <summary>收集待预热 shader：工程自有 shader 目录全量 + 工程材质/TMP 材质引用到的 shader。</summary>
    private static List<Shader> CollectShaders()
    {
        var shaders = new List<Shader>();
        var seen = new HashSet<Shader>();
        void Add(Shader s)
        {
            if (s != null && seen.Add(s)) shaders.Add(s);
        }

        foreach (string folder in ShaderScanFolders)
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { folder }))
                Add(AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid)));

        foreach (string folder in MaterialScanFolders)
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsExcluded(path)) continue;
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                Add(mat != null ? mat.shader : null);
            }

        // 内置 shader（Standard / UI/Default / Sprites/Default 等）始终包含在包内，无需预热，跳过。
        shaders.RemoveAll(s => s == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(s)));
        return shaders;
    }

    /// <summary>扫描全工程材质（排除商店包），返回被引用到的 shader 集合。</summary>
    private static HashSet<Shader> CollectAllMaterialShaders(out int materialCount)
    {
        var used = new HashSet<Shader>();
        materialCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsExcluded(path)) continue;
            materialCount++;
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && mat.shader != null) used.Add(mat.shader);
        }
        return used;
    }

    private static bool IsExcluded(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        foreach (string prefix in ExcludedPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────
    // 变体枚举：优先反射精确枚举（与 ShaderVariantCollection 检视器同源），
    // 失败则回退到「关键字组合」近似枚举。
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 通过内部 API <c>ShaderUtil.GetShaderVariantEntries</c> 拿到 shader 的全部精确变体
    /// （含 surface shader 隐式生成的 fog/lightmap/shadow 变体），合并进 <paramref name="target"/>。
    /// 返回是否成功。
    /// </summary>
    private static bool TryAddExactVariants(ShaderVariantCollection target, Shader shader)
    {
        try
        {
            ShaderVariantCollection perShader = InvokeGetShaderVariantEntries(shader);
            if (perShader == null) return false;

            object list = ReadMember(perShader, "m_Shaders", "m_shaders");
            if (!(list is System.Collections.IEnumerable enumerable)) return false;

            int added = 0;
            foreach (object variant in enumerable)
            {
                Shader s = ReadMember(variant, "shader", "m_Shader") as Shader;
                object passObj = ReadMember(variant, "passType", "m_PassType");
                if (s == null || passObj == null) continue;

                string[] keywords = ReadMember(variant, "keywords", "m_Keywords") as string[];
                if (keywords == null)
                {
                    // 部分版本只序列化 ShaderKeyword[]。
                    if (ReadMember(variant, "shaderKeywords", "m_ShaderKeywords") is ShaderKeyword[] skws)
                        keywords = ToKeywordNames(skws);
                }

                try
                {
                    PassType passType = (PassType)Convert.ToInt32(passObj);
                    ShaderVariantCollection.ShaderVariant svcVariant = new ShaderVariantCollection.ShaderVariant(s, passType, keywords ?? Array.Empty<string>());
                    if (target.Add(svcVariant)) added++;
                }
                catch
                {
                    // 单个变体 Add 失败不影响其余。
                }
            }
            return added > 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShaderVariantPreloadTool] 精确枚举 {shader.name} 失败（{e.Message}），回退关键字枚举。");
            return false;
        }
    }

    private static ShaderVariantCollection InvokeGetShaderVariantEntries(Shader shader)
    {
        Type shaderUtil = typeof(ShaderUtil);
        var signatures = new[]
        {
            new[] { typeof(Shader), typeof(ShaderVariantCollection) },
            new[] { typeof(Shader) },
        };

        foreach (Type[] sig in signatures)
        {
            MethodInfo method = shaderUtil.GetMethod("GetShaderVariantEntries",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, sig, null);
            if (method == null) continue;

            object[] args = sig.Length == 1 ? new object[] { shader } : new object[] { shader, null };
            try
            {
                return method.Invoke(null, args) as ShaderVariantCollection;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>近似回退：按 shader 声明关键字（全局+局部）枚举基础变体与关键字组合。</summary>
    private static void AddVariantsByKeywordEnumeration(ShaderVariantCollection collection, Shader shader)
    {
        var keywordNames = new List<string>();
        try
        {
            // Unity 2021.2+：ShaderUtil.GetShaderGlobalKeywords / GetShaderLocalKeywords 已移除（改为 internal 且不再公开）。
            // 改用公开 API shader.keywordSpace.keywordNames 枚举该 shader 声明的全部关键字。
            LocalKeywordSpace keywordSpace = shader.keywordSpace;
            string[] declared = keywordSpace != null ? keywordSpace.keywordNames : null;
            if (declared != null)
            {
                foreach (string n in declared)
                    if (!string.IsNullOrEmpty(n) && !keywordNames.Contains(n)) keywordNames.Add(n);
            }
        }
        catch
        {
            // 内置/异常 shader 可能抛错，忽略关键字即可。
        }

        var passTypes = new[]
        {
            PassType.ForwardBase, PassType.ForwardAdd, PassType.ShadowCaster,
            PassType.Deferred, PassType.Meta, PassType.Normal,
            PassType.Vertex, PassType.VertexLM, PassType.VertexLMRGBM
        };

        // 基础变体（无关键字）。
        foreach (PassType pt in passTypes)
            TryAdd(collection, shader, pt, Array.Empty<string>());

        // 单个关键字。
        foreach (string kw in keywordNames)
            foreach (PassType pt in passTypes)
                TryAdd(collection, shader, pt, new[] { kw });

        // 全组合（关键字数 ≤ 6 才枚举子集，避免 2^N 爆炸）。
        if (keywordNames.Count > 0 && keywordNames.Count <= 6)
        {
            int total = 1 << keywordNames.Count;
            for (int mask = 1; mask < total; mask++)
            {
                var combo = new List<string>();
                for (int b = 0; b < keywordNames.Count; b++)
                    if ((mask & (1 << b)) != 0) combo.Add(keywordNames[b]);
                foreach (PassType pt in passTypes)
                    TryAdd(collection, shader, pt, combo.ToArray());
            }
        }
    }

    private static void TryAdd(ShaderVariantCollection collection, Shader shader, PassType passType, string[] keywords)
    {
        try { collection.Add(new ShaderVariantCollection.ShaderVariant(shader, passType, keywords)); }
        catch { /* 无效 pass / 关键字组合，忽略 */ }
    }

    private static string[] ToKeywordNames(ShaderKeyword[] keywords)
    {
        if (keywords == null) return Array.Empty<string>();
        var list = new List<string>(keywords.Length);
        foreach (ShaderKeyword kw in keywords)
        {
            try { list.Add(kw.name); }
            catch { /* 跳过无法取名的关键字 */ }
        }
        return list.ToArray();
    }

    private static object ReadMember(object target, params string[] names)
    {
        if (target == null) return null;
        Type t = target.GetType();
        foreach (string n in names)
        {
            FieldInfo f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(target);
            PropertyInfo p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead) return p.GetValue(target);
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────
    // Graphics Settings 注册与读取
    // ─────────────────────────────────────────────────────────────

    private static void RegisterInGraphicsSettings(ShaderVariantCollection collection)
    {
        UnityEngine.Object gs = LoadGraphicsSettings();
        if (gs == null)
        {
            Debug.LogWarning("[ShaderVariantPreloadTool] 未找到 GraphicsSettings.asset，未注册 Preloaded Shaders。");
            return;
        }

        SerializedObject so = new SerializedObject(gs);
        SerializedProperty preloaded = so.FindProperty("m_PreloadedShaders");
        if (preloaded == null || !preloaded.isArray)
        {
            Debug.LogWarning("[ShaderVariantPreloadTool] GraphicsSettings 缺少 m_PreloadedShaders 字段，未注册。");
            so.Dispose();
            return;
        }

        // 幂等：已注册则跳过。
        for (int i = 0; i < preloaded.arraySize; i++)
        {
            if (preloaded.GetArrayElementAtIndex(i).objectReferenceValue == collection)
            {
                so.Dispose();
                return;
            }
        }

        preloaded.arraySize++;
        preloaded.GetArrayElementAtIndex(preloaded.arraySize - 1).objectReferenceValue = collection;
        so.ApplyModifiedPropertiesWithoutUndo();
        so.Dispose();
    }

    private static List<Shader> LoadAlwaysIncludedShaders()
    {
        var result = new List<Shader>();
        UnityEngine.Object gs = LoadGraphicsSettings();
        if (gs == null) return result;

        SerializedObject so = new SerializedObject(gs);
        SerializedProperty always = so.FindProperty("m_AlwaysIncludedShaders");
        if (always != null && always.isArray)
        {
            for (int i = 0; i < always.arraySize; i++)
            {
                Shader shader = always.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader != null) result.Add(shader);
            }
        }
        so.Dispose();
        return result;
    }

    private static UnityEngine.Object LoadGraphicsSettings()
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath);
        return (assets != null && assets.Length > 0) ? assets[0] : null;
    }

    // ─────────────────────────────────────────────────────────────
    // 变体量逐 shader 打印（核对 Fog.mat 等变体是否异常爆炸）
    // ─────────────────────────────────────────────────────────────

    private static void LogVariantCountsByShader(ShaderVariantCollection collection)
    {
        Dictionary<string, int> counts = CountVariantsByShader(collection);
        foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            Debug.Log($"[ShaderVariantPreloadTool]   {kv.Key}: {kv.Value} 个变体");
    }

    private static Dictionary<string, int> CountVariantsByShader(ShaderVariantCollection collection)
    {
        var counts = new Dictionary<string, int>();
        if (collection == null) return counts;

        // 复用反射读取内部 m_Shaders，逐变体按 shader 名计数（只读、容错）。
        try
        {
            object list = ReadMember(collection, "m_Shaders", "m_shaders");
            if (!(list is System.Collections.IEnumerable enumerable)) return counts;
            foreach (object variant in enumerable)
            {
                Shader s = ReadMember(variant, "shader", "m_Shader") as Shader;
                if (s == null) continue;
                string name = s.name;
                counts.TryGetValue(name, out int c);
                counts[name] = c + 1;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShaderVariantPreloadTool] 读取变体明细失败：{e.Message}");
        }
        return counts;
    }

    // ─────────────────────────────────────────────────────────────
    // 目录
    // ─────────────────────────────────────────────────────────────

    private static void EnsureResourcesFolder()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
    }
}
