#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// 一次性编辑器工具：把 arrow.prefab 从实体箭矢改造为纯白拖尾线载体。
// 执行方式：Unity 加载/编译完成后自动运行一次（EditorPrefs 标记去重），跑完打印结果。
//   也可手动通过菜单 Tools/箭矢改拖尾/立即执行 再次运行。
// 执行内容：
//   1) 新建纯白拖尾材质（Sprites/Default，unlit + 顶点色）到 Assets/Resources。
//   2) 禁用 arrow.prefab 内所有 MeshRenderer（保留层级 Transform 以维持采样点位置）。
//   3) 移除所有 BoxCollider（原本无用）。
//   4) 在 root 加 TrailRenderer 并配置纯白、头粗尾细、短存活的参数。
[InitializeOnLoad]
public static class ArrowTrailSetup
{
    private const string PrefabPath = "Assets/Resources/arrow.prefab";
    private const string MatPath = "Assets/Resources/ArrowTrail.mat";
    private const string DoneKey = "ArrowTrailSetup_Done_v1";

    static ArrowTrailSetup()
    {
        // 延迟到首帧，确保 AssetDatabase 就绪
        EditorApplication.delayCall += AutoRunOnce;
    }

    private static void AutoRunOnce()
    {
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        Run();
        EditorPrefs.SetBool(DoneKey, true);
    }

    [MenuItem("Tools/箭矢改拖尾/立即执行")]
    public static void Execute()
    {
        Run();
    }

    private static void Run()
    {
        // ---- 1) 纯白拖尾材质 ----
        Material trailMat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (trailMat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            trailMat = new Material(sh) { color = Color.white };
            AssetDatabase.CreateAsset(trailMat, MatPath);
            Debug.Log("[ArrowTrailSetup] 已创建纯白拖尾材质: " + MatPath);
        }

        // ---- 2~4) 改造 prefab ----
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("[ArrowTrailSetup] 找不到 prefab: " + PrefabPath);
            return;
        }

        foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
            mr.enabled = false;

        foreach (BoxCollider bc in root.GetComponentsInChildren<BoxCollider>(true))
            Object.DestroyImmediate(bc, true);

        TrailRenderer tr = root.GetComponent<TrailRenderer>();
        if (tr == null) tr = root.AddComponent<TrailRenderer>();

        tr.time = 0.3f;
        tr.startWidth = 0.15f;
        tr.endWidth = 0f;
        tr.minVertexDistance = 0.1f;
        tr.autodestruct = false;
        tr.emitting = true;
        tr.alignment = LineAlignment.View;
        tr.textureMode = LineTextureMode.Stretch;
        tr.numCornerVertices = 2;
        tr.numCapVertices = 2;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.sharedMaterial = trailMat;

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        tr.colorGradient = grad;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ArrowTrailSetup] arrow.prefab 改造完成：MeshRenderer已禁用 / BoxCollider已移除 / 已加纯白TrailRenderer。");
    }
}
#endif
