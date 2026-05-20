using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Zenject;


public class Tech_CultureTreeController : MonoBehaviour, ITechCultureService
{
    //注入
    [Inject] private IUnitDataProvider unitDataProvider;
    [Inject] private IBuildingDataProvider buildingDataProvider;
    [Inject] private ITechTreeIconsProvider techTreeIconsProvider;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private TechData _techData;
    [Inject] private CultureData _cultureData;

    //科技、文化值结构体
    public class Tech_Culture
    {
        //树
        public Image Tree;
        //值（每回合产量）
        [HideInInspector]
        public float Points;
        //累积值（进度条）
        [HideInInspector]
        public float AccumulatedPoints;
        //上回合累积值
        public float PreviousAccumulatedPoints;
        //是否播放动画
        public bool PlayAni;
        //用作动画的每帧增量
        public List<float> IncrementPoints;
        //用作动画的增量索引
        public int Index;
        //当前图标
        public Sprite Icon;
        //当前命名
        public string Name;
        //当前描述
        public string Description;
        //等级索引
        public int Level;

        //当前回合是否已切换选项
        public int SwitchOptionsTurn = 0;
    }

    //科技
    public Tech_Culture Tech = new Tech_Culture();
    //科技树
    public Image TechTree;


    //文化
    public Tech_Culture Culture = new Tech_Culture();
    //文化树
    public Image CultureTree;

    // ========== ITechCultureService 接口实现 ==========
    public float TechPoints => Tech.Points;
    public float CulturePoints => Culture.Points;
    public int TechLevel => Tech.Level;
    public int CultureLevel => Culture.Level;

    public event Action OnTechPointsChanged;
    public event Action OnCulturePointsChanged;
    public void TriggerProgressAnimation()
    {
        Tech.PlayAni = true;
        Culture.PlayAni = true;
    }

    [Inject] private IGameStateMachine _gameStateMachine;

    public void Start()
    {
        //科技初始化
        InitTechCulture(Tech, TechTree, 0, techTreeIconsProvider.GetAllTechIcon(), _techData.TechName, _techData.TechDescription);
        Tech.Tree.transform.parent.GetChild(1).GetComponent<Image>().sprite = Tech.Icon;
        Tech.Tree.transform.parent.GetChild(2).GetComponent<Text>().text = Tech.Name;
        Tech.Tree.transform.parent.GetChild(3).GetChild(0).GetComponent<Text>().text = Tech.Description;

        //文化初始化
        InitTechCulture(Culture, CultureTree, 0, techTreeIconsProvider.GetAllCultureIcon(), _cultureData.CultureName, _cultureData.CultureDescription);
        Culture.Tree.transform.parent.GetChild(1).GetComponent<Image>().sprite = Culture.Icon;
        Culture.Tree.transform.parent.GetChild(2).GetComponent<Text>().text = Culture.Name;
        Culture.Tree.transform.parent.GetChild(3).GetChild(0).GetComponent<Text>().text = Culture.Description;
    }

    private void Update()
    {
        //进度条动画
        PlayProgressAni(ref Tech);
        PlayProgressAni(ref Culture);

        //切换科技、文化选项
        SwitchOptions(ref Tech);
        SwitchOptions(ref Culture);

        //文化等级效果
        CultureLevelEffect(Culture);
    }

    //一次性添加科技值
    public void AddTechPoints(float techPoints)
    {
        //数据更新
        Tech.PreviousAccumulatedPoints = Tech.AccumulatedPoints;
        Tech.AccumulatedPoints = Mathf.Min(1, Tech.AccumulatedPoints + techPoints / _techData.TechCost[Tech.Level]);

        //UI更新
        Tech.PlayAni = true;

        //触发事件
        OnTechPointsChanged?.Invoke();
    }

    //一次性添加文化值
    public void AddCulturePoints(float CulturePoints)
    {
        //数据更新
        Culture.PreviousAccumulatedPoints = Culture.AccumulatedPoints;
        Culture.AccumulatedPoints = Mathf.Min(1, Culture.AccumulatedPoints + CulturePoints / _cultureData.CultureCost[Culture.Level]);

        //UI更新
        Culture.PlayAni = true;

        //触发事件
        OnCulturePointsChanged?.Invoke();
    }

    //添加每回合的科技值产能
    public void AddTechPointsPerTurn(float TechPointsPerTurn)
    {
        Tech.Points += TechPointsPerTurn;
        OnTechPointsChanged?.Invoke(); // 每回合产量变化时触发
    }

    //添加每回合的文化值产能
    public void AddCulturePointsPerTurn(float CulturePointsPerTurn)
    {
        Culture.Points += CulturePointsPerTurn;
        OnCulturePointsChanged?.Invoke();
    }

    //每回合添加科技、文化值（累积值）
    public void AddPointsPerTurn()
    {
        //科技点数
        Tech.PreviousAccumulatedPoints = Tech.AccumulatedPoints;
        Tech.AccumulatedPoints = Mathf.Min(1, Tech.AccumulatedPoints + Tech.Points / _techData.TechCost[Tech.Level]);

        //文化点数
        Culture.PreviousAccumulatedPoints = Culture.AccumulatedPoints;
        Culture.AccumulatedPoints = Mathf.Min(1, Culture.AccumulatedPoints + Culture.Points / _cultureData.CultureCost[Culture.Level]);

        //触发事件（累积值变化时 UI 进度条由 PlayAni 处理，但若需要刷新每回合产量显示也可触发）
        OnTechPointsChanged?.Invoke();
        OnCulturePointsChanged?.Invoke();
    }

    // 更新进度条动画的插值（n秒内完成，根据帧率自动计算步数）
    private List<float> ProgressBarAniInterp(float originalPoints, float currentPoints)
    {
        List<float> progress = new List<float>();
        // 动画总时长固定为1秒
        float totalDuration = 0.75f;
        // 每帧时间（上一帧的间隔，用于计算总步数）
        float frameInterval = Time.deltaTime;

        // 计算总步数（1秒内的帧数），避免除零，最小步数为1
        int totalSteps = Mathf.Max(1, Mathf.RoundToInt(totalDuration / frameInterval));
        // 总差值（需要插值的总量）
        float totalDelta = currentPoints - originalPoints;
        // 每步的增量
        float stepIncrement = totalDelta / totalSteps;

        // 初始值加入列表
        progress.Add(originalPoints);

        // 循环生成每帧的插值（总步数次）
        for (int i = 1; i <= totalSteps; i++)
        {
            float currentValue = originalPoints + stepIncrement * i;
            // 最后一步强制等于目标值，避免浮点误差
            if (i == totalSteps)
            {
                currentValue = currentPoints;
            }
            progress.Add(currentValue);
        }

        return progress;
    }

    //切换科技、文化项
    private void SwitchOptions(ref Tech_Culture tech_culture)
    {
        bool b = (_gameStateMachine != null && tech_culture.SwitchOptionsTurn != _gameStateMachine.CurrentTurn);

        if (tech_culture.Tree == null || tech_culture.Tree.fillAmount < 1 || !b)
            return;

        // 重置进度
        tech_culture.Tree.fillAmount = 0;
        tech_culture.AccumulatedPoints = 0;
        tech_culture.PreviousAccumulatedPoints = 0;

        tech_culture.SwitchOptionsTurn = _gameStateMachine?.CurrentTurn ?? tech_culture.SwitchOptionsTurn;

        tech_culture.Level++;

        // === 安全限制等级 ===
        int maxLevel = (tech_culture == Tech)
            ? (techTreeIconsProvider?.GetAllTechIcon()?.Count ?? 1) - 1
            : (techTreeIconsProvider?.GetAllCultureIcon()?.Count ?? 1) - 1;

        if (tech_culture.Level > maxLevel) tech_culture.Level = maxLevel;

        // === 安全读取图标/名称/描述 ===
        if (tech_culture == Tech)
        {
            tech_culture.Icon = techTreeIconsProvider?.GetTechIcon(tech_culture.Level) ?? null;
            tech_culture.Name = (_techData?.TechName != null && tech_culture.Level < _techData.TechName.Count)
                ? _techData.TechName[tech_culture.Level] : "已满级";
            tech_culture.Description = (_techData?.TechDescription != null && tech_culture.Level < _techData.TechDescription.Count)
                ? _techData.TechDescription[tech_culture.Level] : "";
        }
        else
        {
            tech_culture.Icon = techTreeIconsProvider?.GetCultureIcon(tech_culture.Level) ?? null;
            tech_culture.Name = (_cultureData?.CultureName != null && tech_culture.Level < _cultureData.CultureName.Count)
                ? _cultureData.CultureName[tech_culture.Level] : "已满级";
            tech_culture.Description = (_cultureData?.CultureDescription != null && tech_culture.Level < _cultureData.CultureDescription.Count)
                ? _cultureData.CultureDescription[tech_culture.Level] : "";
        }

        // === UI 更新加空值保护 ===
        if (tech_culture.Tree != null && tech_culture.Tree.transform.parent != null)
        {
            Transform p = tech_culture.Tree.transform.parent;
            if (p.childCount > 1)
                p.GetChild(1).GetComponent<Image>().sprite = tech_culture.Icon;
            if (p.childCount > 2)
                p.GetChild(2).GetComponent<Text>().text = tech_culture.Name;
            if (p.childCount > 3 && p.GetChild(3).childCount > 0)
                p.GetChild(3).GetChild(0).GetComponent<Text>().text = tech_culture.Description;
        }
    }

    //播放动画
    private void PlayProgressAni(ref Tech_Culture tech_culture)
    {
        //进度条动画
        if (tech_culture.PlayAni)
        {
            if (tech_culture.IncrementPoints.Count == 0)
            {
                tech_culture.IncrementPoints = ProgressBarAniInterp(tech_culture.PreviousAccumulatedPoints, tech_culture.AccumulatedPoints);
            }

            tech_culture.Tree.fillAmount = tech_culture.IncrementPoints[tech_culture.Index++];

            if (tech_culture.Index == tech_culture.IncrementPoints.Count)
            {
                tech_culture.PlayAni = false;
                tech_culture.Index = 0;
                tech_culture.IncrementPoints.Clear();
            }
        }
    }

    // 通用初始化方法
    private void InitTechCulture(Tech_Culture item, Image tree, float points,
    List<Sprite> icons, List<string> names, List<string> descs)
    {
        // 防御性初始化
        item.Tree = tree;
        item.Level = 0;
        item.Tree.fillAmount = 0;
        item.Points = points;
        item.AccumulatedPoints = 0;
        item.PreviousAccumulatedPoints = 0;
        item.PlayAni = false;
        item.IncrementPoints = new List<float>();
        item.Index = 0;
        item.SwitchOptionsTurn = 0;

        // 图标安全获取
        if (icons != null && icons.Count > 0)
            item.Icon = icons[0];
        else
        {
            Debug.LogError($"[Tech_Culture] 图标列表为空！请检查 TechTreeIconsSO");
            item.Icon = null;
        }

        // 名称/描述安全获取
        if (names != null && names.Count > 0)
            item.Name = names[0];
        else
        {
            Debug.LogError($"[Tech_Culture] 名称列表为空！请检查 TechData / CultureData");
            item.Name = "未解锁";
        }

        if (descs != null && descs.Count > 0)
            item.Description = descs[0];
        else
            item.Description = "数据未配置";
    }

    //文化等级提升
    private void CultureLevelEffect(Tech_Culture tech_culture)
    {
        List<CharacterData> l = new List<CharacterData>();
        switch (tech_culture.Level)
        {
            //5、提升绿皮、巫妖的数值
            case 5:
                //修改初始数值unitDataProvider.GetUnitData(UnitIndex)
                unitDataProvider.GetUnitData(3).BasicAttackValue = 30;
                unitDataProvider.GetUnitData(3).Defense = 10;

                unitDataProvider.GetUnitData(4).BasicAttackValue = 30;
                unitDataProvider.GetUnitData(4).Defense = 10;

                //修改已有卡牌的数值
                l = _unitRepository.AllPlayerUnits.Values.ToList();
                foreach (CharacterData c in l)
                {
                    if (c.UnitID == 3)
                    {
                        c.currentAttackValue = 30;
                        c.Defense = 10;
                    }
                    else if (c.UnitID == 4)
                    {
                        c.currentAttackValue = 30;
                        c.Defense = 10;
                    }
                }

                break;
            //6、提升RPG英杰、大眼、宝箱怪的数值
            case 6:
                //修改初始数值
                unitDataProvider.GetUnitData(9).BasicAttackValue = 30;
                unitDataProvider.GetUnitData(9).Defense = 10;

                unitDataProvider.GetUnitData(10).BasicAttackValue = 30;
                unitDataProvider.GetUnitData(10).Defense = 10;

                unitDataProvider.GetUnitData(11).BasicAttackValue = 30;
                unitDataProvider.GetUnitData(11).Defense = 10;

                //修改已有卡牌的数值
                foreach (CharacterData c in _unitRepository.AllPlayerUnits.Values)
                {
                    if (c.UnitID == 9)
                    {
                        c.currentAttackValue = 30;
                        c.Defense = 10;
                    }
                    else if (c.UnitID == 10)
                    {
                        c.currentAttackValue = 30;
                        c.Defense = 10;
                    }
                    else if (c.UnitID == 11)
                    {
                        c.currentAttackValue = 30;
                        c.Defense = 10;
                    }
                }
                break;
            //7、提升近战Ⅲ、远程Ⅲ的数值
            case 7:
                //修改初始数值
                unitDataProvider.GetUnitData(5).BasicAttackValue = 30;
                unitDataProvider.GetUnitData(6).Defense = 10;

                unitDataProvider.GetUnitData(5).BasicAttackValue = 30;
                unitDataProvider.GetUnitData(6).Defense = 10;

                //修改已有卡牌的数值
                foreach (CharacterData c in _unitRepository.AllPlayerUnits.Values)
                {
                    if (c.UnitID == 5)
                    {
                        c.currentAttackValue = 30;
                        c.Defense = 10;
                    }
                    else if (c.UnitID == 6)
                    {
                        c.currentAttackValue = 30;
                        c.Defense = 10;
                    }
                }

                break;
            //8、提升回血阵的数值
            case 8:
                foreach (GameObject g in _playerModelManager.Index_AltarBuilding.Values)
                {
                    g.GetComponent<BuildingData>().AltarValue = 0.7f;
                }
                break;
            //9、提升进攻、防御建筑的数值
            case 9:
                //修改初始数值
                buildingDataProvider.SetBuildingBaseHP(40);

                //修改已有卡牌的数值
                foreach (GameObject g in _playerModelManager.Index_AttackBuilding.Values)
                {
                    g.GetComponent<BuildingData>().hp = 40;
                    g.GetComponent<BuildingData>().currentHp = 40;
                }

                foreach (GameObject g in _playerModelManager.Index_AltarBuilding.Values)
                {
                    g.GetComponent<BuildingData>().hp = 40;
                    g.GetComponent<BuildingData>().currentHp = 40;
                }
                break;
        }
    }
}