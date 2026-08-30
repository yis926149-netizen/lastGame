using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class TalentCardSlotVisual : MonoBehaviour
{
    [SerializeField] private float _peakScale = 1.14f;
    [SerializeField] private float _duration = 0.35f;
    [SerializeField] private float _shakeStrength = 6f;

    private RectTransform _rt;
    private CanvasGroup _canvasGroup;
    private Image _flashOverlay;
    private Vector2 _basePos;
    private Sequence _selectSequence;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        var overlayGO = new GameObject("_FlashOverlay", typeof(RectTransform), typeof(Image));
        overlayGO.transform.SetParent(transform, false);
        var overlayRt = overlayGO.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.sizeDelta = Vector2.zero;
        overlayRt.SetAsLastSibling();
        _flashOverlay = overlayGO.GetComponent<Image>();
        _flashOverlay.color = new Color(1f, 0.95f, 0.7f, 0f);
        _flashOverlay.raycastTarget = false;
    }

    /// <summary>由外部（TalentCardSelectionUI）指定本卡槽使用的背景 sprite，保证三张卡互不重复。</summary>
    public void SetBackground(Sprite sprite)
    {
        if (sprite == null) return;
        var bgImage = GetComponent<Image>();
        if (bgImage != null) bgImage.sprite = sprite;
    }

    /// <summary>
    /// 选中卡牌的消失特效：放大 + 闪光 + 抖动 + 淡出。
    /// baseScale 传入卡槽当前的稳态缩放（竖屏下非 1）。
    /// </summary>
    public void PlaySelectAnimation(float baseScale, Action onComplete)
    {
        // 抖动基准位取播放时的实时位置：Awake 时 LayoutSlots 尚未摆位，缓存下来会是 (0,0)。
        _basePos = _rt.anchoredPosition;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        float half = _duration * 0.5f;
        float elapsed = 0f;

        _selectSequence?.Kill();
        _selectSequence = DOTween.Sequence()
            .Join(_rt.DOScale(_peakScale * baseScale, _duration).SetEase(Ease.OutQuad))
            .Join(_canvasGroup.DOFade(0f, _duration))
            .Join(_flashOverlay.DOFade(0.55f, half).SetEase(Ease.OutQuad))
            .Join(_flashOverlay.DOFade(0f, half).SetEase(Ease.OutQuad).SetDelay(half))
            .Join(DOTween.To(
                () => elapsed,
                x =>
                {
                    elapsed = x;
                    float t = x / _duration;
                    float shake = _shakeStrength * (1f - t);
                    _rt.anchoredPosition = _basePos + new Vector2(
                        UnityEngine.Random.Range(-shake, shake),
                        UnityEngine.Random.Range(-shake, shake));
                },
                _duration, _duration))
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                _rt.anchoredPosition = _basePos;
                _flashOverlay.color = new Color(1f, 0.95f, 0.7f, 0f);
                onComplete?.Invoke();
            });
    }

    private void OnDestroy()
    {
        _selectSequence?.Kill();
        _selectSequence = null;
    }
}
