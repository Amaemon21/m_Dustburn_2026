using NaughtyAttributes;
using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "PlayerInputSettings", menuName = "Dustborn/Input/Player Input Settings")]
public sealed class PlayerInputSettings : InputSettings
{
    [field: SerializeField, BoxGroup("Gameplay Actions"), HorizontalLine(2f, EColor.Blue)] public InputActionReference Interact { get; private set; }
    [field: SerializeField, BoxGroup("Movement Actions"), HorizontalLine(2f, EColor.Blue)] public InputActionReference Move { get; private set; }
    [field: SerializeField, BoxGroup("Movement Actions")] public InputActionReference Look { get; private set; }
    [field: SerializeField, BoxGroup("Movement Actions")] public InputActionReference Sprint { get; private set; }
    [field: SerializeField, BoxGroup("Movement Actions")] public InputActionReference Jump { get; private set; }
    [field: SerializeField, BoxGroup("Movement Actions")] public InputActionReference Crouch { get; private set; }
}
