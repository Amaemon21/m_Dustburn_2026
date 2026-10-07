using System;
using System.Collections.Generic;

public sealed class ItemActionFactory
{
    private readonly Dictionary<Type, Func<ItemActionConfig, ItemActionContext, IItemAction>> _creators = new();

    public ItemActionFactory Register<TConfig>(Func<TConfig, ItemActionContext, IItemAction> create) where TConfig : ItemActionConfig
    {
        if (create == null)
            throw new ArgumentNullException(nameof(create));
        if (_creators.ContainsKey(typeof(TConfig)))
            throw new InvalidOperationException($"Item action for {typeof(TConfig).Name} is already registered");

        _creators.Add(typeof(TConfig), (config, context) => create((TConfig)config, context));
        return this;
    }

    public IItemAction Create(ItemActionConfig config, ItemActionContext context)
    {
        if (config == null)
            return null;
        if (!_creators.TryGetValue(config.GetType(), out Func<ItemActionConfig, ItemActionContext, IItemAction> create))
            throw new InvalidOperationException($"No item action is registered for {config.GetType().Name}; register it in HandsInstaller");

        return create(config, context);
    }
}
