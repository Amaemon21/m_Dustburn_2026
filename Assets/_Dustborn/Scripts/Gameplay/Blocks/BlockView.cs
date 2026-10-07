using System;
using NaughtyAttributes;
using R3;
using UnityEngine;
using Random = System.Random;

[DisallowMultipleComponent]
public sealed class BlockView : MonoBehaviour, IDamageableView, IDecorObject
{
    private const string LOCAL_PREFIX = "local/";

    private static readonly Random RANDOM = new();

    [SerializeField, Required] private BlockConfig _config;

    private BlockService _service;
    private Block _local;
    private IDisposable _localDestroyed;

    public BlockConfig Config => _config;
    public string Id { get; private set; }
    public IDamageable Damageable => _service != null ? _service.Resolve(this) : Local();

    public void Attach(BlockService service, string id)
    {
        _service = service;
        Id = id;
    }

    public void Remove()
    {
        _service = null;
        Destroy(gameObject);
    }

    private IDamageable Local()
    {
        if (_local != null || _config == null)
            return _local;

        _local = new Block(LOCAL_PREFIX + GetInstanceID(), _config, 0, RANDOM);
        _localDestroyed = _local.Health.Where(health => health <= 0).Take(1).Subscribe(_ => Remove());
        return _local;
    }

    private void OnDestroy()
    {
        _service?.Detach(this);
        _service = null;
        _localDestroyed?.Dispose();
        _local?.Dispose();
    }
}
