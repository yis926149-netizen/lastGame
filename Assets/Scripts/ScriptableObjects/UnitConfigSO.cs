using System.Collections.Generic;
using UnityEngine;

//****************************************
//功能说明：攻击音效配置条目——延迟 seconds 秒后播放 sfxName。
//****************************************
[System.Serializable]
public class AttackSfxEntry
{
    [Tooltip("音效名")]
    public string sfxName;

    [Tooltip("延迟秒数（0 = 紧随主音效立即播放）")]
    public float delay;
}

//****************************************
//功能说明：单位攻击音效配置。替代 UnitMovementController 中按 UnitID 的 switch 硬编码。
//         播放顺序：primarySfx 立即播放，随后 delayedSfx 按各自 delay 延迟播放。
//****************************************
[System.Serializable]
public class AttackSfxConfig
{
    [Tooltip("立即播放的主音效")]
    public string primarySfx;

    [Tooltip("延迟回放列表（次数/延迟秒）")]
    public List<AttackSfxEntry> delayedSfx = new List<AttackSfxEntry>();
}

//****************************************
//功能说明：单位卡配置对象。与天赋卡 TalentCardConfigSO 同方向的单位卡对象化。
//         运行时单位 ID 以 unitData.id 为唯一来源。
//****************************************
[CreateAssetMenu(fileName = "UnitConfig", menuName = "Game Data/Normal Cards/Unit Config")]
public class UnitConfigSO : NormalCardConfigSO
{
    [Tooltip("单位数据（unitData.id 即运行时单位 ID 的唯一来源）")]
    public UnitData unitData;

    [Tooltip("单位模型预制体")]
    public GameObject unitModel;

    [Tooltip("单位图标")]
    public Sprite unitIcon;

    [Tooltip("技能图标")]
    public Sprite skillIcon;

    [Tooltip("基础兵种策略类型（替代 UnitStrategyFactory 的 UnitID 魔法数）")]
    public UnitStrategyType strategyType = UnitStrategyType.Melee;

    [Tooltip("攻击音效配置（替代 UnitMovementController 的 UnitID switch）")]
    public AttackSfxConfig attackSfx;

    /// <summary>运行时单位 ID。以 unitData.id 为唯一来源，避免双事实。</summary>
    public int Id => unitData != null ? unitData.id : -1;
}
