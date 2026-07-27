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
    [Inject] private CardPresenter _presenter;
    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private GameLoop _gameLoop;
    [Inject] private LazyInject<PlayerInputHandler> _playerInputHandler;

    private RectTransform _rectTransform;
    private Image _image;

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
    public int PlacementID { get; set; }
    public bool IsNextCard { get => _isNextCard; set => _isNextCard = value; }
    public RectTransform RectTransform => _rectTransform;

    [Inject]
    private void Construct()
    {
        _rectTransform = GetComponent<RectTransform>();
        _image = GetComponent<Image>();
        originalSiblingIndex = transform.GetSiblingIndex();

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

        _image.alphaHitTestMinimumThreshold = 0.01f;
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
        _rectTransform.anchoredPosition = localPoint;
        float distanceY = Mathf.Abs(localPoint.y - originPos.y);
        float maxDistance = Screen.height * 0.37f; // B3: 拖拽最大距离改为屏幕高度比例
        float minScale = 0.6f;
        float scaleRatio = Mathf.Lerp(1f, minScale, Mathf.Clamp01(distanceY / maxDistance));
        transform.localScale = _uiConfig.CardSize * scaleRatio;
    }

    public void ResetToOrigin()
    {
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
        // 【批次 C】暂停时禁止拖牌，运行时始终可拖（移除回合阶段门控）
        if (IsNextCard || _gameLoop.IsPaused)
        {
            eventData.pointerDrag = null;
            return;
        }
        _playerInputHandler.Value.ForceDeselectUnit();
        _isDragging = true;                        
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isDragging) return;
        // 【批次 C】暂停时取消拖拽
        if (_gameLoop.IsPaused)
        {
            CancelDrag();
            return;
        }

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

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.transform.gameObject == _mapDataService.MapGameObject)
        {
            HexCellData targetCell = _mapDataService.GetCellByWorldPosition(hit.point);
            if (targetCell != null)
            {
                if (_presenter.HandleCardDragEnd(this, targetCell, hit.point))
                    return;
            }
        }

        ResetToOrigin();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus || !_isDragging) return;

        CancelDrag();
    }

    private void OnDisable()
    {
        _rectTransform?.DOKill();
    }

    private void CancelDrag()
    {
        _isDragging = false;
        transform.SetSiblingIndex(originalSiblingIndex);
        ClearHighlights();
        ResetToOrigin();
    }
}
