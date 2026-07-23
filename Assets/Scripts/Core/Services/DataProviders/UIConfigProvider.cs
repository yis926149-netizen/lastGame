using System.Collections.Generic;
using UnityEngine;

public interface IUIConfigProvider
{
    GameObject GetMovementIndicatorPrefab();
    GameObject GetEnemyUnitIndicatorPrefab();
    GameObject GetCardPrefab();
    Sprite GetMovementPointsIcon();
    Sprite GetMeleeAttackPointsIcon();

    List<Canvas> RuntimeCanvases { get;}
    void AddRuntimeCanvas(Canvas canvas);

    GameObject NextCardPlaceholder { get; set; }

    void SetNextCardPlaceholder(GameObject placeholder);

    Vector3 CardSize { get; }
    Vector3 NextCardSize { get; }
}

public class UIConfigProvider : IUIConfigProvider
{
    private readonly UIConfigSO _uiConfig;
    private Vector3 _cardSize;
    private Vector3 _nextCardSize;

    public UIConfigProvider(UIConfigSO unitDatabase)
    {
        _uiConfig = unitDatabase;
    }

    public GameObject GetMovementIndicatorPrefab() => _uiConfig.movementIndicatorPrefab;
    public GameObject GetEnemyUnitIndicatorPrefab() => _uiConfig.enemyUnitIndicatorPrefab;
    public GameObject GetCardPrefab() => _uiConfig.cardPrefab;
    public Sprite GetMovementPointsIcon() => _uiConfig.movementPointsIcon;
    public Sprite GetMeleeAttackPointsIcon() => _uiConfig.meleeAttackPointsIcon;

    public List<Canvas> RuntimeCanvases { get; } = new List<Canvas>(); // 运行时存储

    public void AddRuntimeCanvas(Canvas canvas)
    {
        RuntimeCanvases.Add(canvas);
    }

    public GameObject NextCardPlaceholder { get; set; } // 运行时存储
    public Vector3 CardSize => _cardSize;
    public Vector3 NextCardSize => _nextCardSize;

    public void SetNextCardPlaceholder(GameObject placeholder)
    {
        NextCardPlaceholder = placeholder;
        if (placeholder != null)
        {
            var rect = placeholder.GetComponent<RectTransform>();
            //Debug.Log($"[UIConfig] Placeholder localScale: {rect.localScale}"); 
            _nextCardSize = rect.localScale + new Vector3(0.6f, 0.6f, 0.6f);
            //_nextCardSize = rect.localScale;
            _cardSize = rect.localScale + new Vector3(0.3f, 0.3f, 0.3f);
            //_cardSize = rect.localScale;
            //Debug.Log($"[UIConfig] NextCardSize: {_nextCardSize}, CardSize: {_cardSize}"); 
        }
    }
}