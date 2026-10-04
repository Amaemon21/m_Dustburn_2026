using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;
using VContainer;
using VContainer.Unity;

public class GameplayLifetimeScope : LifetimeScope
{
    [SerializeField] private GameplaySceneKind _kind = GameplaySceneKind.BakedWorld;
    [SerializeField, FormerlySerializedAs("_playerTransform"), ShowIf(nameof(IsTestScene))]
    private Transform _testSpawnPoint;
    [SerializeField] private UnitSpawnPoint[] _spawnPoints;
    [SerializeField] private Camera _mainCamera;

    private bool IsTestScene => _kind == GameplaySceneKind.TestScene;

    protected override void Configure(IContainerBuilder builder)
    {
        RegisterAssets(builder);
        RegisterEntry(builder);
        RegisterPreparation(builder);
        RegisterInput(builder);
        RegisterSaving(builder);
        RegisterPlayer(builder);
        RegisterInteraction(builder);
        RegisterCommands(builder);
        RegisterUnits(builder);
        RegisterInventory(builder);
        RegisterUI(builder);
    }

    private static void RegisterAssets(IContainerBuilder builder)
    {
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().InventoryItems, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().InventorySettings, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().UnitDatabase, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().PlayerInput, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().UIInput, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().InteractionSettings, Lifetime.Singleton);
    }

    private static void RegisterEntry(IContainerBuilder builder)
    {
        builder.Register<GameplayEntryPoint>(Lifetime.Singleton);
        builder.Register<GameplayExit>(Lifetime.Singleton).As<IGameplayExit>();
    }

    private void RegisterPreparation(IContainerBuilder builder)
    {
        builder.Register<PlayerSpawnService>(Lifetime.Singleton).AsSelf().As<IPlayerProvider>();

        if (_kind == GameplaySceneKind.TestScene)
        {
            builder.Register<TestScenePreparation>(Lifetime.Singleton).As<IGameplayPreparation>()
                .WithParameter(_testSpawnPoint);
            return;
        }

        builder.RegisterComponentInHierarchy<WorldGenerator>();
        builder.Register<WorldPreparation>(Lifetime.Singleton).As<IGameplayPreparation>();
    }

    private void RegisterInput(IContainerBuilder builder)
    {
        builder.Register<UIInputService>(Lifetime.Singleton);
        builder.Register<PlayerInputService>(Lifetime.Singleton);
        builder.Register<PlayerMenuInputService>(Lifetime.Singleton).AsSelf().As<IGameplayActivatable>();
    }

    private void RegisterPlayer(IContainerBuilder builder)
    {
        builder.Register<CameraService>(Lifetime.Singleton).AsSelf().As<ICameraService>().WithParameter(_mainCamera);
        builder.Register<PlayerMovementModifiers>(Lifetime.Singleton);
        builder.RegisterEntryPoint<PlayerCharacterController>().AsSelf().As<IGameplayActivatable>();
        builder.Register<PlayerControlService>(Lifetime.Singleton).AsSelf().As<IGameplayActivatable>();
    }

    private void RegisterSaving(IContainerBuilder builder)
    {
        builder.Register<GameplaySaveInputService>(Lifetime.Singleton).As<IGameplayActivatable>();
    }

    private void RegisterInteraction(IContainerBuilder builder)
    {
        builder.Register<InteractedViewModel>(Lifetime.Singleton);
        builder.Register<IInteractionTargetProvider, RaycastInteractionTargetProvider>(Lifetime.Singleton);
        builder.Register<IInteractionContext, PlayerInteractionContext>(Lifetime.Singleton);
        builder.Register<InteractHandler>(Lifetime.Singleton);
        builder.RegisterEntryPoint<InteractController>().AsSelf().As<IGameplayActivatable>();
    }

    private void RegisterCommands(IContainerBuilder builder)
    {
        builder.Register<ICommandProcessor, CommandProcessor>(Lifetime.Singleton);
    }

    private void RegisterUnits(IContainerBuilder builder)
    {
        UnitInstaller unitInstaller = new UnitInstaller(_spawnPoints);
        unitInstaller.Install(builder);
    }

    private void RegisterInventory(IContainerBuilder builder)
    {
        InventoryInstaller inventoryInstaller = new InventoryInstaller();
        inventoryInstaller.Install(builder);

        ContainerInstaller containerInstaller = new ContainerInstaller();
        containerInstaller.Install(builder);
    }

    private void RegisterUI(IContainerBuilder builder)
    {
        GameplayUIInstaller gameplayUIInstaller = new GameplayUIInstaller();
        gameplayUIInstaller.Install(builder);
        
        builder.Register(container => new HudHotbarViewModel(
            container.Resolve<LocalPlayerInventory>().Proxy,
            container.Resolve<PlayerInventoryService>(),
            container.Resolve<ItemCatalog>()), Lifetime.Singleton);
    }

#if UNITY_EDITOR
    [Button("Collect Spawn Points")]
    private void CollectSpawnPoints()
    {
        _spawnPoints = FindObjectsByType<UnitSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
