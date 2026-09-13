using UnityEngine;
using VContainer;
using VContainer.Unity;

public class ProjectLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        UIRootView uiRootPrefab = Resources.Load<UIRootView>(UIRootView.RESOURCES_PATH);

        if (uiRootPrefab == null)
        {
            Debug.LogError($"{nameof(ProjectLifetimeScope)}: no {nameof(UIRootView)} prefab at Resources/{UIRootView.RESOURCES_PATH}, the game cannot start", this);
            return;
        }

        builder.RegisterComponentInNewPrefab(uiRootPrefab, Lifetime.Singleton).DontDestroyOnLoad();
        builder.Register<SceneLoader>(Lifetime.Singleton);

        new SaveInstaller().Install(builder);

        builder.RegisterEntryPoint<GameEntryPoint>();
    }
}
