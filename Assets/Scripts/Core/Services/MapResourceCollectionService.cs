using UnityEngine;
using UIToolkitDemo;

/// <summary>
/// 地图资源统一消费服务：单位拾取与探索/占领收割的唯一入口。
/// 【地图资源配置化】收敛 UnitMovementController.AutoHarvestResource 的效果 switch，
/// 以及 ExplorationService / AIAutoExplorer / PublicBuildingBase 三处收割流程（读取→清空→销毁→发金币）。
/// 两个入口都基于 HexCellData.TakeResource() 原子式消费，防止同一资源被重复结算。
/// </summary>
public class MapResourceCollectionService
{
    private readonly GoldWallet _goldWallet;
    private readonly AudioManager _audioManager;
    private readonly MapResourceProvider _provider;

    public MapResourceCollectionService(
        GoldWallet goldWallet,
        AudioManager audioManager,
        MapResourceProvider provider)
    {
        _goldWallet = goldWallet;
        _audioManager = audioManager;
        _provider = provider;
    }

    /// <summary>
    /// 单位踩格拾取：原子消费地块资源并应用拾取效果。
    /// </summary>
    /// <param name="cell">单位所在格</param>
    /// <param name="character">单位数据（效果施加对象）</param>
    /// <param name="factionId">阵营索引（0=玩家；Gold 效果仅玩家有效，与改造前行为一致）</param>
    /// <returns>是否成功拾取了资源</returns>
    public bool TryCollectForUnit(HexCellData cell, CharacterData character, int factionId)
    {
        if (cell == null || character?.unitData == null) return false;

        MapResourceSO resource = cell.TakeResource();
        if (resource == null) return false;

        DestroyResourceModel(cell);
        PlayPickupVisual(cell, resource);

        ResourcePickupEffect effect = _provider.GetPickupEffect(resource);
        switch (_provider.GetPickupEffectType(resource))
        {
            case ResourcePickupEffectType.AttackBoost:
                character.Resource_Animals = effect.attackBonus;
                break;

            case ResourcePickupEffectType.Heal:
                character.Heal(effect.healRatio * character.unitData.hp);
                break;

            case ResourcePickupEffectType.DefenseBoost:
                character.Resource_Minerals = effect.defenseBonus;
                break;

            case ResourcePickupEffectType.Gold:
                if (factionId == 0 && _goldWallet != null)
                    _goldWallet.AddGold(0, effect.goldAmount);
                break;

            case ResourcePickupEffectType.None:
            default:
                break;
        }

        return true;
    }

    /// <summary>
    /// 探索/占领收割：原子消费地块资源并换算为金币。
    /// 无资源格也发放基础奖励（与改造前行为一致）。
    /// </summary>
    /// <param name="cell">被收割的地块</param>
    /// <param name="factionId">受益阵营索引</param>
    /// <returns>发放的金币数量</returns>
    public int HarvestForGold(HexCellData cell, int factionId)
    {
        if (cell == null) return 0;

        MapResourceSO resource = cell.TakeResource();
        int reward = _provider.ComputeExplorationReward(resource);

        if (resource != null)
        {
            // 收割只销毁模型，不播放拾取特效/音效（与改造前行为一致）
            DestroyResourceModel(cell);
        }

        _goldWallet?.AddGold(factionId, reward);
        return reward;
    }

    private void DestroyResourceModel(HexCellData cell)
    {
        if (cell.resourceModel != null)
        {
            Object.Destroy(cell.resourceModel);
            cell.resourceModel = null;
        }
    }

    private void PlayPickupVisual(HexCellData cell, MapResourceSO resource)
    {
        if (resource.reapEffectPrefab != null)
        {
            GameObject effect = Object.Instantiate(resource.reapEffectPrefab);
            effect.transform.position = cell.RealCenterWorldCoordinate + new Vector3(0, 0.5f, 0);
            Object.Destroy(effect, 4f);
        }

        if (!string.IsNullOrEmpty(resource.pickupSfxName) && _audioManager != null)
        {
            _audioManager.PlaySFX(resource.pickupSfxName);
        }
    }
}
