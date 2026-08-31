using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【UI 弧光拖尾】资产自动创建（实施计划 §9 + D2）。
///
/// 域重载后自动补齐（幂等，资产已存在则不覆盖已有参数）：
///   - TrailGlow.png        （64×16 软边光带占位贴图，中心白芯 → 暖黄扩散 → 边缘 alpha 归零）
///   - TrailGlow.mat        （Custom/UITrailGlow 材质，引用占位贴图）
///   - DefaultTrailProfile.asset（默认 profile，曲线/渐变/参数填默认值，引用材质+贴图）
///
/// D2 说明：占位图由本脚本一次性产出静态 PNG 资源（Texture2D.EncodeToPNG），
/// 不是运行时动态生成 Texture2D（后者违反 WebGL 预算 C3）。美术正式图到位后
/// 直接替换同路径 TrailGlow.png 即可，无需改代码。
///
/// 手动入口：菜单 Tools/UI拖尾/生成占位贴图与默认配置。
/// </summary>
public static class UITrailTextureGenerator
{
    private const string TexturePath = "Assets/UI/Textures/TrailGlow.png";
    private const string MaterialPath = "Assets/UI/Trail/TrailGlow.mat";
    private const string ProfilePath = "Assets/UI/Trail/DefaultTrailProfile.asset";

    private const int TexWidth = 64;
    private const int TexHeight = 16;

    [InitializeOnLoadMethod]
    private static void AutoSetupOnReload()
    {
        // 仅当 profile 缺失时自动补齐（幂等），避免每次域重载都触碰资产。
        if (AssetDatabase.LoadAssetAtPath<UITrailProfile>(ProfilePath) == null)
            EditorApplication.delayCall += GenerateAll;
    }

    [MenuItem("Tools/UI拖尾/生成占位贴图与默认配置")]
    public static void GenerateAll()
    {
        EnsureFolders();

        // 1) 占位贴图（已存在则跳过，尊重美术后续的原路径替换，见 D2）
        GenerateTextureAsset(false);

        // 2) 材质（Shader 缺失时跳过材质与 profile 的材质引用，贴图仍会生成）
        Material mat = EnsureMaterial();

        // 3) 默认 profile
        EnsureProfile(mat);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[UITrail] 占位贴图 + 材质 + 默认 profile 已就绪。挂 UITrail 时可直接引用 DefaultTrailProfile。");
    }

    [MenuItem("Tools/UI拖尾/重建占位贴图（覆盖现有）")]
    public static void RegenerateTexture()
    {
        EnsureFolders();
        GenerateTextureAsset(true);

        Material mat = EnsureMaterial();
        EnsureProfile(mat);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[UITrail] 占位贴图已重建，材质与默认 profile 引用已刷新。");
    }

    [MenuItem("Tools/UI拖尾/切换详细日志")]
    public static void ToggleVerbose()
    {
        UITrailRenderer.VerboseLogging = !UITrailRenderer.VerboseLogging;
        Debug.Log(
            $"[UITrail] 详细日志已{(UITrailRenderer.VerboseLogging ? "开启" : "关闭")}。" +
            (UITrailRenderer.VerboseLogging
                ? "现在拖动/移动带 UITrail 的元素，会看到 [UITrail·采样] 与 [UITrail·mesh] 日志。定位完请关掉。"
                : ""));
    }

    [MenuItem("Tools/UI拖尾/切换纯色调试模式")]
    public static void ToggleSolidMode()
    {
        UITrailRenderer.DebugSolidMode = !UITrailRenderer.DebugSolidMode;
        UITrailRenderer.RefreshAll();
        Debug.Log(
            $"[UITrail] 纯色调试模式已{(UITrailRenderer.DebugSolidMode ? "开启" : "关闭")}。" +
            (UITrailRenderer.DebugSolidMode
                ? "现在用默认 UI 材质 + 白贴图 + 24px 不透明品红画同一份 mesh。" +
                  "看得见 → 几何没问题，故障在 shader/贴图/颜色；仍看不见 → 几何为空或被遮挡/裁剪。"
                : ""));
    }

    // ── 冒烟测试（诊断入口）─────────────────────────────────────────────    [MenuItem("Tools/UI拖尾/运行时冒烟测试（Play 模式下点）")]
    public static void RunSmokeTest()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("UI 拖尾冒烟测试", "请先进入 Play 模式再执行本命令。", "好");
            return;
        }

        // 挑一个 Overlay Canvas 优先；没有就用第一个找到的。
        Canvas target = null;
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (Canvas c in canvases)
        {
            if (!c.isRootCanvas) continue;
            if (target == null) target = c;
            if (c.renderMode == RenderMode.ScreenSpaceOverlay) { target = c; break; }
        }

        UITrailProfile profile = AssetDatabase.LoadAssetAtPath<UITrailProfile>(ProfilePath);
        UITrailSmokeTest.Spawn(target, profile, UITrailLayer.Above);
    }

    [MenuItem("Tools/UI拖尾/打印当前拖尾状态")]
    public static void DumpState()
    {
        UITrailRenderer.DebugDump();

        UITrail[] emitters = Object.FindObjectsByType<UITrail>(FindObjectsSortMode.None);
        if (emitters.Length == 0)
        {
            Debug.LogWarning(
                "[UITrail] 场景里一个激活的 UITrail 组件都没有。" +
                "注意：对象池里 SetActive(false) 的飞币不算激活——这是最常见的『看起来什么都没发生』的原因。");
            return;
        }
        foreach (UITrail t in emitters)
        {
            Debug.Log(
                $"[UITrail] Emitter '{t.gameObject.name}' active={t.gameObject.activeInHierarchy} " +
                $"profile={(t.profile != null ? t.profile.name : "null")} layer={t.layer} " +
                $"emitting={t.emitting} points={t.PointCount}", t);
        }
    }

    // ── 文件夹 ─────────────────────────────────────────────────────────
    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/UI/Textures"))
            AssetDatabase.CreateFolder("Assets/UI", "Textures");
        if (!AssetDatabase.IsValidFolder("Assets/UI/Trail"))
            AssetDatabase.CreateFolder("Assets/UI", "Trail");
    }

    // ── 占位贴图（§6.1）──────────────────────────────────────────────
    private static void GenerateTextureAsset(bool overwrite)
    {
        if (!overwrite && File.Exists(TexturePath))
        {
            // 贴图已存在：跳过生成，保留可能已被美术替换的正式图（D2 原路径替换约定）。
            Debug.Log($"[UITrail] 占位贴图已存在（{TexturePath}），跳过生成。如需重建请用『重建占位贴图』菜单。");
            return;
        }

        Texture2D tex = BuildPlaceholderTexture();
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        File.WriteAllBytes(TexturePath, png);
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;          // 关闭 mipmap（§6.1）
        importer.wrapMode = TextureWrapMode.Clamp; // V 方向必须 Clamp，否则边缘串色（§6.1）
        importer.filterMode = FilterMode.Bilinear;
        importer.sRGBTexture = true;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }

    private static Texture2D BuildPlaceholderTexture()
    {
        var tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        // U 沿长度（x，均匀），V 跨宽度（y，软边渐变）。
        for (int y = 0; y < TexHeight; y++)
        {
            float v = (y + 0.5f) / TexHeight;      // 0..1 跨宽度
            float d = Mathf.Abs(v - 0.5f) * 2f;    // 0=中心，1=边缘

            float core = Mathf.Exp(-d * d * 24f);  // 过曝白芯（很窄）
            float alpha = Mathf.Exp(-d * d * 3.5f); // 软边衰减（边缘归零）

            // 中心过曝白 → 暖黄扩散
            float r = 1f;
            float g = Mathf.Lerp(0.70f, 1f, core);
            float b = Mathf.Lerp(0.18f, 1f, core);

            for (int x = 0; x < TexWidth; x++)
                tex.SetPixel(x, y, new Color(r, g, b, alpha));
        }

        tex.Apply();
        return tex;
    }

    // ── 材质 ───────────────────────────────────────────────────────────
    private static Material EnsureMaterial()
    {
        Shader shader = Shader.Find(UITrailProfile.DefaultShaderName);
        if (shader == null)
        {
            Debug.LogError($"[UITrail] 找不到 Shader '{UITrailProfile.DefaultShaderName}'，请确认 UITrailGlow.shader 已正确导入。");
            return null;
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "TrailGlow" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        else
        {
            mat.shader = shader;
        }

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        mat.mainTexture = tex != null ? tex : Texture2D.whiteTexture;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── 默认 profile ───────────────────────────────────────────────────
    private static void EnsureProfile(Material mat)
    {
        UITrailProfile profile = AssetDatabase.LoadAssetAtPath<UITrailProfile>(ProfilePath);
        bool created = false;
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<UITrailProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            created = true;
        }

        // 仅在新建时填默认曲线/参数，已有资产保留用户的定制（幂等）。
        if (created) profile.ApplyDefaults();

        // 材质/贴图引用始终刷新：贴图原路径被美术替换后，引用自动指向新图（D2）。
        if (mat != null) profile.material = mat;
        profile.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        EditorUtility.SetDirty(profile);
    }
}
