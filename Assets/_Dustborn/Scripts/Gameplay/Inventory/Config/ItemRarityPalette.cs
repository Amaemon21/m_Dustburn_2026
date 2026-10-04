using System;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "ItemRarityPalette", menuName = "Dustborn/Inventory/Item Rarity Palette")]
public sealed class ItemRarityPalette : ScriptableObject
{
    [Serializable]
    private sealed class Entry
    {
        public ItemRarity Rarity;
        public Sprite Sprite;
    }

    [SerializeField, BoxGroup("Rarity Sprites"), HorizontalLine(2f, EColor.Orange)]
    private Entry[] _entries =
    {
        new() { Rarity = ItemRarity.Faulty },
        new() { Rarity = ItemRarity.Poor },
        new() { Rarity = ItemRarity.Good },
        new() { Rarity = ItemRarity.Great },
        new() { Rarity = ItemRarity.Superior },
        new() { Rarity = ItemRarity.Excellent }
    };

    public Sprite SpriteOf(ItemRarity rarity)
    {
        foreach (Entry entry in _entries)
            if (entry.Rarity == rarity)
                return entry.Sprite;

        return null;
    }
}
