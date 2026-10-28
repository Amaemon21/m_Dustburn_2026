using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "HeldItem", menuName = "Dustborn/Hands/Held Item")]
public sealed class HeldItemConfig : ScriptableObject
{
    [field: SerializeField, BoxGroup("View"), HorizontalLine(2f, EColor.Blue)]
    public GameObject ViewPrefab { get; private set; }
    [field: SerializeField, BoxGroup("View"), Required]
    public HandsAnimationSet Animations { get; private set; }

    [field: SerializeField, BoxGroup("Timing"), HorizontalLine(2f, EColor.Green), Min(0f)]
    public float DrawTime { get; private set; } = 0.75f;
    [field: SerializeField, BoxGroup("Timing"), Min(0f)]
    public float HolsterTime { get; private set; } = 0.75f;
    [field: SerializeField, BoxGroup("Timing"), Min(0f)]
    public float RunEndTime { get; private set; } = 0.3f;

    [field: SerializeReference, BoxGroup("Actions"), HorizontalLine(2f, EColor.Orange), SubclassSelector]
    public ItemActionConfig Primary { get; private set; }
    [field: SerializeReference, BoxGroup("Actions"), SubclassSelector]
    public ItemActionConfig Secondary { get; private set; }

    public void CollectStats(ICollection<ItemStat> stats)
    {
        Primary?.CollectStats(stats, this);
    }

    private void OnValidate()
    {
        Primary?.Validate(this);
        Secondary?.Validate(this);
    }
}
