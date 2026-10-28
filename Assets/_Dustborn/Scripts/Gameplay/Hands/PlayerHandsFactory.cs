public sealed class PlayerHandsFactory
{
    private readonly ItemActionFactory _actions;

    public PlayerHandsFactory(ItemActionFactory actions)
    {
        _actions = actions;
    }

    public PlayerHands Create(PlayerView view, IViewImpact impact)
        => new(new FirstPersonHandsPresenter(view, impact), new PlayerAimSource(view), _actions, view.UnarmedHands);
}
