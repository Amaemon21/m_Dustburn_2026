public sealed class ItemActionContext
{
    public HeldItem Item { get; }
    public IHeldItemPresenter Presenter { get; }
    public IAimSource Aim { get; }

    public ItemActionContext(HeldItem item, IHeldItemPresenter presenter, IAimSource aim)
    {
        Item = item;
        Presenter = presenter;
        Aim = aim;
    }
}
