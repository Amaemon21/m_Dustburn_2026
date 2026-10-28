using System;
using System.Collections.Generic;

[Serializable]
public class WorldSaveData
{
    public string WorldId;
    public string WorldName;
    public string Fingerprint;
    public int Seed;
    public int WorldSize;
    public Dictionary<string, string> VoxelEdits = new();
}
