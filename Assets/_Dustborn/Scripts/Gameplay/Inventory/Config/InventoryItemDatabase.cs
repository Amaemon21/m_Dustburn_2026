using NaughtyAttributes;
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InventoryItems", menuName = "Dustborn/Inventory/Database/Item Database")]
public sealed class InventoryItemDatabase : ScriptableObject
{
    [SerializeField, BoxGroup("Item Catalog"), Label("Items"), HorizontalLine(2f, EColor.Blue)] private InventoryItem[] _items = Array.Empty<InventoryItem>();
    public IReadOnlyList<InventoryItem> Items => _items;
}