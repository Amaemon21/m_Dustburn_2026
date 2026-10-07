using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using Random = System.Random;

public sealed class BlockService : IDecorObjectHook, IDisposable
{
    private readonly BlockRepository _repository;
    private readonly Dictionary<string, Block> _blocks = new();
    private readonly Dictionary<string, BlockView> _views = new();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly Random _random = new();

    public BlockService(BlockRepository repository)
    {
        _repository = repository;
    }

    public bool IsRemoved(DecorKey key) => _repository.IsDestroyed(key.ToString());

    public void Spawned(GameObject copy, DecorKey key)
    {
        if (copy.TryGetComponent(out BlockView view))
            Attach(view, key.ToString());
    }

    public void Attach(BlockView view, string id)
    {
        if (view.Config == null)
        {
            Debug.LogWarning($"{nameof(BlockService)}: block '{view.name}' has no {nameof(BlockConfig)}, it cannot be damaged", view);
            return;
        }

        if (_repository.IsDestroyed(id))
        {
            view.Remove();
            return;
        }

        view.Attach(this, id);
        _views[id] = view;
    }

    public IDamageable Resolve(BlockView view)
    {
        if (view.Id == null)
            return null;
        if (_blocks.TryGetValue(view.Id, out Block block))
            return block;

        block = new Block(view.Id, view.Config, _repository.DamageOf(view.Id), _random);
        _blocks.Add(view.Id, block);
        _subscriptions.Add(block.Health.Skip(1).Subscribe(_ => OnDamaged(block)));
        return block;
    }

    public void Detach(BlockView view)
    {
        if (view.Id != null && _views.TryGetValue(view.Id, out BlockView current) && current == view)
            _views.Remove(view.Id);
    }

    private void OnDamaged(Block block)
    {
        if (!block.IsDestroyed)
        {
            _repository.SetDamage(block.Id, block.Damage);
            return;
        }

        _repository.MarkDestroyed(block.Id);
        if (_views.Remove(block.Id, out BlockView view) && view != null)
            view.Remove();
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        foreach (Block block in _blocks.Values)
            block.Dispose();
        _blocks.Clear();
        _views.Clear();
    }
}
