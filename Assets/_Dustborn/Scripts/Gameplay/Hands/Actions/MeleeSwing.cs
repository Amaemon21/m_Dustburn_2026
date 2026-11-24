public sealed class MeleeSwing
{
    private readonly MeleeActionConfig _config;
    private readonly HeldItem _item;
    private readonly IAimSource _aim;
    private readonly IMeleeHitScanner _scanner;
    private readonly IHeldItemEffects _effects;
    private float _elapsed;
    private bool _active;
    private bool _resolved;

    public MeleeSwing(MeleeActionConfig config, HeldItem item, IAimSource aim, IMeleeHitScanner scanner, IHeldItemEffects effects)
    {
        _config = config;
        _item = item;
        _aim = aim;
        _scanner = scanner;
        _effects = effects;
    }

    public bool TryStart()
    {
        if (_active || !_effects.IsUsable(_item) || !_effects.CanExert(_config.StaminaCost))
            return false;

        _elapsed = 0f;
        _resolved = false;
        _active = true;
        _effects.Exert(_config.StaminaCost);
        return true;
    }

    public bool Tick(float deltaTime, out MeleeImpact? impact)
    {
        impact = null;

        if (!_active)
            return false;

        _elapsed += deltaTime;

        if (!_resolved && _elapsed >= _config.Windup)
        {
            _resolved = true;

            if (_scanner.TryHit(_aim, _config.Reach, _config.Radius, _config.HitMask, out MeleeHit hit))
            {
                StrikeInfo strike = new(_config.Damage, _config.WearPerHit, _config.Bonuses, hit.Point, hit.Normal);

                if (_effects.TryStrike(_item, hit.Target, strike))
                    impact = new MeleeImpact(hit.Point, hit.Normal);
            }
        }

        _active = _elapsed < _config.CycleTime;
        return _active;
    }

    public void Cancel() => _active = false;
}
