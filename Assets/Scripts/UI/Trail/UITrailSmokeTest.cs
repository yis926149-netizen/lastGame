using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 弧光拖尾 · 运行时冒烟测试（诊断专用，不参与正式表现）。
///
/// 用途：把拖尾链路从业务逻辑（探索 → 奖励广播 → 飞币 Presenter → 对象池）里完全剥离出来，
/// 造一个自带运动的 UI 元素挂上 UITrail，直接回答"这套东西到底能不能出图"。
///
/// 启动方式二选一：
///   1) 菜单 Tools/UI拖尾/运行时冒烟测试（Play 模式下点一次）；
///   2) 手动把本组件挂到任意 Canvas 下的 GameObject 上。
///
/// 每帧沿圆周运动，位移远大于 profile.minSampleDistance，因此必定产生采样点。
/// 看得到光带 = 渲染链路正常，问题在业务侧（飞币没激活 / 位移不够 / 层级被遮）。
/// 看不到 = 渲染链路本身有问题，看 Console 里的 [UITrail] 诊断日志。
/// </summary>
[DisallowMultipleComponent]
public class UITrailSmokeTest : MonoBehaviour
{
    [Tooltip("圆周运动半径（画布单位）。必须远大于 profile.minSampleDistance 才会产生采样点。")]
    public float radius = 220f;

    [Tooltip("圆周运动角速度（弧度/秒）。")]
    public float angularSpeed = 3f;

    private RectTransform _rect;
    private Vector2 _center;
    private float _angle;
    private float _elapsed;
    private UITrail _trail;

    /// <summary>在指定 Canvas 下创建一个自走的测试图标（含 Image + UITrail + 本组件）。</summary>
    public static UITrailSmokeTest Spawn(Canvas canvas, UITrailProfile profile, UITrailLayer layer)
    {
        if (canvas == null)
        {
            Debug.LogError("[UITrail·冒烟] 没有可用的 Canvas，无法创建测试对象。");
            return null;
        }
        if (profile == null)
        {
            Debug.LogError("[UITrail·冒烟] profile 为空。请先执行菜单 Tools/UI拖尾/生成占位贴图与默认配置。");
            return null;
        }

        // 与 UITrailRenderer.GetOrCreate 同样的顺序要求：先 inactive → 挂父子 → 配参数 → 最后激活，
        // 否则 Graphic/UITrail 的 OnEnable 会在错误的层级下运行。
        GameObject go = new GameObject("__UITrailSmokeTest");
        go.SetActive(false);
        go.layer = canvas.gameObject.layer;

        RectTransform rt = go.AddComponent<RectTransform>();
        go.transform.SetParent(canvas.transform, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(48f, 48f);
        rt.anchoredPosition = Vector2.zero;

        Image img = go.AddComponent<Image>();
        img.color = new Color(1f, 0.3f, 0.3f, 1f); // 醒目红点：先确认这个点本身可见
        img.raycastTarget = false;

        UITrail trail = go.AddComponent<UITrail>();
        trail.profile = profile;
        trail.layer = layer;
        trail.emitting = true;
        trail.tint = Color.white;

        UITrailSmokeTest test = go.AddComponent<UITrailSmokeTest>();
        go.SetActive(true);

        Debug.Log(
            $"[UITrail·冒烟] 已在 Canvas '{canvas.name}'（renderMode={canvas.renderMode}）下创建测试对象。" +
            $"屏幕中央应出现一个绕圈的红点，其后拖出光带。" +
            $"红点都看不到 → Canvas/分辨率问题；只有红点没光带 → 看后续 [UITrail] 日志。", test);

        return test;
    }

    private void Awake()
    {
        _rect = transform as RectTransform;
        _trail = GetComponent<UITrail>();
        if (_rect != null) _center = _rect.anchoredPosition;
    }

    private void Update()
    {
        if (_rect == null) return;

        // 刻意用 unscaledDeltaTime：与 UITrail 的时钟源一致，暂停时也能测。
        _angle += angularSpeed * Time.unscaledDeltaTime;
        _rect.anchoredPosition = _center + new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle)) * radius;

        // 跑满 2 秒后汇报一次实际采样点数——这是区分"没采到点"和"采到了但没画出来"的关键证据。
        if (_elapsed >= 0f)
        {
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed >= 2f)
            {
                _elapsed = -1f;
                int pts = _trail != null ? _trail.PointCount : -1;
                Debug.Log(
                    $"[UITrail·冒烟] 运行 2 秒后采样点数 = {pts}。" +
                    (pts >= 2
                        ? "采样正常。若仍看不到光带，问题在渲染（材质/贴图/层级/blend）。"
                        : "采样异常：一个 ribbon 至少需要 2 个点，检查 profile.minSampleDistance 与 lifetime。"),
                    this);
            }
        }
    }
}
