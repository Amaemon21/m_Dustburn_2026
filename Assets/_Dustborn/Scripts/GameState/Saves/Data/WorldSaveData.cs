using System;
using System.Collections.Generic;

[Serializable]
public class WorldSaveData
{
    public int Seed;
    public int WorldSize;
    public Dictionary<string, string> VoxelEdits = new();
}
