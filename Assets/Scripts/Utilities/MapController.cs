using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：地图控制器
//****************************************

public class MapController : MonoBehaviour
{

    /// <summary>
    /// 创建一个Mesh - 单网格，单着色器（网格线、移动路径连线在用）
    /// </summary>
    /// <param name="vertices">顶点数组</param>
    /// <param name="uv">UV</param>
    /// <param name="indices">三角形绘制顺序</param>
    /// <param name="gameObject">承载网格的物体</param>
    /// <param name="shader">shader</param>
    /// <param name="isUInt32">是否用 32 位无符号整数作为网格索引的类型。不用的话Mesh只能小于 65,535 个顶点，否则将出错</param>
    public static Mesh CreatMesh(Vector3[] vertices, Vector2[] uv, int[] indices, GameObject gameObject, Material mat, bool isUInt32 = false, bool addCollider = true)
    {
        Mesh mesh = new Mesh();
        if (isUInt32 || vertices.Length > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        //顶点数组
        mesh.vertices = vertices;
        //UV 
        mesh.uv = uv;
        //三角形绘制顺序
        mesh.triangles = indices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        //Material mat = new Material(shader);
        meshRenderer.sharedMaterial = mat;
        meshFilter.sharedMesh = mesh;

        if (addCollider)
        {
            MeshCollider meshCollider = gameObject.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = gameObject.AddComponent<MeshCollider>();
            }
            meshCollider.sharedMesh = mesh;
        }

        return mesh;
    }

    /// <summary>
    /// 矩形过渡区域的混合材质（矩形过渡 - 两材质混合）
    /// </summary>
    /// <param name="baseMaterialA">混合材质A</param>
    /// <param name="baseMaterialB">混合材质B</param>
    /// <param name="blendMask">混合遮罩图</param>
    /// <param name="blendSmooth">过渡宽度 </param>
    /// <param name="blendContrast">混合对比度 </param
    /// <returns>配置完成的混合材质</returns>
    // 材质构造入口公开：ChunkMapRenderer 运行时重建按材质键缓存复用，
    // 避免重复重建时泄漏材质实例（§六-2）。
    public static Material ConfigureBlendMaterial(Material baseMaterialA, Material baseMaterialB, Texture2D blendMask, float blendContrast, float blendSmooth)
    {
        Shader blendShader = Shader.Find("Custom/RealMaterialMaskBlend");
        if (blendShader == null)
        {
            Debug.LogError("无法找到 Custom/RealMaterialMaskBlend（打包时可能被剥离）。已在 GraphicsSettings 加入时请重新构建；否则检查 Shader 路径。");
            Shader fb = Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            return new Material(fb);
        }

        if (baseMaterialA == null || baseMaterialB == null)
        {
            Debug.LogWarning("配置混合材质时传入的材质为空！");
            Shader fb = Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            return new Material(fb);
        }

        Material blendMaterial = new Material(blendShader);

        // 配置材质 A 属性
        blendMaterial.SetTexture("_MainTexA", baseMaterialA.mainTexture ?? Texture2D.whiteTexture);
        blendMaterial.SetTexture("_NormalMapA", GetNormalMapFromMaterial(baseMaterialA) ?? Texture2D.normalTexture);
        blendMaterial.SetFloat("_MetallicA", baseMaterialA.HasProperty("_Metallic") ? baseMaterialA.GetFloat("_Metallic") : 0.0f);
        blendMaterial.SetFloat("_SmoothnessA", baseMaterialA.HasProperty("_Smoothness") ? baseMaterialA.GetFloat("_Smoothness") : 0.5f);

        // 配置材质 B 属性
        blendMaterial.SetTexture("_MainTexB", baseMaterialB.mainTexture ?? Texture2D.whiteTexture);
        blendMaterial.SetTexture("_NormalMapB", GetNormalMapFromMaterial(baseMaterialB) ?? Texture2D.normalTexture);
        blendMaterial.SetFloat("_MetallicB", baseMaterialB.HasProperty("_Metallic") ? baseMaterialB.GetFloat("_Metallic") : 0.0f);
        blendMaterial.SetFloat("_SmoothnessB", baseMaterialB.HasProperty("_Smoothness") ? baseMaterialB.GetFloat("_Smoothness") : 0.5f);

        // 配置混合参数
        blendMaterial.SetTexture("_MaskTex", blendMask);
        blendMaterial.SetFloat("_BlendSmooth", blendSmooth);
        blendMaterial.SetFloat("_BlendContrast", blendContrast);

        return blendMaterial;
    }

    /// <summary>
    /// 三角过渡区域的混合材质（三角过渡 - 三材质混合）
    /// </summary>
    /// <param name="baseMaterialA">材质A </param>
    /// <param name="baseMaterialB">材质B </param>
    /// <param name="baseMaterialC">材质C </param>
    /// <param name="blendMask">RGB遮罩图（R=A, G=B, B=C）</param>
    /// <param name="globalSmoothness">全局光滑度 </param>
    /// <param name="blendContrast">混合对比度 </param
    /// <returns>配置完成的三材质混合材质</returns>
    public static Material ConfigureBlendMaterial(Material baseMaterialA, Material baseMaterialB, Material baseMaterialC, Texture2D blendMask, float blendContrast, float globalSmoothness)
    {
        // 1. 加载三材质Shader（关键：替换为三材质Shader路径）
        Shader threeMatShader = Shader.Find("Custom/ThreeMaterialBlend_Land");
        if (threeMatShader == null)
        {
            Debug.Log("无法找到三材质Shader：Custom/ThreeMaterialBlend_Land，请检查路径！");
            return new Material(Shader.Find("Standard"));
        }
        // 添加材质空值检查
        if (baseMaterialA == null || baseMaterialB == null || baseMaterialC == null)
        {
            Debug.Log("三配置混合材质时传入的材质为空！");
            return new Material(Shader.Find("Standard"));
        }

        // 2. 创建独立材质实例（避免共享参数）
        Material blendMaterial = new Material(threeMatShader);

        // 3. 配置材质A属性（土地强制非金属）
        blendMaterial.SetTexture("_MainTexA", baseMaterialA?.mainTexture ?? Texture2D.whiteTexture);
        blendMaterial.SetTexture("_NormalMapA", GetNormalMapFromMaterial(baseMaterialA) ?? Texture2D.normalTexture);
        blendMaterial.SetFloat("_MetallicA", 0.0f); // 土地强制非金属
        blendMaterial.SetFloat("_SmoothnessA", baseMaterialA?.HasProperty("_Smoothness") ?? false ? baseMaterialA.GetFloat("_Smoothness") : 0.15f);

        // 4. 配置材质B属性（同上）
        blendMaterial.SetTexture("_MainTexB", baseMaterialB?.mainTexture ?? Texture2D.whiteTexture);
        blendMaterial.SetTexture("_NormalMapB", GetNormalMapFromMaterial(baseMaterialB) ?? Texture2D.normalTexture);
        blendMaterial.SetFloat("_MetallicB", 0.0f);
        blendMaterial.SetFloat("_SmoothnessB", baseMaterialB?.HasProperty("_Smoothness") ?? false ? baseMaterialB.GetFloat("_Smoothness") : 0.15f);

        // 5. 配置材质C属性（新增：三材质专属）
        blendMaterial.SetTexture("_MainTexC", baseMaterialC?.mainTexture ?? Texture2D.whiteTexture);
        blendMaterial.SetTexture("_NormalMapC", GetNormalMapFromMaterial(baseMaterialC) ?? Texture2D.normalTexture);
        blendMaterial.SetFloat("_MetallicC", 0.0f);
        blendMaterial.SetFloat("_SmoothnessC", baseMaterialC?.HasProperty("_Smoothness") ?? false ? baseMaterialC.GetFloat("_Smoothness") : 0.2f);

        // 6. 配置混合控制参数（适配三材质Shader）
        blendMaterial.SetTexture("_MaskTex", blendMask);
        blendMaterial.SetFloat("_BlendSmooth", globalSmoothness);

        return blendMaterial;
    }

    private static Texture2D _barycentricMask;
    public static Texture2D GetOrCreateBarycentricMask()
    {
        if (_barycentricMask != null) return _barycentricMask;

        int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            float v = y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float r = Mathf.Clamp01(1f - u - v);
                float g = u;
                float b = v;
                float sum = r + g + b;
                if (sum > 0.0001f)
                {
                    r /= sum; g /= sum; b /= sum;
                }
                else { r = g = b = 1f / 3f; }
                tex.SetPixel(x, y, new Color(r, g, b, 1f));
            }
        }
        tex.Apply();
        _barycentricMask = tex;
        return tex;
    }

    /// <summary>
    /// 从材质中获取法线贴图（兼容Standard Shader）
    /// </summary>
    private static Texture2D GetNormalMapFromMaterial(Material mat)
    {
        // 添加空值检查
        if (mat == null)
        {
            Debug.LogWarning("尝试从空材质获取法线贴图");
            return null;
        }

        if (mat.HasProperty("_BumpMap"))
        {
            return mat.GetTexture("_BumpMap") as Texture2D;
        }
        return null;
    }

    /// <summary>
    /// 兜底修正网格里 NaN/Inf/零长的法线与切线，保证切线空间（TBN）始终有效。
    /// <para>
    /// 过渡面的 XZ 平面投影 UV 在近垂直面上退化成零面积，RecalculateTangents 会产生 Inf/NaN 切线；
    /// 混合 Shader 用切线空间法线贴图设置 o.Normal，NaN 切线会让整个片元输出 NaN → 死黑且无法被光照点亮。
    /// 这里把坏法线换成安全值、把坏切线换成“由法线正交推出的有效切线”，从而消除死黑。
    /// </para>
    /// </summary>
    public static void SanitizeNormalsAndTangents(Mesh mesh)
    {
        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length == 0)
        {
            return;
        }

        Vector4[] tangents = mesh.tangents;
        bool hasTangents = tangents != null && tangents.Length == normals.Length;
        bool normalsChanged = false;
        bool tangentsChanged = false;

        for (int i = 0; i < normals.Length; i++)
        {
            Vector3 n = normals[i];
            if (IsInvalid(n.x) || IsInvalid(n.y) || IsInvalid(n.z) || n.sqrMagnitude < 1e-10f)
            {
                n = Vector3.up;
                normals[i] = n;
                normalsChanged = true;
            }

            if (!hasTangents)
            {
                continue;
            }

            Vector4 t = tangents[i];
            Vector3 t3 = new Vector3(t.x, t.y, t.z);
            // 切线正交化到法线，判断是否退化
            Vector3 ortho = t3 - n * Vector3.Dot(n, t3);
            bool badTangent =
                IsInvalid(t.x) || IsInvalid(t.y) || IsInvalid(t.z) || IsInvalid(t.w) ||
                ortho.sqrMagnitude < 1e-10f;

            if (badTangent)
            {
                // 由法线叉乘一个不平行的参考轴，得到一条有效切线
                Vector3 reference = Mathf.Abs(n.y) < 0.99f ? Vector3.up : Vector3.right;
                Vector3 fallback = Vector3.Cross(reference, n);
                if (fallback.sqrMagnitude < 1e-10f)
                {
                    fallback = Vector3.Cross(Vector3.forward, n);
                }
                fallback.Normalize();
                float w = (t.w == 1f || t.w == -1f) ? t.w : 1f;
                tangents[i] = new Vector4(fallback.x, fallback.y, fallback.z, w);
                tangentsChanged = true;
            }
        }

        if (normalsChanged)
        {
            mesh.normals = normals;
        }
        if (tangentsChanged)
        {
            mesh.tangents = tangents;
        }
    }

    public static void RecalculateTangentsSafe(Mesh mesh)
    {
        if (mesh == null) return;
        mesh.RecalculateTangents();
        SanitizeNormalsAndTangents(mesh);
    }

    private static bool IsInvalid(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value);
    }

    public static Material CreateTerrainFogMaterial(Material sourceMaterial, Shader terrainFogShader)
    {
        // 克隆源 Standard 材质（完整保留 _MainTex 及其 tiling/offset、_BumpMap、_Color、
        // 关键字等所有属性），再只替换 Shader。设置 .shader 时 Unity 会保留两个 Shader 中
        // 同名的属性值，因此地形外观与原 Standard 完全一致，只是额外获得迷雾混合。
        // 之前逐个 SetTexture 手抄属性的做法会漏掉 tiling/offset 与关键字，导致中心区域
        // 丢失纹理、渲染成纯色。
        Material mat = new Material(sourceMaterial);
        if (terrainFogShader != null)
            mat.shader = terrainFogShader;
        return mat;
    }

}
