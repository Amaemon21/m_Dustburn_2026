using System;
using Newtonsoft.Json;
using UnityEngine;

[Serializable]
public sealed class PlacedBlockData
{
    public string Id;
    public string ItemId;
    public int X;
    public int Y;
    public int Z;
    public int Rotation;
    public int Height = 1;
    public bool? Grounded;

    [JsonIgnore] public int CellHeight => Math.Max(1, Height);

    public Vector3Int GetCell(int level = 0) => new(X, Y + level, Z);
}
