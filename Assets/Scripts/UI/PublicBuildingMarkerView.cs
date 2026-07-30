using UnityEngine;
using UnityEngine.UI;

public class PublicBuildingMarkerView : MonoBehaviour
{
    [SerializeField] private Image _icon;

    private Camera _camera;
    private CanvasGroup _canvasGroup;
    private Vector3 _baseScale;
    private float _phase;

    public void SetIcon(Sprite sprite)
    {
        if (_icon != null && sprite != null)
        {
            _icon.sprite = sprite;
            _icon.preserveAspect = true;
        }
    }

    private void Awake()
    {
        _camera = Camera.main;
        _canvasGroup = GetComponent<CanvasGroup>();
        _baseScale = transform.localScale;
        _phase = Random.Range(0f, Mathf.PI * 2f);
    }

    private void LateUpdate()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera != null)
            transform.LookAt(transform.position + _camera.transform.forward, _camera.transform.up);

        float pulse = (Mathf.Sin(Time.time * 2f + _phase) + 1f) * 0.5f;
        transform.localScale = _baseScale * Mathf.Lerp(0.94f, 1.06f, pulse);
        if (_canvasGroup != null)
            _canvasGroup.alpha = Mathf.Lerp(0.72f, 1f, pulse);
    }
}
