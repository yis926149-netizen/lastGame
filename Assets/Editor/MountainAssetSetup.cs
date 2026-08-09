using UnityEditor;
using UnityEngine;

/// <summary>
/// 【程序化山脉】资产自动创建（阶段 1/2 收尾 + 阶段 4.2 稳定材质）。
/// 域重载后自动补齐：
///  - Mountain.asset（山脉地貌 SO：mountainForm=true、blockBuildingSpawn=true、无模型/无浮标/无效果）
///  - MountainConfig.asset（山脉生成配置，mountainLandForm 指向 Mountain.asset）
///  - MountainLowPoly_Fog.mat（山体专属稳定材质资产，阶段 4.2；缺失时按 Shader 创建并写入 config.stableMaterial）
///  - 把 mountainConfig 引用写入 MapGenerationConfig.asset（MapGenerator 注入读取）
/// 幂等：资产已存在时不做任何修改（决策 ⑬ 山脉地貌不入地貌数据库，无需改 MapLandFormDatabase.asset；
/// 阶段 4.2 仅补齐缺失的 stableMaterial 引用，不覆盖已配置值）。
/// 手动入口：菜单 Tools/程序化山脉/重建山脉配置资产。
/// </summary>
public static class MountainAssetSetup
{
    private const string MountainPath = "Assets/Scripts/ScriptableObjects/MapLandForm/Mountain.asset";
    private const string MountainConfigPath = "Assets/Scripts/ScriptableObjects/MapLandForm/MountainConfig.asset";
    private const string MapGenConfigPath = "Assets/Scripts/ScriptableObjects/MapGenerationConfig.asset";
    private const string MaterialPath = "Assets/Materials/MountainLowPoly_Fog.mat";
    private const string StableShaderName = "Custom/MountainLowPoly_Fog";

    [InitializeOnLoadMethod]
    private static void AutoSetupOnReload()
    {
        // 仅当配置资产缺失或 stableMaterial 引用缺失时自动补齐（幂等），避免每次域重载都触碰资产
        MountainConfigSO config = AssetDatabase.LoadAssetAtPath<MountainConfigSO>(MountainConfigPath);
        if (config == null || config.stableMaterial == null)
            EditorApplication.delayCall += Setup;
    }

    [MenuItem("Tools/程序化山脉/诊断山脉材质与配置")]
    public static void Diagnose()
    {
        MountainConfigSO config = AssetDatabase.LoadAssetAtPath<MountainConfigSO>(MountainConfigPath);
        if (config == null)
        {
            Debug.LogError("[程序化山脉] 诊断失败：MountainConfig.asset 缺失（请先执行『重建山脉配置资产』）。");
            return;
        }

        bool valid = MountainMaterialContract.IsValid(config, out string error);
        Debug.Log(valid
            ? "[程序化山脉] 配置参数有效（world scale > 0 / blend sharpness ≥ 1 / roughness·metallic·shadowStrength ∈ [0,1]）"
            : $"[程序化山脉] 配置参数无效：{error}");

        Shader shader = Shader.Find(MountainMaterialContract.StableShaderName);
        Debug.Log(shader != null
            ? $"[程序化山脉] Shader 找到：{MountainMaterialContract.StableShaderName}"
            : $"[程序化山脉] Shader 缺失：{MountainMaterialContract.StableShaderName}（山体槽将回落 _terrainBaseMaterial0，见 ChunkMapRenderer）");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            Debug.LogWarning($"[程序化山脉] 材质资产缺失：{MaterialPath}（执行『重建山脉配置资产』自动创建）");
        }
        else
        {
            Debug.Log(mat.shader == shader
                ? $"[程序化山脉] 材质资产 Shader 引用正确（{MaterialPath}）"
                : $"[程序化山脉] 材质资产 Shader 引用异常：{(mat.shader != null ? mat.shader.name : "null")}（期望 {MountainMaterialContract.StableShaderName}）");
        }

        if (config.stableMaterial == null)
            Debug.LogWarning("[程序化山脉] MountainConfig.stableMaterial 未配置（运行时将走 Shader.Find 路径；执行『重建山脉配置资产』自动补齐）");
        else
            Debug.Log(config.stableMaterial.shader == shader
                ? "[程序化山脉] MountainConfig.stableMaterial 引用正确"
                : $"[程序化山脉] MountainConfig.stableMaterial.shader 异常：{(config.stableMaterial.shader != null ? config.stableMaterial.shader.name : "null")}");

        // 阶段 7.7：动画专用 Transition Shader 存在性（缺失时动画期间山体槽回落稳定材质，只报一次）。
        Shader transitionShader = Shader.Find(MountainMaterialContract.TransitionShaderName);
        Debug.Log(transitionShader != null
            ? $"[程序化山脉] Transition Shader 找到：{MountainMaterialContract.TransitionShaderName}"
            : $"[程序化山脉] Transition Shader 缺失：{MountainMaterialContract.TransitionShaderName}（动画期间山体槽回落稳定材质，见 ChunkMapRenderer）");

        // 阶段 7.7：视觉契约摘要——三档色阶互不相同且不透明（决策 ㉘ 色阶 3 段起步），
        // 纹理模式 / 纯色模式判定（纯色模式零纹理采样成本，阶段 4.3）。
        bool tierColorsDistinct = config.tierColorLow != config.tierColorMid
            && config.tierColorMid != config.tierColorHigh
            && config.tierColorLow != config.tierColorHigh;
        bool tierColorsOpaque = config.tierColorLow.a > 0.999f && config.tierColorMid.a > 0.999f && config.tierColorHigh.a > 0.999f;
        Debug.Log(tierColorsDistinct && tierColorsOpaque
            ? "[程序化山脉] 视觉契约：三档色阶互不相同且不透明（岩褐/灰岩/浅灰），档序可辨。"
            : $"[程序化山脉] 视觉契约异常：三档色阶需互不相同且不透明（当前 low={config.tierColorLow} mid={config.tierColorMid} high={config.tierColorHigh}）。");
        if (config.rockTexture == null)
            Debug.Log("[程序化山脉] 纯色模式：无岩石纹理（_ROCK_TEXTURE 关闭，零纹理采样成本；渲染稳定）。");
        else
            Debug.Log($"[程序化山脉] 纹理模式：{config.rockTexture.name}（Triplanar 世界空间采样 × 色阶染色，world scale={config.triplanarWorldScale}）。");

        // 阶段 6.7：镜头预算诊断（超预算只在编辑器警告，运行时绝不静默裁峰）。
        // 基线 = CameraController 场景默认（minZoomDistance=20、near clip≈0.3）；
        // H_max 上限接近 minZoomDistance - nearClip 时峰顶有穿近裁剪/遮挡风险。
        // 2026-08-06：有效上限 = MountainConfig.maxHeight × MapGenerationConfig.mountainHeightScale。
        float heightScale = 1f;
        MapGenerationConfigSO mapGenConfig = AssetDatabase.LoadAssetAtPath<MapGenerationConfigSO>(MapGenConfigPath);
        if (mapGenConfig != null) heightScale = Mathf.Max(0.01f, mapGenConfig.mountainHeightScale);
        float effectiveMaxHeight = config.maxHeight * heightScale;
        const float MinCameraDistance = 20f;
        const float DefaultNearClip = 0.3f;
        if (effectiveMaxHeight > MinCameraDistance - DefaultNearClip)
            Debug.LogWarning($"[程序化山脉] 镜头预算超限：有效 H_max 上限={effectiveMaxHeight}（maxHeight={config.maxHeight} × mountainHeightScale={heightScale}）> {MinCameraDistance - DefaultNearClip}（minZoomDistance={MinCameraDistance} - near clip={DefaultNearClip}）。峰顶可能穿近裁剪或遮挡单位/UI，请降低 maxHeight/mountainHeightScale 或提高 minZoomDistance（阶段 6.7）。");
        else
            Debug.Log($"[程序化山脉] 镜头预算：有效 H_max 上限={effectiveMaxHeight}（maxHeight={config.maxHeight} × mountainHeightScale={heightScale}）≤ {MinCameraDistance - DefaultNearClip}（minZoomDistance={MinCameraDistance} - near clip={DefaultNearClip}），预算内。");
    }

    [MenuItem("Tools/程序化山脉/重建山脉配置资产")]
    public static void Setup()
    {
        MapLandFormSO mountain = AssetDatabase.LoadAssetAtPath<MapLandFormSO>(MountainPath);
        if (mountain == null)
        {
            mountain = ScriptableObject.CreateInstance<MapLandFormSO>();
            mountain.landFormId = "mountain";
            mountain.landFormName = "山脉";
            mountain.description = "程序化山脉地貌占用标记：无模型/无浮标/无效果；由 RidgeGenerator 专属 pass 生成（决策 ⑬）";
            mountain.mountainForm = true;
            mountain.blockBuildingSpawn = true;
            mountain.spawnWeight = 0;     // 不入散落权重池，其他地貌权重不受影响（决策 ⑬）
            mountain.clusterSpawn = true; // 簇专属语义标记（实际由 RidgeGenerator 生成）
            mountain.effectType = LandFormEffectType.None;
            AssetDatabase.CreateAsset(mountain, MountainPath);
            Debug.Log("[程序化山脉] 已创建 Mountain.asset");
        }

        MountainConfigSO config = AssetDatabase.LoadAssetAtPath<MountainConfigSO>(MountainConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<MountainConfigSO>();
            config.mountainLandForm = mountain;
            AssetDatabase.CreateAsset(config, MountainConfigPath);
            Debug.Log("[程序化山脉] 已创建 MountainConfig.asset");
        }
        else if (config.mountainLandForm == null)
        {
            config.mountainLandForm = mountain;
            EditorUtility.SetDirty(config);
        }

        // 阶段 4.2：山体稳定材质资产缺失时按专属 Shader 创建；config.stableMaterial 缺失时写入引用
        Material stableMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (stableMaterial == null)
        {
            Shader shader = Shader.Find(StableShaderName);
            if (shader != null)
            {
                stableMaterial = new Material(shader);
                stableMaterial.name = "MountainLowPoly_Fog";
                AssetDatabase.CreateAsset(stableMaterial, MaterialPath);
                Debug.Log("[程序化山脉] 已创建 MountainLowPoly_Fog.mat");
            }
            else
            {
                Debug.LogWarning($"[程序化山脉] 找不到 {StableShaderName} Shader，暂不创建稳定材质资产（阶段 4.2；导入后重跑本菜单）。");
            }
        }
        if (config != null && config.stableMaterial == null && stableMaterial != null)
        {
            config.stableMaterial = stableMaterial;
            EditorUtility.SetDirty(config);
            Debug.Log("[程序化山脉] 已把 stableMaterial 写入 MountainConfig.asset");
        }

        var genConfig = AssetDatabase.LoadAssetAtPath<MapGenerationConfigSO>(MapGenConfigPath);
        if (genConfig != null && genConfig.mountainConfig == null)
        {
            var so = new SerializedObject(genConfig);
            so.FindProperty("mountainConfig").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(genConfig);
            Debug.Log("[程序化山脉] 已把 mountainConfig 写入 MapGenerationConfig.asset");
        }

        AssetDatabase.SaveAssets();
    }
}
