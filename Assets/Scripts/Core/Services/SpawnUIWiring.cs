using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
//功能说明：单位/建筑运行时 UI 拼接的共享样板（玩家与 AI 生成路径共用）。
//         仅抽取两侧完全一致的"Canvas + 子 UIController + 血条"拼接逻辑；
//         图标源、文化加成、可见性、归属、tag/父物体、仓库等分歧仍由各调用方保留。
//         血条颜色作为参数传入，保留各方原有的颜色决策（零假设、零行为变更）。
//         注：UIController.UIType 仅在 Start/事件中读取，Zenject 字段注入不读它，
//         故 Inject 与 UIType 赋值的先后对行为无影响，此处归一化顺序是安全的。
//****************************************

public static class SpawnUIWiring
{
    /// <summary>单位 UI：根 Canvas(unitCanvas) + 图标(unitIcon, child0) + 血条(healthBar, child1)。设置 cd.healthBar。</summary>
    public static void WireUnitCanvas(GameObject unitGo, CharacterData cd, Color healthBarColor, DiContainer container, IUIConfigProvider uiConfig)
    {
        Canvas canvas = unitGo.GetComponentInChildren<Canvas>();
        UIController unitCanvas = canvas.gameObject.AddComponent<UIController>();
        container.Inject(unitCanvas);
        unitCanvas.UIType = "unitCanvas";
        uiConfig.AddRuntimeCanvas(canvas);

        GameObject icon = canvas.transform.GetChild(0).gameObject;
        UIController iconUI = icon.AddComponent<UIController>();
        container.Inject(iconUI);
        iconUI.UIType = "unitIcon";

        GameObject healthBar = canvas.transform.GetChild(1).gameObject;
        UIController healthBarUI = healthBar.AddComponent<UIController>();
        container.Inject(healthBarUI);
        healthBarUI.UIType = "healthBar";
        cd.healthBar = healthBar.GetComponent<Slider>();
        UITool.TrySetSliderFillColor(cd.healthBar, healthBarColor);
    }

    /// <summary>
    /// 建筑 UI：根 Canvas(buildingCanvas) + 血条(buildingHealthBar, child0)。设置 controller.uiHealthBar。
    /// canvas 为空返回 false（AI 依赖此返回值中止生成；玩家已预校验 canvas 非空）。
    /// </summary>
    public static bool WireBuildingCanvas(GameObject buildingGo, BuildingController controller, Color healthBarColor, DiContainer container, IUIConfigProvider uiConfig)
    {
        Canvas canvas = buildingGo.GetComponentInChildren<Canvas>();
        if (canvas == null) return false;

        UIController buildingCanvas = canvas.gameObject.AddComponent<UIController>();
        container.Inject(buildingCanvas);
        buildingCanvas.UIType = "buildingCanvas";
        uiConfig.AddRuntimeCanvas(canvas);

        GameObject healthBar = canvas.transform.GetChild(0).gameObject;
        UIController healthBarUI = healthBar.AddComponent<UIController>();
        container.Inject(healthBarUI);
        healthBarUI.UIType = "buildingHealthBar";
        controller.uiHealthBar = healthBar.GetComponent<Slider>();
        UITool.TrySetSliderFillColor(controller.uiHealthBar, healthBarColor);
        return true;
    }
}
