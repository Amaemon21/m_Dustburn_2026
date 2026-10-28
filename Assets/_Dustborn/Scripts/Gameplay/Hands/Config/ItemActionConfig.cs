using System;
using System.Collections.Generic;

[Serializable]
public abstract class ItemActionConfig
{
    public virtual void Validate(UnityEngine.Object owner)
    {
    }

    public virtual void CollectStats(ICollection<ItemStat> stats, HeldItemConfig held)
    {
    }
}
