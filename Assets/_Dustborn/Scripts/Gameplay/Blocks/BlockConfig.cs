using System;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "BlockConfig", menuName = "Dustborn/Blocks/Block Config")]
public sealed class BlockConfig : ScriptableObject
{
    [field: SerializeField, BoxGroup("Identity"), HorizontalLine(2f, EColor.Blue)] public BlockMaterial Material { get; private set; }

    [field: SerializeField, BoxGroup("Health"), HorizontalLine(2f, EColor.Red), Min(1)]
    public int MaxHealth { get; private set; } = 100;

    [field: SerializeField, Foldout("Harvest")] public HarvestYield[] Harvest { get; private set; } = Array.Empty<HarvestYield>();
    [field: SerializeField, Foldout("Destroy Drops")] public DestroyDrop[] DestroyDrops { get; private set; } = Array.Empty<DestroyDrop>();

    private void OnValidate()
    {
        foreach (HarvestYield yield in Harvest ?? Array.Empty<HarvestYield>())
            if (yield != null && yield.IsUnset)
                yield.ApplyDefaults();

        foreach (DestroyDrop drop in DestroyDrops ?? Array.Empty<DestroyDrop>())
        {
            if (drop == null)
                continue;
            if (drop.IsUnset)
                drop.ApplyDefaults();
            drop.Validate();
        }
    }
}
