using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Zenject;

public class TalentCardSelectionUI : MonoBehaviour
{
    [Header("UI 根节点")]
    [SerializeField] private GameObject _panelRoot;

    [Header("暗幕")]
    [Tooltip("可选：拖入面板下一个全屏半透明黑 Image。留空则代码自动创建")]
    [SerializeField] private Image _darkOverlay;

    [Header("卡牌背景图（不重复随机）")]
    [Tooltip("填入多张背景 Sprite，每次弹出时 shuffle 后依次分配给各卡槽，保证三张卡背景互不重复。数量建议 ≥ 3。")]
    [SerializeField] private Sprite[] _backgroundSprites;

    [Header("卡牌槽位设置")]
    [SerializeField] private GameObject _cardSlotPrefab;
    [SerializeField] private Transform _slotsParent;

    [Header("横排布局")]
    [SerializeField] private float _cardWidth = 452f;
    [SerializeField] private float _cardSpacing = 40f;
    [Header("B2: 竖屏适配")]
    [Tooltip("竖屏下卡牌宽度缩放系数")]
    [SerializeField] private float _portraitWidthScale = 0.65f;

    [Header("入场动画")]
    [Tooltip("每张卡错开间隔（秒）")]
    [SerializeField] private float _staggerSec = 0.06f;
    [Tooltip("单张卡动画时长（秒）")]
    [SerializeField] private float _cardAnimSec = 0.35f;
    [Tooltip("入场起始缩放")]
    [SerializeField] private float _entranceStartScale = 0.94f;

    [Header("选中震动")]
    [Tooltip("震动强度")]
    [SerializeField] private float _screenShakeStrength = 0.15f;
    [Tooltip("震动时长（秒）")]
    [SerializeField] private float _screenShakeDuration = 0.3f;

    private TalentCardTriggerAdapter _trigger;
    private GameLoop _gameLoop;
    private CameraController _cameraController;
    private GlobalTimerService _timer;

    private List<TalentCardConfigSO> _currentCards;
    private bool _wasPausedBeforeOffer;
    private Sequence _entranceSequence;
    private bool _duringEntrance;
    private bool _subscribed;

    // 面板专用高层 Canvas：选卡期间 sortOrder=1000 覆盖所有其他 UI（含手牌），
    // 其全屏暗幕(raycastTarget=true)即可拦截所有背景点击，卡牌渲染在暗幕之上仍可点。
    private Canvas _panelCanvas;
    private GraphicRaycaster _panelRaycaster;
    private int _originalSortOrder;
    private bool _hadCanvasOriginally;

    private struct SlotRef
    {
        public GameObject   Root;
        public Button       Button;
        public CanvasGroup  CanvasGroup;
        public Image        TypeIcon;
        public Image        MainIcon;
        public Text         NameText;
        public Text         DescText;
        public TalentCardSlotVisual Visual;
    }
    private readonly List<SlotRef> _slots = new();

    [Inject]
    private void InjectDependencies(TalentCardTriggerAdapter trigger, GameLoop gameLoop, CameraController cameraController, GlobalTimerService timer)
    {
        _trigger = trigger;
        _gameLoop = gameLoop;
        _cameraController = cameraController;
        _timer = timer;

        if (!_subscribed && _trigger != null)
        {
            _trigger.OnOfferRequested += HandleOffer;
            _subscribed = true;
            Debug.Log("[TalentCardUI] Subscribed to OnOfferRequested (via Inject).");
        }
    }

    private void Awake()
    {
        if (_panelRoot != null)
        {
            _panelRoot.SetActive(false);
            if (_darkOverlay == null) _darkOverlay = CreateDarkOverlayChild();
            SetupPanelCanvas();
        }
        BuildSlots();
    }

    // 确保面板有独立 Canvas（可单独设置 sortingOrder），若无则动态添加。
    private void SetupPanelCanvas()
    {
        _panelCanvas = _panelRoot.GetComponent<Canvas>();
        _hadCanvasOriginally = _panelCanvas != null;

        if (_panelCanvas == null)
        {
            _panelCanvas = _panelRoot.AddComponent<Canvas>();
            _panelCanvas.overrideSorting = true;
        }
        else
        {
            _panelCanvas.overrideSorting = true;
        }
        _originalSortOrder = _panelCanvas.sortingOrder;

        // 有自己的 Canvas 就必须有 GraphicRaycaster 才能接收点击
        _panelRaycaster = _panelRoot.GetComponent<GraphicRaycaster>();
        if (_panelRaycaster == null)
            _panelRaycaster = _panelRoot.AddComponent<GraphicRaycaster>();
    }

    // 提升/恢复面板 Canvas 层级，选卡期间盖住所有其他 UI（含手牌）
    private void SetPanelOnTop(bool onTop)
    {
        if (_panelCanvas == null) return;
        _panelCanvas.overrideSorting = true;
        _panelCanvas.sortingOrder = onTop ? 1000 : _originalSortOrder;
    }

    private Image CreateDarkOverlayChild()
    {
        var go = new GameObject("DarkOverlay", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_panelRoot.transform, false);
        go.transform.SetAsFirstSibling();
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;
        Debug.Log("[TalentCardUI] DarkOverlay created programmatically.");
        return img;
    }

    private void BuildSlots()
    {
        // B2: 竖屏适配 - 根据屏幕宽高比缩小卡牌尺寸
        float aspectRatio = (float)Screen.width / Screen.height;
        float cardWidthActual = aspectRatio < 1f ? _cardWidth * _portraitWidthScale : _cardWidth;
        float cardSpacingActual = aspectRatio < 1f ? _cardSpacing * _portraitWidthScale : _cardSpacing;

        float step   = cardWidthActual + cardSpacingActual;
        float startX = -(step);

        for (int i = 0; i < 3; i++)
        {
            var go = Instantiate(_cardSlotPrefab, _slotsParent);
            go.SetActive(false);

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(startX + i * step, 0f);
                // B2: 竖屏适配 - 缩放卡牌预制体本身
                if (aspectRatio < 1f)
                {
                    rt.localScale = Vector3.one * _portraitWidthScale;
                }
            }

            var nameText = go.transform.Find("name")?.GetComponent<Text>();
            var descText = go.transform.Find("introduction")?.GetComponent<Text>();
            var typeIcon = go.transform.Find("type")?.GetComponent<Image>();
            var mainIcon = go.transform.Find("main")?.GetComponent<Image>();

            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Graphic>();

            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var visual = go.GetComponent<TalentCardSlotVisual>();
            if (visual == null) visual = go.AddComponent<TalentCardSlotVisual>();

            int index = i;
            btn.onClick.AddListener(() => OnCardClicked(index));

            _slots.Add(new SlotRef
            {
                Root = go, Button = btn, CanvasGroup = cg, Visual = visual,
                TypeIcon = typeIcon, MainIcon = mainIcon,
                NameText = nameText, DescText = descText,
            });
        }
    }

    private void HandleOffer(object sender, TalentCardOfferEventArgs args)
    {
        if (args.Faction != 0) return;
        if (_panelRoot != null && _panelRoot.activeSelf) return;

        Debug.Log($"[TalentCardUI] HandleOffer received, cards: {args.Cards?.Count ?? 0}");
        _wasPausedBeforeOffer = _gameLoop != null ? _gameLoop.IsPaused : false;
        if (_gameLoop != null) _gameLoop.SetPaused(true);
        _currentCards = args.Cards;
        _duringEntrance = true;

        // 背景图 shuffle 后不重复分配给各活跃卡槽
        AssignShuffledBackgrounds();

        for (int i = 0; i < _slots.Count; i++)
        {
            if (i < _currentCards.Count && _currentCards[i] != null)
            {
                var card = _currentCards[i];
                _slots[i].Root.SetActive(true);
                if (_slots[i].TypeIcon != null) _slots[i].TypeIcon.sprite = card.typeIcon;
                if (_slots[i].MainIcon != null) _slots[i].MainIcon.sprite = card.mainIcon;
                if (_slots[i].NameText != null) _slots[i].NameText.text  = card.talentName;
                if (_slots[i].DescText != null) _slots[i].DescText.text  = card.description;
            }
            else
            {
                _slots[i].Root.SetActive(false);
            }
        }

        // 暗幕先激活（raycastTarget 拦截所有穿透点击），再播入场动画
        if (_darkOverlay != null)
        {
            _darkOverlay.raycastTarget = true;
            _darkOverlay.gameObject.SetActive(true);
            _darkOverlay.color = new Color(0f, 0f, 0f, 0f);
        }
        SetPanelOnTop(true); // 提升面板 Canvas 层级盖住手牌等，暗幕即可拦截所有背景点击
        _panelRoot?.SetActive(true);
        PlayEntranceAnimation();
    }

    private void PlayEntranceAnimation()
    {
        _entranceSequence?.Kill();
        _entranceSequence = DOTween.Sequence();

        if (_darkOverlay != null)
        {
            _entranceSequence.Join(_darkOverlay.DOFade(0.7f, 0.4f).SetEase(Ease.OutQuad));
        }

        // 卡牌错开弹入
        for (int i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].Root.activeSelf) continue;

            var t = _slots[i].Root.transform;
            var cg = _slots[i].CanvasGroup;
            t.localScale = Vector3.one * _entranceStartScale;
            cg.alpha = 0f;

            _entranceSequence.Insert(
                _staggerSec * i,
                t.DOScale(1f, _cardAnimSec).SetEase(Ease.OutBack).SetDelay(0.08f));
            _entranceSequence.Insert(
                _staggerSec * i,
                cg.DOFade(1f, _cardAnimSec).SetEase(Ease.OutQuad));
        }

        _entranceSequence.OnComplete(() =>
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].Root.activeSelf) continue;
                var cg = _slots[i].CanvasGroup;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            _duringEntrance = false;
        });
    }

    private void OnCardClicked(int index)
    {
        if (_duringEntrance) return;
        if (_currentCards == null || index >= _currentCards.Count) return;
        var card = _currentCards[index];
        if (card == null) return;

        // 禁用所有按钮防止连点
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].CanvasGroup.interactable = false;
        }

        // 隐藏未选中的卡
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i != index) _slots[i].Root.SetActive(false);
        }

        // 屏幕震动
        _cameraController?.Shake(_screenShakeStrength, _screenShakeDuration);

        Debug.Log($"[TalentCardUI] Card clicked: {card.talentName} (id={card.talentId})");

        // 播放选中动画 → 结算
        _slots[index].Visual.PlaySelectAnimation(() =>
        {
            Debug.Log($"[TalentCardUI] ApplyCard: {card.talentName}");
            _trigger.ApplyCard(0, card);
            SetPanelOnTop(false); // 恢复原层级
            _panelRoot?.SetActive(false);
            if (_gameLoop != null) _gameLoop.SetPaused(_wasPausedBeforeOffer);
            _timer?.StartTimer(GlobalTimerService.DefaultDurationSeconds);
        });
    }

    // Fisher-Yates shuffle 后按顺序分配背景，保证三张卡互不重复
    private void AssignShuffledBackgrounds()
    {
        if (_backgroundSprites == null || _backgroundSprites.Length == 0) return;

        // 复制一份再 shuffle，不破坏原数组顺序
        var pool = new Sprite[_backgroundSprites.Length];
        System.Array.Copy(_backgroundSprites, pool, pool.Length);
        for (int i = pool.Length - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int poolIdx = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Visual == null) continue;
            _slots[i].Visual.SetBackground(pool[poolIdx % pool.Length]);
            poolIdx++;
        }
    }

    private void OnDestroy()
    {
        _entranceSequence?.Kill();
        if (_trigger != null)
            _trigger.OnOfferRequested -= HandleOffer;
    }
}
