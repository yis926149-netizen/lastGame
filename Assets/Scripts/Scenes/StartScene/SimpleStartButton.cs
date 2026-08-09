using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

//****************************************
//创建人：易生
//功能说明：开局按钮简化系统。
//场景启动后延迟 _delaySeconds 秒激活按钮，点击后直接加载 GameScene（index 1）。
//激活后按钮持续播放呼吸缩放动效（参数均在 Inspector 中暴露）。
//与原有 openController 菜单系统完全独立，互不影响。
//****************************************

public class SimpleStartButton : MonoBehaviour
{
    [Tooltip("场景启动后多少秒激活并显示按钮（Inspector 可调）")]
    [SerializeField] private float _delaySeconds = 3f;

    [Tooltip("需要延迟激活的按钮组件，在 Inspector 中拖入")]
    [SerializeField] private Button _button;

    [Header("呼吸缩放动效")]
    [Tooltip("是否启用持续呼吸缩放动效")]
    [SerializeField] private bool _enableBreath = true;

    [Tooltip("呼吸缩放幅度（如 0.06 表示在 1.0 ~ 1.06 之间循环缩放）")]
    [SerializeField] private float _breathAmplitude = 0.06f;

    [Tooltip("单个呼吸周期时长（秒），放大加回缩为一个完整周期")]
    [SerializeField] private float _breathCycleDuration = 1.6f;

    [Tooltip("呼吸波形偏移（秒），多个按钮可错开相位避免同步跳动")]
    [SerializeField] private float _breathPhaseOffset = 0f;

    [Tooltip("执行缩放动效的目标节点列表（不填则默认缩放 _button 自身）")]
    [SerializeField] private Transform[] _breathTargets;

    [Tooltip("延迟期间需要隐藏的物体节点列表（不填则默认隐藏 _button 自身）")]
    [SerializeField] private GameObject[] _hideTargets;

    //防止快速连点重复加载（静态锁，与 UIControl.ToGameScene 保持一致的防护模式）
    private static bool _isLoading;

    private bool _isActive;
    private float _breathTime;

    private void Awake()
    {
        //返回 StartScene 时复位静态锁
        _isLoading = false;

        if (_button == null)
        {
            Debug.LogError("[SimpleStartButton] _button 未在 Inspector 中配置，脚本失效。", this);
            return;
        }

        //呼吸缩放目标默认为按钮自身节点
        if (_breathTargets == null || _breathTargets.Length == 0)
            _breathTargets = new Transform[] { _button.transform };

        //隐藏目标默认为按钮自身节点
        if (_hideTargets == null || _hideTargets.Length == 0)
            _hideTargets = new GameObject[] { _button.gameObject };

        //先隐藏，等延迟结束后再显示
        foreach (GameObject target in _hideTargets)
            target.SetActive(false);

        _button.interactable = false;
    }

    private void Start()
    {
        if (_button == null) return;

        if (_delaySeconds <= 0f)
        {
            Activate();
        }
        else
        {
            Invoke(nameof(Activate), _delaySeconds);
        }
    }

    private void Update()
    {
        if (!_isActive || !_enableBreath || _breathAmplitude <= 0f || _breathCycleDuration <= 0f) return;

        _breathTime += Time.deltaTime;
        float cycle = (_breathTime + _breathPhaseOffset) / _breathCycleDuration;
        float scale = 1f + Mathf.Sin(cycle * Mathf.PI * 2f) * _breathAmplitude;
        Vector3 targetScale = Vector3.one * scale;

        foreach (Transform target in _breathTargets)
            target.localScale = targetScale;
    }

    private void Activate()
    {
        if (_button == null) return;

        _isActive = true;
        foreach (GameObject target in _hideTargets)
            target.SetActive(true);

        _button.interactable = true;
    }

    /// <summary>
    /// 绑定到 Button 的 onClick 事件（Inspector 拖入或 Awake 里 AddListener 均可）。
    /// </summary>
    public void OnStartClicked()
    {
        if (_isLoading) return;
        _isLoading = true;

        //禁用按钮并停止呼吸动效、复位缩放，防止加载期间重复点击
        if (_button != null)
        {
            _button.interactable = false;
            _isActive = false;

            foreach (Transform target in _breathTargets)
                target.localScale = Vector3.one;
        }

        SceneManager.LoadScene(1);
    }

    private void OnDestroy()
    {
        CancelInvoke(nameof(Activate));
    }
}
