public sealed class ItemActionContext
{
    public HeldItem Item { get; }
    public HeldItemConfig Config { get; }
    public IHeldItemPresenter Presenter { get; }
    public IAimSource Aim { get; }

    public ItemActionContext(HeldItem item, HeldItemConfig config, IHeldItemPresenter presenter, IAimSource aim)
    {
        Item = item;
        Config = config;
        Presenter = presenter;
        Aim = aim;
    }
}
