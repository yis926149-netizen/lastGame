using UnityEngine;
using UnityEngine.EventSystems;
using Zenject;

public class GameInstaller : MonoInstaller
{
    [SerializeField] private UnitDatabaseSO _unitDatabaseSO;
    [SerializeField] private BuildingDatabaseSO _buildingDatabaseSO;
    [SerializeField] private UIConfigSO _uiConfigSO;
    [SerializeField] private EnvironmentModelsSO _environmentModelsSO;
    [SerializeField] private TechTreeIconsSO _techTreeIconsSO;
    [SerializeField] private MapGenerationConfigSO _mapGenerationConfigSO;
    [SerializeField] private MapVisualEventSO _mapVisualEventSO;
    [SerializeField] private EventSystem _eventSystem; // 拖入场景中的 EventSystem
    [SerializeField] private Camera _mainCamera;       // 主摄像机
    [SerializeField] private UIManager _uiManager; // 拖入场景中的 UIManager
    [SerializeField] private Canvas _targetUICanvas; // 拖入用于遮挡检测的 Canvas
    [SerializeField] private AudioManager audioManagerPrefabOrInstance;
    public override void InstallBindings()
    {
        // 单位
        if (_unitDatabaseSO == null)
        {
            Debug.LogError("GameInstaller中未赋值UnitDatabaseSO！请在Inspector面板拖入对应的SO文件");
            return;
        }

        Container.Bind<UnitDatabaseSO>().FromInstance(_unitDatabaseSO).AsSingle();

        Container.Bind<IUnitDataProvider>().To<UnitDataProvider>().AsSingle();

        // 建筑
        if (_buildingDatabaseSO == null)
        {
            Debug.LogError("GameInstaller中未赋值BuildingDatabaseSO！请在Inspector面板拖入对应的SO文件");
            return;
        }

        Container.Bind<BuildingDatabaseSO>().FromInstance(_buildingDatabaseSO).AsSingle();

        Container.Bind<IBuildingDataProvider>().To<BuildingDataProvider>().AsSingle();

        // UI
        if (_uiConfigSO == null)
        {
            Debug.LogError("GameInstaller中未赋值UIConfigSO！请在Inspector面板拖入对应的SO文件");
            return;
        }

        Container.Bind<UIConfigSO>().FromInstance(_uiConfigSO).AsSingle();

        Container.Bind<IUIConfigProvider>().To<UIConfigProvider>().AsSingle();
        Container.Bind<ICardUnlockRuleProvider>().To<CardUnlockRuleProvider>().AsSingle();

        // 地块资源、地貌
        if (_environmentModelsSO == null)
        {
            Debug.LogError("GameInstaller中未赋值EnvironmentModelsSO！请在Inspector面板拖入对应的SO文件");
            return;
        }

        Container.Bind<EnvironmentModelsSO>().FromInstance(_environmentModelsSO).AsSingle();

        Container.Bind<IEnvironmentModelsProvider>().To<EnvironmentModelsProvider>().AsSingle();

        //科技树
        if (_techTreeIconsSO == null)
        {
            Debug.LogError("GameInstaller中未赋值TechTreeIconsSO！请在Inspector面板拖入对应的SO文件");
            return;
        }

        Container.Bind<TechTreeIconsSO>().FromInstance(_techTreeIconsSO).AsSingle();

        Container.Bind<ITechTreeIconsProvider>().To<TechTreeIconsProvider>().AsSingle();

        //地图地块服务类
        Container.Bind<IMapDataService>()
                 .To<HexMapService>()
                 .AsSingle();

        // 地图配置
        if (_mapGenerationConfigSO == null)
        {
            Debug.LogError("GameInstaller中未赋值MapGenerationConfigSO！请在Inspector面板拖入对应的SO文件");
            return;
        }
        Container.Bind<MapGenerationConfigSO>()
                 .FromInstance(_mapGenerationConfigSO)
                 .AsSingle();

        // 绑定地图生成器
        Container.Bind<MapGenerator>()
                 .FromComponentInHierarchy()
                 .AsSingle();

        // 绑定地图渲染器
        Container.Bind<MapRenderer>()
                 .FromComponentInHierarchy()
                 .AsSingle();

        // 绑定网格生成服务
        Container.Bind<IMeshGenerator>().To<MeshGeneratorService>().AsSingle();

        //地图事件
        Container.BindInstance(_mapVisualEventSO).AsSingle();

        //势力范围相关
        Container.Bind<PlayerModelManager>().FromComponentInHierarchy().AsSingle();
        Container.Bind<EnemyModelManager>().FromComponentInHierarchy().AsSingle();

        // 回合状态机的阶段与AI
        Container.Bind<PlayerPhase>().FromComponentInHierarchy().AsSingle();
        Container.Bind<SettlementPhase>().FromComponentInHierarchy().AsSingle();
        Container.Bind<IAIManager>().FromComponentInHierarchy().AsSingle();
        Container.Bind<AIPhase>().AsSingle();

        // 绑定游戏状态机（实现IInitiable），以便创建和初始化
        Container.BindInterfacesAndSelfTo<GameStateMachine>().AsSingle().NonLazy();
        Container.Bind<IUnitService>().To<UnitService>().AsSingle();

        // concrete class 注入和 ITickable 注册
        Container.BindInterfacesAndSelfTo<UnitMovementSystem>().AsSingle();

        // 输入服务
        Container.Bind<IInputService>()
                 .To<InputService>()
                 .AsSingle()
                 .WithArguments(_eventSystem, _mainCamera);

        // 摄像机控制器
        Container.BindInterfacesAndSelfTo<CameraController>()
         .FromComponentInHierarchy()
         .AsSingle();

        // 玩家输入处理器（ITickable 将每帧被调用）
        Container.BindInterfacesAndSelfTo<PlayerInputHandler>().AsSingle();

        // 绑定 UIManager
        if (_uiManager == null)
        {
            Debug.LogError("GameInstaller中未赋值UIManager！");
        }
        Container.Bind<UIManager>().FromInstance(_uiManager).AsSingle();

        // 绑定带 ID 的 Canvas (PlayerInputHandler 构造函数里要求的)
        if (_targetUICanvas == null)
        {
            Debug.LogError("GameInstaller中未赋值TargetUICanvas！");
        }
        Container.Bind<Canvas>()
                 .WithId("TargetUICanvas")
                 .FromInstance(_targetUICanvas)
                 .AsSingle();

        //卡牌
        Container.BindInterfacesAndSelfTo<CardService>().AsSingle();
        Container.BindInterfacesAndSelfTo<CardPresenter>().AsSingle().NonLazy();

        // 科技文化服务（绑定到场景中的 Tech_CultureTreeController）
        Container.BindInterfacesAndSelfTo<Tech_CultureTreeController>().FromComponentInHierarchy().AsSingle();

        // UIManager 视图 —— 直接解析已有的 UIManager 实例
        Container.Bind<IUIManagerView>().To<UIManager>().FromResolve();
        
        // UIManager Presenter
        Container.BindInterfacesAndSelfTo<UIManagerPresenter>().AsSingle();

        Container.Bind<IUnitRepository>().To<UnitRepository>().AsSingle();

        // 绑定 EndGame
        if (FindObjectOfType<EndGame>() != null)
        {
            Container.Bind<EndGame>().FromComponentInHierarchy().AsSingle();
        }
        else
        {
            Debug.LogWarning("场景中没有找到 EndGame 组件");
        }

        // 绑定 TechData 和 CultureData
        if (FindObjectOfType<TechData>() != null)
        {
            Container.Bind<TechData>().FromComponentInHierarchy().AsSingle();
        }
        else
        {
            Debug.LogWarning("场景中没有找到 TechData 组件");
        }

        if (FindObjectOfType<CultureData>() != null)
        {
            Container.Bind<CultureData>().FromComponentInHierarchy().AsSingle();
        }
        else
        {
            Debug.LogWarning("场景中没有找到 CultureData 组件");
        }


        Container.Bind<AudioManager>()
         .FromComponentOn(audioManagerPrefabOrInstance.gameObject)
         .AsSingle()
         .NonLazy();

    }
}