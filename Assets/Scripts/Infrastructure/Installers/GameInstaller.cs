using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Zenject;
using GameConfig;

public class GameInstaller : MonoInstaller
{
    [SerializeField] private UnitDatabaseSO _unitDatabaseSO;
    [SerializeField] private BuildingDatabaseSO _buildingDatabaseSO;
    [SerializeField] private PublicBuildingSO _publicBuildingSO;
    [SerializeField] private UIConfigSO _uiConfigSO;
    [SerializeField] private MapGenerationConfigSO _mapGenerationConfigSO;
    [SerializeField] private MapVisualEventSO _mapVisualEventSO;
    [SerializeField] private EventSystem _eventSystem; // 引用场景中的 EventSystem
    [SerializeField] private Camera _mainCamera;       // 主相机
    [SerializeField] private UIManager _uiManager; // 引用场景中的 UIManager
    [SerializeField] private Canvas _targetUICanvas; // 拖拽操作的目标 Canvas
    [SerializeField] private TalentCardPoolSO _talentCardPoolSO; // 天赋卡池
    [SerializeField] private NormalCardPoolSO _normalCardPoolSO; // 普通卡池
    [Header("Excel 生成的运行库（阶段2/3 接入；未生成时留空则回退 Legacy SO）")]
    [SerializeField] private UnitBalanceDatabaseSO _unitBalanceSO;          // 单位数值库（Excel 生成）
    [SerializeField] private BuildingBalanceDatabaseSO _buildingBalanceSO;  // 建筑数值库（Excel 生成）
    [SerializeField] private NormalCardPoolDatabaseSO _cardPoolDatabaseSO;  // 普通卡池数值库（Excel 生成）
    [SerializeField] private TacticalCardBalanceDatabaseSO _tacticalCardBalanceSO;  // 战术卡数值库（Excel 生成）
    [SerializeField] private TalentCardBalanceDatabaseSO _talentCardBalanceSO;      // 天赋卡数值库（Excel 生成）
    [SerializeField] private TalentDrawRuleDatabaseSO _talentDrawRuleSO;            // 天赋抽卡规则库（Excel 生成）
    [SerializeField] private ExplorationRewardConfigDatabaseSO _explorationRewardConfigDbSO;  // 探索奖励配置（Excel 生成）
    [SerializeField] private ExplorationRewardPoolDatabaseSO _explorationRewardPoolDbSO;      // 探索奖励池（Excel 生成）
    [SerializeField] private MapResourceBalanceDatabaseSO _mapResourceBalanceSO;              // 地图资源数值库（Excel 生成）
    [SerializeField] private ResourceGlobalConfigDatabaseSO _resourceGlobalConfigSO;          // 地图资源全局（Excel 生成）
    [SerializeField] private MapLandFormBalanceDatabaseSO _mapLandFormBalanceSO;              // 地图地貌数值库（Excel 生成）
    [SerializeField] private LandFormGlobalConfigDatabaseSO _landFormGlobalConfigSO;          // 地图地貌全局（Excel 生成）
    [SerializeField] private PublicBuildingBalanceDatabaseSO _publicBuildingBalanceSO;        // 公共建筑数值库（Excel 生成）
    [SerializeField] private EconomyConfigDatabaseSO _economyConfigSO;          // 经济配置（Excel 生成）
    [SerializeField] private GameFlowConfigDatabaseSO _gameFlowConfigSO;        // 游戏流程配置（Excel 生成）
    [SerializeField] private AIConfigDatabaseSO _aiConfigSO;                    // AI 配置（Excel 生成）
    [SerializeField] private BattleFormulaConfigDatabaseSO _battleFormulaConfigSO;  // 战斗公式（Excel 生成）
    [SerializeField] private MapGenConfigDatabaseSO _mapGenConfigSO;            // 地图生成参数（Excel 生成）
    [SerializeField] private TalentCardSelectionUI _talentCardSelectionUI; // 天赋卡选择 UI
    [SerializeField] private ExplorationRewardConfigSO _explorationRewardConfigSO; // 探索奖励配置
    [SerializeField] private TacticalCardDatabaseSO _tacticalCardDatabaseSO; // 战术牌数据库
    [SerializeField] private Transform _tacticalCardAnchor1; // 战术牌锚点1
    [SerializeField] private Transform _tacticalCardAnchor2; // 战术牌锚点2
    [SerializeField] private GameObject _tacticalCardQuantityBadge1; // 战术牌数量文本1（物体内挂 Text）
    [SerializeField] private GameObject _tacticalCardQuantityBadge2; // 战术牌数量文本2（物体内挂 Text）
    [SerializeField] private MapResourceDatabaseSO _mapResourceDatabaseSO; // 地图资源数据库
    [SerializeField] private MapLandFormDatabaseSO _mapLandFormDatabaseSO; // 地图地貌数据库
    public override void InstallBindings()
    {
        ValidateRequiredReferences();
        EnableMapVisualComponents();

        // ��λ

        // 【Excel 数值化（阶段2 接入）】数值运行库：字段为空（未生成）时跳过绑定，
        // Provider 构造参数带默认值 null → 自动回退 Legacy SO（双轨迁移开关）。
        if (_unitBalanceSO != null)
            Container.Bind<UnitBalanceDatabaseSO>().FromInstance(_unitBalanceSO).AsSingle();
        if (_buildingBalanceSO != null)
            Container.Bind<BuildingBalanceDatabaseSO>().FromInstance(_buildingBalanceSO).AsSingle();
        if (_cardPoolDatabaseSO != null)
            Container.Bind<NormalCardPoolDatabaseSO>().FromInstance(_cardPoolDatabaseSO).AsSingle();
        if (_tacticalCardBalanceSO != null)
            Container.Bind<TacticalCardBalanceDatabaseSO>().FromInstance(_tacticalCardBalanceSO).AsSingle();
        if (_talentCardBalanceSO != null)
            Container.Bind<TalentCardBalanceDatabaseSO>().FromInstance(_talentCardBalanceSO).AsSingle();
        if (_talentDrawRuleSO != null)
            Container.Bind<TalentDrawRuleDatabaseSO>().FromInstance(_talentDrawRuleSO).AsSingle();
        if (_explorationRewardConfigDbSO != null)
            Container.Bind<ExplorationRewardConfigDatabaseSO>().FromInstance(_explorationRewardConfigDbSO).AsSingle();
        if (_explorationRewardPoolDbSO != null)
            Container.Bind<ExplorationRewardPoolDatabaseSO>().FromInstance(_explorationRewardPoolDbSO).AsSingle();
        if (_mapResourceBalanceSO != null)
            Container.Bind<MapResourceBalanceDatabaseSO>().FromInstance(_mapResourceBalanceSO).AsSingle();
        if (_resourceGlobalConfigSO != null)
            Container.Bind<ResourceGlobalConfigDatabaseSO>().FromInstance(_resourceGlobalConfigSO).AsSingle();
        if (_mapLandFormBalanceSO != null)
            Container.Bind<MapLandFormBalanceDatabaseSO>().FromInstance(_mapLandFormBalanceSO).AsSingle();
        if (_landFormGlobalConfigSO != null)
            Container.Bind<LandFormGlobalConfigDatabaseSO>().FromInstance(_landFormGlobalConfigSO).AsSingle();
        if (_publicBuildingBalanceSO != null)
            Container.Bind<PublicBuildingBalanceDatabaseSO>().FromInstance(_publicBuildingBalanceSO).AsSingle();
        if (_economyConfigSO != null)
            Container.Bind<EconomyConfigDatabaseSO>().FromInstance(_economyConfigSO).AsSingle();
        if (_gameFlowConfigSO != null)
            Container.Bind<GameFlowConfigDatabaseSO>().FromInstance(_gameFlowConfigSO).AsSingle();
        if (_aiConfigSO != null)
            Container.Bind<AIConfigDatabaseSO>().FromInstance(_aiConfigSO).AsSingle();
        if (_battleFormulaConfigSO != null)
            Container.Bind<BattleFormulaConfigDatabaseSO>().FromInstance(_battleFormulaConfigSO).AsSingle();
        if (_mapGenConfigSO != null)
            Container.Bind<MapGenConfigDatabaseSO>().FromInstance(_mapGenConfigSO).AsSingle();

        // 【Excel 数值化（阶段5）】经济/流程/AI/地图生成 提供者
        Container.Bind<EconomyConfigProvider>().AsSingle();
        Container.Bind<GameFlowConfigProvider>().AsSingle();
        Container.Bind<AIConfigProvider>().AsSingle();
        Container.Bind<MapGenConfigProvider>().AsSingle();
        // 战斗公式系数：静态注入（消费点分散且含静态/纯类，避免逐处 DI）
        BattleFormulaRule.Configure(_battleFormulaConfigSO);

        // 迷雾过渡速度：静态注入（FogTransitionManager 由 ChunkMapRenderer new 出，非 DI 实例）
        FogTransitionManager.Configure(_gameFlowConfigSO != null && _gameFlowConfigSO.Config != null
            ? _gameFlowConfigSO.Config.fogTransitionSpeed : 0.5f);

        Container.Bind<UnitDatabaseSO>().FromInstance(_unitDatabaseSO).AsSingle();

        Container.Bind<IUnitDataProvider>().To<UnitDataProvider>().AsSingle();

        // ����

        Container.Bind<BuildingDatabaseSO>().FromInstance(_buildingDatabaseSO).AsSingle();

        Container.Bind<IBuildingDataProvider>().To<BuildingDataProvider>().AsSingle();

        // 【公共建筑系统】公共建筑配置与生成器
        Container.Bind<PublicBuildingSO>().FromInstance(_publicBuildingSO).AsSingle();
        Container.Bind<IPublicBuildingDataProvider>().To<PublicBuildingDataProvider>().AsSingle();
        Container.Bind<PublicBuildingMarkerManager>().AsSingle();
        Container.Bind<PublicBuildingGenerator>().AsSingle();
        Container.Bind<ExplorationPillarPool>().FromComponentInHierarchy().AsSingle();
        Container.Bind<ExplorationCoinPresenter>().FromComponentInHierarchy().AsSingle();

        // UI

        Container.Bind<UIConfigSO>().FromInstance(_uiConfigSO).AsSingle();

        Container.Bind<IUIConfigProvider>().To<UIConfigProvider>().AsSingle();
        Container.Bind<ICardUnlockRuleProvider>().To<CardUnlockRuleProvider>().AsSingle();

        // �ؿ���Դ����ò

        // 【地图资源配置化 + Excel 数值化】地图资源数据库、提供者与统一消费服务
        Container.Bind<MapResourceDatabaseSO>().FromInstance(_mapResourceDatabaseSO).AsSingle();
        Container.Bind<MapResourceProvider>().AsSingle();
        Container.Bind<MapResourceCollectionService>().AsSingle();

        // 【地图地貌配置化 + Excel 数值化】地貌数据库与提供者（生成权重表）
        Container.Bind<MapLandFormDatabaseSO>().FromInstance(_mapLandFormDatabaseSO).AsSingle();
        Container.Bind<MapLandFormProvider>().AsSingle();
        // 地貌效果规则：静态注入 Excel 数值库（未生成时为 null，回退 Legacy）
        LandFormEffectRule.Configure(_mapLandFormBalanceSO);

        // 【金矿提示图标】地貌提示浮标管理（复用公共建筑浮标视图；ITickable 轮询移除）
        Container.BindInterfacesAndSelfTo<LandFormMarkerManager>().AsSingle().NonLazy();

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

        // 地图表现启动器（保留场景序列化的 CostLabelPrefab，不再实现渲染后端）。
        Container.Bind<MapPresentationBootstrap>()
                 .FromComponentInHierarchy()
                 .AsSingle();
        Container.Bind<IMapPresentationBootstrap>().To<MapPresentationBootstrap>().FromResolve();

        // 唯一 Chunk 渲染后端。
        Container.Bind<ChunkMapRenderer>()
                 .FromNewComponentOnNewGameObject()
                 .WithGameObjectName("ChunkMapRenderer")
                 .AsSingle();
        Container.Bind<IMapRenderBackend>().To<ChunkMapRenderer>().FromResolve();

        // 【动态地图-阶段三】统一地图射线服务（卡牌/拖拽高亮入口收敛，§11）
        Container.Bind<IMapRaycastService>().To<MapRaycastService>().AsSingle();

        // 单格高亮渲染器（不依赖逐格 GridMesh）。
        Container.Bind<HexHighlightRenderer>()
                 .FromNewComponentOnNewGameObject()
                 .WithGameObjectName("HexHighlightRenderer")
                 .AsSingle();

        // ���������ɷ���
        Container.Bind<IMeshGenerator>().To<MeshGeneratorService>().AsSingle();

        // 三态记忆迷雾 - 视野计算服务
        // 【探索重构-阶段6】FieldOfViewService 和 AIFogService 已移除

        // 【探索重构-阶段3】主动探索服务及占位实现
        Container.Bind<IExplorationService>().To<ExplorationService>().AsSingle();
        Container.Bind<IExplorationRule>().To<AdjacencyExplorationRule>().AsSingle();   // 邻接规则

        // 【探索奖励随机机制 + Excel 数值化】探索奖励系统与提供者
        Container.Bind<ExplorationRewardConfigSO>().FromInstance(_explorationRewardConfigSO).AsSingle();
        Container.Bind<ExplorationRewardProvider>().AsSingle();
        Container.Bind<ExplorationRewardSystem>().AsSingle().NonLazy(); // NonLazy 确保立即构造并订阅事件

        // 【探索重构-阶段7】金币资源系统
        Container.Bind(typeof(GoldWallet), typeof(IPlayerResourceWallet)).To<GoldWallet>().AsSingle();
        Container.Bind<IExplorationCostProvider>().To<FixedExplorationCostProvider>().AsSingle();
        Container.BindInterfacesAndSelfTo<GoldIncomeService>().AsSingle().NonLazy();   // ITickable 被动收入
        Container.BindInterfacesAndSelfTo<SunCycleController>().AsSingle().NonLazy(); // ITickable 太阳升降循环

        // 【探索重构-阶段5.5】势力范围服务（新模型：主城固有范围 + 探索占领 + 公共建筑占领）
        Container.Bind<ITerritoryService>().To<TerritoryService>().AsSingle();
        Container.Bind<ILogisticsService>().To<LogisticsService>().AsSingle();

        // 【动态地图-阶段二】地块变化管线：临时可见性（VisibilityLease）/ 地块变化服务（事务 + 交互锁）
        Container.BindInterfacesAndSelfTo<TemporaryVisibilityService>().AsSingle();
        Container.Bind<MapInteractionGate>().AsSingle();
        Container.Bind<IMapInteractionGate>().To<MapInteractionGate>().FromResolve();
        Container.Bind<MapMutationService>().AsSingle();

        // 【动态地图-阶段五】分帧提交执行器（每帧驱动 CommitSliced 的脏 Chunk 几何构建，§阶段五-分帧提交）
        Container.BindInterfacesAndSelfTo<MapSlicedCommitExecutor>().AsSingle();

        // 【动态地图-阶段四】视觉过渡服务（Shader 顶点动画驱动，ITickable；§13.7/§20-10）
        Container.BindInterfacesAndSelfTo<MapVisualTransitionService>().AsSingle();

        // 【竞技场】恢复正常驱动（波浪测试已结束，恢复 ITickable 注册 → Activate 正常触发）
        Container.BindInterfacesAndSelfTo<ArenaEventManager>().AsSingle();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 【动态地图-能力测试】全地图波浪式上下变化测试控制器（V 键触发，两次提交升→降）。
        // 仅编辑器/开发构建绑定——Release 构建中 V 键不得触发全图地形提交（评审 2026-08-05）。
        Container.BindInterfacesAndSelfTo<MapWaveTestController>().AsSingle();

        // 【动态地图-能力测试】鼠标指格地形高度微调测试（R/F 键单格 ±1 永久修改，2026-08-05）。
        // 仅编辑器/开发构建绑定——Release 构建中 R/F 键不得修改地形（与波浪测试同评审口径）。
        // 【2026-08-05 屏蔽】该测试已临时停用，类文件保留以备后续使用——取消下行注释即可恢复。
        // Container.BindInterfacesAndSelfTo<MapHeightEditTestController>().AsSingle();
#endif

        //【普通卡池对象化】普通卡池配置
        Container.Bind<NormalCardPoolSO>().FromInstance(_normalCardPoolSO).AsSingle();

        //��ͼ�¼�
        Container.BindInstance(_mapVisualEventSO).AsSingle();

        //������Χ���
        Container.Bind<PlayerModelManager>().FromComponentInHierarchy().AsSingle();
        Container.Bind<EnemyModelManager>().FromComponentInHierarchy().AsSingle();
        // 势力范围渲染器：改为场景组件（需在 Inspector 指定城墙/城墩预制体）。
        // 场景中若无该组件，回退到新建组件（预制体为空 → 自动回退面片渲染）。
        Container.Bind<SphereOfInfluenceRenderer>()
                 .FromComponentInHierarchy()
                 .AsSingle()
                 .NonLazy();

        // 【检查点 6】PlayerPhase/SettlementPhase/AIPhase/AITacticalBrain/GameStateMachine 已删除

        // AI 管理器：同时绑定具体类 AIManager 与接口 IAIManager 到同一场景组件。
        Container.BindInterfacesAndSelfTo<AIManager>().FromComponentInHierarchy().AsSingle();

        // AI 拆分后的协作服务（Tier 1）。AIPlayerState 作为共享状态单例，供各 AI 服务共用。
        Container.Bind<AIPlayerState>().AsSingle();
        Container.Bind<AIRandomProvider>().AsSingle();
        Container.Bind<AIEntityFactory>().AsSingle();
        Container.Bind<AICardBrain>().AsSingle();

        // 【探索重构】AI 自动探索 + 卡牌定时器
        Container.BindInterfacesAndSelfTo<AIAutoExplorer>().AsSingle().NonLazy();
        Container.BindInterfacesAndSelfTo<AICardTicker>().AsSingle().NonLazy();

        Container.BindInterfacesAndSelfTo<GameFlowManager>()
                 .FromComponentInHierarchy()
                 .AsSingle();

        // 【检查点 6】GameStateMachine 已删除。GameLoop 替代为 IInitializable + ITickable。

        Container.Bind<IUnitService>().To<UnitService>().AsSingle();
        Container.Bind<UnitRemovalService>().AsSingle();

        // 【批次 D】战斗结算器
        Container.Bind<CombatResolver>().AsSingle();

        // 【天赋卡系统】阵营级 Buff 服务
        Container.Bind<IFactionBuffService>().To<FactionBuffService>().AsSingle();

        // concrete class ע��� ITickable ע��
        Container.BindInterfacesAndSelfTo<UnitMovementSystem>().AsSingle();

        // 全局倒计时服务
        Container.Bind<GlobalTimerService>().AsSingle();

        // 【批次 A】GameLoop：注册为 IInitializable + ITickable + 具体类（AsSingle）
        Container.BindInterfacesAndSelfTo<GameLoop>().AsSingle();


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
        // 【天赋卡系统】数据、触发、UI、AI自动选择
        Container.Bind<TalentCardPoolSO>().FromInstance(_talentCardPoolSO).AsSingle();
        Container.Bind<TalentCardProvider>().AsSingle();
        Container.Bind<TalentCardTriggerAdapter>().AsSingle();
        Container.Bind<AITalentCardAutoSelector>().AsSingle().NonLazy();
        Container.BindInterfacesAndSelfTo<TalentCardBootstrap>().AsSingle();
        Container.Bind<TalentCardSelectionUI>().FromInstance(_talentCardSelectionUI).AsSingle();

        Container.BindInterfacesAndSelfTo<CardService>().AsSingle();
        Container.BindInterfacesAndSelfTo<CardPresenter>().AsSingle().NonLazy();

        // 战术牌系统：具体类型 AsSingle 创建唯一实例（不绑定 ICardDropHandler，避免与 CardPresenter 冲突），
        // IInitializable 从该实例解析，ExplorationRewardSystem 注入同一实例。
        Container.Bind<TacticalCardDatabaseSO>().FromInstance(_tacticalCardDatabaseSO).AsSingle();
        Container.Bind<TacticalCardPresenter>().AsSingle()
            .WithArguments(_tacticalCardAnchor1, _tacticalCardAnchor2,
                _tacticalCardQuantityBadge1, _tacticalCardQuantityBadge2);
        Container.Bind<IInitializable>().To<TacticalCardPresenter>().FromResolve();
        // IPlayerUnitSpawnService 已通过 BindInterfacesAndSelfTo<CardPresenter> 自动绑定，无需额外注册

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
        Container.BindInitializableExecutionOrder<TalentCardBootstrap>(10);
    }

    private void ValidateRequiredReferences()
    {
        var missing = new List<string>();

        AddMissing(missing, _unitDatabaseSO, nameof(_unitDatabaseSO));
        AddMissing(missing, _buildingDatabaseSO, nameof(_buildingDatabaseSO));
        AddMissing(missing, _publicBuildingSO, nameof(_publicBuildingSO));
        AddMissing(missing, _uiConfigSO, nameof(_uiConfigSO));
        AddMissing(missing, _mapGenerationConfigSO, nameof(_mapGenerationConfigSO));
        AddMissing(missing, _mapVisualEventSO, nameof(_mapVisualEventSO));
        AddMissing(missing, _eventSystem, nameof(_eventSystem));
        AddMissing(missing, _mainCamera, nameof(_mainCamera));
        AddMissing(missing, _uiManager, nameof(_uiManager));
        AddMissing(missing, _targetUICanvas, nameof(_targetUICanvas));
        AddMissing(missing, _talentCardPoolSO, nameof(_talentCardPoolSO));
        AddMissing(missing, _normalCardPoolSO, nameof(_normalCardPoolSO));
        AddMissing(missing, _talentCardSelectionUI, nameof(_talentCardSelectionUI));
        AddMissing(missing, _explorationRewardConfigSO, nameof(_explorationRewardConfigSO));
        AddMissing(missing, _tacticalCardDatabaseSO, nameof(_tacticalCardDatabaseSO));
        AddMissing(missing, _mapResourceDatabaseSO, nameof(_mapResourceDatabaseSO));
        AddMissing(missing, _mapLandFormDatabaseSO, nameof(_mapLandFormDatabaseSO));

        AddMissingInScene<MapGenerator>(missing);
        AddMissingInScene<MapPresentationBootstrap>(missing);
        AddMissingInScene<PlayerModelManager>(missing);
        AddMissingInScene<EnemyModelManager>(missing);
        AddMissingInScene<AIManager>(missing);
        AddMissingInScene<GameFlowManager>(missing);
        AddMissingInScene<CameraController>(missing);
        AddMissingInScene<EndGame>(missing);
        AddMissingInScene<ExplorationPillarPool>(missing);
        AddMissingInScene<ExplorationCoinPresenter>(missing);

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
            foreach (ExplorationPillarPool pool in root.GetComponentsInChildren<ExplorationPillarPool>(true))
            {
                pool.enabled = true;
            }
            foreach (ExplorationCoinPresenter presenter in root.GetComponentsInChildren<ExplorationCoinPresenter>(true))
            {
                presenter.enabled = true;
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
