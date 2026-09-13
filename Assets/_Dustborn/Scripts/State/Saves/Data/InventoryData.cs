using System;
using System.Collections.Generic;

[Serializable]
public class InventoryData
{
    public Dictionary<string, int> Items = new();
    public List<string> Equipped = new();
}
