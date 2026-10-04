using System;
using System.Collections.Generic;

public sealed class PlayerMovementModifiers
{
    private readonly List<IPlayerMovementModifier> _modifiers = new();

    public void Add(IPlayerMovementModifier modifier)
    {
        if (modifier == null)
            throw new ArgumentNullException(nameof(modifier));
        if (!_modifiers.Contains(modifier))
            _modifiers.Add(modifier);
    }

    public void Remove(IPlayerMovementModifier modifier) => _modifiers.Remove(modifier);

    public void Modify(ref PlayerMovementRequest request)
    {
        foreach (IPlayerMovementModifier modifier in _modifiers)
            modifier.Modify(ref request);
    }

    public void Apply(in PlayerMovementResult result, float deltaTime)
    {
        foreach (IPlayerMovementModifier modifier in _modifiers)
            modifier.Apply(result, deltaTime);
    }
}
