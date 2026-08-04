using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zenject;

[RequireComponent(typeof(Image), typeof(RectTransform))]
public class CardController : MonoBehaviour, ICardView, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private ICardDropHandler _dropHandler;
    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private GameLoop _gameLoop;
    [Inject] private LazyInject<PlayerInputHandler> _playerInputHandler;
    [Inject(Optional = true)] private IMapRaycastService _mapRaycastService;

    /// <summary>允许外部覆盖 drop handler（战术卡等非默认材质）。应在 Zenject 注入之后、首次拖拽之前调用。</summary>
    public void OverrideDropHandler(ICardDropHandler handler) => _dropHandler = handler;

    /// <summary>设置拖拽代理（幽灵）：非空时 OnDragUpdate 移动/缩放代理而非本体。</summary>
    public void SetDragProxy(RectTransform proxy) => _dragProxy = proxy;

    private RectTransform _rectTransform;
    private Image _image;
    private RectTransform _dragProxy;

    public CardData _data;
    private Vector3 _originPosition;
    public Vector3 OriginPosition
    {
        get => _originPosition;
        set => _originPosition = value;
    }

    private bool _isDragging;
    private bool _isNextCard;


    private int originalSiblingIndex;  

    public int CardID => _data?.ID ?? -1;
    public CardData Data => _data;
    public int PlacementID { get; set; }
    public bool IsNextCard { get => _isNextCard; set => _isNextCard = value; }
    public RectTransform RectTransform => _rectTransform;

    [Inject]
    private void Construct()
    {
        _rectTransform = GetComponent<RectTransform>();
        _image = GetComponent<Image>();
        originalSiblingIndex = transform.GetSiblingIndex();

        // Prefab 内透明 Button 的 RectTransform 远大于卡面，若参与射线会在卡外大范围触发悬浮。
        // 卡牌交互统一由根节点 Image + CardController 处理，子 Button 只保留视觉层。
        Button childButton = GetComponentInChildren<Button>(true);
        if (childButton != null && childButton.gameObject != gameObject)
        {
            childButton.interactable = false;
            if (childButton.targetGraphic != null)
                childButton.targetGraphic.raycastTarget = false;
        }

        //_image.alphaHitTestMinimumThreshold = 0.01f;
    }

    public void SetData(CardData data, int placementID, Vector3 originPosition)
    {
        if (data == null)
        {
            Debug.LogError("[CardController] SetData: data is null!");
            return;
        }
        _data = data;
        PlacementID = placementID;
        _originPosition = originPosition;
        _image.sprite = data.CardSprite;

        //_image.SetNativeSize();

        _image.color = Color.white;

        try { _image.alphaHitTestMinimumThreshold = 0.01f; } catch (System.Exception) { }
    }

    public void PlayDealAnimation(Vector3 targetPosition, System.Action onComplete, bool isNextCard = false)
    {
        if (isNextCard)
        {
            // 预告卡弹出动画
            // 初始状态：稍小 + 稍低位置
            // B3: 竖屏适配 —— 偏移量改为屏幕高度的比例，替代横屏硬编码值
            _rectTransform.localScale = _uiConfig.NextCardSize * 0.65f;
            Vector3 startPos = targetPosition + new Vector3(0, Screen.height * -0.015f, 0);
            _rectTransform.anchoredPosition = startPos;

            // 0.6s 带回弹的弹出
            _rectTransform.DOAnchorPos(targetPosition, 0.6f).SetEase(Ease.OutBack, 1.2f);
            _rectTransform.DOScale(_uiConfig.NextCardSize, 0.6f).SetEase(Ease.OutBack, 1.2f);

            onComplete?.Invoke(); // 预告卡无需等待
            return;
        }

        // 普通手牌发牌逻辑（保持不变）
        _rectTransform.DOAnchorPos(targetPosition, 0.4f).OnComplete(() => onComplete?.Invoke());
        _rectTransform.DOScale(_uiConfig.CardSize, 0.4f);
    }

    public void OnDragUpdate(Vector2 localPoint, Vector2 originPos)
    {
        RectTransform target = _dragProxy != null ? _dragProxy : _rectTransform;
        target.anchoredPosition = localPoint;
        float distanceY = Mathf.Abs(localPoint.y - originPos.y);
        float maxDistance = Screen.height * 0.37f; // B3: 拖拽最大距离改为屏幕高度比例
        float minScale = 0.6f;
        float scaleRatio = Mathf.Lerp(1f, minScale, Mathf.Clamp01(distanceY / maxDistance));
        target.localScale = _uiConfig.CardSize * scaleRatio;
    }

    public void ResetToOrigin()
    {
        _rectTransform.DOKill();
        _rectTransform.DOAnchorPos(_originPosition, 0.2f);
        transform.DOScale(_uiConfig.CardSize, 0.2f);
    }

    public void ClearHighlights()
    {
        _playerInputHandler.Value.ClearCardDragHighlight();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsNextCard) return;
        if (_isDragging) return;

        _rectTransform.DOAnchorPos(_originPosition + new Vector3(0, Screen.height * 0.025f, 0), 0.2f); // B3: 悬停上移改为屏幕高度比例
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (IsNextCard) return;
        if (_isDragging) return;

        _rectTransform.DOAnchorPos(_originPosition, 0.2f);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsNextCard || (_gameLoop != null && _gameLoop.IsPaused))
        {
            eventData.pointerDrag = null;
            return;
        }
        _playerInputHandler.Value.ForceDeselectUnit();
        _isDragging = true;
        _dropHandler?.OnCardDragBegin(this);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isDragging) return;

        transform.SetAsLastSibling();               

        RectTransform handPanelRect = _rectTransform.parent as RectTransform;
        if (handPanelRect == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            handPanelRect,
            eventData.position,
            null,
            out Vector2 handPanelLocalPos))
        {
            OnDragUpdate(handPanelLocalPos, _originPosition);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isDragging) return;

        transform.SetSiblingIndex(originalSiblingIndex);   
        _isDragging = false;

        ClearHighlights();

        if (_gameLoop != null && _gameLoop.IsPaused)
        {
            _dropHandler?.OnCardDragCancel(this);
            ResetToOrigin();
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        bool isMapHit;
        if (_mapRaycastService != null)
        {
            // 【动态地图-阶段三】统一射线服务：兼容 Chunk 后端（MapChunkView 后代）与 WholeMap 后端（地图根）
            isMapHit = _mapRaycastService.RaycastMap(Input.mousePosition, out hit);
        }
        else
        {
            isMapHit = Physics.Raycast(ray, out hit) && hit.transform.gameObject == _mapDataService.MapGameObject;
        }
        if (isMapHit)
        {
            HexCellData targetCell = _mapRaycastService != null
                ? _mapRaycastService.GetCellByWorldPosition(hit.point)
                : _mapDataService.GetCellByWorldPosition(hit.point);
            if (targetCell != null)
            {
                if (_dropHandler.HandleCardDragEnd(this, targetCell, hit.point))
                    return;
            }
        }

        _dropHandler?.OnCardDragCancel(this);
        ResetToOrigin();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus || !_isDragging) return;

        CancelDrag();
    }

    private void OnDisable()
    {
        if (_isDragging) _dropHandler?.OnCardDragCancel(this);
        _rectTransform?.DOKill();
    }

    private void CancelDrag()
    {
        _isDragging = false;
        transform.SetSiblingIndex(originalSiblingIndex);
        ClearHighlights();
        _dropHandler?.OnCardDragCancel(this);
        ResetToOrigin();
    }
}
