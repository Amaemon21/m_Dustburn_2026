using UnityEngine;

public sealed class MeleeAction : IItemAction
{
    private readonly MeleeActionConfig _config;
    private readonly ItemActionContext _context;
    private readonly MeleeHitScanner _scanner;
    private readonly IHeldItemEffects _effects;
    private readonly HandsMotionClip _motion;
    private float _elapsed;
    private bool _struck;

    public MeleeAction(MeleeActionConfig config, ItemActionContext context, MeleeHitScanner scanner, IHeldItemEffects effects)
    {
        _config = config;
        _context = context;
        _scanner = scanner;
        _effects = effects;
        _motion = MotionOf(config, context.Config);
    }

    public bool CanStart => _motion != null && _effects.IsUsable(_context.Item) && _effects.CanExert(_config.StaminaCost);

    public void Start()
    {
        _elapsed = 0f;
        _struck = false;
        _effects.Exert(_config.StaminaCost);
        _context.Presenter.Play(_config.Motion);
    }

    public bool Tick(float deltaTime)
    {
        _elapsed += deltaTime;
        if (!_struck && _elapsed >= _motion.ImpactTime)
        {
            _struck = true;
            Strike();
        }
        return _elapsed < _motion.PlayTime + _config.Recovery;
    }

    public void Cancel() => _struck = true;

    private void Strike()
    {
        if (!_scanner.TryHit(_context.Aim, _config.Reach, _config.Radius, _config.HitMask, out MeleeHit hit))
            return;

        StrikeInfo strike = new(_config.Damage, _config.WearPerHit, _config.Bonuses, hit.Point, hit.Normal);
        _effects.Strike(_context.Item, hit.Target, strike);
        _context.Presenter.Impact(_config.ImpactShake);
    }

    private static HandsMotionClip MotionOf(MeleeActionConfig config, HeldItemConfig item)
    {
        HandsMotionClip motion = item == null || item.Animations == null ? null : item.Animations.Find(config.Motion);

        if (motion == null)
            Debug.LogError($"{nameof(MeleeAction)}: no '{config.Motion}' motion in the item's animation set, the swing has no timing and never starts", item);
        else if (motion.Impact <= 0f)
            Debug.LogWarning($"{nameof(MeleeAction)}: the '{config.Motion}' motion has no Impact, the swing lands on its first frame");

        return motion;
    }
}
