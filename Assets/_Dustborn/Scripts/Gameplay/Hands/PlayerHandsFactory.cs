public sealed class PlayerHandsFactory
{
    private readonly ItemActionFactory _actions;

    public PlayerHandsFactory(ItemActionFactory actions)
    {
        _actions = actions;
    }

    public PlayerHands Create(PlayerView view)
        => new(new FirstPersonHandsPresenter(view), new PlayerAimSource(view), _actions, view.UnarmedHands);
}
