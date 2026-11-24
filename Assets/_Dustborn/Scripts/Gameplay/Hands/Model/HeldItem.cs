using System;
using UnityEngine;

public readonly struct HeldItem : IEquatable<HeldItem>
{
    public string OwnerId { get; }
    public Vector2Int Coordinates { get; }
    public string ItemId { get; }
    public HeldItemConfig Config { get; }
    public bool IsEmpty => ItemId == null;

    public HeldItem(string ownerId, Vector2Int coordinates)
    {
        OwnerId = ownerId;
        Coordinates = coordinates;
        ItemId = null;
        Config = null;
    }

    public HeldItem(string ownerId, Vector2Int coordinates, string itemId) : this(ownerId, coordinates)
    {
        ItemId = itemId;
    }

    public HeldItem(string ownerId, Vector2Int coordinates, string itemId, HeldItemConfig config) : this(ownerId, coordinates, itemId)
    {
        Config = config;
    }

    public bool Equals(HeldItem other)
    {
        if (IsEmpty || other.IsEmpty)
            return IsEmpty && other.IsEmpty;
        return OwnerId == other.OwnerId && Coordinates == other.Coordinates && ItemId == other.ItemId;
    }

    public override bool Equals(object obj) => obj is HeldItem other && Equals(other);
    public override int GetHashCode() => IsEmpty ? 0 : HashCode.Combine(OwnerId, Coordinates, ItemId);
}
