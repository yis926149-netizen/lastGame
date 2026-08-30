using UnityEngine;

/// <summary>
/// 卡牌拖拽落点提示（图标 + 连线）的生命周期与状态入口（落点图标与连线计划 §4.3），
/// 注入 PlayerInputHandler 使用。
///
/// - 两个视图从编辑器绑定的 prefab 实例化（构造注入，见 GameInstaller），本类不 new GameObject、
///   不 Resources.Load；图标 prefab 为空时 LogError 后整个功能降级为空操作；
///   连线 prefab 为空时仅连线关闭（LogWarning），图标仍正常工作。
/// - 视图懒创建一次并复用（SetActive 收起），不允许每次拖拽 Instantiate/Destroy。
/// - 连线与图标共存亡：图标隐藏时连线同帧隐藏（控制器 Hide + CardDragLinkView.LateUpdate 双重保证）。
/// - Camera.main 为空时全部按隐藏处理，不抛异常。
/// - IDisposable：容器销毁时 Destroy 两个实例（与 CardDragWorldPreviewController.Dispose 同规格）。
/// </summary>
public class CardDragTargetMarkerController : System.IDisposable
{
    private readonly CardDragTargetMarkerView _iconPrefab;
    private readonly CardDragLinkView _linkPrefab;

    private CardDragTargetMarkerView _view;
    private CardDragLinkView _linkView;
    private bool _shown;
    private bool _degraded;
    private bool _linkDegraded;
    private bool _isDisposed;

    public CardDragTargetMarkerController(CardDragTargetMarkerView iconPrefab, CardDragLinkView linkPrefab)
    {
        _iconPrefab = iconPrefab;
        _linkPrefab = linkPrefab;

        if (iconPrefab == null)
        {
            _degraded = true;
            Debug.LogError(
                "[CardDragTargetMarker] 未在 GameInstaller 绑定落点图标 prefab（Card Drag Target Icon Prefab）：" +
                "落点提示功能整体降级为空操作。请在 GameScene 的 GameInstaller 上赋值。");
        }

        if (linkPrefab == null)
        {
            _linkDegraded = true;
            Debug.LogWarning(
                "[CardDragTargetMarker] 未在 GameInstaller 绑定连线 prefab（Card Drag Link Prefab）：" +
                "连线功能关闭，落点图标仍正常工作。");
        }
    }

    /// <summary>
    /// 同步本帧落点提示（PlayerInputHandler.HighlightGridOnMouseHover 每帧无条件调用）。
    /// terrainHit = false 时隐藏；为 true 时在 hitPoint 上方显示，
    /// 不受「是否可放置」约束——与可放置格高亮的触发条件独立。
    /// </summary>
    public void SetTarget(bool terrainHit, Vector3 hitPoint)
    {
        if (_isDisposed || _degraded) return;

        if (!terrainHit)
        {
            Hide();
            return;
        }

        if (Camera.main == null)
        {
            // Camera.main 为空：全部按隐藏处理，不抛异常。
            Hide();
            return;
        }

        if (_view == null)
        {
            _view = Object.Instantiate(_iconPrefab);
            _view.name = "CardDragTargetMarker";
        }

        _shown = true;
        _view.ShowAtPosition(hitPoint);

        // 连线与图标共存亡（约束 3）：图标显示时连线同帧显示。
        if (!_linkDegraded)
        {
            if (_linkView == null)
            {
                _linkView = Object.Instantiate(_linkPrefab);
                _linkView.name = "CardDragLink";
                _linkView.SetMarkerAnchor(_view);
            }
            _linkView.Show();
        }
    }

    /// <summary>拖拽结束 / 取消 / 暂停：隐藏图标与连线并清空状态（唯一收尾入口由 ClearCardDragHighlight 调用）。</summary>
    public void Clear()
    {
        if (_isDisposed) return;
        Hide();
    }

    private void Hide()
    {
        _shown = false;

        if (_view != null) _view.Hide();
        if (_linkView != null) _linkView.Hide();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Hide();

        if (_view != null)
        {
            Object.Destroy(_view.gameObject);
            _view = null;
        }
        if (_linkView != null)
        {
            Object.Destroy(_linkView.gameObject);
            _linkView = null;
        }
    }
}
