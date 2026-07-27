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
        _basePos = _rt.anchoredPosition;

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

    public void PlaySelectAnimation(Action onComplete)
    {
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        float half = _duration * 0.5f;
        float elapsed = 0f;

        _selectSequence?.Kill();
        _selectSequence = DOTween.Sequence()
            .Join(_rt.DOScale(_peakScale, _duration).SetEase(Ease.OutQuad))
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
            .OnComplete(() =>
            {
                _rt.anchoredPosition = _basePos;
                onComplete?.Invoke();
            });
    }

    private void OnDestroy()
    {
        _selectSequence?.Kill();
        _selectSequence = null;
    }
}
