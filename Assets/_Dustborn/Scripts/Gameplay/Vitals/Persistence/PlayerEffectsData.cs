using System;
using System.Collections.Generic;

[Serializable]
public sealed class PlayerEffectsData
{
    public Dictionary<PlayerEffectType, TimedPlayerEffectData> Active = new();
}

[Serializable]
public struct TimedPlayerEffectData
{
    public float Remaining;
    public float Strength;

    public TimedPlayerEffectData(float remaining, float strength)
    {
        Remaining = remaining;
        Strength = strength;
    }
}
