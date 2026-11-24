using VContainer;
using VContainer.Unity;

public sealed class HandsInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<IMeleeHitScanner, MeleeHitScanner>(Lifetime.Singleton);
        builder.Register<LocalHeldItemEffects>(Lifetime.Singleton).As<IHeldItemEffects>();
        builder.Register<ItemActionFactory>(CreateActions, Lifetime.Singleton);
        builder.Register<PlayerHandsFactory>(Lifetime.Singleton);
        builder.Register(resolver => new HeldItemSelection(resolver.Resolve<LocalPlayerInventory>().Proxy, resolver.Resolve<ItemCatalog>()), Lifetime.Singleton);
        builder.RegisterEntryPoint<PlayerHandsController>().AsSelf().As<IGameplayActivatable>();
    }

    private static ItemActionFactory CreateActions(IObjectResolver resolver)
    {
        IMeleeHitScanner scanner = resolver.Resolve<IMeleeHitScanner>();
        IHeldItemEffects effects = resolver.Resolve<IHeldItemEffects>();
        return new ItemActionFactory()
            .Register<MeleeActionConfig>((config, context) =>
            {
                MeleeSwing swing = new(config, context.Item, context.Aim, scanner, effects);
                return new MeleeAction(swing, context.Presenter, context.MeleePresentation);
            });
    }
}
