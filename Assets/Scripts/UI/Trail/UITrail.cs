using UnityEngine;

/// <summary>
/// UI 弧光拖尾 · 层枚举。
/// Overlay Canvas 严格按 hierarchy 顺序渲染，共享 Renderer 相对所有 UI 的前后关系是固定的。
/// Below = Renderer 节点排最前 + sortingOrder -1；Above = 排最后 + 大 sortingOrder。
/// </summary>
public enum UITrailLayer
{
    /// <summary>拖尾位于全部业务 UI 之下（被 UI 遮挡）。Renderer 节点 SetAsFirstSibling + sortingOrder -1。</summary>
    Below = 0,

    /// <summary>拖尾覆盖全部业务 UI。Renderer 节点 SetAsLastSibling + overrideSorting 大 sortingOrder。</summary>
    Above = 1,
}

/// <summary>
/// UI 弧光拖尾 · Emitter（用户挂载到要拖尾的 UI 元素上）。
///
/// 关键设计（见实施计划 §2）：拖尾 mesh 由常驻不动的 UITrailRenderer 在自身局部坐标系生成，
/// Emitter 只负责每帧采样自身位置 → 转换到 Renderer 坐标空间 → 提交采样点。
/// 这样尾巴留在运动路径上，而不是黏在图标屁股后面的一根棍子。
///
/// 时钟源（§8 D1）：采样节流与逐点老化一律使用 Time.unscaledTime / unscaledDeltaTime，
/// 刻意不接入 GameLoop.GameTime——接入会引入 Zenject 依赖，破坏"挂上即用"的通用性。
/// 代价：游戏暂停时拖尾仍会老化消散；若业务需要暂停冻结，请在暂停时置 emitting = false
/// （注意：这只停止新采样，已有点仍会淡出；需要完全冻结则调用 Clear()）。
///
/// 生命周期：OnEnable 自动查找/创建 Renderer 并 Clear()（覆盖对象池复用场景）；
/// OnDisable 从 Renderer 注销。瞬移/切页签/池复用后必须调用 Clear()，否则会拉出横穿屏幕的光带。
/// </summary>
[DisallowMultipleComponent]
public class UITrail : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("拖尾参数容器，同时是合批分组键（同 profile 共享一个 Renderer）。")]
    public UITrailProfile profile;

    [Tooltip("拖尾层。首版仅 Below 生效（见 D3）。")]
    public UITrailLayer layer = UITrailLayer.Below;

    [Header("运行状态")]
    [Tooltip("是否发射新采样点。关闭后停止新采样，已有点仍正常老化消散。")]
    public bool emitting = true;

    [Tooltip("叠加到 colorGradient 上的色调（走顶点色，同 profile 下每条尾巴可独立着色）。")]
    public Color tint = Color.white;

    // ── 采样点环形缓冲（禁止 List.RemoveAt(0)，见 §4.1）──
    private struct Sample
    {
        public Vector2 local; // Renderer 局部坐标
        public float time;    // Time.unscaledTime 采样时间戳
    }

    private UITrailRenderer _renderer;
    private Sample[] _ring;
    private int _start;   // 最旧点下标
    private int _count;   // 有效点数
    private Vector2 _lastLocal;
    private bool _hasLast;
    private float _lastSampleLogTime;

    private void OnEnable()
    {
        if (profile == null)
        {
            Debug.LogWarning($"[UITrail] '{gameObject.name}' 未配置 profile，拖尾不生效。", this);
            return;
        }

        // 通常对象已在 Canvas 下（场景/预制体激活），直接绑定；Instantiate 后 SetParent 的时序
        // 会让 OnEnable 先于挂载执行，此时 GetComponentInParent<Canvas>() 为 null，交给 LateUpdate 重试。
        if (GetComponentInParent<Canvas>() != null)
            TryBind();
    }

    private void OnDisable()
    {
        if (_renderer != null)
        {
            _renderer.Unregister(this);
            _renderer = null;
        }
    }

    private void LateUpdate()
    {
        if (profile == null) return;

        if (_renderer == null)
        {
            // 对象尚未挂到 Canvas 下时静默等待，挂载完成后绑定（本帧先绑定，下帧再采样，保证坐标基准就绪）。
            if (GetComponentInParent<Canvas>() == null) return;
            TryBind();
            return;
        }

        float now = Time.unscaledTime;
        AgePoints(now);
        if (emitting) SamplePoint(now);
    }

    private void TryBind()
    {
        if (_renderer != null) return;

        _renderer = UITrailRenderer.GetOrCreate(profile, layer, this);
        if (_renderer == null) return;

        _renderer.Register(this);
        Clear();

        if (UITrailRenderer.VerboseLogging)
            Debug.Log(
                $"[UITrail·绑定] '{gameObject.name}' 已绑定 Renderer '{_renderer.name}' " +
                $"（layer={layer}）。对象池复用会反复走这里。", this);
    }

    /// <summary>
    /// 立即清空全部采样点。瞬移 / 切页签 / 对象池取出复用后必须调用，
    /// 否则会从旧位置拉出一条横穿屏幕的光带。OnEnable 时已自动调用。
    /// </summary>
    public void Clear()
    {
        _count = 0;
        _start = 0;
        _hasLast = false;
        if (_renderer != null) _renderer.MarkDirty();
    }

    [ContextMenu("Debug/打印拖尾状态")]
    private void DebugPrintState()
    {
        Debug.Log(
            $"[UITrail] name={gameObject.name} active={gameObject.activeInHierarchy} enabled={enabled} " +
            $"profile={(profile != null ? profile.name : "null")} " +
            $"renderer={(_renderer != null ? _renderer.name : "null")} " +
            $"points={_count} emitting={emitting}", this);
        UITrailRenderer.DebugDump();
    }

    [ContextMenu("Debug/模拟一段弧线（测试渲染链路）")]
    public void DebugSimulateArc()
    {
        if (profile == null)
        {
            Debug.LogWarning("[UITrail] profile 为空，无法模拟。");
            return;
        }
        if (_renderer == null)
            TryBind();
        if (_renderer == null)
        {
            Debug.LogWarning("[UITrail] 未绑定 Renderer（对象可能不在 Canvas 下），无法模拟。");
            return;
        }

        EnsureCapacity();
        _count = 0;
        _start = 0;

        float now = Time.unscaledTime;
        Vector2 head = GetLocalPos();
        const int n = 24;
        // 尾端（最旧）→ 头端（最新）依次写入，匹配环形缓冲的时序约定
        for (int i = n - 1; i >= 0; i--)
        {
            float t = (float)i / (n - 1); // 0=头端, 1=尾端
            float age = t * profile.lifetime * 0.95f;
            Vector2 p = head + new Vector2(-t * 320f, Mathf.Sin(t * Mathf.PI) * 140f);
            Append(p, now - age);
        }

        _hasLast = true;
        _lastLocal = head;
        _renderer.MarkDirty();
        Debug.Log($"[UITrail] 已写入 {_count} 个模拟采样点。此时看得到弧线 → 问题在运行期采样；仍看不到 → 问题在渲染（shader/层级/Canvas）。");
    }

    // ── 供 Renderer 读取 ──────────────────────────────────────────────
    public int PointCount => _count;

    /// <summary>按序读取采样点（index 0 = 最旧/尾端）。</summary>
    public Vector2 GetPointLocal(int index)
    {
        int cap = _ring.Length;
        return _ring[(_start + index) % cap].local;
    }

    // ── 内部实现 ──────────────────────────────────────────────────────
    private void EnsureCapacity()
    {
        int cap = Mathf.Max(4, profile.maxPoints);
        if (_ring == null || _ring.Length != cap)
        {
            _ring = new Sample[cap];
            _start = 0;
            _count = 0;
            _hasLast = false;
        }
    }

    private void SamplePoint(float now)
    {
        EnsureCapacity();
        Vector2 local = GetLocalPos();

        if (!_hasLast)
        {
            // 首个点：静止物体只有单点，无 ribbon，属正常。
            Append(local, now);
            _hasLast = true;
            _lastLocal = local;
            LogSample(local, 0f, true);
            return;
        }

        float dist = (local - _lastLocal).magnitude;
        if (dist >= profile.minSampleDistance)
        {
            Append(local, now);
            _lastLocal = local;
            LogSample(local, dist, true);
        }
        else
        {
            LogSample(local, dist, false);
        }
    }

    /// <summary>
    /// 详细日志：每帧汇报本帧位移与是否越过采样门槛（节流到每 0.25 秒一条，避免刷屏）。
    /// 这是回答"元素到底动没动、动得够不够采样"的直接证据——
    /// 位移 &lt; minSampleDistance 时永远只有 1 个点，ribbon 需要 ≥2 个点，屏幕上必然什么都没有。
    /// </summary>
    private void LogSample(Vector2 local, float dist, bool appended)
    {
        if (!UITrailRenderer.VerboseLogging) return;

        float now = Time.unscaledTime;
        if (now - _lastSampleLogTime < 0.25f) return;
        _lastSampleLogTime = now;

        Vector3 world = transform.position;
        Debug.Log(
            $"[UITrail·采样] '{gameObject.name}' 本帧位移={dist:F2}（门槛 {profile.minSampleDistance}）" +
            $"{(appended ? " → 已采样" : " → 未达门槛，丢弃")} | 点数={_count}/{(_ring != null ? _ring.Length : 0)} " +
            $"| Renderer局部坐标={local} 世界坐标={world} " +
            $"| emitting={emitting} activeInHierarchy={gameObject.activeInHierarchy} " +
            $"| lossyScale={transform.lossyScale}", this);
    }

    private void Append(Vector2 local, float now)
    {
        int cap = _ring.Length;
        if (_count < cap)
        {
            _ring[(_start + _count) % cap] = new Sample { local = local, time = now };
            _count++;
        }
        else
        {
            // 环形缓冲写满：覆盖最旧点，头指针前移。
            _ring[_start] = new Sample { local = local, time = now };
            _start = (_start + 1) % cap;
        }
    }

    private void AgePoints(float now)
    {
        if (_count == 0) return;

        int cap = _ring.Length;
        float lifetime = profile.lifetime;
        int dropped = 0;
        while (dropped < _count && now - _ring[(_start + dropped) % cap].time > lifetime)
            dropped++;

        if (dropped > 0)
        {
            _start = (_start + dropped) % cap;
            _count -= dropped;
        }
    }

    private Vector2 GetLocalPos()
    {
        if (_renderer == null) return Vector2.zero;

        // 采样 UI 元素的视觉中心（rect.center），比 pivot 更稳（pivot 可偏离中心）。
        Vector3 world;
        RectTransform rt = transform as RectTransform;
        if (rt != null)
        {
            world = rt.TransformPoint(rt.rect.center);
        }
        else
        {
            world = transform.position;
        }

        // §4.2：世界坐标 → Renderer 局部坐标。
        // 假设：Overlay Canvas 下世界坐标即屏幕空间尺度，InverseTransformPoint 稳定。
        // 若后续 Canvas 改 Screen Space - Camera，需改走 RectTransformUtility.WorldToScreenPoint
        // + ScreenPointToLocalPointInRectangle 两段式。
        Vector3 local = _renderer.rectTransform.InverseTransformPoint(world);
        return new Vector2(local.x, local.y);
    }
}
