using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：StartScene 的轻量异常提示组件。
//   原先继承主游戏场景的 UIController，会带入大量主游戏专用的 Zenject [Inject] 依赖；
//   而 StartScene 无对应注入链，一旦设置 UIType 即会空引用。改为继承 MonoBehaviour，
//   与主游戏 UI 系统解耦（场景引用的脚本 GUID 不变，无需在编辑器重新挂载）。
//****************************************

public class exceptionController : MonoBehaviour
{

    void Awake()
    {
        
    }


    void Update()
    {
        
    }
}
