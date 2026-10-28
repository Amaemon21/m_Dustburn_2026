using System;

public sealed class PlayerHands : IDisposable
{
    private enum Phase
    {
        Hidden,
        Drawing,
        Ready,
        Using,
        Holstering
    }

    private readonly IHeldItemPresenter _presenter;
    private readonly IAimSource _aim;
    private readonly ItemActionFactory _actions;
    private readonly HeldItemConfig _unarmed;

    private HeldItem _desired;
    private HeldItem? _current;
    private HeldItemConfig _config;
    private IItemAction _primary;
    private IItemAction _secondary;
    private IItemAction _active;
    private Phase _phase = Phase.Hidden;
    private HandsMotion _pose = HandsMotion.Idle;
    private float _timer;
    private float _poseHold;

    public PlayerHands(IHeldItemPresenter presenter, IAimSource aim, ItemActionFactory actions, HeldItemConfig unarmed)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _aim = aim ?? throw new ArgumentNullException(nameof(aim));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _unarmed = unarmed;
    }

    public void Hold(HeldItem item) => _desired = item;

    public void Tick(in HandsRequest request, float deltaTime)
    {
        if (!_current.HasValue || !_desired.Equals(_current.Value))
            Leave();

        switch (_phase)
        {
            case Phase.Drawing:
                if (Elapse(deltaTime))
                    _phase = Phase.Ready;
                break;
            case Phase.Holstering:
                if (Elapse(deltaTime))
                    Swap();
                break;
            case Phase.Ready:
                TryUse(request);
                Pose(request, deltaTime);
                break;
            case Phase.Using:
                if (!_active.Tick(deltaTime))
                    Finish();
                break;
        }

        _presenter.Tick(deltaTime);
    }

    private void Leave()
    {
        switch (_phase)
        {
            case Phase.Hidden:
                Swap();
                return;
            case Phase.Holstering:
                return;
            case Phase.Using:
                _active.Cancel();
                _active = null;
                break;
        }

        _presenter.Play(HandsMotion.Unequip);
        _timer = _config.HolsterTime;
        _phase = Phase.Holstering;
    }

    private void Swap()
    {
        _primary = null;
        _secondary = null;
        _active = null;
        _phase = Phase.Hidden;
        _current = _desired;
        _config = _desired.Config != null ? _desired.Config : _unarmed;

        if (!_presenter.Show(_config))
        {
            _presenter.Hide();
            _config = null;
            return;
        }

        ItemActionContext context = new(_desired, _config, _presenter, _aim);
        _primary = _actions.Create(_config.Primary, context);
        _secondary = _actions.Create(_config.Secondary, context);
        _presenter.Play(HandsMotion.Equip);
        _pose = HandsMotion.Idle;
        _poseHold = 0f;
        _timer = _config.DrawTime;
        _phase = Phase.Drawing;
    }

    private void TryUse(in HandsRequest request)
    {
        IItemAction action = request.Primary ? _primary : request.Secondary ? _secondary : null;
        if (action == null || !action.CanStart)
            return;

        _active = action;
        _active.Start();
        _pose = HandsMotion.Idle;
        _poseHold = 0f;
        _phase = Phase.Using;
    }

    private void Pose(in HandsRequest request, float deltaTime)
    {
        HandsMotion pose = request.Running ? HandsMotion.Run : request.Moving ? HandsMotion.Walk : HandsMotion.Idle;
        if (_phase != Phase.Ready)
            return;

        _poseHold -= deltaTime;
        if (pose == _pose || _poseHold > 0f && pose != HandsMotion.Run)
            return;

        if (pose == HandsMotion.Run)
        {
            StartRun();
            return;
        }

        if (_pose == HandsMotion.Run && _presenter.Play(HandsMotion.RunEnd))
        {
            _pose = HandsMotion.Idle;
            _poseHold = _config.RunEndTime;
            return;
        }

        _pose = pose;
        _presenter.Play(pose);
    }

    private void StartRun()
    {
        if (!_presenter.Play(HandsMotion.RunStart))
            _presenter.Play(HandsMotion.Run);

        _pose = HandsMotion.Run;
        _poseHold = 0f;
    }

    private void Finish()
    {
        _active = null;
        _phase = Phase.Ready;
    }

    private bool Elapse(float deltaTime)
    {
        _timer -= deltaTime;
        return _timer <= 0f;
    }

    public void Dispose()
    {
        _active?.Cancel();
        _active = null;
        _presenter.Dispose();
    }
}
