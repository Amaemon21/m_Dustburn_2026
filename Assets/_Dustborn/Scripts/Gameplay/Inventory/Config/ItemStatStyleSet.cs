using System;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "ItemStatStyles", menuName = "Dustborn/Inventory/Item Stat Styles")]
public sealed class ItemStatStyleSet : ScriptableObject
{
    [Serializable]
    private sealed class Entry
    {
        public ItemStatType Type;
        public Sprite Icon;
    }

    [SerializeField, BoxGroup("Icons"), HorizontalLine(2f, EColor.Blue)]
    private Entry[] _icons = Array.Empty<Entry>();
    [SerializeField, BoxGroup("Colors"), HorizontalLine(2f, EColor.Green)]
    private Color _neutral = Color.white;
    [SerializeField, BoxGroup("Colors")]
    private Color _good = new(0.56f, 0.84f, 0.44f);
    [SerializeField, BoxGroup("Colors")]
    private Color _bad = new(0.9f, 0.38f, 0.32f);

    public Sprite IconOf(ItemStatType type)
    {
        foreach (Entry entry in _icons)
            if (entry != null && entry.Type == type)
                return entry.Icon;

        return null;
    }

    public Color ColorOf(ItemStatMood mood) => mood switch
    {
        ItemStatMood.Good => _good,
        ItemStatMood.Bad => _bad,
        _ => _neutral
    };
}
