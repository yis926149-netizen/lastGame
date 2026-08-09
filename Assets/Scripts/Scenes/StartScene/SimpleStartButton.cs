using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

//****************************************
//创建人：易生
//功能说明：开局按钮简化系统。
//场景启动后延迟 _delaySeconds 秒激活按钮，点击后直接加载 GameScene（index 1）。
//与原有 openController 菜单系统完全独立，互不影响。
//****************************************

public class SimpleStartButton : MonoBehaviour
{
    [Tooltip("场景启动后多少秒激活并显示按钮（Inspector 可调）")]
    [SerializeField] private float _delaySeconds = 3f;

    [Tooltip("需要延迟激活的按钮组件，在 Inspector 中拖入")]
    [SerializeField] private Button _button;

    //防止快速连点重复加载（静态锁，与 UIControl.ToGameScene 保持一致的防护模式）
    private static bool _isLoading;

    private void Awake()
    {
        //返回 StartScene 时复位静态锁
        _isLoading = false;

        if (_button == null)
        {
            Debug.LogError("[SimpleStartButton] _button 未在 Inspector 中配置，脚本失效。", this);
            return;
        }

        //先隐藏，等延迟结束后再显示
        _button.gameObject.SetActive(false);
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

    private void Activate()
    {
        if (_button == null) return;

        _button.gameObject.SetActive(true);
        _button.interactable = true;
    }

    /// <summary>
    /// 绑定到 Button 的 onClick 事件（Inspector 拖入或 Awake 里 AddListener 均可）。
    /// </summary>
    public void OnStartClicked()
    {
        if (_isLoading) return;
        _isLoading = true;

        //禁用按钮，防止加载期间重复点击
        if (_button != null)
            _button.interactable = false;

        SceneManager.LoadScene(1);
    }

    private void OnDestroy()
    {
        CancelInvoke(nameof(Activate));
    }
}
