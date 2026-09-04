using UnityEngine;
using DG.Tweening;

//****************************************
// 功能说明：弓箭手（远程单位）的箭矢飞行表现。
//   实现与箭塔 ArrowTowerShooter.FireArrow 完全一致：
//     实例化 arrow 预制体（已改造为纯白 TrailRenderer 载体，无可见网格）→ DOPath(CatmullRom)
//     三点弧线飞行，途中拉出一条纯白拖尾线 → 到达后停发拖尾并延迟销毁（拖尾自然淡出）。
//   差异：本组件不直接扣血；伤害由调用方通过 onArrive 回调在箭到达目的地时结算
//         （RangedStrategy 传入 CombatResolver 结算逻辑，命中时机对齐箭塔）。
//
//   发射点：优先取模型中的弓节点 WK_weapon_Bow（活动状态），找不到则依次回退
//           Bip001 R Hand / R_hand_container，最后回退 transform + 上抬偏移。
//   箭矢预制体：运行时通过 Resources.Load("arrow") 加载（arrow.prefab 已移入 Assets/Resources）。
//   弧高/飞行时长：暂复用箭塔的 CoreGameplayConfigProvider.ArrowTowerArcHeight / FlightDuration。
//****************************************
public class UnitRangedShooter : MonoBehaviour
{
    private static GameObject _cachedArrowPrefab;

    private Transform _shootPoint;
    private bool _shootPointResolved;

    private float ArcHeight => CoreGameplayConfigProvider.ArrowTowerArcHeight;
    private float FlightDuration => CoreGameplayConfigProvider.ArrowTowerFlightDuration;

    /// <summary>
    /// 射出一支箭飞向目标；onArrive 在箭到达目的地时调用（用于命中结算）。
    /// speedMultiplier：随游戏速度档同步加速（暂停时 0 冻结飞行；由调用方从 GameLoop.SpeedMultiplier 传入）。
    /// </summary>
    public void Shoot(GameObject target, System.Action onArrive = null, float speedMultiplier = 1f)
    {
        if (target == null) return;

        GameObject arrowPrefab = GetArrowPrefab();
        if (arrowPrefab == null) return;

        Transform shootPoint = GetShootPoint();
        Vector3 startPos = shootPoint != null ? shootPoint.position : transform.position + Vector3.up * 1f;
        Vector3 endPos = target.transform.position + Vector3.up * 1f;

        // 【拖尾线】arrow.prefab 已改造为纯白 TrailRenderer 载体（无可见网格）：
        // 飞行体沿抛物线飞行，途中拉出一条纯白拖尾线代替实体箭矢（与箭塔一致）。
        GameObject arrow = Object.Instantiate(arrowPrefab);
        arrow.transform.position = startPos;
        arrow.SetActive(true);

        Sequence seq = DOTween.Sequence();

        Vector3[] path = new Vector3[3];
        path[0] = startPos;
        path[1] = (startPos + endPos) * 0.5f + Vector3.up * ArcHeight;
        path[2] = endPos;

        // 箭矢飞行随速度档同步加速（暂停时 0 冻结，恢复后继续）
        seq.timeScale = speedMultiplier;

        seq.Append(arrow.transform.DOPath(path, FlightDuration, PathType.CatmullRom).SetEase(Ease.Linear));

        seq.OnComplete(() =>
        {
            // 【拖尾线】停止发射并延迟销毁，让残留拖尾自然淡出后再回收（避免线瞬间断掉）。
            FadeOutAndDestroyTrail(arrow);

            onArrive?.Invoke();
        });
    }

    /// <summary>
    /// 【拖尾线】箭到落点后：停止拖尾发射并延迟销毁，等残留拖尾按 TrailRenderer.time 淡出后再回收，
    /// 避免直接 Destroy 导致整条线瞬间消失。无 TrailRenderer 时退化为立即销毁。
    /// </summary>
    private static void FadeOutAndDestroyTrail(GameObject arrow)
    {
        if (arrow == null) return;

        TrailRenderer trail = arrow.GetComponentInChildren<TrailRenderer>();
        if (trail == null)
        {
            Object.Destroy(arrow);
            return;
        }

        trail.emitting = false;
        Object.Destroy(arrow, trail.time + 0.05f);
    }

    //****************************************
    // 【临时方案 B】箭矢发射延迟：人物模型未定前，用固定秒数近似对齐攻击动画的"放箭帧"。
    //   ★ 模型确定后请改回方案 A：在攻击动画片段的放箭帧加 Animation Event（如 OnShoot），
    //     由事件直接调用 Shoot()，并删除下面的 ShootDelaySeconds 常量与 ShootDelayed()，做到帧级对齐。★
    //****************************************
    private const float ShootDelaySeconds = 0.55f;

    /// <summary>延迟 ShootDelaySeconds 秒后射箭（方案 B：近似对齐动画放箭帧）。speedMultiplier 透传给飞行加速。</summary>
    public void ShootDelayed(GameObject target, System.Action onArrive, float speedMultiplier = 1f)
    {
        StartCoroutine(DelayedShootCoroutine(target, onArrive, speedMultiplier));
    }

    private System.Collections.IEnumerator DelayedShootCoroutine(GameObject target, System.Action onArrive, float speedMultiplier)
    {
        yield return new WaitForSeconds(ShootDelaySeconds);
        Shoot(target, onArrive, speedMultiplier);
    }

    private static GameObject GetArrowPrefab()
    {
        if (_cachedArrowPrefab == null)
            _cachedArrowPrefab = Resources.Load<GameObject>("arrow");
        return _cachedArrowPrefab;
    }

    private Transform GetShootPoint()
    {
        if (_shootPointResolved) return _shootPoint;
        _shootPointResolved = true;

        _shootPoint = FindActiveChild(transform, "WK_weapon_Bow");
        if (_shootPoint == null) _shootPoint = FindActiveChild(transform, "Bip001 R Hand");
        if (_shootPoint == null) _shootPoint = FindActiveChild(transform, "R_hand_container");
        return _shootPoint;
    }

    private static Transform FindActiveChild(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name && root.gameObject.activeInHierarchy) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindActiveChild(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
