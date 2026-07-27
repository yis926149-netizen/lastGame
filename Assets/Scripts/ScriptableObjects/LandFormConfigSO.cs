using UnityEngine;

//****************************************
// 功能说明：地貌配置 ScriptableObject。
//   持有农田/祭坛等地貌的回血相关参数。
//   在 UnitBrainBase 回血计时器中引用，实现每 HealInterval 秒触发一次回血。
//
// 创建方式：Assets/Create/GameConfig/LandFormConfig
//
// 【批次 C】新建，供 UnitBrainBase 注入使用。
//   默认值：HealRatio = 0.1f，HealInterval = 5f。
//   Inspector 可覆盖。
//****************************************

[CreateAssetMenu(fileName = "LandFormConfig", menuName = "GameConfig/LandFormConfig")]
public class LandFormConfigSO : ScriptableObject
{
    [Tooltip("农田地貌每次回血量（占单位最大血量的比例）")]
    public float HealRatio = 0.1f;

    [Tooltip("农田/建筑回血触发间隔（秒）")]
    public float HealInterval = 5f;
}
