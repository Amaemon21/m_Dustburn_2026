using UnityEngine;
using UnityEngine.Serialization;
using VContainer;
using VContainer.Unity;

public class GameplayLifetimeScope : LifetimeScope
{
    [SerializeField] private GameplaySceneKind _kind = GameplaySceneKind.BakedWorld;

    [SerializeField, FormerlySerializedAs("_playerTransform")]
    private Transform _testSpawnPoint;

    [SerializeField] private UnitSpawnPoint[] _spawnPoints;
    [SerializeField] private Camera _mainCamera;

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
        RegisterDamage(builder);
        new GameplayUIInstaller().Install(builder);
    }

    private static void RegisterAssets(IContainerBuilder builder)
    {
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().InventoryItems, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().InventorySettings, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().UnitDatabase, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().PlayerInput, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().UIInput, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().InteractionSettings, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().PlayerVitals, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<GameplayAssets>().PlayerProgression, Lifetime.Singleton);
    }

    private static void RegisterEntry(IContainerBuilder builder)
    {
        builder.Register<GameplayEntryPoint>(Lifetime.Singleton);
        builder.Register<GameplayExit>(Lifetime.Singleton).As<IGameplayExit>();
    }

    private void RegisterPreparation(IContainerBuilder builder)
    {
        builder.Register<PlayerSpawnService>(Lifetime.Singleton).AsSelf().As<IPlayerProvider>();
        builder.Register<WorldSaveRepository>(Lifetime.Singleton);
        builder.Register<PlayerPlacementRepository>(Lifetime.Singleton);
        builder.RegisterEntryPoint<PlayerPlacementTracker>().AsSelf().As<IGameplayActivatable>();

        if (_kind == GameplaySceneKind.TestScene)
        {
            builder.Register<TestScenePreparation>(Lifetime.Singleton).As<IGameplayPreparation>().WithParameter(_testSpawnPoint);
            return;
        }

        builder.RegisterComponentInHierarchy<WorldGenerator>();
        builder.Register<WorldObjectBinder>(Lifetime.Singleton).As<IWorldObjectHook>();
        builder.Register<GameWorldLoader>(Lifetime.Singleton);
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
        new PlayerStatsInstaller().Install(builder);
        new VitalsInstaller().Install(builder);
        new ProgressionInstaller().Install(builder);
        new HandsInstaller().Install(builder);
    }

    private void RegisterSaving(IContainerBuilder builder)
    {
        builder.Register<GameplaySaveScreenshotService>(Lifetime.Singleton).As<IGameplayActivatable>();
        builder.Register<GameplaySaveInputService>(Lifetime.Singleton).As<IGameplayActivatable>();
    }

    private void RegisterInteraction(IContainerBuilder builder)
    {
        builder.Register<InteractedViewModel>(Lifetime.Singleton);
        builder.Register<IInteractionTargetProvider, RaycastInteractionTargetProvider>(Lifetime.Singleton);
        builder.Register<IInteractionContext, PlayerInteractionContext>(Lifetime.Singleton);
        builder.Register<InteractHandler>(Lifetime.Singleton);
        builder.Register<ItemActivationService>(Lifetime.Singleton);
        builder.RegisterEntryPoint<InteractController>().AsSelf().As<IGameplayActivatable>();
    }

    private void RegisterCommands(IContainerBuilder builder)
    {
        builder.Register<ICommandProcessor, CommandProcessor>(Lifetime.Singleton);
    }

    private void RegisterUnits(IContainerBuilder builder)
    {
        new UnitInstaller(_spawnPoints).Install(builder);
    }

    private void RegisterInventory(IContainerBuilder builder)
    {
        new InventoryInstaller().Install(builder);
        new ContainerInstaller().Install(builder);
        new DroppedItemsInstaller().Install(builder);
    }

    private void RegisterDamage(IContainerBuilder builder)
    {
        new DamageInstaller().Install(builder);
        new BlocksInstaller().Install(builder);
    }

}
