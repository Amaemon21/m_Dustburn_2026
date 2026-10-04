using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "ForgeSmelting", menuName = "Dustborn/Crafting/Forge Smelting Table")]
public sealed class ForgeSmeltingTable : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [field: SerializeField, Required] public InventoryItem Item { get; private set; }
        [field: SerializeField] public ForgeMaterial Material { get; private set; } = ForgeMaterial.Iron;
        [field: SerializeField, Min(1)] public int Units { get; private set; } = 1;
    }

    [SerializeField, BoxGroup("Smelting"), Label("Entries"), HorizontalLine(2f, EColor.Orange)]
    private Entry[] _entries = Array.Empty<Entry>();

    public IReadOnlyList<Entry> Entries => _entries;
}
