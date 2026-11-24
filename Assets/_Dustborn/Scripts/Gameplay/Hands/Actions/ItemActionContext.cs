public sealed class ItemActionContext
{
    public HeldItem Item { get; }
    public IHeldItemPresenter Presenter { get; }
    public IAimSource Aim { get; }
    public MeleePresentationConfig MeleePresentation { get; }

    public ItemActionContext(HeldItem item, IHeldItemPresenter presenter, IAimSource aim, MeleePresentationConfig meleePresentation)
    {
        Item = item;
        Presenter = presenter;
        Aim = aim;
        MeleePresentation = meleePresentation;
    }
}
