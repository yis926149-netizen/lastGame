using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using DG.Tweening;

//****************************************
//创建人：易生
//功能说明：可复用的“入场动画”播放器（集中列表管理）。
//          一个常驻激活节点挂一个本组件，在 effects 列表里配置多个目标（每个条目可独立选择动画类型与参数），
//          通过 Play(index)/PlayAll()/Stop(index)/StopAll() 触发。
//          支持三种动画类型：
//          · Scale（缩放入场）：激活后先放大 n 倍再缩回原始大小；
//          · Fade（淡入恢复）：一组节点（含全部子物体）开始时完全透明，随后做恢复动画，m 秒后恢复正常；
//          · Position（位移恢复）：一组节点开始时处于 原始位置+偏移，随后做恢复位移动画，m 秒后恢复正常位置。
//****************************************

public class ScaleInEffectPlayer : MonoBehaviour
{
    [Tooltip("要播放的入场动画列表，每个条目可独立选择动画类型并配置对应参数")]
    public List<ScaleInEffectConfig> effects = new List<ScaleInEffectConfig>();

    private Dictionary<ScaleInEffectConfig, EffectRuntime> _runtimes;

    private Dictionary<ScaleInEffectConfig, EffectRuntime> Runtimes
    {
        get
        {
            if (_runtimes == null)
                _runtimes = new Dictionary<ScaleInEffectConfig, EffectRuntime>();
            return _runtimes;
        }
    }

    /// <summary>播放第 index 个条目的入场动画。</summary>
    public void Play(int index)
    {
        if (index < 0 || index >= effects.Count)
        {
            Debug.LogWarning($"[ScaleInEffectPlayer] Play({index}) 越界，effects 数量为 {effects.Count}。", this);
            return;
        }

        var cfg = effects[index];
        Debug.Log($"[ScaleInEffectPlayer] Play({index}) → {Describe(cfg)}", this);
        Play(cfg);
    }

    /// <summary>同时播放指定索引的多个条目。</summary>
    public void Play(params int[] indices)
    {
        if (indices == null) return;
        foreach (var i in indices) Play(i);
    }

    /// <summary>依次播放所有条目的入场动画。</summary>
    public void PlayAll()
    {
        foreach (var cfg in effects)
            if (cfg != null) Play(cfg);
    }

    /// <summary>停止第 index 个条目（取消延迟与补间，并复位到原始状态）。</summary>
    public void Stop(int index)
    {
        if (index < 0 || index >= effects.Count) return;
        Stop(effects[index]);
    }

    /// <summary>同时停止指定索引的多个条目。</summary>
    public void Stop(params int[] indices)
    {
        if (indices == null) return;
        foreach (var i in indices) Stop(i);
    }

    /// <summary>停止所有条目。</summary>
    public void StopAll()
    {
        foreach (var cfg in effects)
            if (cfg != null) Stop(cfg);
    }

    private void Play(ScaleInEffectConfig cfg)
    {
        if (cfg == null)
        {
            Debug.LogWarning("[ScaleInEffectPlayer] 存在空条目。", this);
            return;
        }

        var rt = GetRuntime(cfg);
        StopTweens(rt);

        // 运行前准备（失活 / 置为完全透明 / 应用位置偏移，并缓存原始值）
        bool ready;
        switch (cfg.type)
        {
            case ScaleInEffectType.Fade: ready = PrepareFade(cfg, rt); break;
            case ScaleInEffectType.Position: ready = PreparePosition(cfg, rt); break;
            default: ready = PrepareScale(cfg, rt); break;
        }
        if (!ready) return;

        // 延迟 n 秒后播放；n<=0 时立即播放
        if (cfg.delaySeconds > 0f)
            rt.delayTween = DOVirtual.DelayedCall(cfg.delaySeconds, () => PlayCore(cfg), cfg.useUnscaledTime);
        else
            PlayCore(cfg);
    }

    private bool PrepareScale(ScaleInEffectConfig cfg, EffectRuntime rt)
    {
        if (cfg.target == null)
        {
            Debug.LogWarning("[ScaleInEffectPlayer] 存在未配置 target 的 Scale 条目。", this);
            return false;
        }

        // 运行前先失活
        if (cfg.hideBeforePlay)
            cfg.target.gameObject.SetActive(false);

        // 记录原始 scale / position（仅首次）
        if (!rt.originalCached)
        {
            rt.originalScale = cfg.target.localScale;
            rt.originalPosition = cfg.target.localPosition;
            rt.originalCached = true;
        }
        return true;
    }

    private bool PrepareFade(ScaleInEffectConfig cfg, EffectRuntime rt)
    {
        // 记录每个节点（含全部子物体）的原始透明度（仅首次），以便“恢复正常”
        if (rt.fadeStates == null)
        {
            rt.fadeStates = new List<FadeState>();
            foreach (var t in cfg.fadeTargets)
            {
                if (t == null) continue;
                CollectFadeTargets(t, false, rt.fadeStates);
            }
        }

        if (rt.fadeStates.Count == 0)
        {
            Debug.LogWarning("[ScaleInEffectPlayer] Fade 条目未配置任何可淡化的节点。", this);
            return false;
        }

        // 开始时完全透明（影响自身与全部子物体）
        foreach (var s in rt.fadeStates)
            SetAlpha(s.component, 0f);

        return true;
    }

    private bool PreparePosition(ScaleInEffectConfig cfg, EffectRuntime rt)
    {
        // 记录每个节点的原始 localPosition（仅首次），以便“恢复正常位置”
        if (rt.positionStates == null)
        {
            rt.positionStates = new List<PositionState>();
            foreach (var t in cfg.positionTargets)
            {
                if (t == null) continue;
                rt.positionStates.Add(new PositionState { target = t, originalPosition = t.localPosition });
            }
        }

        if (rt.positionStates.Count == 0)
        {
            Debug.LogWarning("[ScaleInEffectPlayer] Position 条目未配置任何节点。", this);
            return false;
        }

        // 开始时应用位置偏移
        foreach (var s in rt.positionStates)
            if (s.target != null) s.target.localPosition = s.originalPosition + cfg.positionOffset;

        return true;
    }

    private void PlayCore(ScaleInEffectConfig cfg)
    {
        if (cfg == null) return;

        var rt = GetRuntime(cfg);

        switch (cfg.type)
        {
            case ScaleInEffectType.Fade: PlayFadeCore(cfg, rt); break;
            case ScaleInEffectType.Position: PlayPositionCore(cfg, rt); break;
            default: PlayScaleCore(cfg, rt); break;
        }
    }

    private void PlayScaleCore(ScaleInEffectConfig cfg, EffectRuntime rt)
    {
        if (cfg.target == null) return;

        // 激活目标（延迟期间它是失活的）
        cfg.target.gameObject.SetActive(true);

        // 先放大 n 倍，并复位到原始位置
        cfg.target.localScale = rt.originalScale * cfg.scaleUpMultiplier;
        cfg.target.localPosition = rt.originalPosition;

        // 做 DOScale 动画缩回原始大小
        rt.scaleTween?.Kill();
        rt.scaleTween = cfg.target.DOScale(rt.originalScale, cfg.scaleDownDuration)
            .SetEase(cfg.scaleDownEase, cfg.overshoot)
            .SetUpdate(cfg.useUnscaledTime)
            .OnComplete(() => cfg.onComplete?.Invoke());
    }

    private void PlayFadeCore(ScaleInEffectConfig cfg, EffectRuntime rt)
    {
        if (rt.fadeStates == null || rt.fadeStates.Count == 0) return;

        // 恢复动画：m（fadeDuration）秒内从完全透明恢复到各自原始透明度
        rt.fadeTween?.Kill();
        var seq = DOTween.Sequence().SetUpdate(cfg.useUnscaledTime);
        foreach (var s in rt.fadeStates)
        {
            var tw = CreateFadeTween(s.component, s.originalAlpha, cfg.fadeDuration, cfg.fadeEase, cfg.useUnscaledTime);
            if (tw != null) seq.Join(tw);
        }
        seq.OnComplete(() => cfg.onComplete?.Invoke());
        rt.fadeTween = seq;
    }

    private void PlayPositionCore(ScaleInEffectConfig cfg, EffectRuntime rt)
    {
        if (rt.positionStates == null || rt.positionStates.Count == 0) return;

        // 恢复位移动画：m（positionDuration）秒内回到原始位置
        rt.positionTween?.Kill();
        var seq = DOTween.Sequence().SetUpdate(cfg.useUnscaledTime);
        foreach (var s in rt.positionStates)
        {
            if (s.target == null) continue;
            seq.Join(s.target.DOLocalMove(s.originalPosition, cfg.positionDuration)
                .SetEase(cfg.positionEase)
                .SetUpdate(cfg.useUnscaledTime));
        }
        seq.OnComplete(() => cfg.onComplete?.Invoke());
        rt.positionTween = seq;
    }

    private void Stop(ScaleInEffectConfig cfg)
    {
        if (cfg == null) return;
        if (!Runtimes.TryGetValue(cfg, out var rt)) return;

        StopTweens(rt);

        switch (cfg.type)
        {
            case ScaleInEffectType.Fade:
                // 恢复到各节点的原始透明度
                if (rt.fadeStates != null)
                    foreach (var s in rt.fadeStates)
                        if (s.component != null) SetAlpha(s.component, s.originalAlpha);
                break;
            case ScaleInEffectType.Position:
                // 恢复到各节点的原始位置
                if (rt.positionStates != null)
                    foreach (var s in rt.positionStates)
                        if (s.target != null) s.target.localPosition = s.originalPosition;
                break;
            default:
                if (cfg.target != null && rt.originalCached)
                {
                    cfg.target.localScale = rt.originalScale;
                    cfg.target.localPosition = rt.originalPosition;
                }
                break;
        }
    }

    private EffectRuntime GetRuntime(ScaleInEffectConfig cfg)
    {
        if (!Runtimes.TryGetValue(cfg, out var rt))
        {
            rt = new EffectRuntime();
            Runtimes[cfg] = rt;
        }
        return rt;
    }

    private void StopTweens(EffectRuntime rt)
    {
        rt.delayTween?.Kill();
        rt.delayTween = null;
        rt.scaleTween?.Kill();
        rt.scaleTween = null;
        rt.fadeTween?.Kill();
        rt.fadeTween = null;
        rt.positionTween?.Kill();
        rt.positionTween = null;
    }

    private void OnDestroy()
    {
        if (_runtimes == null) return;
        foreach (var rt in _runtimes.Values)
            StopTweens(rt);
        _runtimes.Clear();
    }

    private static string Describe(ScaleInEffectConfig cfg)
    {
        if (cfg == null) return "(空条目)";
        switch (cfg.type)
        {
            case ScaleInEffectType.Fade: return $"Fade ×{cfg.fadeTargets?.Count ?? 0}";
            case ScaleInEffectType.Position: return $"Position ×{cfg.positionTargets?.Count ?? 0}";
            default: return cfg.target != null ? cfg.target.name : "(未配置 target)";
        }
    }

    // ============================ 透明度工具 ============================

    // 递归收集某个节点及其全部子物体中“可淡化”的组件。
    // 优先级：CanvasGroup > Graphic(Image/Text/TMP) > SpriteRenderer > Renderer。
    // 若某个节点已有 CanvasGroup，其 alpha 会向下继承给所有 UI 子元素，
    // 因此其子树下的 UI Graphic 不再单独淡化，避免重复叠加；但非 UI 的 SpriteRenderer/Renderer 仍会单独处理。
    private static void CollectFadeTargets(Transform node, bool underCanvasGroup, List<FadeState> results)
    {
        if (node == null) return;

        var cg = node.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            results.Add(new FadeState { component = cg, originalAlpha = ReadAlpha(cg) });
            underCanvasGroup = true;
        }
        else
        {
            var g = node.GetComponent<Graphic>();
            if (g != null)
            {
                if (!underCanvasGroup)
                    results.Add(new FadeState { component = g, originalAlpha = ReadAlpha(g) });
            }
            else
            {
                var sr = node.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    results.Add(new FadeState { component = sr, originalAlpha = ReadAlpha(sr) });
                }
                else
                {
                    var r = node.GetComponent<Renderer>();
                    if (r != null)
                        results.Add(new FadeState { component = r, originalAlpha = ReadAlpha(r) });
                }
            }
        }

        for (int i = 0; i < node.childCount; i++)
            CollectFadeTargets(node.GetChild(i), underCanvasGroup, results);
    }

    private static float ReadAlpha(Component c)
    {
        if (c is CanvasGroup cg) return cg.alpha;
        if (c is Graphic g) return g.color.a;
        if (c is SpriteRenderer sr) return sr.color.a;
        if (c is Renderer r && r.sharedMaterial != null) return r.sharedMaterial.color.a;
        return 1f;
    }

    private static void SetAlpha(Component c, float a)
    {
        if (c is CanvasGroup cg) { cg.alpha = a; return; }
        if (c is Graphic g) { var col = g.color; col.a = a; g.color = col; return; }
        if (c is SpriteRenderer sr) { var col = sr.color; col.a = a; sr.color = col; return; }
        if (c is Renderer r) { var m = r.material; var col = m.color; col.a = a; m.color = col; }
    }

    private static Tween CreateFadeTween(Component c, float endAlpha, float duration, Ease ease, bool unscaled)
    {
        if (c is CanvasGroup cg) return cg.DOFade(endAlpha, duration).SetEase(ease).SetUpdate(unscaled);
        if (c is Graphic g) return g.DOFade(endAlpha, duration).SetEase(ease).SetUpdate(unscaled);
        if (c is SpriteRenderer sr) return sr.DOFade(endAlpha, duration).SetEase(ease).SetUpdate(unscaled);
        if (c is Renderer r)
        {
            var m = r.material;
            return DOTween.ToAlpha(() => m.color, x => m.color = x, endAlpha, duration)
                .SetEase(ease)
                .SetUpdate(unscaled);
        }
        return null;
    }

    private class EffectRuntime
    {
        public bool originalCached;
        public Vector3 originalScale;
        public Vector3 originalPosition;
        public List<FadeState> fadeStates;
        public List<PositionState> positionStates;
        public Tween delayTween;
        public Tween scaleTween;
        public Tween fadeTween;
        public Tween positionTween;
    }

    private class FadeState
    {
        public Component component;   // CanvasGroup / Graphic / SpriteRenderer / Renderer
        public float originalAlpha;
    }

    private class PositionState
    {
        public Transform target;
        public Vector3 originalPosition;
    }
}

//****************************************
//创建人：易生
//功能说明：入场动画的配置条目（纯数据），供 ScaleInEffectPlayer 的 effects 列表使用。
//          通过 type 下拉框选择动画类型，不同类型暴露不同参数。
//****************************************

public enum ScaleInEffectType
{
    Scale,      // 缩放入场
    Fade,       // 淡入恢复（完全透明 → 恢复正常）
    Position    // 位移恢复（原始位置 + 偏移 → 恢复正常位置）
}

[System.Serializable]
public class ScaleInEffectConfig
{
    [Tooltip("动画类型：Scale = 缩放入场；Fade = 淡入恢复（开始完全透明，随后恢复，m 秒后正常）")]
    public ScaleInEffectType type = ScaleInEffectType.Scale;

    [Header("通用参数")]
    [Tooltip("延迟 n 秒后再启动动画")]
    public float delaySeconds = 0f;
    [Tooltip("是否使用不受 Time.timeScale 影响的时间（勾选 = 按真实时间播放）")]
    public bool useUnscaledTime = false;
    [Tooltip("动画播放完成后触发")]
    public UnityEvent onComplete;

    [Header("缩放入场 (Scale)")]
    [Tooltip("要播放缩放入场动画的节点（运行前会被暂时失活）")]
    public Transform target;
    [Tooltip("放大倍数 n：激活后先将目标 scale 放大 n 倍")]
    public float scaleUpMultiplier = 2f;
    [Tooltip("DOScale 动画时长（秒）：从放大状态缩回原始大小所需的时间")]
    public float scaleDownDuration = 0.5f;
    [Tooltip("DOScale 动画的缓动曲线。使用 OutBack 等 Back 类型缓动时，缩回原大小会先额外缩小一点再弹回")]
    public Ease scaleDownEase = Ease.OutBack;
    [Tooltip("回弹幅度（仅对 Back 类型缓动有效）：数值越大，缩回时越过原始大小越多，弹回越明显")]
    public float overshoot = 1.70158f;
    [Tooltip("是否在播放前先把目标失活")]
    public bool hideBeforePlay = true;

    [Header("淡入恢复 (Fade)")]
    [Tooltip("要播放淡入动画的物体节点列表：该节点及其全部子物体开始时完全透明，随后做恢复动画到各自原始透明度。支持 CanvasGroup / UI Graphic(Image/Text/TMP) / SpriteRenderer / Renderer")]
    public List<Transform> fadeTargets = new List<Transform>();
    [Tooltip("恢复动画时长 m（秒）：m 秒后恢复正常")]
    public float fadeDuration = 0.5f;
    [Tooltip("淡入恢复的缓动曲线")]
    public Ease fadeEase = Ease.OutQuad;

    [Header("位移恢复 (Position)")]
    [Tooltip("要播放位移恢复动画的物体节点列表：开始时处于 原始位置 + 偏移，随后做恢复位移动画回到原始位置")]
    public List<Transform> positionTargets = new List<Transform>();
    [Tooltip("起始位置偏移：播放时节点先处于 原始位置 + 该偏移")]
    public Vector3 positionOffset = Vector3.zero;
    [Tooltip("恢复位移动画时长 m（秒）：m 秒后恢复正常位置")]
    public float positionDuration = 0.5f;
    [Tooltip("位移恢复的缓动曲线")]
    public Ease positionEase = Ease.OutQuad;
}
