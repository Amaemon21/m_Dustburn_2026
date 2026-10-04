using System;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "ContainerConfig", menuName = "Dustborn/Containers/Container Config")]
public sealed class ContainerConfig : InteractableConfig
{
    [field: SerializeField, BoxGroup("Storage"), Label("Grid Size"), HorizontalLine(2f, EColor.Blue)]
    public Vector2Int Size { get; private set; } = new(6, 5);
    [field: SerializeField, Foldout("Starting Loot")] public ContainerLoot[] Loot { get; private set; } = Array.Empty<ContainerLoot>();
}
