using System;

[Serializable]
public sealed class PlayerInventoryData
{
    public string OwnerId;
    public string BackpackId;
    public string HotbarId;
    public string EquipmentId;
    public int SelectedHotbarSlot;
}
