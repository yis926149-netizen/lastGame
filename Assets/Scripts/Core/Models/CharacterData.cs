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
    //现在的视野范围 - 判断效果如寻路
    public float currentViewPoints;
    //防御力
    public float Defense;

    //资源效果
    //动物 - 提升下一次攻击力
    public float Resource_Animals = 0;
    //植物 - 立即回血
    public float Resource_Plants = 0;
    //矿物 - 提升下一次防御力
    public float Resource_Minerals = 0;

    //地貌效果
    //石头 - 地形高度 + 1
    //Stone,
    //大骨阵 - 防御力增加 
    public float LandFormType_BigBones = 0;
    //农田 - 每回合自动回血
    public float LandFormType_FromLand = 0.1f;
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
        this.unitData = unitData;
        currentHp = unitData.hp;
        currentAttackValue = unitData.BasicAttackValue;
        Defense = unitData.Defense;
    }

}
