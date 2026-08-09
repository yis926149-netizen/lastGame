using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【程序化山脉】阶段 7.7：表现与视觉验收的代码侧契约测试（防回滚）。
/// 锁住视觉契约：Shader 属性声明、keep-below clip 与手动 ShadowCaster 一致性、
/// 单一 FogBlend 雾化路径（无选择性对象雾化）、Triplanar 世界坐标采样、
/// 材质参数推送与 _ROCK_TEXTURE 关键字、每 Renderer 单实例与 OnDestroy 销毁、
/// 配置资产三档色阶有效、稳定材质资产 Shader 引用与默认纯色模式。
/// 目检项（截图矩阵/色阶观感/阴影轮廓/雾边界/镜头预算）仍需 Unity 场景验收。
/// </summary>
public class MountainStage7VisualContractTests
{
    private const string ShaderRoot = "Assets/Shader";
    private const string ScriptsRoot = "Assets/Scripts";
    private const string ConfigAssetPath = "Assets/Scripts/ScriptableObjects/MapLandForm/MountainConfig.asset";
    private const string MaterialAssetPath = "Assets/Materials/MountainLowPoly_Fog.mat";

    private static string ReadShader(string fileName) => File.ReadAllText(Path.Combine(ShaderRoot, fileName));
    private static string ReadScript(string relativePath) => File.ReadAllText(Path.Combine(ScriptsRoot, relativePath));

    // ── Shader 可加载性（阶段 7.1 已人工验证，此处防回归）────────

    [Test]
    public void MountainShaders_ShaderFind_StableAndTransitionBothImportable()
    {
        Assert.IsNotNull(Shader.Find(MountainMaterialContract.StableShaderName),
            "稳定山体 Shader 必须已导入且可按名查找（阶段 4.2）");
        Assert.IsNotNull(Shader.Find(MountainMaterialContract.TransitionShaderName),
            "山体 Transition Shader 必须已导入且可按名查找（阶段 5.4）");
    }

    // ── Shader 源码契约（决策 ⑥/㉗/㉘）──────────────────────────

    [Test]
    public void StableShader_DeclaresTierColorsTriplanarAndRockTexture()
    {
        string shader = ReadShader("MountainLowPoly_Fog.shader");

        StringAssert.Contains("_ColorLow", shader, "色阶 0（岩褐）属性必须声明（决策 ㉘）");
        StringAssert.Contains("_ColorMid", shader, "色阶 1（灰岩）属性必须声明");
        StringAssert.Contains("_ColorHigh", shader, "色阶 2（浅灰）属性必须声明");
        StringAssert.Contains("[Toggle(_ROCK_TEXTURE)]", shader, "纹理开关关键字必须存在（4.2 契约）");
        StringAssert.Contains("#pragma shader_feature _ROCK_TEXTURE", shader,
            "必须编译 Rock Texture Shader 变体；仅 EnableKeyword 而无 pragma 时采样分支会被裁掉");
        StringAssert.Contains("_RockTexture", shader, "可选岩石纹理属性必须存在（纯色模式零采样）");
        StringAssert.Contains("_TriplanarWorldScale", shader, "Triplanar world scale 属性必须存在（决策 ㉗）");
        StringAssert.Contains("_TriplanarBlendSharpness", shader, "Triplanar 权重锐度属性必须存在");
        StringAssert.Contains("_Roughness", shader, "粗糙度属性必须存在");
        StringAssert.Contains("_Metallic", shader, "金属度属性必须存在");
        StringAssert.Contains("_ShadowStrength", shader, "阴影强度预留参数必须存在（4.6）");
    }

    [Test]
    public void TransitionShader_KeepBelowClip_MatchesSurfAndShadowCaster()
    {
        string shader = ReadShader("MountainLowPoly_Fog_Transition.shader");

        StringAssert.Contains("_ChunkProgress", shader, "动画进度属性必须存在（阶段 5.4）");
        StringAssert.Contains("_ChunkAnimBaseY", shader, "clip 平面基座属性必须存在");
        StringAssert.Contains("_ChunkAnimRiseHeight", shader, "clip 平面抬升高度属性必须存在");
        StringAssert.Contains("clip(animClipY - IN.worldPos.y + 0.02)", shader,
            "surf 必须执行 keep-below clip（与 TerrainBase_Fog_Transition 同一契约）");
        StringAssert.Contains("clip(animClipY - i.worldPosY + 0.02)", shader,
            "ShadowCaster 必须与 surf 同一 clip 平面（阴影几何 = 可见几何）");
        StringAssert.Contains("o.worldPosY = mul(unity_ObjectToWorld, v.vertex).y", shader,
            "ShadowCaster 必须把世界 Y 传给片元供 clip");
    }

    [Test]
    public void StableShader_NoKeepBelowClip_StableStateUnclipped()
    {
        string shader = ReadShader("MountainLowPoly_Fog.shader");

        StringAssert.DoesNotContain("Chunk Animation Progress", shader,
            "稳定态不得声明动画进度属性（阶段 4.6；仅注释提及 _ChunkProgress 属容忍范围）");
        StringAssert.DoesNotContain("clip(animClipY", shader, "稳定态不得出现 keep-below clip 计算");
    }

    [Test]
    public void MountainShaders_SingleFogPath_NoSelectiveObjectFog()
    {
        string stable = ReadShader("MountainLowPoly_Fog.shader");
        string transition = ReadShader("MountainLowPoly_Fog_Transition.shader");

        foreach (string shader in new[] { stable, transition })
        {
            StringAssert.Contains("FogBlend.cginc", shader, "必须使用全局 FogBlend 契约（阶段 6.3）");
            StringAssert.Contains("FogBlend_final(color, IN.fogCoord)", shader, "雾化只走 FogBlend_final 单一路径");
            StringAssert.DoesNotContain("FogEnvironment", shader,
                "禁止选择性对象雾化采样（决策 ⑪：山体只被 Terrain FogBlend 处理，防双重雾化）");
        }
    }

    [Test]
    public void MountainShaders_TriplanarUsesWorldCoordinates_WithNaNGuards()
    {
        string stable = ReadShader("MountainLowPoly_Fog.shader");
        string transition = ReadShader("MountainLowPoly_Fog_Transition.shader");

        StringAssert.Contains("#pragma shader_feature _ROCK_TEXTURE", stable,
            "稳定 Shader 必须声明 Triplanar 纹理变体");
        StringAssert.Contains("#pragma shader_feature _ROCK_TEXTURE", transition,
            "Transition Shader 必须声明同一 Triplanar 纹理变体");
        StringAssert.Contains("#pragma shader_feature _MOUNTAIN_TERRAIN_BLEND", stable,
            "稳定 Shader 必须声明山脚地形融合变体");
        StringAssert.Contains("#pragma shader_feature _MOUNTAIN_TERRAIN_BLEND", transition,
            "Transition Shader 必须声明同一山脚地形融合变体");
        StringAssert.Contains("v.texcoord3.xy", stable,
            "山脚材质必须从 UV4/TEXCOORD3 读取 terrain UV 与融合权重");
        StringAssert.Contains("TRANSFORM_TEX(v.texcoord3.xy, _TerrainTex)", stable,
            "山脚 terrain UV 必须应用邻接地形贴图的 tiling/offset");
        StringAssert.Contains("lerp(terrain.rgb, albedo, mountainWeight)", stable,
            "山脚必须在单一 Opaque pass 内融合 terrain 与 mountain albedo");
        StringAssert.Contains("INTERNAL_DATA", stable,
            "稳定 Shader 读取 worldNormal 且写 o.Normal 时必须提供 Surface Shader 内部数据");
        StringAssert.Contains("INTERNAL_DATA", transition,
            "Transition Shader 必须保持相同的 worldNormal/INTERNAL_DATA 契约");
        StringAssert.Contains("abs(worldPos.zy)", stable, "+X/-X 轴采样必须用世界 YZ 坐标（决策 ㉗）");
        StringAssert.Contains("abs(worldPos.xz)", stable, "+Y/-Y 轴采样必须用世界 XZ 坐标");
        StringAssert.Contains("abs(worldPos.xy)", stable, "+Z/-Z 轴采样必须用世界 XY 坐标");
        StringAssert.Contains("max(_TriplanarWorldScale, 1e-4)", stable, "world scale 防 0/负值双保险（4.3）");
        StringAssert.Contains("total > 1e-6 ? w / total : float3(1.0, 0.0, 0.0)", stable,
            "权重归一化分母兜底，禁止 NaN");
    }

    [Test]
    public void MountainShaders_ManualShadowCaster_PresentInBoth()
    {
        foreach (string fileName in new[] { "MountainLowPoly_Fog.shader", "MountainLowPoly_Fog_Transition.shader" })
        {
            string shader = ReadShader(fileName);
            StringAssert.Contains("\"LightMode\" = \"ShadowCaster\"", shader, $"{fileName} 必须含手动 ShadowCaster pass（4.6）");
            StringAssert.Contains("TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)", shader, $"{fileName} ShadowCaster 必须使用法线偏移");
            StringAssert.Contains("SHADOW_CASTER_FRAGMENT(i)", shader, $"{fileName} ShadowCaster 片元必须输出深度");
            StringAssert.Contains("Cull Off", shader, $"{fileName} 双面契约必须与主 pass 一致");
        }
    }

    // ── ChunkMapRenderer 材质推送与生命周期（阶段 4.2/4.7/5.5）────

    [Test]
    public void ChunkMapRenderer_MountainMaterialPushesFullVisualContract()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("SetColor(\"_ColorLow\"", renderer, "岩褐档颜色必须推入（决策 ㉘）");
        StringAssert.Contains("SetColor(\"_ColorMid\"", renderer, "灰岩档颜色必须推入");
        StringAssert.Contains("SetColor(\"_ColorHigh\"", renderer, "浅灰档颜色必须推入");
        StringAssert.Contains("SetFloat(\"_TriplanarWorldScale\"", renderer, "Triplanar world scale 必须推入（决策 ㉗）");
        StringAssert.Contains("SetFloat(\"_TriplanarBlendSharpness\"", renderer, "Triplanar 锐度必须推入");
        StringAssert.Contains("EnableKeyword(\"_ROCK_TEXTURE\")", renderer, "有纹理时关键字必须开启（4.3）");
        StringAssert.Contains("DisableKeyword(\"_ROCK_TEXTURE\")", renderer, "无纹理时关键字必须关闭（纯色模式零采样）");
    }

    [Test]
    public void ChunkMapRenderer_TransitionMaterial_RestoresRockTextureKeyword()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("ApplyMountainMaterialConfig(_mountainTransitionMaterial", renderer,
            "Transition 材质必须通过统一配置同步函数恢复纹理/关键字和色阶参数");
        StringAssert.Contains("ApplyMountainMaterialConfig(_mountainMaterial", renderer,
            "稳定材质必须在缓存命中时重新同步 Inspector 配置");
    }

    [Test]
    public void ChunkMapRenderer_MountainMaterials_PerRendererCached_AndDestroyed()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("private Material _mountainMaterial;", renderer, "稳定山体材质必须每 Renderer 单实例缓存（4.7）");
        StringAssert.Contains("private Material _mountainTransitionMaterial;", renderer, "Transition 材质必须每 Renderer 单实例缓存（5.5）");
        StringAssert.Contains("_mountainShaderLookupAttempted", renderer, "Shader 查找必须只尝试一次");
        StringAssert.Contains("_mountainTransitionShaderLookupAttempted", renderer, "Transition Shader 查找必须只尝试一次");
        StringAssert.Contains("DestroyMaterialIfNotNull(_mountainMaterial);", renderer, "OnDestroy 必须销毁稳定山体材质（防泄漏）");
        StringAssert.Contains("DestroyMaterialIfNotNull(_mountainTransitionMaterial);", renderer, "OnDestroy 必须销毁 Transition 山体材质");
    }

    // ── 资产契约（EditMode：资产已导入时校验）────────────────────

    [Test]
    public void ConfigAsset_IsValid_WithDistinctOpaqueTierColors()
    {
        var config = AssetDatabase.LoadAssetAtPath<MountainConfigSO>(ConfigAssetPath);
        Assert.IsNotNull(config, $"MountainConfig.asset 缺失：{ConfigAssetPath}（执行 Tools/程序化山脉/重建山脉配置资产）");

        Assert.IsTrue(MountainMaterialContract.IsValid(config, out string error), error);
        Assert.AreNotEqual(config.tierColorLow, config.tierColorMid, "色阶 0/1 必须互不相同（3 档可辨）");
        Assert.AreNotEqual(config.tierColorMid, config.tierColorHigh, "色阶 1/2 必须互不相同（3 档可辨）");
        Assert.AreNotEqual(config.tierColorLow, config.tierColorHigh, "色阶 0/2 必须互不相同（3 档可辨）");
        Assert.AreEqual(1f, config.tierColorLow.a, 1e-4f, "色阶 0 必须不透明");
        Assert.AreEqual(1f, config.tierColorMid.a, 1e-4f, "色阶 1 必须不透明");
        Assert.AreEqual(1f, config.tierColorHigh.a, 1e-4f, "色阶 2 必须不透明");
        Assert.Greater(config.triplanarWorldScale, 0f, "Triplanar world scale 必须 > 0");
        Assert.GreaterOrEqual(config.triplanarBlendSharpness, 1f, "Triplanar 锐度必须 ≥ 1");
    }

    [Test]
    public void StableMaterialAsset_ReferencesStableShader_DefaultPureColorMode()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
        Assert.IsNotNull(mat, $"稳定材质资产缺失：{MaterialAssetPath}（执行 Tools/程序化山脉/重建山脉配置资产）");

        Assert.IsNotNull(mat.shader, "材质必须引用 Shader");
        Assert.AreEqual(MountainMaterialContract.StableShaderName, mat.shader.name,
            "材质资产必须引用稳定山体 Shader（阶段 4.2）");
        Assert.IsNull(mat.GetTexture("_RockTexture"), "默认资产应为纯色模式（无岩石纹理，零采样成本）");
        Assert.IsFalse(mat.IsKeywordEnabled("_ROCK_TEXTURE"), "默认资产纹理关键字应关闭（纯色模式）");
    }
}
