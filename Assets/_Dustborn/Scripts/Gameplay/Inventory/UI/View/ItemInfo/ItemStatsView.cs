using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public sealed class ItemStatsView : MonoBehaviour
{
    [SerializeField, Required] private ItemStatStyleSet _styles;
    [SerializeField, Required] private ItemStatView _template;
    [SerializeField] private ItemStatType[] _hidden = { ItemStatType.Durability };

    private readonly List<ItemStat> _stats = new();
    private List<ItemStatView> _rows;

    public void Show(InventoryItem item, InventorySlotState state)
    {
        _stats.Clear();
        if (item != null)
            item.CollectStats(_stats, state);

        int shown = 0;
        foreach (ItemStat stat in _stats)
        {
            if (IsHidden(stat.Type))
                continue;

            Row(shown++).Show(_styles.IconOf(stat.Type), ItemStatText.Label(stat), ItemStatText.Value(stat),
                _styles.ColorOf(ItemStatText.Mood(stat)));
        }

        List<ItemStatView> rows = Rows;
        for (int i = shown; i < rows.Count; i++)
            rows[i].Hide();
    }

    public void Clear() => Show(null, default);

    private List<ItemStatView> Rows => _rows ??= new List<ItemStatView>(_template.transform.parent.GetComponentsInChildren<ItemStatView>(true));

    private ItemStatView Row(int index)
    {
        List<ItemStatView> rows = Rows;
        while (rows.Count <= index)
            rows.Add(Instantiate(_template, _template.transform.parent));

        return rows[index];
    }

    private bool IsHidden(ItemStatType type)
    {
        foreach (ItemStatType hidden in _hidden)
            if (hidden == type)
                return true;

        return false;
    }
}
