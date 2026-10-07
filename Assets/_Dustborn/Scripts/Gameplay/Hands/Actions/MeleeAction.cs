public sealed class MeleeAction : IItemAction
{
    private readonly MeleeActionConfig _config;
    private readonly ItemActionContext _context;
    private readonly MeleeHitScanner _scanner;
    private readonly IHeldItemEffects _effects;
    private float _elapsed;
    private bool _struck;

    public MeleeAction(MeleeActionConfig config, ItemActionContext context, MeleeHitScanner scanner, IHeldItemEffects effects)
    {
        _config = config;
        _context = context;
        _scanner = scanner;
        _effects = effects;
    }

    public bool CanStart => _effects.IsUsable(_context.Item);

    public void Start()
    {
        _elapsed = 0f;
        _struck = false;
        _context.Presenter.Play(_config.Motion);
    }

    public bool Tick(float deltaTime)
    {
        _elapsed += deltaTime;
        if (!_struck && _elapsed >= _config.Duration * _config.HitTime)
        {
            _struck = true;
            Strike();
        }
        return _elapsed < _config.Duration;
    }

    public void Cancel() => _struck = true;

    private void Strike()
    {
        if (!_scanner.TryHit(_context.Aim, _config.Reach, _config.Radius, _config.HitMask, out MeleeHit hit))
            return;

        StrikeInfo strike = new(_config.Damage, _config.WearPerHit, _config.Bonuses, hit.Point, hit.Normal);
        _effects.Strike(_context.Item, hit.Target, strike);
    }
}
