using UnityEngine;

//****************************************
    // 【公共建筑系统-决策#35】公共建筑配置 ScriptableObject
    // 存储：prefab、captureHp、defenseHp、子格方向（固定形状）
//****************************************

[CreateAssetMenu(fileName = "PublicBuildingDatabase", menuName = "Game/PublicBuildingDatabase")]
public class PublicBuildingSO : ScriptableObject
{
    [Tooltip("所有公共建筑共用的浮标预制体（World Space Canvas，子级Image占位）")]
    public GameObject markerPrefab;

    [System.Serializable]
    public class PublicBuildingConfig
    {
        [Tooltip("公共建筑预制体")]
        public GameObject prefab;

        [Tooltip("未发现时浮标上显示的建筑图标")]
        public Sprite markerIcon;

        [Tooltip("首次夺取所需血量")]
        public float captureHp = 100f;

        [Tooltip("归属后防守血量")]
        public float defenseHp = 150f;

        [Tooltip("子格偏移方向（相对根格，3个方向固定形状）。例如：NE, E, SE 组成固定四格")]
        public Enums.HexDirection[] subHexDirections = new Enums.HexDirection[3] 
        { 
            Enums.HexDirection.NE, 
            Enums.HexDirection.E, 
            Enums.HexDirection.SE 
        };
    }

    [Tooltip("公共建筑配置列表（索引0对应ID 0）")]
    public PublicBuildingConfig[] buildings;
}
