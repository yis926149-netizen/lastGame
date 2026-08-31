using System.Collections.Generic;
using TMPro;
using UnityEngine;
using DG.Tweening;

//****************************************
// 功能说明：伤害飘字渲染器（屏幕空间方案）。
//   订阅 DamageEventBroker.DamageDealt，在受击者头顶世界坐标对应的屏幕位置生成飘字。
//   方案对齐 CostLabelRenderer：Screen Space - Overlay Canvas + WorldToScreenPoint +
//   ScreenPointToLocalPointInRectangle，文字大小恒定、始终显示在最上层、全部实例合批到同一 Canvas。
//   实例走对象池：上飘 + 淡出动画结束后回收（SetActive(false) 只在动画完成后执行，避免杀 DOTween）。
//   所有伤害结算均显示（含迷雾区敌打敌），不做可见性过滤。
//****************************************

public class DamageFloatTextRenderer : MonoBehaviour
{
    private GameObject _prefab;
    private Canvas _parentCanvas;
    private RectTransform _containerRect;
    private Camera _camera;
    private DamageEventBroker _broker;

    private readonly Stack<GameObject> _pool = new Stack<GameObject>();
    private readonly List<GameObject> _active = new List<GameObject>();

    // ── 飘字表现参数（默认值即可用；组件运行时由代码创建，如需微调可改为预制体挂载）──
    [SerializeField, Tooltip("上飘距离（像素）")] private float _riseDistance = 60f;
    [SerializeField, Tooltip("飘字总时长（秒）")] private float _duration = 0.9f;
    [SerializeField, Tooltip("水平随机偏移幅度（像素），避免同点连击数字完全重叠")] private float _jitterX = 24f;
    [SerializeField, Tooltip("垂直随机偏移幅度（像素），避免同点连击数字完全重叠")] private float _jitterY = 18f;
    [SerializeField, Tooltip("飘字最小字号（伤害趋近 0 时的字号）")] private float _fontSizeMin = 18f;
    [SerializeField, Tooltip("飘字最大字号（伤害达到 _fontScaleMaxDamage 及以上的字号）")] private float _fontSizeMax = 36f;
    [SerializeField, Tooltip("字号达到上限所需的伤害值；低于它按线性映射到 [_fontSizeMin, _fontSizeMax]")] private float _fontScaleMaxDamage = 30f;
    [SerializeField, Tooltip("暴击字体放大倍数（当前公式无暴击，预留）")] private float _critFontScale = 1.4f;
    [SerializeField, Tooltip("我方打出的伤害（敌方受击）颜色")] private Color _outgoingColor = Color.white;
    [SerializeField, Tooltip("我方受击（敌人打我）颜色")] private Color _incomingColor = Color.red;
    [SerializeField] private Color _critColor = new Color(1f, 0.42f, 0.2f);

    public void Initialize(GameObject prefab, Canvas parentCanvas, DamageEventBroker broker)
    {
        _prefab = prefab;
        _parentCanvas = parentCanvas;
        _broker = broker;

        Transform container = parentCanvas != null ? parentCanvas.transform.Find("DamageFloatTextContainer") : null;
        if (container == null && parentCanvas != null)
        {
            var go = new GameObject("DamageFloatTextContainer", typeof(RectTransform));
            go.transform.SetParent(parentCanvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.transform.SetAsLastSibling(); // 飘字绘制在大多数 HUD 之上
            container = go.transform;
        }
        _containerRect = container != null ? container.GetComponent<RectTransform>() : null;

        if (_broker != null)
            _broker.DamageDealt += OnDamageDealt;
    }

    private void OnDestroy()
    {
        if (_broker != null)
            _broker.DamageDealt -= OnDamageDealt;

        foreach (var go in _active)
        {
            if (go == null) continue;
            DOTween.Kill(go);
            Destroy(go);
        }
        while (_pool.Count > 0)
        {
            var go = _pool.Pop();
            if (go == null) continue;
            DOTween.Kill(go);
            Destroy(go);
        }
    }

    private void OnDamageDealt(Vector3 worldPos, float damage, bool isCrit, int targetFaction)
    {
        if (damage <= 0f || _prefab == null || _parentCanvas == null || _containerRect == null) return;

        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;

        Vector3 screenPos = _camera.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0) return; // 受击点在相机背后：不出字

        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _containerRect, screenPos,
            _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _camera,
            out localPos);
        localPos.x += Random.Range(-_jitterX, _jitterX);
        localPos.y += Random.Range(-_jitterY, _jitterY);

        var instance = GetOrCreateInstance();
        var rt = instance.GetComponent<RectTransform>();
        var text = instance.GetComponent<TextMeshProUGUI>();
        if (rt == null || text == null)
        {
            Debug.LogWarning("[DamageFloatTextRenderer] FloatingText 预制体缺少 RectTransform/TextMeshProUGUI，跳过本次飘字。");
            instance.SetActive(false);
            _pool.Push(instance);
            return;
        }

        text.text = Mathf.RoundToInt(damage).ToString();
        // 我方受击（受击者阵营 0）显示红色，我方打出的伤害（敌方/中立受击）显示白色；暴击色覆盖两者
        text.color = isCrit ? _critColor : (targetFaction == 0 ? _incomingColor : _outgoingColor);
        // 伤害越高字号越大：线性映射到 [_fontSizeMin, _fontSizeMax]；暴击额外放大
        float sizeT = Mathf.Clamp01(damage / Mathf.Max(0.0001f, _fontScaleMaxDamage));
        float size = Mathf.Lerp(_fontSizeMin, _fontSizeMax, sizeT);
        if (isCrit) size *= _critFontScale;
        text.fontSize = size;
        text.raycastTarget = false; // 飘字不可点击，且避免额外合批消耗

        rt.localScale = Vector3.one;
        rt.anchoredPosition = localPos;

        var cg = instance.GetComponent<CanvasGroup>();
        if (cg == null) cg = instance.AddComponent<CanvasGroup>();
        cg.alpha = 1f;

        _active.Add(instance);

        var seq = DOTween.Sequence();
        seq.SetLink(instance); // 实例被销毁时自动杀动画
        seq.Append(rt.DOAnchorPosY(localPos.y + _riseDistance, _duration).SetEase(Ease.OutQuad));
        seq.Join(cg.DOFade(0f, _duration).SetEase(Ease.InQuad).SetDelay(_duration * 0.35f));
        if (isCrit)
            seq.Join(rt.DOPunchScale(new Vector3(0.25f, 0.25f, 0f), _duration * 0.4f, 4, 0.4f));
        seq.OnComplete(() => Recycle(instance));
    }

    private GameObject GetOrCreateInstance()
    {
        GameObject go;
        if (_pool.Count > 0)
        {
            go = _pool.Pop();
            go.SetActive(true);
            return go;
        }

        go = Instantiate(_prefab, _containerRect);
        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
        return go;
    }

    private void Recycle(GameObject instance)
    {
        if (instance == null) return;
        _active.Remove(instance);
        instance.SetActive(false); // 动画已完成，此时停用不会杀到活跃 tween
        _pool.Push(instance);
    }
}
