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
        public Color Color = Color.white;
    }

    [SerializeField, BoxGroup("Rarity Colors"), HorizontalLine(2f, EColor.Orange)]
    private Entry[] _entries =
    {
        new() { Rarity = ItemRarity.Faulty, Color = new Color(0.55f, 0.37f, 0.23f) },
        new() { Rarity = ItemRarity.Poor, Color = new Color(0.85f, 0.48f, 0.13f) },
        new() { Rarity = ItemRarity.Good, Color = new Color(0.85f, 0.78f, 0.16f) },
        new() { Rarity = ItemRarity.Great, Color = new Color(0.25f, 0.68f, 0.25f) },
        new() { Rarity = ItemRarity.Superior, Color = new Color(0.25f, 0.44f, 0.85f) },
        new() { Rarity = ItemRarity.Excellent, Color = new Color(0.63f, 0.25f, 0.85f) }
    };

    public bool TryGetColor(ItemRarity rarity, out Color color)
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Rarity != rarity)
                continue;

            color = entry.Color;
            return true;
        }

        color = default;
        return false;
    }
}
