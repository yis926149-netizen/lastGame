using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 粒子特效标识。新增特效：在此加一个枚举值，再到 VfxConfig.asset 里补一行映射即可，
/// 无需改动任何注入接线。命名按「触发场景 + 动作」，不要按美术资源名。
/// </summary>
public enum VfxId
{
    None = 0,

    /// <summary>卡牌部署：模型落地撞击瞬间在落点播放。</summary>
    CardLanding = 1,
}

/// <summary>
/// 粒子特效配置表（世界空间一次性特效的唯一注册处）。
/// 只登记 prefab 与播放参数；实例化、播放、自毁由 VfxService 统一负责。
/// 常驻特效（模型自带的环境粒子等）不属于此表，随 prefab 走。
/// </summary>
[CreateAssetMenu(fileName = "VfxConfig", menuName = "Game/VfxConfig")]
public class VfxConfigSO : ScriptableObject
{
    [System.Serializable]
    public class VfxEntry
    {
        [Tooltip("特效标识，与 VfxId 枚举对应")]
        public VfxId id;

        [Tooltip("粒子 prefab；留空表示该特效暂未接入（VfxService 静默跳过）")]
        public ParticleSystem prefab;

        [Tooltip("相对触发点的额外偏移（世界单位），用于把特效抬离地面避免穿插")]
        public Vector3 positionOffset = Vector3.zero;

        [Min(0f)]
        [Tooltip("整体缩放倍率，1 = 用 prefab 原缩放")]
        public float scale = 1f;

        [Min(0f)]
        [Tooltip("自毁前的额外余量（秒），用于容忍子发射器与拖尾的收尾时间")]
        public float destroyPadding = 0.5f;
    }

    [Tooltip("特效映射表。同一 id 重复登记时以第一条为准（VfxService 会告警）。")]
    public List<VfxEntry> entries = new List<VfxEntry>();
}
