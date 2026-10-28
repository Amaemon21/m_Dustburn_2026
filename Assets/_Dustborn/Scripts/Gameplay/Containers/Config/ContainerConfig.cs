using System;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "ContainerConfig", menuName = "Dustborn/Containers/Container Config")]
public sealed class ContainerConfig : InteractableConfig
{
    [field: SerializeField, BoxGroup("Storage"), Label("Grid Size"), HorizontalLine(2f, EColor.Blue)]
    public Vector2Int Size { get; private set; } = new(6, 5);
    [field: SerializeField, Foldout("Starting Loot")] public ContainerLoot[] Loot { get; private set; } = Array.Empty<ContainerLoot>();
    [field: SerializeField, Foldout("Starting Loot"), Min(0)] public int MaxStacks { get; private set; }

#if UNITY_EDITOR
    public void EditorSetup(string displayName, Vector2Int size, ContainerLoot[] loot, int maxStacks)
    {
        EditorSetName(displayName);
        Size = size;
        Loot = loot ?? Array.Empty<ContainerLoot>();
        MaxStacks = maxStacks;
    }
#endif

    private void OnValidate()
    {
        Loot ??= Array.Empty<ContainerLoot>();
        ListDuplicates.Clear(Loot, entry => entry.Item, index => Loot[index].ClearItem(), this, nameof(Loot));
    }
}
