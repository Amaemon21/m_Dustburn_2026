using NaughtyAttributes;
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InventoryItems", menuName = "Dustborn/Inventory/Database/Item Database")]
public sealed class InventoryItemDatabase : ScriptableObject
{
    [SerializeField, BoxGroup("Item Catalog"), Label("Items"), HorizontalLine(2f, EColor.Blue)] private InventoryItem[] _items = Array.Empty<InventoryItem>();
    public IReadOnlyList<InventoryItem> Items => _items;

#if UNITY_EDITOR
    [Button("Add All Inventory Items")]
    private void AddAllInventoryItems()
    {
        var items = new List<InventoryItem>();
        var knownItems = new HashSet<InventoryItem>();

        foreach (InventoryItem item in _items ?? Array.Empty<InventoryItem>())
            if (item != null && knownItems.Add(item))
                items.Add(item);

        string[] paths = Array.ConvertAll(UnityEditor.AssetDatabase.FindAssets($"t:{nameof(InventoryItem)}", new[] { "Assets" }), UnityEditor.AssetDatabase.GUIDToAssetPath);
        Array.Sort(paths, StringComparer.Ordinal);

        foreach (string path in paths)
            foreach (UnityEngine.Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is InventoryItem item && knownItems.Add(item))
                    items.Add(item);

        UnityEditor.Undo.RecordObject(this, "Add All Inventory Items");
        _items = items.ToArray();
        OnValidate();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    public void EditorAdd(InventoryItem item)
    {
        if (item == null || Array.IndexOf(_items, item) >= 0)
            return;

        Array.Resize(ref _items, _items.Length + 1);
        _items[^1] = item;
    }
#endif

    private void OnValidate()
    {
        _items ??= Array.Empty<InventoryItem>();
        ListDuplicates.Clear(_items, item => item, index => _items[index] = null, this, "Items");
        ListDuplicates.Warn(_items, item => item.ItemId, this, "Items (by Item ID)");
    }
}
