using System;
using UnityEngine;

public sealed class PlayerPlacementRepository
{
    private readonly ISaveService _saves;

    public PlayerPlacementData Origin { get; }

    public PlayerPlacementRepository(ISaveService saves)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        Origin = saves.Get<PlayerPlacementData>();
    }

    public bool TryGet(out Vector3 position, out float yaw)
    {
        position = Origin.Position;
        yaw = Origin.Yaw;
        return Origin.Saved;
    }

    public void Write(Vector3 position, float yaw)
    {
        Origin.Saved = true;
        Origin.Position = position;
        Origin.Yaw = yaw;
        _saves.MarkDirty<PlayerPlacementData>();
    }
}
