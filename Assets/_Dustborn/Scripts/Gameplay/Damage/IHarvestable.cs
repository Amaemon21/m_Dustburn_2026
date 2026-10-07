using System.Collections.Generic;

public interface IHarvestable
{
    void Harvest(DamageResult result, float multiplier, ICollection<InventorySlotState> yields);
}
