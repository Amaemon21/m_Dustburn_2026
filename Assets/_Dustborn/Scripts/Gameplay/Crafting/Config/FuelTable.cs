using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "Fuel", menuName = "Dustborn/Crafting/Fuel Table")]
public sealed class FuelTable : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [field: SerializeField, Required] public InventoryItem Item { get; private set; }
        [field: SerializeField, Min(0.1f)] public float BurnSeconds { get; private set; } = 10f;
    }

    [SerializeField, BoxGroup("Fuel"), Label("Entries"), HorizontalLine(2f, EColor.Orange)]
    private Entry[] _entries = Array.Empty<Entry>();

    public IReadOnlyList<Entry> Entries => _entries;
}
