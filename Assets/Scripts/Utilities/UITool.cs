using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class UITool : MonoBehaviour
{ 
    //按钮绑定函数  
    public static void AddButtonClickEvent(Button btn, UnityAction action)
    {
        if (btn != null)
        {
            btn.onClick.AddListener(action);
        }
    }

    public static bool TrySetSliderFillColor(Slider slider, Color color)
    {
        if (slider == null || slider.fillRect == null ||
            !slider.fillRect.TryGetComponent<Image>(out var fillImage))
        {
            return false;
        }

        fillImage.color = color;
        return true;
    }
}
