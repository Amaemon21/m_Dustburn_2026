using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "InteractSettings", menuName = "Dustborn/Interaction/Settings")]
public sealed class InteractSettings : ScriptableObject
{
    [field: SerializeField, BoxGroup("Interaction"), HorizontalLine(2f, EColor.Blue)] public InteractProperty Detection { get; private set; } = new();
}
