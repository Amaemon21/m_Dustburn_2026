using VContainer;
using VContainer.Unity;

public sealed class VitalsInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register(resolver => resolver.Resolve<PlayerVitalsConfig>().Health, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<PlayerVitalsConfig>().Stamina, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<PlayerVitalsConfig>().Metabolism, Lifetime.Singleton);

        builder.Register<PlayerVitalsRepository>(Lifetime.Singleton);
        builder.Register<PlayerVitals>(Lifetime.Singleton);
        builder.Register<StaminaSystem>(Lifetime.Singleton).AsSelf().As<IPlayerStamina>().As<IPlayerVitalsSystem>();
        builder.Register<MetabolismSystem>(Lifetime.Singleton).AsSelf().As<IPlayerVitalsSystem>();
        builder.Register<HealthRegenSystem>(Lifetime.Singleton).AsSelf().As<IPlayerVitalsSystem>();
        builder.Register<ConsumableUseService>(Lifetime.Singleton).As<IItemUseService>();
        builder.RegisterEntryPoint<PlayerVitalsController>().AsSelf().As<IGameplayActivatable>();
    }
}
