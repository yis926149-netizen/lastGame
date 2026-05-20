using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(menuName = "Events/MapVisualEvent")]
public class MapVisualEventSO : ScriptableObject
{
    public UnityEvent OnMapVisualChanged = new UnityEvent();
    public UnityEvent fogInit = new UnityEvent();

    public void Raise()
    {
        if(OnMapVisualChanged == null)
        {
            Debug.LogError("OnMapVisualChanged事件未初始化！");
            return;
        }
        else
        {
            //Debug.Log("地图视觉事件被触发了！");
            OnMapVisualChanged.Invoke();
        }
           
    }

    public void FogInit()
    {
        if(fogInit == null)
        {
            Debug.LogError("fogInit事件未初始化！");
            return;
        }
        else
        {
            //Debug.Log("迷雾初始化事件被触发了！");
            fogInit.Invoke();
        }
           
    }
}