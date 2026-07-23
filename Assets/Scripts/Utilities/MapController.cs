using System.Collections;
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
    /// 创建一个Mesh - 单网格，单材质（河水在用）
    /// </summary>
    /// <param name="vertices">顶点数组</param>
    /// <param name="uv">UV</param>
    /// <param name="indices">三角形绘制顺序</param>
    /// <param name="gameObject">承载网格的物体</param>
    /// <param name="materials">要使用的材质</param>
    /// <param name="isUInt32">是否用 32 位无符号整数作为网格索引的类型。不用的话Mesh只能小于 65,535 个顶点，否则将出错</param>
    public static Mesh CreatMesh(Vector3[] vertices, Vector2[] uv, int[] indices, GameObject gameObject, Material[] materials, Color[] colors = null, bool isUInt32 = false)
    {
        Mesh mesh = new Mesh();
        if (isUInt32 || vertices.Length > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        //顶点数组
        mesh.vertices = vertices;
        //UV
        mesh.uv = uv;
        //顶点色（可选，用于迷雾等按顶点控制的效果）
        if (colors != null && colors.Length == vertices.Length) mesh.colors = colors;
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

        meshRenderer.sharedMaterials = materials;
        meshFilter.sharedMesh = mesh;

        return mesh;
    }

    /// <summary>
    /// 创建一个Mesh - 多个子网格，各自对应材质(湖或海在用，子网格1：海岸网格、子网格1：海水网格)
    /// </summary>
    /// <param name="vertices">顶点数组</param>
    /// <param name="uv">UV</param>
    /// <param name="indices">三角形绘制顺序</param>
    /// <param name="gameObject">承载网格的物体</param>
    /// <param name="materials">要使用的材质</param>
    /// <param name="isUInt32">是否用 32 位无符号整数作为网格索引的类型。不用的话Mesh只能小于 65,535 个顶点，否则将出错</param>
    public static Mesh CreatMesh(Vector3[] vertices, Vector2[] uv, int[][] indices, GameObject gameObject, Material[] materials, Color[] colors = null, bool isUInt32 = false)
    {
        Mesh mesh = new Mesh();
        if (isUInt32 || vertices.Length > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        //顶点数组
        mesh.vertices = vertices;
        //UV
        mesh.uv = uv;
        //顶点色（可选，用于迷雾等按顶点控制的效果）
        if (colors != null && colors.Length == vertices.Length) mesh.colors = colors;
        //三角形绘制顺序
        mesh.subMeshCount = indices.Length;
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i]?.Length > 0)
            {
                mesh.SetTriangles(indices[i], i);
            }
        }
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

        meshRenderer.sharedMaterials = materials;
        meshFilter.sharedMesh = mesh;

        return mesh;
    }

    /// <summary>
    /// 创建地图Mesh（重载-支持多个子网格+真实材质混合）
    /// 子网格顺序：3个基础类型 - 2个过渡类型
    /// 基础类型：0=海底、1=平地、2=高地
    /// 过渡类型：矩形过渡、三角过渡
    /// 过渡区域使用RealMaterialMaskBlend Shader实现材质混合
    /// </summary>
    /// <param name="vertices">顶点数组</param>
    /// <param name="uv">UV数组</param>
    /// <param name="subMeshIndices">子网格的三角形索引
    /// （
    /// [0]海底、
    /// [1]平地、
    /// [2]高地、
    /// [3,3+baseMaterialAs.Count-1]矩形区域、
    /// [3+baseMaterialAs.Count，3+baseMaterialAs.Count+baseMaterialAsTri.Count-1]三角区域
    /// ）
    /// </param>
    /// <param name="gameObject">承载网格的物体</param>
    /// <param name="baseMaterials">基础材质数组（[0]海底、[1]平地、[2]高地）</param>
    /// <param name="baseMaterialAs、baseMaterialBs">矩形过渡区域两个材质（用RealMaterialMaskBlend.shader实现双材质混合）</param>
    /// <param name="baseMaterialAsTri、baseMaterialBsTri、baseMaterialCsTri">三角过渡区域三个材质（用ThreeMaterialBlend_Land.shader实现三材质混合）</param>
    /// <param name="blendMask">混合遮罩图（白=A材质，黑=B材质，灰=混合）</param>
    /// <param name="blendSmooth">矩形区域的过渡宽度 </param>
    /// <param name="blendContrast">矩形、三角区域的混合对比度 </param>
    /// <param name="globalSmoothness" > 三角区域的全局光滑度 </ param >
    /// <param name="isUInt32">是否用 32 位无符号整数作为网格索引的类型。不用的话Mesh只能小于 65,535 个顶点，否则将出错</param>
    public static Mesh CreatMesh(
        Vector3[] vertices,
        Vector2[] uv,
        Color[] colors,
        int[][] subMeshIndices,
        GameObject gameObject,
        Material[] baseMaterials,
        Material[] baseMaterialAs,
        Material[] baseMaterialBs,
        Material[] baseMaterialAsTri,
        Material[] baseMaterialBsTri,
        Material[] baseMaterialCsTri,
        Texture2D blendMask,
        float blendContrast,
        float blendSmooth,
        float globalSmoothness,
        bool isUInt32 = false
    )
    {
        // 1. 安全检查：确保 gameObject 不为 null
        if (gameObject == null)
        {
            return null;
        }

        // 2. 初始化Mesh
        Mesh mesh = new Mesh();
        if (isUInt32 || vertices.Length > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.colors = colors;

        // 3. 设置子网格数量
        mesh.subMeshCount = subMeshIndices.Length;

        // 4. 给每个子网格分配三角形索引
        for (int i = 0; i < subMeshIndices.Length; i++)
        {
            if (subMeshIndices[i] != null && subMeshIndices[i].Length > 0)
            {
                mesh.SetTriangles(subMeshIndices[i], i);
            }
        }

        // 5. 自动计算法线和切线
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        // 5.1 去除 NaN/退化的法线与切线。
        // 过渡面（尤其近垂直的墙面/踢面）的 XZ 平面投影 UV 会退化成零面积三角形，
        // RecalculateTangents 因此算出 Inf/NaN 切线；混合 Shader 用切线空间法线贴图设置 o.Normal，
        // NaN 切线会让 TBN 崩坏、输出 NaN，被渲染成“任何光照都点不亮的死黑”。这里兜底修正，保证 TBN 有效。
        SanitizeNormalsAndTangents(mesh);
        mesh.RecalculateBounds();
        // 不调用 mesh.Optimize()：它会重排顶点缓冲区，导致 HexCellData 中记录的
        // MeshSolidAreaVertexStartIndex / MeshTransitionVertexRanges 失效，
        // 运行时按索引回写探索顶点色会写到错误的顶点上（黑白棋盘格乱纹）。

        // 6. 配置MeshFilter和MeshRenderer（添加安全检查）
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

        // 7. 配置材质（所有子网格使用独立材质副本）
        Material[] allMaterials = new Material[subMeshIndices.Length];
        // 子网格0-2：基础材质使用 TerrainBase_Fog Shader（创建独立副本）
        Shader terrainFogShader = Shader.Find("Custom/TerrainBase_Fog");
        if (terrainFogShader == null)
        {
            terrainFogShader = Shader.Find("Standard");
        }
        allMaterials[0] = baseMaterials[0] != null ? CreateTerrainFogMaterial(baseMaterials[0], terrainFogShader) : null; // 海底
        allMaterials[1] = baseMaterials[1] != null ? CreateTerrainFogMaterial(baseMaterials[1], terrainFogShader) : null; // 平地
        allMaterials[2] = baseMaterials[2] != null ? CreateTerrainFogMaterial(baseMaterials[2], terrainFogShader) : null; // 高地

        // 矩形过渡区域混合材质（已通过ConfigureBlendMaterial创建独立实例）
        // UV 沿 profile 方向参数化：V=0 为 self 端、V=1 为 neighbor 端。
        // 遮罩渐变暗→亮(V=0→V=1)，配合 shader 的 lerp(B,A,weight)，
        // self 端(V=0,暗) → _MainTexB=self，neighbor 端(V=1,亮) → _MainTexA=neighbor。
        for (int i = 0; i < baseMaterialAs.Length; i++)
        {
            allMaterials[3 + i] = ConfigureBlendMaterial(
                baseMaterialBs[i],
                baseMaterialAs[i],
                blendMask,
                blendContrast,
                blendSmooth
            );
        }

        // 三角过渡区域混合材质（已通过ConfigureBlendMaterial创建独立实例）
        var triMask = GetOrCreateBarycentricMask();
        for (int i = 0; i < baseMaterialAsTri.Length; i++)
        {
            allMaterials[baseMaterialAs.Length + 3 + i] = ConfigureBlendMaterial(
                baseMaterialAsTri[i],
                baseMaterialBsTri[i],
                baseMaterialCsTri[i],
                triMask,
                blendContrast,
                globalSmoothness
            );
        }

        // 8. 设置材质前再次检查
        if (allMaterials != null && allMaterials.Length > 0)
        {
            try
            {
                meshRenderer.sharedMaterials = allMaterials;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to set materials on {gameObject.name}: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"No materials to set for {gameObject.name}");
        }

        meshFilter.sharedMesh = mesh;

        // 9. 配置碰撞体
        MeshCollider meshCollider = gameObject.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        meshCollider.sharedMesh = mesh;

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
    private static Material ConfigureBlendMaterial(Material baseMaterialA, Material baseMaterialB, Texture2D blendMask, float blendContrast, float blendSmooth)
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
    private static Material ConfigureBlendMaterial(Material baseMaterialA, Material baseMaterialB, Material baseMaterialC, Texture2D blendMask, float blendContrast, float globalSmoothness)
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
    private static Texture2D GetOrCreateBarycentricMask()
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
    private static void SanitizeNormalsAndTangents(Mesh mesh)
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

    private static bool IsInvalid(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value);
    }

    private static Material CreateTerrainFogMaterial(Material sourceMaterial, Shader terrainFogShader)
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
