using System.Collections.Generic;
using UnityEngine;

public sealed partial class TileStreetBuilder
{
    private sealed class HalfStreet
    {
        public SettlementTile Tile;
        public TilePorts Side;
        public RoadKind Kind;
        public Vector2 Center;
        public Vector2 Port;
        public HalfStreet Pair;
        public HalfStreet Link;
        public bool Used;
        public readonly List<float> Anchors = new();
    }

    private readonly struct Court
    {
        public readonly HalfStreet Host;
        public readonly float Along;
        public readonly TilePorts Toward;
        public readonly float Length;

        public Court(HalfStreet host, float along, TilePorts toward, float length)
        {
            Host = host;
            Along = along;
            Toward = toward;
            Length = length;
        }
    }
}
