using UnityEngine;
using UnityEngine.UI;
using Zenject;
using DG.Tweening;

/// <summary>
/// 全局倒计时 HUD 显示。挂载于场景 Canvas 下，从 GlobalTimerService 读取剩余时间。
/// </summary>
public class GlobalTimerUI : MonoBehaviour
{
    [Inject] private GlobalTimerService _timer;

    [Header("显示")]
    [SerializeField] private Text _label;

    [Header("紧急样式（剩余 ≤ 60 秒）")]
    [SerializeField] private Color _urgentColor = Color.red;
    [SerializeField] private float _urgentPulseMax = 1.15f;
    [SerializeField] private float _urgentPulseDuration = 0.6f;

    private Color _normalColor = Color.white;
    private bool _urgentActive;
    private Tween _pulseTween;

    private void Start()
    {
        if (_label != null)
            _normalColor = _label.color;
    }

    private void Update()
    {
        if (_timer == null || _label == null) return;

        int total = Mathf.CeilToInt(_timer.Remaining);
        if (total < 0) total = 0;

        _label.text = total.ToString();

        if (_timer.IsRunning && _timer.Remaining <= 60f && !_urgentActive)
        {
            _urgentActive = true;
            StartUrgentPulse();
        }
        else if ((!_timer.IsRunning || _timer.Remaining > 60f) && _urgentActive)
        {
            _urgentActive = false;
            StopUrgentPulse();
        }
    }

    private void StartUrgentPulse()
    {
        StopUrgentPulse();
        if (_label == null) return;

        _label.color = _urgentColor;

        _pulseTween = _label.transform
            .DOScale(_urgentPulseMax, _urgentPulseDuration * 0.5f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    private void StopUrgentPulse()
    {
        if (_pulseTween != null)
        {
            _pulseTween.Kill();
            _pulseTween = null;
        }
        if (_label != null)
        {
            _label.color = _normalColor;
            _label.transform.localScale = Vector3.one;
        }
    }

    private void OnDestroy()
    {
        StopUrgentPulse();
    }
}
