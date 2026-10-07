using VContainer;
using VContainer.Unity;

public sealed class DamageInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<DamageFeed>(Lifetime.Singleton);
        builder.Register<TargetHealthService>(Lifetime.Singleton);
    }
}
