using VContainer;
using VContainer.Unity;

public sealed class ProgressionInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register(resolver => resolver.Resolve<PlayerProgressionConfig>().Harvest, Lifetime.Singleton);
        builder.Register(resolver => resolver.Resolve<PlayerProgressionConfig>().Levels.CreateCurve(), Lifetime.Singleton);

        builder.Register<PlayerProgressionRepository>(Lifetime.Singleton);
        builder.Register<PlayerProgression>(Lifetime.Singleton);
        builder.Register<HarvestExperienceSystem>(Lifetime.Singleton).As<IGameplayActivatable>();
        builder.Register<ExperienceNotifier>(Lifetime.Singleton).As<IGameplayActivatable>();
    }
}
