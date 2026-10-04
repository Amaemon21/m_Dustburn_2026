using NaughtyAttributes;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "UIInputSettings", menuName = "Dustborn/Input/UI Input Settings")]
public sealed class UIInputSettings : InputSettings
{
    [field: SerializeField, BoxGroup("Window Actions"), HorizontalLine(2f, EColor.Blue)]
    public InputActionReference TogglePlayerMenu { get; private set; }
    [field: SerializeField, BoxGroup("Window Actions")] public InputActionReference CloseWindow { get; private set; }
    [SerializeField, Foldout("Hotbar Actions"), Label("Slot Actions"), HorizontalLine(2f, EColor.Blue)]
    private InputActionReference[] _hotbarSlots = Array.Empty<InputActionReference>();
    public IReadOnlyList<InputActionReference> HotbarSlots => _hotbarSlots;
    [field: SerializeField, BoxGroup("Inventory Actions"), HorizontalLine(2f, EColor.Blue)]
    public InputActionReference QuickTransfer { get; private set; }
    [field: SerializeField, BoxGroup("Inventory Actions")]
    public InputActionReference Scroll { get; private set; }
    [field: SerializeField, BoxGroup("Inventory Actions")]
    public InputActionReference UseItem { get; private set; }
    [field: SerializeField, BoxGroup("Inventory Actions")]
    public InputActionReference DropItem { get; private set; }
    [field: SerializeField, BoxGroup("Container Actions"), HorizontalLine(2f, EColor.Blue)]
    public InputActionReference TakeAll { get; private set; }
    [field: SerializeField, BoxGroup("Container Actions")]
    public InputActionReference InteractClose { get; private set; }
}
