//****************************************
//功能说明：单位基础兵种策略类型。替代 UnitStrategyFactory 中按 UnitID 的魔法数判定。
//         0(移民)→Settler；3/5/9(远程)→Ranged；其他→Melee（迁移时逐单位写入）。
//****************************************
public enum UnitStrategyType
{
    Melee = 0,
    Settler = 1,
    Ranged = 2,
}
