using NaughtyAttributes;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class ProjectLifetimeScope : LifetimeScope
{
    [SerializeField, BoxGroup("Project Assets"), Label("References"), HorizontalLine(2f, EColor.Blue)] private ProjectAssetReferences _projectAssets = new();

    [SerializeField, BoxGroup("Gameplay Assets"), Label("References"), HorizontalLine(2f, EColor.Blue)] private GameplayAssetReferences _gameplayAssets = new();

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterInstance(_projectAssets);
        builder.RegisterInstance(_gameplayAssets);
        builder.Register<IAssetService, AddressableAssetService>(Lifetime.Singleton);
        builder.Register<GameplayAssetLoader>(Lifetime.Singleton).AsSelf().As<ISceneAssetLoader>();
        builder.Register<MainMenuAssetLoader>(Lifetime.Singleton).AsSelf().As<ISceneAssetLoader>();
        builder.Register<UIRootService>(Lifetime.Singleton);

        new SaveInstaller().Install(builder);

        builder.Register<SceneLoader>(Lifetime.Singleton);
        builder.Register<SceneTransition>(Lifetime.Singleton);
        builder.Register<MainMenuFlow>(Lifetime.Singleton).As<ISceneFlow>();
        builder.Register<GameplayFlow>(Lifetime.Singleton).As<ISceneFlow>();

        builder.RegisterEntryPoint<GameEntryPoint>();
    }
}
