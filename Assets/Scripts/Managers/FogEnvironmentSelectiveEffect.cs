using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class FogEnvironmentSelectiveEffect : MonoBehaviour
{
    private static readonly int ObjectMaskId = Shader.PropertyToID("_FogAffectedObjectMask");
    private static readonly int SceneColorId = Shader.PropertyToID("_FogSceneColorTex");
    private static readonly int UnitUIRectsId = Shader.PropertyToID("_UnitUIRects");
    private static readonly int UnitUICountId = Shader.PropertyToID("_UnitUICount");

    private const int MaxUnitUIRects = 32;

    private readonly List<Renderer> _renderers = new List<Renderer>();
    private readonly List<Renderer> _alwaysRenderers = new List<Renderer>();
    private readonly List<Renderer> _eraseRenderers = new List<Renderer>();
    private readonly List<Canvas> _eraseCanvases = new List<Canvas>();
    private readonly Vector4[] _uiRects = new Vector4[MaxUnitUIRects];
    private readonly Vector3[] _uiCorners = new Vector3[4];
    private Camera _camera;
    private GameObject _landFormRoot;
    private GameObject _resourceRoot;
    private GameObject[] _fogRoots = new GameObject[0];
    private GameObject[] _eraseRoots = new GameObject[0];
    private Material _maskMaterial;
    private Material _maskAlwaysMaterial;
    private Material _eraseMaterial;
    private Material _eraseUIMaterial;
    private Mesh _eraseUIQuad;
    private Material _validationMaterial;
    private RenderTexture _objectMask;
    private CommandBuffer _maskCommands;
    private int _maskWidth;
    private int _maskHeight;
    private bool _initialized;

    /// <summary>
    /// 【断供方案-阶段5】fogRoots：额外纳入雾化对象遮罩的根节点
    ///（建筑根 PlayerBuilding/EnemyBuilding——断供地块上的建筑随地面一起被迷雾覆盖）。
    /// 【地貌/资源常驻遮罩】landFormRoot/resourceRoot 使用不依赖相机深度的常驻遮罩
    ///（FogEnvironmentObjectMaskAlways）：贴地/半埋模型（金矿等）的像素会随相机角度
    /// 被深度测试裁出遮罩，导致"拉近时从迷雾中显露"；地貌/资源的雾化只取决于地块
    /// 探索状态，与相机视角无关。被建筑遮挡的像素由后绘制的建筑遮罩覆盖。
    /// 【单位擦除层-方案A】eraseRoots：从雾化遮罩中"擦除"的根节点
    ///（单位根 PlayerUnit/EnemyUnit）——单位是透明队列不在相机深度纹理中，
    /// 对象遮罩的深度裁剪看不到单位，雾化会连带盖住单位；擦除 pass 用单位自身
    /// 深度与场景深度比较，把可见单位像素从遮罩清除（决策 8 单位不雾化）。
    /// </summary>
    public void Initialize(
        GameObject landFormRoot,
        GameObject resourceRoot,
        GameObject[] fogRoots,
        GameObject[] eraseRoots)
    {
        _camera = GetComponent<Camera>();
        _camera.depthTextureMode |= DepthTextureMode.Depth;
        _landFormRoot = landFormRoot;
        _resourceRoot = resourceRoot;
        _fogRoots = fogRoots ?? new GameObject[0];
        _eraseRoots = eraseRoots ?? new GameObject[0];

        if (!CreateMaterials())
        {
            enabled = false;
            return;
        }

        _initialized = true;
        RefreshRenderers();
    }

    public void RefreshRenderers()
    {
        if (!_initialized) return;

        _renderers.Clear();
        _alwaysRenderers.Clear();
        _eraseRenderers.Clear();
        _eraseCanvases.Clear();
        AddRenderers(_landFormRoot, _alwaysRenderers);
        AddRenderers(_resourceRoot, _alwaysRenderers);
        foreach (GameObject root in _fogRoots)
            AddRenderers(root, _renderers);
        foreach (GameObject root in _eraseRoots)
        {
            AddRenderers(root, _eraseRenderers);
            // 单位 UI（世界空间 Canvas）同步收集，用于屏幕矩形擦除
            if (root != null)
                _eraseCanvases.AddRange(root.GetComponentsInChildren<Canvas>(true));
        }
        EnsureMaskResources();
        RebuildMaskCommands();
    }

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        _camera.depthTextureMode |= DepthTextureMode.Depth;
        if (_initialized)
            RefreshRenderers();
    }

    private void OnPreCull()
    {
        if (!_initialized) return;
        EnsureMaskResources();
        UpdateEraseUIRects();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (!_initialized || _validationMaterial == null || _objectMask == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        _validationMaterial.SetTexture(ObjectMaskId, _objectMask);
        // 不依赖 Graphics.Blit 对隐式 _MainTex 的绑定；该 Shader include 多套全局纹理后，
        // 部分平台/编辑器路径下 _MainTex 会采到默认灰纹理。
        _validationMaterial.SetTexture(SceneColorId, source);
        Graphics.Blit(source, destination, _validationMaterial);
    }

    private bool CreateMaterials()
    {
        if (_maskMaterial == null)
        {
            Shader maskShader = Shader.Find("Hidden/FogEnvironmentObjectMask");
            if (maskShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentObjectMask Shader。");
                return false;
            }
            _maskMaterial = new Material(maskShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_maskAlwaysMaterial == null)
        {
            Shader maskAlwaysShader = Shader.Find("Hidden/FogEnvironmentObjectMaskAlways");
            if (maskAlwaysShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentObjectMaskAlways Shader。");
                return false;
            }
            _maskAlwaysMaterial = new Material(maskAlwaysShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_validationMaterial == null)
        {
            Shader effectShader = Shader.Find("Hidden/FogEnvironmentSelective");
            if (effectShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentSelective Shader。");
                return false;
            }
            _validationMaterial = new Material(effectShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_eraseMaterial == null)
        {
            Shader eraseShader = Shader.Find("Hidden/FogEnvironmentUnitErase");
            if (eraseShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentUnitErase Shader。");
                return false;
            }
            _eraseMaterial = new Material(eraseShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_eraseUIMaterial == null)
        {
            Shader eraseUIShader = Shader.Find("Hidden/FogEnvironmentUnitUIErase");
            if (eraseUIShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentUnitUIErase Shader。");
                return false;
            }
            _eraseUIMaterial = new Material(eraseUIShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        return true;
    }

    /// <summary>
    /// 【单位 UI 擦除】每帧把单位世界空间 Canvas 的屏幕矩形投影到遮罩坐标系，
    /// 写入擦除材质（CommandBuffer 执行时读取最新值）——UI 像素从雾化遮罩中清除，
    /// 与单位模型擦除同理（世界空间 UI 不写深度，遮罩深度裁剪看不到它）。
    /// 提示浮标不在此列：浮标已改用 MarkerOverlayCamera 叠加相机渲染，
    /// 矩形擦除会连带清除浮标周围地面/金矿模型的雾，故不采用。
    /// </summary>
    private void UpdateEraseUIRects()
    {
        if (_eraseUIMaterial == null || _camera == null) return;

        Rect pixelRect = _camera.pixelRect;
        int count = 0;

        for (int i = 0; i < _eraseCanvases.Count && count < MaxUnitUIRects; i++)
            AddEraseRect(_eraseCanvases[i], pixelRect, ref count);

        _eraseUIMaterial.SetVectorArray(UnitUIRectsId, _uiRects);
        _eraseUIMaterial.SetInt(UnitUICountId, count);
    }

    private void AddEraseRect(Canvas canvas, Rect pixelRect, ref int count)
    {
        if (canvas == null || !canvas.gameObject.activeInHierarchy) return;

        RectTransform rect = canvas.GetComponent<RectTransform>();
        if (rect == null) return;

        rect.GetWorldCorners(_uiCorners);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int c = 0; c < 4; c++)
        {
            Vector3 screen = _camera.WorldToScreenPoint(_uiCorners[c]);
            float u = (screen.x - pixelRect.xMin) / Mathf.Max(1f, pixelRect.width);
            float v = (screen.y - pixelRect.yMin) / Mathf.Max(1f, pixelRect.height);
            if (u < min.x) min.x = u;
            if (v < min.y) min.y = v;
            if (u > max.x) max.x = u;
            if (v > max.y) max.y = v;
        }

        // 完全离屏的 UI 无需擦除
        if (max.x <= 0f || max.y <= 0f || min.x >= 1f || min.y >= 1f) return;

        // 1 像素 padding，防边缘残留雾化
        float pad = 1f / Mathf.Max(1f, Mathf.Max(pixelRect.width, pixelRect.height));
        _uiRects[count++] = new Vector4(min.x - pad, min.y - pad, max.x + pad, max.y + pad);
    }

    private void EnsureMaskResources()
    {
        int width = Mathf.Max(1, _camera.pixelWidth);
        int height = Mathf.Max(1, _camera.pixelHeight);
        if (_objectMask != null && width == _maskWidth && height == _maskHeight) return;

        ReleaseMaskTexture();
        _maskWidth = width;
        _maskHeight = height;

        // RG 存模型片元的地图 UV，B 存有效标记；不能再用单通道 R8。
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            ? RenderTextureFormat.ARGBHalf
            : RenderTextureFormat.ARGB32;
        _objectMask = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
        {
            name = "FogAffectedObjectMask",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        _objectMask.Create();
        RebuildMaskCommands();
    }

    private void RebuildMaskCommands()
    {
        if (!_initialized || _objectMask == null || _maskMaterial == null || _maskAlwaysMaterial == null) return;

        RemoveMaskCommands();
        _maskCommands = new CommandBuffer { name = "Fog Environment Object Mask" };
        _maskCommands.SetRenderTarget(_objectMask);
        _maskCommands.ClearRenderTarget(false, true, Color.black);

        // 【地貌/资源常驻遮罩】先绘制（不依赖相机深度）：雾化只取决于地块探索状态，
        // 贴地/半埋模型不会因相机角度变化被裁出遮罩（"拉近从迷雾中显露"的根因）。
        foreach (Renderer renderer in _alwaysRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

            int subMeshCount = GetSubMeshCount(renderer);
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                _maskCommands.DrawRenderer(renderer, _maskAlwaysMaterial, subMesh, 0);
        }

        // 建筑遮罩后绘制：深度裁剪保留（断供雾化语义），建筑像素覆盖前方地貌/资源遮罩。
        foreach (Renderer renderer in _renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

            int subMeshCount = GetSubMeshCount(renderer);
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                _maskCommands.DrawRenderer(renderer, _maskMaterial, subMesh, 0);
        }

        // 【单位擦除层-方案A】先雾化对象、后擦除单位：单位覆盖的像素从遮罩清零，
        // 雾化不会连带盖住单位（CommandBuffer 每帧按当前变换重绘，移动的单位实时生效）。
        foreach (Renderer renderer in _eraseRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

            int subMeshCount = GetSubMeshCount(renderer);
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                _maskCommands.DrawRenderer(renderer, _eraseMaterial, subMesh, 0);
        }

        // 【单位 UI 擦除】全屏 quad 按屏幕矩形把单位世界空间 UI（血条/图标）像素清零——
        // UI 像素处被雾化对象标记 B=1 时，后处理会连 UI 一起雾化，必须单独擦除。
        if (_eraseUIMaterial != null)
        {
            if (_eraseUIQuad == null)
                _eraseUIQuad = CreateFullScreenQuad();
            _maskCommands.DrawMesh(_eraseUIQuad, Matrix4x4.identity, _eraseUIMaterial, 0, 0);
        }

        // SetRenderTarget 会持续影响后续相机步骤，必须在图像效果前恢复颜色目标；
        // 否则 OnRenderImage 的 source 可能来自单通道对象遮罩而非场景颜色。
        _maskCommands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        _camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, _maskCommands);
    }

    private void AddRenderers(GameObject root, List<Renderer> target)
    {
        if (root == null) return;

        // 环境预制体可能附带 ParticleSystemRenderer、TrailRenderer 等特效。
        // 这些渲染器使用纯几何替换 Shader 重绘时可能生成覆盖全屏的错误遮罩，
        // 选择性雾化只标记实际模型表面。
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                target.Add(renderer);
        }
    }

    private static int GetSubMeshCount(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            return skinned.sharedMesh.subMeshCount;

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 1;
    }

    // 全屏 quad（clip 空间 -1..1），用于单位 UI 屏幕矩形擦除
    private static Mesh CreateFullScreenQuad()
    {
        var mesh = new Mesh { name = "FogUnitUIEraseQuad", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f),
            new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f),
            new Vector3(-1f, 1f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        return mesh;
    }

    private void OnDisable()
    {
        RemoveMaskCommands();
        ReleaseMaskTexture();
    }

    private void OnDestroy()
    {
        if (_maskMaterial != null) Destroy(_maskMaterial);
        if (_maskAlwaysMaterial != null) Destroy(_maskAlwaysMaterial);
        if (_eraseMaterial != null) Destroy(_eraseMaterial);
        if (_eraseUIMaterial != null) Destroy(_eraseUIMaterial);
        if (_eraseUIQuad != null) Destroy(_eraseUIQuad);
        if (_validationMaterial != null) Destroy(_validationMaterial);
    }

    private void RemoveMaskCommands()
    {
        if (_maskCommands == null) return;
        if (_camera != null)
            _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _maskCommands);
        _maskCommands.Release();
        _maskCommands = null;
    }

    private void ReleaseMaskTexture()
    {
        if (_objectMask == null) return;
        _objectMask.Release();
        Destroy(_objectMask);
        _objectMask = null;
    }
}
