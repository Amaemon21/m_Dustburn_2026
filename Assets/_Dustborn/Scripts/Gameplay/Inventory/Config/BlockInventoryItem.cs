using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "BlockInventoryItem", menuName = "Dustborn/Inventory/Items/Block Inventory Item")]
public sealed class BlockInventoryItem : InventoryItem
{
    [field: SerializeField, BoxGroup("Building"), Required]
    [field: Tooltip("A building piece with a pivot at the centre of the base cell, root box colliders and BlockView.")]
    public BlockView Prefab { get; private set; }
    [field: SerializeField, BoxGroup("Building"), Min(1)]
    public int Height { get; private set; } = 1;
    [field: SerializeField, BoxGroup("Building"), Min(1f)]
    public float Reach { get; private set; } = 5f;
    [field: SerializeField, BoxGroup("Building"), Min(0.1f)]
    public float PlacementInterval { get; private set; } = 0.25f;
    [field: SerializeField, BoxGroup("Building")]
    public LayerMask SurfaceMask { get; private set; } = ~((1 << 2) | (1 << 6) | (1 << 7));

    protected override ItemCategory DefaultCategory => ItemCategory.Building;
}
