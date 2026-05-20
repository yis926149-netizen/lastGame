using System.Collections.Generic;
using UnityEngine;
using Zenject;


public class CultureData : MonoBehaviour
{
    [Inject] private ITechTreeIconsProvider techTreeIconsProvider;

    [HideInInspector] public List<Sprite> CultureIcon = new List<Sprite>();
    [HideInInspector] public List<string> CultureName = new List<string>();
    [HideInInspector] public List<string> CultureDescription = new List<string>();
    [HideInInspector] public List<int> CultureCost = new List<int>();

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        // 清空现有数据，防止重复添加
        CultureName.Clear();
        CultureDescription.Clear();
        CultureCost.Clear();
        CultureIcon.Clear();

        // 文化命名
        CultureName.AddRange(new List<string>
        {
            "回血建筑", "进攻建筑", "防御建筑", "减少蘑菇的概率", "减少仙人掌的概率",
            "提升绿皮、巫妖的数值", "提升RPG英杰、大眼、宝箱怪的数值",
            "提升近战Ⅲ、远程Ⅲ的数值", "提升回血阵的数值", "提升进攻、防御建筑的数值"
        });

        // 文化描述
        CultureDescription.AddRange(new List<string>
        {
            "解锁回血建筑", "解锁进攻建筑", "解锁防御建筑", "减少蘑菇的概率", "减少仙人掌的概率",
            "提升绿皮、巫妖的数值", "提升RPG英杰、大眼、宝箱怪的数值",
            "提升近战Ⅲ、远程Ⅲ的数值", "提升回血阵的数值", "提升进攻、防御建筑的数值"
        });

        // 文化花费
        CultureCost.AddRange(new List<int> { 251, 300, 550, 700, 800, 950, 1000, 1100, 1250, 1300 });

        // 文化图标
        if (techTreeIconsProvider == null)
        {
            Debug.LogError("CultureData: techTreeIconsProvider 未注入，请检查 Zenject 绑定！");
            return;
        }

        var icons = techTreeIconsProvider.GetAllCultureIcon();
        if (icons == null || icons.Count == 0)
        {
            Debug.LogError("CultureData: 文化图标列表为空！请检查 TechTreeIconsSO 配置。");
            return;
        }

        CultureIcon.AddRange(icons);
    }
}