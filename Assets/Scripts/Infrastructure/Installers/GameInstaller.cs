using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Zenject;

public class GameInstaller : MonoInstaller
{
    [SerializeField] private UnitDatabaseSO _unitDatabaseSO;
    [SerializeField] private BuildingDatabaseSO _buildingDatabaseSO;
    [SerializeField] private UIConfigSO _uiConfigSO;
    [SerializeField] private EnvironmentModelsSO _environmentModelsSO;
    [SerializeField] private MapGenerationConfigSO _mapGenerationConfigSO;
    [SerializeField] private MapVisualEventSO _mapVisualEventSO;
    [SerializeField] private EventSystem _eventSystem; // ���볡���е� EventSystem
    [SerializeField] private Camera _mainCamera;       // �������
    [SerializeField] private UIManager _uiManager; // ���볡���е� UIManager
    [SerializeField] private Canvas _targetUICanvas; // ���������ڵ����� Canvas
    public override void InstallBindings()
    {
        ValidateRequiredReferences();
        EnableMapVisualComponents();

        // ��λ

        Container.Bind<UnitDatabaseSO>().FromInstance(_unitDatabaseSO).AsSingle();

        Container.Bind<IUnitDataProvider>().To<UnitDataProvider>().AsSingle();

        // ����

        Container.Bind<BuildingDatabaseSO>().FromInstance(_buildingDatabaseSO).AsSingle();

        Container.Bind<IBuildingDataProvider>().To<BuildingDataProvider>().AsSingle();

        // UI

        Container.Bind<UIConfigSO>().FromInstance(_uiConfigSO).AsSingle();

        Container.Bind<IUIConfigProvider>().To<UIConfigProvider>().AsSingle();
        Container.Bind<ICardUnlockRuleProvider>().To<CardUnlockRuleProvider>().AsSingle();

        // �ؿ���Դ����ò

        Container.Bind<EnvironmentModelsSO>().FromInstance(_environmentModelsSO).AsSingle();

        Container.Bind<IEnvironmentModelsProvider>().To<EnvironmentModelsProvider>().AsSingle();

        //科技图标已移除

        //地图格子数据服务
        Container.Bind<IMapDataService>()
                 .To<HexMapService>()
                 .AsSingle();

        // ��ͼ����
        Container.Bind<MapGenerationConfigSO>()
                 .FromInstance(_mapGenerationConfigSO)
                 .AsSingle();

        // �󶨵�ͼ������
        Container.Bind<MapGenerator>()
                 .FromComponentInHierarchy()
                 .AsSingle();

        // �󶨵�ͼ��Ⱦ��
        Container.Bind<MapRenderer>()
                 .FromComponentInHierarchy()
                 .AsSingle();

        // ���������ɷ���
        Container.Bind<IMeshGenerator>().To<MeshGeneratorService>().AsSingle();

        // 三态记忆迷雾 - 视野计算服务
        Container.Bind<FieldOfViewService>().AsSingle();

        // AI 逻辑迷雾服务（仅逻辑，不渲染）
        Container.Bind<AIFogService>().AsSingle();

        //��ͼ�¼�
        Container.BindInstance(_mapVisualEventSO).AsSingle();

        //������Χ���
        Container.Bind<PlayerModelManager>().FromComponentInHierarchy().AsSingle();
        Container.Bind<EnemyModelManager>().FromComponentInHierarchy().AsSingle();
        Container.Bind<SphereOfInfluenceRenderer>()
                 .FromNewComponentOnNewGameObject()
                 .AsSingle()
                 .NonLazy();

        // �غ�״̬���Ľ׶���AI
        Container.Bind<PlayerPhase>().FromComponentInHierarchy().AsSingle();
        Container.Bind<SettlementPhase>().FromComponentInHierarchy().AsSingle();
        // AI 管理器：同时绑定具体类 AIManager 与接口 IAIManager 到同一场景组件。
        // GameFlowManager 依赖接口；AIPhase 依赖具体类（需 MonoBehaviour.StartCoroutine 作协程宿主）。
        Container.BindInterfacesAndSelfTo<AIManager>().FromComponentInHierarchy().AsSingle();

        // AI 拆分后的协作服务（Tier 1）。AIPlayerState 作为共享状态单例，供各 AI 服务共用。
        Container.Bind<AIPlayerState>().AsSingle();
        Container.Bind<AIRandomProvider>().AsSingle();
        Container.Bind<AIEntityFactory>().AsSingle();
        Container.Bind<AICardBrain>().AsSingle();
        Container.Bind<AITacticalBrain>().AsSingle();

        Container.Bind<AIPhase>().AsSingle();
        Container.BindInterfacesAndSelfTo<GameFlowManager>()
                 .FromComponentInHierarchy()
                 .AsSingle();

        // ����Ϸ״̬����ʵ��IInitiable�����Ա㴴���ͳ�ʼ��
        Container.BindInterfacesAndSelfTo<GameStateMachine>().AsSingle().NonLazy();
        Container.Bind<IUnitService>().To<UnitService>().AsSingle();
        Container.Bind<UnitRemovalService>().AsSingle();

        // concrete class ע��� ITickable ע��
        Container.BindInterfacesAndSelfTo<UnitMovementSystem>().AsSingle();

        // �������
        Container.Bind<IInputService>()
                 .To<InputService>()
                 .AsSingle()
                 .WithArguments(_eventSystem, _mainCamera);

        // �����������
        Container.BindInterfacesAndSelfTo<CameraController>()
         .FromComponentInHierarchy()
         .AsSingle();

        // ������봦������ITickable ��ÿ֡�����ã�
        Container.BindInterfacesAndSelfTo<PlayerInputHandler>().AsSingle();

        // �� UIManager
        Container.Bind<UIManager>().FromInstance(_uiManager).AsSingle();

        // �󶨴� ID �� Canvas (PlayerInputHandler ���캯����Ҫ���)
        Container.Bind<Canvas>()
                 .WithId("TargetUICanvas")
                 .FromInstance(_targetUICanvas)
                 .AsSingle();

        //����
        Container.BindInterfacesAndSelfTo<CardService>().AsSingle();
        Container.BindInterfacesAndSelfTo<CardPresenter>().AsSingle().NonLazy();

        // UIManager ��ͼ ���� ֱ�ӽ������е� UIManager ʵ��
        Container.Bind<IUIManagerView>().To<UIManager>().FromResolve();
        
        // UIManager Presenter
        Container.BindInterfacesAndSelfTo<UIManagerPresenter>().AsSingle();

        Container.Bind<IUnitRepository>().To<UnitRepository>().AsSingle();

        // �� EndGame
        Container.Bind<EndGame>().FromComponentInHierarchy().AsSingle();

        Container.BindInitializableExecutionOrder<GameFlowManager>(-30);
        Container.BindInitializableExecutionOrder<CardPresenter>(-20);
        Container.BindInitializableExecutionOrder<UIManagerPresenter>(-10);
        Container.BindInitializableExecutionOrder<GameStateMachine>(0);
    }

    private void ValidateRequiredReferences()
    {
        var missing = new List<string>();

        AddMissing(missing, _unitDatabaseSO, nameof(_unitDatabaseSO));
        AddMissing(missing, _buildingDatabaseSO, nameof(_buildingDatabaseSO));
        AddMissing(missing, _uiConfigSO, nameof(_uiConfigSO));
        AddMissing(missing, _environmentModelsSO, nameof(_environmentModelsSO));
        AddMissing(missing, _mapGenerationConfigSO, nameof(_mapGenerationConfigSO));
        AddMissing(missing, _mapVisualEventSO, nameof(_mapVisualEventSO));
        AddMissing(missing, _eventSystem, nameof(_eventSystem));
        AddMissing(missing, _mainCamera, nameof(_mainCamera));
        AddMissing(missing, _uiManager, nameof(_uiManager));
        AddMissing(missing, _targetUICanvas, nameof(_targetUICanvas));

        AddMissingInScene<MapGenerator>(missing);
        AddMissingInScene<MapRenderer>(missing);
        AddMissingInScene<PlayerModelManager>(missing);
        AddMissingInScene<EnemyModelManager>(missing);
        AddMissingInScene<PlayerPhase>(missing);
        AddMissingInScene<SettlementPhase>(missing);
        AddMissingInScene<AIManager>(missing);
        AddMissingInScene<GameFlowManager>(missing);
        AddMissingInScene<CameraController>(missing);
        AddMissingInScene<EndGame>(missing);

        if (missing.Count > 0)
        {
            throw new ZenjectException(
                "GameInstaller configuration is incomplete. Missing required references: " +
                string.Join(", ", missing));
        }
    }

    private static void AddMissing(List<string> missing, Object value, string name)
    {
        if (value == null)
        {
            missing.Add(name);
        }
    }

    private void EnableMapVisualComponents()
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (FogManager fogManager in root.GetComponentsInChildren<FogManager>(true))
            {
                fogManager.enabled = true;
            }
        }
    }

    private void AddMissingInScene<T>(List<string> missing) where T : Object
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<T>(true) != null)
            {
                return;
            }
        }

        missing.Add(typeof(T).Name);
    }
}
