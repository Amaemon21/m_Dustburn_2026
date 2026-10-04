using System;
using System.Collections.Generic;

[Serializable]
public class InventoryData
{
    public List<InventoryGridData> Grids = new();
    public List<PlayerInventoryData> Players = new();
}
