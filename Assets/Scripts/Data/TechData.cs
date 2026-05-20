using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class TechData : MonoBehaviour
{
    // 注入
    [Inject] private ITechTreeIconsProvider techTreeIconsProvider;

    [HideInInspector] public List<Sprite> TechIcon = new List<Sprite>();
    [HideInInspector] public List<string> TechName = new List<string>();
    [HideInInspector] public List<string> TechDescription = new List<string>();
    [HideInInspector] public List<int> TechCost = new List<int>();

    private void Awake()
    {
        // 在 Start 中调用初始化，确保依赖已注入
        Initialize();
    }

    public void Initialize()
    {
        // 清空现有数据，防止重复添加（如果是重复调用）
        TechName.Clear();
        TechDescription.Clear();
        TechCost.Clear();
        TechIcon.Clear();

        // 科技命名
        TechName.AddRange(new List<string>
        {
            "仙人掌", "免费巫妖", "绿皮", "大眼", "宝箱怪",
            "RPG英杰", "近战Ⅲ", "远程Ⅲ", "噬魂龙", "恐惧龙"
        });

        // 科技描述
        TechDescription.AddRange(new List<string>
        {
            "解锁单位：仙人掌", "解锁单位：免费巫妖", "解锁单位：绿皮",
            "解锁单位：大眼", "解锁单位：宝箱怪", "解锁单位：RPG英杰",
            "解锁单位：近战Ⅲ", "解锁单位：远程Ⅲ", "解锁单位：噬魂龙",
            "解锁单位：恐惧龙"
        });

        // 科技花费
        TechCost.AddRange(new List<int> { 151, 200, 350, 400, 500, 600, 750, 800, 900, 1000 });

        // 科技图标
        if (techTreeIconsProvider == null)
        {
            Debug.LogError("TechData: techTreeIconsProvider 未注入，请检查 Zenject 绑定！");
            return;
        }

        var icons = techTreeIconsProvider.GetAllTechIcon();
        if (icons == null || icons.Count == 0)
        {
            Debug.LogError("TechData: 科技图标列表为空！请检查 TechTreeIconsSO 配置。");
            return;
        }

        TechIcon.AddRange(icons);
    }
}