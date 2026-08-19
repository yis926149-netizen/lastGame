using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class CharacterData
{
    //编号
    public int UnitID;
    //对应的模型
    public GameObject model;
    //对应的单位移动控制器
    public UnitMovementController unitMovementController;
    //是否被选择
    public bool isSelected = false;
    //血条
    public Slider healthBar = null;

    //属性
    public UnitData unitData;
    //现在的血量
    public float currentHp;
    //现在的攻击力
    public float currentAttackValue;
    //战术卡「战斗号令」临时移速增益（1 = 无增益，>1 = 移速提升）
    public float moveSpeedMultiplier = 1f;
    //现在的视野范围 - 判断效果如寻路
    public float currentViewPoints;
    //防御力
    public float Defense;

    //资源效果
    //动物 - 提升下一次攻击力
    public float Resource_Animals = 0;
    //矿物 - 提升下一次防御力
    public float Resource_Minerals = 0;

    //地貌效果（【地图地貌配置化】BigBones/FromLand 字段已删除：
    // 防御加成改为按被攻击者所在格配置实时查询 LandFormEffectRule，回血参数改由 MapLandFormSO 提供）
    //河流 - 防御力下降
    public float LandFormType_River = 0;

    //信息面板描述
    public struct InfoPanelData
    {
        //图标
        public Sprite sprite;
        //名字
        public string name;
        //技能图标
        public Sprite skillIcon;
        //信息项 - <<信息项图标，信息项描述>，对应数值>
        public List<KeyValuePair<KeyValuePair<Sprite, string>, float>> InfoDatas;
    }
    public InfoPanelData infoPanelData;




    public CharacterData(int UnitID, GameObject model, UnitMovementController unitMovementController, UnitData unitData)
    {
        this.UnitID = UnitID;
        this.model = model;
        this.unitMovementController = unitMovementController;
        this.unitData = new UnitData(unitData);
        currentHp = this.unitData.hp;
        currentAttackValue = this.unitData.BasicAttackValue;
        Defense = this.unitData.Defense;
    }

    public float Heal(float amount)
    {
        if (amount <= 0 || unitData == null) return 0;

        float previousHp = currentHp;
        currentHp = Mathf.Clamp(currentHp + amount, 0, unitData.hp);
        if (healthBar != null)
        {
            healthBar.value = unitData.hp > 0 ? currentHp / unitData.hp : 0;
        }

        return currentHp - previousHp;
    }

}
