using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 卡牌拖拽「卡牌→模型」预览控制器（实施计划 §5.1）。
/// 方案 A：专用正交预览相机 + 隔离 Layer(CardPreview) + 全局单张 RenderTexture + UI 上的 RawImage。
///
/// 生命周期完全由本控制器拥有（§7）：基础设施惰性创建、全局复用，
/// Begin/UpdateProgress/End 逐帧驱动，Dispose 释放 RT 与所有场景物件。
/// End() 幂等，成功/失败/暂停/失焦/场景切换都可安全重复调用。
/// </summary>
public class CardDragPreviewController : System.IDisposable
{
    private readonly Canvas _uiCanvas;
    private readonly GameLoop _gameLoop;

    private Camera _previewCamera;
    private RenderTexture _previewRT;
    private RawImage _previewImage;
    private RectTransform _previewRect;
    private RectTransform _previewParent;
    private Transform _previewAnchor;
    private GameObject _previewRoot;

    private GameObject _currentModel;
    private GameObject _currentPrefab;

    /// <summary>当前拖拽 token：拒绝旧卡的迟到回调（§8「有效释放后回调迟到」）。</summary>
    private object _currentToken;

    private bool _isActive;
    private bool _isDisposed;
    private bool _infrastructureFailed;

    private int _previewLayer = -1;
    private float _baseOrthographicSize = 1f;

    public CardDragPreviewController(Canvas uiCanvas, GameLoop gameLoop)
    {
        _uiCanvas = uiCanvas;
        _gameLoop = gameLoop;
    }

    /// <summary>当前是否有活动预览（供调试与断言使用）。</summary>
    public bool IsActive => _isActive;

    /// <summary>
    /// 开始预览：实例化模型 → 纯展示化 → 按 bounds 配置相机取景 → 显示 RawImage。
    /// modelPrefab 为空、Layer 缺失或基础设施创建失败时静默降级（只缩卡，不出模型）。
    /// </summary>
    public void Begin(GameObject modelPrefab, object token)
    {
        if (_isDisposed || modelPrefab == null) return;

        // 上一次预览若未清理（异常路径），先幂等收尾再开新的。
        End(_currentToken);

        if (!EnsureInfrastructure()) return;

        _currentToken = token;

        // 同一 Prefab 跨拖拽复用实例，避免重复 Instantiate/Destroy（§9）。
        if (_currentModel == null || _currentPrefab != modelPrefab)
        {
            DestroyCurrentModel();

            _currentModel = Object.Instantiate(modelPrefab, _previewAnchor);
            _currentPrefab = modelPrefab;
            CardDragPreviewUtils.StripToVisual(_currentModel, _previewLayer);
        }

        _currentModel.transform.localPosition = Vector3.zero;
        _currentModel.transform.localRotation = Quaternion.identity;
        _currentModel.transform.localScale = Vector3.one;
        _currentModel.SetActive(true);

        // 蒙皮网格的 bounds 由骨骼当前姿势决定，实例化当帧 Animator 尚未求值。
        // 必须先强制求值一次，否则单位（SkinnedMeshRenderer）取景会整体偏移；
        // 建筑是 MeshRenderer，不受影响——这正是「单位偏移、建筑正常」的来源。
        CardDragPreviewUtils.ForceEvaluatePose(_currentModel);

        // bounds 与取景只在 Begin 计算一次（§9 每帧约束）。
        FrameCamera(_currentModel);

        _previewCamera.enabled = true;
        _isActive = true;

        // 初始不可见：由第一帧 UpdateProgress 按 modelProgress 决定缩放与 alpha。
        ApplyVisual(Vector2.zero, 0f, false);
    }

    /// <summary>
    /// 逐帧更新：跟随指针 + 反向缩放 + 淡入（§3 公式的 modelProgress 已由 CardController 算好）。
    /// 暂停时不推进 Animator（§9 暂停约束）。
    /// </summary>
    public void UpdateProgress(Vector2 screenPos, float modelProgress, float modelAlpha, object token)
    {
        if (!_isActive || _isDisposed) return;
        if (!ReferenceEquals(token, _currentToken)) return;

        float scale = Mathf.Lerp(FeelConfigProvider.CardDragModelMinScale, 1f, Mathf.Clamp01(modelProgress));
        ApplyVisual(screenPos, Mathf.Clamp01(modelAlpha), true, scale);

        if (_currentModel != null && _gameLoop != null)
        {
            // 暂停时冻结待机动画，恢复后继续。
            bool paused = _gameLoop.IsPaused;
            foreach (Animator animator in _currentModel.GetComponentsInChildren<Animator>(false))
                if (animator != null) animator.speed = paused ? 0f : 1f;
        }
    }

    /// <summary>结束预览（幂等）：隐藏 RawImage、关闭相机、隐藏模型实例。</summary>
    public void End(object token)
    {
        if (_isDisposed) return;
        if (_currentToken != null && token != null && !ReferenceEquals(token, _currentToken)) return;

        _isActive = false;
        _currentToken = null;

        if (_previewImage != null)
        {
            _previewImage.enabled = false;
            Color c = _previewImage.color;
            c.a = 0f;
            _previewImage.color = c;
        }

        if (_previewCamera != null) _previewCamera.enabled = false;

        // 实例保留供同 Prefab 复用，仅失活（§9 单实例复用）。
        if (_currentModel != null) _currentModel.SetActive(false);
    }

    /// <summary>取消预览：本方案与 End 一致（模型直接消失，§0.4）。</summary>
    public void Cancel(object token) => End(token);

    /// <summary>场景销毁/容器释放：释放 RT 并销毁全部预览物件（§7）。</summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        End(null);
        _isDisposed = true;

        DestroyCurrentModel();

        if (_previewCamera != null) _previewCamera.targetTexture = null;

        if (_previewRT != null)
        {
            _previewRT.Release();
            Object.Destroy(_previewRT);
            _previewRT = null;
        }

        if (_previewImage != null)
        {
            Object.Destroy(_previewImage.gameObject);
            _previewImage = null;
            _previewRect = null;
        }

        if (_previewRoot != null)
        {
            Object.Destroy(_previewRoot);
            _previewRoot = null;
            _previewCamera = null;
            _previewAnchor = null;
        }
    }

    /// <summary>按屏幕坐标定位 RawImage，并写入缩放与 alpha（精确跟随指针，不做边缘 Clamp）。</summary>
    private void ApplyVisual(Vector2 screenPos, float alpha, bool follow, float scale = 1f)
    {
        if (_previewImage == null || _previewRect == null) return;

        _previewImage.enabled = alpha > 0f;

        Color c = _previewImage.color;
        c.a = alpha;
        _previewImage.color = c;

        _previewRect.localScale = Vector3.one * scale;

        if (!follow || _previewParent == null) return;

        // 主 Canvas 为 ScreenSpaceOverlay，camera 参数必须传 null（§4.4）。
        Camera eventCamera = _uiCanvas != null && _uiCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? _uiCanvas.worldCamera
            : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _previewParent, screenPos, eventCamera, out Vector2 localPoint))
            return;

        // 不做边缘 Clamp：模型位置即落点指示（§0.2），在屏幕边缘与指针脱钩会误导落点判断。
        // 且 Clamp 量随缩放变化——满缩放时窗口半宽达 256 参考单位，离边很远就被卡住，
        // 而小模型阶段几乎能贴边，两阶段手感割裂。越界部分自然落到屏幕外，无渲染问题。
        _previewRect.anchoredPosition = localPoint;
    }

    /// <summary>按渲染 bounds 让模型在正交相机内居中且完整入框（Begin 阶段一次性）。</summary>
    private void FrameCamera(GameObject model)
    {
        if (_previewCamera == null) return;

        float size = _baseOrthographicSize;

        if (CardDragPreviewUtils.TryGetRenderBounds(model, out Bounds bounds))
        {
            // bounds.center 是世界坐标，必须换算回 anchor 局部空间再取反；
            // 直接用世界差值赋给 localPosition 会把模型推到 anchor 的世界偏移上（y≈-5000）而飞出取景。
            Vector3 localCenter = _previewAnchor.InverseTransformPoint(bounds.center);
            model.transform.localPosition = -localCenter;

            // 正交尺寸 = 半高。模型以 45° 斜俯视拍摄，用包围盒对角半径而非 XY，
            // 避免绕 Y 旋转后水平方向被裁切。
            float extent = bounds.extents.magnitude;
            if (extent > 0.0001f)
                size = extent * FeelConfigProvider.CardDragPreviewPadding;
        }

        _previewCamera.orthographicSize = Mathf.Max(0.01f, size);
    }

    /// <summary>惰性创建 Layer 校验 + RT + 预览相机 + 挂载点 + RawImage。失败后不再重试。</summary>
    private bool EnsureInfrastructure()
    {
        if (_infrastructureFailed) return false;
        if (_previewCamera != null && _previewRT != null && _previewImage != null) return true;

        if (_previewLayer < 0)
        {
            _previewLayer = LayerMask.NameToLayer(CardDragPreviewUtils.PreviewLayerName);
            if (_previewLayer < 0)
            {
                Debug.LogError($"[CardDragPreview] 缺少 Layer [{CardDragPreviewUtils.PreviewLayerName}]，" +
                               "请在 ProjectSettings/TagManager 中新增；预览已降级为只缩卡。");
                _infrastructureFailed = true;
                return false;
            }
        }

        if (_uiCanvas == null)
        {
            Debug.LogError("[CardDragPreview] 未绑定 UI Canvas，预览已降级为只缩卡。");
            _infrastructureFailed = true;
            return false;
        }

        if (!EnsureRenderTexture()) return false;
        EnsurePreviewStudio();
        EnsurePreviewImage();

        return _previewCamera != null && _previewImage != null;
    }

    /// <summary>创建全局单张 RT（ARGB32、无 mipmap、depth 24）；不支持时回退到默认格式。</summary>
    private bool EnsureRenderTexture()
    {
        if (_previewRT != null) return true;

        int size = Mathf.Max(64, FeelConfigProvider.CardDragPreviewRTSize);

        RenderTextureFormat format = RenderTextureFormat.ARGB32;
        if (!SystemInfo.SupportsRenderTextureFormat(format))
            format = RenderTextureFormat.Default;

        _previewRT = new RenderTexture(size, size, 24, format)
        {
            name = "CardDragPreviewRT",
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        if (!_previewRT.Create())
        {
            Debug.LogError("[CardDragPreview] RenderTexture 创建失败，预览已降级为只缩卡。");
            Object.Destroy(_previewRT);
            _previewRT = null;
            _infrastructureFailed = true;
            return false;
        }

        return true;
    }

    /// <summary>创建「预览工作室」：远离地图的根节点 + 挂载点 + 正交相机 + 专用补光。</summary>
    private void EnsurePreviewStudio()
    {
        if (_previewCamera != null) return;

        _previewRoot = new GameObject("CardDragPreviewRoot");
        Object.DontDestroyOnLoad(_previewRoot);
        // 放到远离地图的位置，确保不与真实场景内容互相干扰。
        _previewRoot.transform.position = new Vector3(0f, -5000f, 0f);

        var anchorGo = new GameObject("Anchor");
        anchorGo.transform.SetParent(_previewRoot.transform, false);
        anchorGo.layer = _previewLayer;
        _previewAnchor = anchorGo.transform;

        var cameraGo = new GameObject("PreviewCamera");
        cameraGo.transform.SetParent(_previewRoot.transform, false);
        cameraGo.layer = _previewLayer;

        // 斜俯视 45°，与游戏内主视角观感接近；距离足够远，配合正交尺寸控制取景。
        float distance = FeelConfigProvider.CardDragPreviewCameraDistance;
        Quaternion rotation = Quaternion.Euler(30f, 45f, 0f);
        cameraGo.transform.localRotation = rotation;
        // 沿自身朝向反向后退 distance（Vector3.back 已含反向，不能对 Quaternion 取负）。
        cameraGo.transform.localPosition = rotation * Vector3.back * distance;

        _previewCamera = cameraGo.AddComponent<Camera>();
        _previewCamera.orthographic = true;
        _previewCamera.orthographicSize = _baseOrthographicSize;
        _previewCamera.clearFlags = CameraClearFlags.SolidColor;
        // alpha = 0：RT 透明，RawImage 才能透出底下的卡牌与地图（§4.2）。
        _previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _previewCamera.cullingMask = 1 << _previewLayer;
        _previewCamera.nearClipPlane = 0.01f;
        _previewCamera.farClipPlane = distance * 4f;
        _previewCamera.targetTexture = _previewRT;
        _previewCamera.allowHDR = false;
        _previewCamera.allowMSAA = false;
        _previewCamera.useOcclusionCulling = false;
        _previewCamera.depth = -100;
        _previewCamera.enabled = false;

        // 预览专用补光：只照亮 CardPreview Layer，避免模型漆黑。
        var lightGo = new GameObject("PreviewLight");
        lightGo.transform.SetParent(_previewRoot.transform, false);
        lightGo.layer = _previewLayer;
        lightGo.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);

        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.cullingMask = 1 << _previewLayer;
        light.shadows = LightShadows.None;
    }

    /// <summary>在 UI Canvas 顶层创建显示 RT 的 RawImage（不拦截射线）。</summary>
    private void EnsurePreviewImage()
    {
        if (_previewImage != null) return;

        _previewParent = _uiCanvas.transform as RectTransform;

        var go = new GameObject("CardDragPreviewImage");
        go.transform.SetParent(_previewParent, false);

        _previewRect = go.AddComponent<RectTransform>();
        _previewRect.anchorMin = new Vector2(0.5f, 0.5f);
        _previewRect.anchorMax = new Vector2(0.5f, 0.5f);
        _previewRect.pivot = new Vector2(0.5f, 0.5f);

        float side = FeelConfigProvider.CardDragPreviewWindowSize;
        _previewRect.sizeDelta = new Vector2(side, side);

        _previewImage = go.AddComponent<RawImage>();
        _previewImage.texture = _previewRT;
        _previewImage.raycastTarget = false;
        _previewImage.color = new Color(1f, 1f, 1f, 0f);
        _previewImage.enabled = false;

        // 顶层：盖在所有卡牌之上。
        go.transform.SetAsLastSibling();
    }

    private void DestroyCurrentModel()
    {
        if (_currentModel != null)
        {
            Object.Destroy(_currentModel);
            _currentModel = null;
        }
        _currentPrefab = null;
    }
}
