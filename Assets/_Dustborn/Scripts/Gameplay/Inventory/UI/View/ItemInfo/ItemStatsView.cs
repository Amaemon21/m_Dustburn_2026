using System.Collections.Generic;
using UnityEngine;

public sealed class ItemStatsView : MonoBehaviour
{
    private readonly List<ItemStat> _stats = new();
    private ItemStatView[] _rows;

    private IReadOnlyList<ItemStatView> Rows => _rows ??= GetComponentsInChildren<ItemStatView>(true);

    public void Show(InventoryItem item, InventorySlotState state)
    {
        _stats.Clear();
        item?.CollectStats(_stats, state);

        foreach (ItemStatView row in Rows)
        {
            if (TryFind(row.Type, out float value))
                row.Show(value);
            else
                row.Hide();
        }
    }

    public void Clear() => Show(null, default);

    private bool TryFind(ItemStatType type, out float value)
    {
        foreach (ItemStat stat in _stats)
        {
            if (stat.Type != type)
                continue;

            value = stat.Value;
            return true;
        }

        value = 0f;
        return false;
    }
}
