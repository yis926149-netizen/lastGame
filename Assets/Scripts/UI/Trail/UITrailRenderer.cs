using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 弧光拖尾 · Renderer（MaskableGraphic 子类，工程内首个 Graphic 子类）。
///
/// 职责：持有全部活跃 Emitter 的采样点，在自身局部坐标系生成 ribbon mesh。
/// 节点全程不动 → mesh 顶点是绝对（局部）坐标 → 尾巴留在运动路径上（§2）。
///
/// 独立 Canvas 隔离（§1.2 C2）：本节点额外挂 Canvas + overrideSorting，把自己从主
/// Canvas 的重建域隔离出去。否则每帧重建 mesh 会拖累整个主 Canvas 的 rebatch。
///
/// 合批（§3.1）：同一 profile × layer 的所有 Emitter 共享一个 Renderer 节点，
/// 全部尾巴填进同一个 VertexHelper（多段互不相连的 strip）→ 一次 drawcall。
/// </summary>
[DisallowMultipleComponent]
public class UITrailRenderer : MaskableGraphic
{
    public UITrailProfile profile;
    public UITrailLayer layer = UITrailLayer.Below;

    /// <summary>Above 层的 sortingOrder。足够大以压过根 Canvas 下的普通内容与常见嵌套 Canvas。</summary>
    private const int AboveSortingOrder = 30000;

    /// <summary>
    /// 逐帧详细日志开关（诊断用）。开启后 Emitter 每次采样、Renderer 每次 mesh 重建都会打日志（有节流）。
    /// 定位完问题请关掉——它每秒会打十几条。菜单 Tools/UI拖尾/切换详细日志 可开关。
    /// </summary>
    public static bool VerboseLogging = false;

    /// <summary>
    /// 纯色调试模式（诊断用）。开启后用默认 UI 材质 + 白贴图 + 不透明品红顶点色画同一份 mesh，
    /// 把 shader、贴图 alpha、colorGradient、additive 混合一次性排除出嫌疑名单：
    ///   开了能看见 → 几何没问题，故障在渲染参数（shader/贴图/颜色/混合）；
    ///   开了还看不见 → 几何或层级问题（mesh 为空、被遮挡、被裁剪）。
    /// 菜单 Tools/UI拖尾/切换纯色调试模式 可开关。
    /// </summary>
    public static bool DebugSolidMode = false;
    private static bool _lastDebugSolidMode;

    private readonly List<UITrail> _emitters = new List<UITrail>();
    private Material _runtimeMaterial;
    private bool _hadGeometry;
    private bool _loggedFirstRibbon;
    private bool _warnedInactive;
    private bool _warnedNoPoints;
    private int _lastVerboseMeshFrame = -1;
    private float _lastHeartbeatTime;
    private Bounds _debugBounds;

    // mesh 构建用的可复用缓冲（避免每帧分配）
    private readonly List<Vector2> _pts = new List<Vector2>();
    private readonly List<float> _cum = new List<float>();

    // 全局注册表：profile × layer → Renderer（分组键，见 §3.1）
    private static readonly Dictionary<RendererKey, UITrailRenderer> Registry =
        new Dictionary<RendererKey, UITrailRenderer>();

    // ── 生命周期 ──────────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false; // 拖尾不接收射线，否则光带会吞掉点击（§3.3）
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        // 【必须关掉 cullTransparentMesh】AddComponent 出来的 CanvasRenderer 该项默认为 true。
        // Graphic.Rebuild() 的第一行就是 `if (canvasRenderer == null || canvasRenderer.cull) return;`——
        // 本 Graphic 的初始 mesh 是空的（还没有采样点），原生 Canvas 据此把它标记为 cull，
        // 之后 Rebuild 直接早退，OnPopulateMesh 永不执行，cull 也就永远没机会被清掉：死锁。
        // 症状与"SetVerticesDirty 被吞"完全一样——没有拖尾，且一条 mesh 日志都没有。
        if (canvasRenderer != null)
        {
            canvasRenderer.cullTransparentMesh = false;
            canvasRenderer.cull = false;
        }
        else
        {
            // 组件缺失是致命的：Rebuild 每帧早退。LateUpdate 里会自愈，这里先出声。
            Debug.LogError(
                $"[UITrail] Renderer '{name}' 上没有 CanvasRenderer，Graphic.Rebuild() 将每帧早退，拖尾不可见。", this);
        }

        ApplyMaterial();
    }

    protected override void OnDestroy()
    {
        RendererKey stale = null;
        foreach (KeyValuePair<RendererKey, UITrailRenderer> kv in Registry)
        {
            if (kv.Value == this) { stale = kv.Key; break; }
        }
        if (stale != null) Registry.Remove(stale);

        if (_runtimeMaterial != null)
        {
            if (Application.isPlaying) Destroy(_runtimeMaterial);
            else DestroyImmediate(_runtimeMaterial);
            _runtimeMaterial = null;
        }

        base.OnDestroy();
    }

    private void LateUpdate()
    {
        // 防御性清理：Emitter 可能被外部 Destroy 而未调用 OnDisable。
        for (int i = _emitters.Count - 1; i >= 0; i--)
        {
            if (_emitters[i] == null) _emitters.RemoveAt(i);
        }

        bool has = _emitters.Count > 0;
        if (!has && !_hadGeometry)
        {
            _hadGeometry = false;
            LogHeartbeat("提前返回（无 Emitter 且上帧无几何）");
            return;
        }
        _hadGeometry = has;

        // 兜底：Graphic.IsActive() 要求内部 m_Canvas 已缓存，否则 SetVerticesDirty() 会被静默丢弃。
        // 读一次 canvas 属性可触发 Graphic 的惰性 CacheCanvas，修复重建域丢失的情况。
        if (!IsActive() && canvas != null) SetAllDirty();

        // 兜底：CanvasRenderer 缺失时 Graphic.Rebuild() 每帧早退，拖尾静默失效且不报错。
        // 正常路径由 GetOrCreate 显式添加，这里只覆盖手工挂载/预制体等其他来源。
        if (canvasRenderer == null)
        {
            gameObject.AddComponent<CanvasRenderer>();
            Debug.LogWarning(
                $"[UITrail] Renderer '{name}' 缺少 CanvasRenderer，已自动补上。" +
                $"缺失时 Graphic.Rebuild() 会每帧早退，OnPopulateMesh 永不执行。", this);
            SetAllDirty();
            return;
        }

        // 兜底：cull 一旦被置上，Graphic.Rebuild() 会在第一行早退，OnPopulateMesh 再也不跑（自锁）。
        if (canvasRenderer.cull) canvasRenderer.cull = false;

        // 调试开关改变时立即换材质（每帧一次比较，开销可忽略）。
        if (DebugSolidMode != _lastDebugSolidMode) RefreshAll();

        SetVerticesDirty(); // 由 OnPopulateMesh 统一填充（canvases willRender 阶段）
        LogHeartbeat("已调用 SetVerticesDirty");

        if (!_loggedFirstRibbon && !_warnedInactive && !IsActive())
        {
            _warnedInactive = true;
            Debug.LogWarning(
                $"[UITrail] Renderer '{name}' 处于非激活渲染态（canvas={(canvas != null ? canvas.name : "null")}），" +
                $"mesh 不会重建，拖尾不可见。", this);
        }
    }

    /// <summary>
    /// Renderer 侧心跳日志（节流 0.25 秒）。它与 [UITrail·mesh] 成对使用：
    /// 只有心跳没有 mesh 日志 → SetVerticesDirty 被吞了 / rebuild 没发生（查 IsActive、canvas、rect）；
    /// 两者都有 → OnPopulateMesh 确实在跑，问题在顶点或可见性。
    /// </summary>
    private void LogHeartbeat(string what)
    {
        if (!VerboseLogging) return;

        float now = Time.unscaledTime;
        if (now - _lastHeartbeatTime < 0.25f) return;
        _lastHeartbeatTime = now;

        Debug.Log(
            $"[UITrail·心跳] '{name}' {what} | emitters={_emitters.Count} " +
            $"IsActive={IsActive()} activeInHierarchy={gameObject.activeInHierarchy} enabled={enabled} " +
            $"canvas={(canvas != null ? canvas.name : "null")} " +
            $"rect={rectTransform.rect} " +
            $"canvasRenderer={(canvasRenderer != null ? "有" : "无")} " +
            $"cullTransparentMesh={(canvasRenderer != null && canvasRenderer.cull ? "被剔除!" : "正常")}", this);
    }

    // ── 对外 API ──────────────────────────────────────────────────────
    public void Register(UITrail emitter)
    {
        if (emitter == null || _emitters.Contains(emitter)) return;
        _emitters.Add(emitter);
        SetVerticesDirty();
    }

    public void Unregister(UITrail emitter)
    {
        if (_emitters.Remove(emitter))
            SetVerticesDirty();
    }

    /// <summary>立即标记 mesh 脏（Emitter.Clear() 时调用，让光带当帧消失）。</summary>
    public void MarkDirty()
    {
        if (isActiveAndEnabled) SetVerticesDirty();
    }

    /// <summary>诊断：打印当前所有 Renderer 与其 Emitter 数（定位"Renderer 是否创建/注册"）。</summary>
    public static void DebugDump()    {
        if (Registry.Count == 0)
        {
            Debug.Log("[UITrail] 当前没有任何 Renderer 节点（说明 Emitter 从未成功绑定，或场景已切换）。");
            return;
        }
        foreach (KeyValuePair<RendererKey, UITrailRenderer> kv in Registry)
        {
            UITrailRenderer r = kv.Value;
            Debug.Log($"[UITrail] Renderer '{(r != null ? r.name : "null")}' profile={(kv.Key.Profile != null ? kv.Key.Profile.name : "null")} layer={kv.Key.Layer} emitters={(r != null ? r._emitters.Count : -1)}");
        }
    }

    /// <summary>
    /// 把 DebugSolidMode 的当前值应用到所有活跃 Renderer（切换开关后立即生效，无需重进 Play）。
    /// </summary>
    public static void RefreshAll()
    {
        _lastDebugSolidMode = DebugSolidMode;
        foreach (KeyValuePair<RendererKey, UITrailRenderer> kv in Registry)
        {
            UITrailRenderer r = kv.Value;
            if (r == null) continue;
            r.ApplyMaterial();
            r.SetAllDirty();
        }
    }

    /// <summary>查找或创建指定 profile × layer 的共享 Renderer 节点。</summary>
    public static UITrailRenderer GetOrCreate(UITrailProfile profile, UITrailLayer layer, UITrail emitter)
    {
        if (profile == null) return null;

        RendererKey key = new RendererKey(profile, layer);
        if (Registry.TryGetValue(key, out UITrailRenderer existing) && existing != null)
            return existing;

        Canvas hostCanvas = emitter != null ? emitter.GetComponentInParent<Canvas>() : null;
        Transform parent = hostCanvas != null ? hostCanvas.rootCanvas.transform : null;
        if (parent == null)
        {
            Debug.LogError("[UITrail] 找不到挂载 Canvas，无法创建拖尾 Renderer。请把 UITrail 挂到 Canvas 下的 UI 元素上。");
            return null;
        }

        // 【必须先建成 inactive 再挂父子】
        // new GameObject(name, types) 创建出的对象是激活的，Graphic.OnEnable() 会在那一行就跑完——
        // 此时对象还在场景根上，缓存到的 canvas 是错的。随后 SetParent 触发 OnTransformParentChanged，
        // 而 UGUI 该函数先 `m_Canvas = null` 再 `if (!IsActive()) return`，Graphic.IsActive() 又要求
        // m_Canvas != null → 直接早退，既不重新 CacheCanvas 也不 SetAllDirty。此后每次 SetVerticesDirty()
        // 都被 IsActive() 静默丢弃，OnPopulateMesh 永不执行（现象：没有拖尾，且一条日志都没有）。
        GameObject go = new GameObject($"__UITrailLayer_{profile.name}_{layer}");
        go.SetActive(false);

        RectTransform rt = go.AddComponent<RectTransform>();
        go.layer = parent.gameObject.layer; // 与宿主 Canvas 同层（Screen Space Camera / WorldSpace 下 culling 需要）
        go.transform.SetParent(parent, false);
        // sibling 顺序统一由激活后的 ApplySorting 负责，此处不设。

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        // 【必须显式加 CanvasRenderer】不要指望 Graphic 上的 [RequireComponent(typeof(CanvasRenderer))]
        // 隐式补齐——实测本工程（Unity 2022.3）走 AddComponent 建 Graphic 子类时它没被加上。
        // 而 Graphic.Rebuild() 的第一行就是：
        //     if (canvasRenderer == null || canvasRenderer.cull) return;
        // 组件缺失 → 每帧无条件早退 → OnPopulateMesh 永不执行 → 一条 mesh 日志、一个像素都没有，
        // 且不报任何错。症状与"SetVerticesDirty 被吞"完全一样，极难区分，只能靠打 canvasRenderer 是否为 null。
        CanvasRenderer cr = go.AddComponent<CanvasRenderer>();
        cr.cullTransparentMesh = false; // 初始 mesh 为空，留 true 会被原生 Canvas 标记 cull

        // 独立 Canvas：overrideSorting 隔离出主 Canvas 的重建域（§3.3）。
        // 渲染模式继承宿主 Canvas（Overlay/Camera/WorldSpace 各自成立，不写死 Overlay）。
        // 注意：排序属性此刻不设——见下方 ApplySorting 的说明。
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = hostCanvas.renderMode;
        if (hostCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            canvas.worldCamera = hostCanvas.worldCamera;
        // 注意：不挂 GraphicRaycaster，拖尾不参与射线（§3.3）。

        UITrailRenderer renderer = go.AddComponent<UITrailRenderer>();
        renderer.profile = profile;
        renderer.layer = layer;

        // 层级、Canvas、profile 全部就位后才激活：OnEnable 此刻缓存到正确的 canvas，
        // 并在 ApplyMaterial 里拿得到 profile。
        go.SetActive(true);

        // 【必须在激活之后设排序】Canvas 是原生组件，overrideSorting / sortingOrder 在对象
        // inactive 时写入不会落地——Canvas 在 OnEnable 里才建立自己的排序状态，之前的赋值被丢弃。
        // 症状：日志打出 sortingOrder=0（而非设定值），节点退化成按 hierarchy 顺序渲染。
        renderer.ApplySorting();

        Registry[key] = renderer;
        Debug.Log(
            $"[UITrail] 已创建 Renderer '{go.name}'：parent={parent.name} renderMode={canvas.renderMode} " +
            $"overrideSorting={canvas.overrideSorting} sortingOrder={canvas.sortingOrder} " +
            $"canvasCached={(renderer.canvas != null ? renderer.canvas.name : "null")} " +
            $"canvasRenderer={(renderer.canvasRenderer != null ? "有" : "无（Rebuild 会早退，拖尾必然不可见！）")} " +
            $"isActive={renderer.IsActive()} material={(renderer.materialForRendering != null ? renderer.materialForRendering.shader.name : "null")}", renderer);
        return renderer;
    }

    /// <summary>
    /// 应用层级排序。必须在 GameObject 激活之后调用（Canvas 原生组件在 inactive 时不接受排序赋值）。
    /// Below：-1，排在根 Canvas 全部内容之前（被业务 UI 遮挡）。
    /// Above：足够大的 order，压过根 Canvas 下所有普通内容与常见嵌套 Canvas。
    /// </summary>
    public void ApplySorting()
    {
        Canvas c = GetComponent<Canvas>();
        if (c == null) return;

        c.overrideSorting = true;
        c.sortingOrder = layer == UITrailLayer.Below ? -1 : AboveSortingOrder;

        if (layer == UITrailLayer.Below) transform.SetAsFirstSibling();
        else transform.SetAsLastSibling();
    }

    /// <summary>按 profile 配置创建/切换材质，并把动态参数推入材质。</summary>
    public void ApplyMaterial()
    {
        if (profile == null)
        {
            material = null;
            return;
        }

        // 纯色调试模式：默认 UI 材质（保证一定能画出来），贴图走 mainTexture 的白图分支。
        if (DebugSolidMode)
        {
            material = Canvas.GetDefaultCanvasMaterial();
            return;
        }

        // 高级用法：profile 提供了自定义材质则原样使用（不再改参数）。
        if (profile.material != null)
        {
            if (profile.material.shader == null || profile.material.shader.name != UITrailProfile.DefaultShaderName)
                Debug.LogWarning(
                    $"[UITrail] profile 材质 '{profile.material.name}' 的 Shader 为 " +
                    $"'{(profile.material.shader != null ? profile.material.shader.name : "null")}'，" +
                    $"期望 '{UITrailProfile.DefaultShaderName}'。若该 Shader 编译失败，拖尾将不可见。", this);
            material = profile.material;
            return;
        }

        if (_runtimeMaterial == null)
        {
            Shader shader = Shader.Find(UITrailProfile.DefaultShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[UITrail] 找不到 Shader '{UITrailProfile.DefaultShaderName}'，回退到默认 UI 材质。");
                material = Canvas.GetDefaultCanvasMaterial();
                return;
            }
            _runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        _runtimeMaterial.mainTexture = profile.texture != null ? profile.texture : Texture2D.whiteTexture;
        _runtimeMaterial.SetFloat("_BreathSpeed", profile.breathSpeed);
        _runtimeMaterial.SetFloat("_BreathAmount", profile.animate ? profile.breathAmount : 0f);
        _runtimeMaterial.SetFloat("_FlowSpeed", profile.flowSpeed);
        _runtimeMaterial.SetFloat("_FlowStrength", profile.animate ? profile.flowStrength : 0f);

        material = _runtimeMaterial;
    }

    /// <summary>让 CanvasRenderer 绑定 profile.texture（[PerRendererData] _MainTex 走此路径）。</summary>
    public override Texture mainTexture
    {
        get
        {
            if (DebugSolidMode) return Texture2D.whiteTexture;
            return profile != null && profile.texture != null ? profile.texture : base.mainTexture;
        }
    }

    // ── mesh 生成（§5）───────────────────────────────────────────────
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        _debugBounds = default; // 每次重建重置，避免读到上一帧的残留
        if (profile == null || profile.widthCurve == null || profile.colorGradient == null)
        {
            if (VerboseLogging)
                Debug.LogWarning(
                    $"[UITrail·mesh] '{name}' 因 profile 字段为空提前返回：" +
                    $"profile={(profile != null ? "有" : "null")} " +
                    $"widthCurve={(profile != null && profile.widthCurve != null ? "有" : "null")} " +
                    $"colorGradient={(profile != null && profile.colorGradient != null ? "有" : "null")}", this);
            return;
        }

        Color layerColor = color; // Graphic.color（默认白，便于整体调暗）
        for (int e = 0; e < _emitters.Count; e++)
        {
            UITrail emitter = _emitters[e];
            if (emitter != null)
                AppendRibbon(vh, emitter, layerColor);
        }

        // 诊断：首次真正生成 ribbon 时打印一次，便于定位"到底有没有 mesh"。
        if (!_loggedFirstRibbon && vh.currentVertCount > 0)
        {
            _loggedFirstRibbon = true;
            Debug.Log($"[UITrail] Renderer '{name}' 首次生成 ribbon：{vh.currentVertCount} 顶点 / {_emitters.Count} 个 Emitter / shader={(materialForRendering != null ? materialForRendering.shader.name : "null")}。", this);
        }

        // 诊断：有 Emitter 却一个顶点都没有 → 采样点不足（ribbon 需要 ≥2 点）。
        // 最常见原因是 minSampleDistance 相对实际位移过大，一帧都跨不过门槛。
        if (!_warnedNoPoints && !_loggedFirstRibbon && _emitters.Count > 0 && vh.currentVertCount == 0)
        {
            _warnedNoPoints = true;
            int maxPts = 0;
            for (int i = 0; i < _emitters.Count; i++)
                if (_emitters[i] != null) maxPts = Mathf.Max(maxPts, _emitters[i].PointCount);
            Debug.LogWarning(
                $"[UITrail] Renderer '{name}' 有 {_emitters.Count} 个 Emitter 但 0 顶点，" +
                $"最多的一条只有 {maxPts} 个采样点（ribbon 需要 ≥2）。" +
                $"通常是 profile.minSampleDistance({profile.minSampleDistance}) 相对实际位移过大。", this);
        }

        // 详细日志：每次 mesh 重建都汇报，**包括 0 顶点的情况**——
        // "OnPopulateMesh 没跑"与"跑了但产出 0 顶点"是两个完全不同的故障，必须能区分。
        if (VerboseLogging && Time.frameCount != _lastVerboseMeshFrame)
        {
            _lastVerboseMeshFrame = Time.frameCount;
            Canvas c = GetComponent<Canvas>();
            Rect r = rectTransform.rect;

            string emitterDetail = "";
            for (int i = 0; i < _emitters.Count; i++)
            {
                UITrail em = _emitters[i];
                emitterDetail += em != null
                    ? $"[{em.gameObject.name}:{em.PointCount}点]"
                    : "[null]";
            }
            if (_emitters.Count == 0) emitterDetail = "（无 Emitter 注册！）";

            Debug.Log(
                $"[UITrail·mesh] '{name}' 顶点={vh.currentVertCount} emitters={_emitters.Count}{emitterDetail} " +
                $"| color={color} crAlpha={(canvasRenderer != null ? canvasRenderer.GetAlpha().ToString() : "无CanvasRenderer!")} " +
                $"| overrideSorting={(c != null ? c.overrideSorting.ToString() : "-")} " +
                $"sortingOrder={(c != null ? c.sortingOrder.ToString() : "-")} " +
                $"sibling={transform.GetSiblingIndex()}/{(transform.parent != null ? transform.parent.childCount : 0)} " +
                $"| rect={r} lossyScale={transform.lossyScale} " +
                $"| shader={(materialForRendering != null ? materialForRendering.shader.name : "null")} " +
                $"tex={(mainTexture != null ? mainTexture.name : "null")} " +
                $"| 顶点包围盒={_debugBounds}", this);
        }
    }

    private void AppendRibbon(VertexHelper vh, UITrail emitter, Color layerColor)
    {
        int n = emitter.PointCount;
        if (n < 2) return;

        // 1) 收集点 + 累积实际距离（U 必须用距离归一化，不能用点序号，见 §5.2）
        _pts.Clear();
        _cum.Clear();
        _pts.Add(emitter.GetPointLocal(0));
        _cum.Add(0f);
        for (int i = 1; i < n; i++)
        {
            Vector2 p = emitter.GetPointLocal(i);
            _pts.Add(p);
            _cum.Add(_cum[i - 1] + Vector2.Distance(p, _pts[i - 1]));
        }
        float totalDist = _cum[n - 1];

        int baseIndex = vh.currentVertCount;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = _pts[i];

            // 切线：中间点中心差分，首尾点单边差分（§5.1）
            Vector2 prev = _pts[Mathf.Max(i - 1, 0)];
            Vector2 next = _pts[Mathf.Min(i + 1, n - 1)];
            Vector2 tangent = next - prev;
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector2.right;
            else tangent.Normalize();

            Vector2 normal = new Vector2(-tangent.y, tangent.x);

            float norm = n > 1 ? (float)i / (n - 1) : 0f; // 0=尾端最旧，1=头端最新
            float halfWidth = profile.widthCurve.Evaluate(norm) * 0.5f * TurnFactor(i, n);

            Color c = profile.colorGradient.Evaluate(norm) * emitter.tint * layerColor;

            // 纯色调试：固定 24px 宽 + 不透明品红，绕开 widthCurve / colorGradient / tint 的一切可能。
            if (DebugSolidMode)
            {
                halfWidth = 12f;
                c = new Color(1f, 0f, 1f, 1f);
            }

            Color32 c32 = c;

            float u = totalDist > 1e-4f ? _cum[i] / totalDist : 0f;

            Vector3 left = new Vector3(p.x + normal.x * halfWidth, p.y + normal.y * halfWidth, 0f);
            Vector3 right = new Vector3(p.x - normal.x * halfWidth, p.y - normal.y * halfWidth, 0f);

            // 诊断：累计顶点包围盒。mesh 落在 Renderer rect 之外（或坐标量级离谱）时一眼可见。
            if (VerboseLogging)
            {
                if (vh.currentVertCount == 0 && i == 0) _debugBounds = new Bounds(left, Vector3.zero);
                else { _debugBounds.Encapsulate(left); _debugBounds.Encapsulate(right); }
            }

            vh.AddVert(left, c32, new Vector2(u, 0f));
            vh.AddVert(right, c32, new Vector2(u, 1f));
        }

        for (int i = 0; i < n - 1; i++)
        {
            int a = baseIndex + i * 2;
            vh.AddTriangle(a, a + 1, a + 2);
            vh.AddTriangle(a + 1, a + 3, a + 2);
        }
    }

    /// <summary>急转弯自交缓解（§5.3）：按相邻段夹角衰减该点宽度，夹角越锐宽度越窄。</summary>
    private float TurnFactor(int i, int n)
    {
        if (i <= 0 || i >= n - 1) return 1f;

        Vector2 inSeg = _pts[i] - _pts[i - 1];
        Vector2 outSeg = _pts[i + 1] - _pts[i];
        if (inSeg.sqrMagnitude < 1e-8f || outSeg.sqrMagnitude < 1e-8f) return 1f;

        float dot = Vector2.Dot(inSeg.normalized, outSeg.normalized);
        float k = Mathf.Clamp01((dot + 1f) * 0.5f); // 0=180°折返，1=直线
        return Mathf.Lerp(0.15f, 1f, k);
    }

    // ── 分组键（profile × layer）──────────────────────────────────────
    private sealed class RendererKey
    {
        public readonly UITrailProfile Profile;
        public readonly UITrailLayer Layer;

        public RendererKey(UITrailProfile profile, UITrailLayer layer)
        {
            Profile = profile;
            Layer = layer;
        }

        public override bool Equals(object obj)
        {
            return obj is RendererKey k && k.Profile == Profile && k.Layer == Layer;
        }

        public override int GetHashCode()
        {
            return (Profile != null ? Profile.GetHashCode() : 0) * 397 ^ (int)Layer;
        }
    }
}
