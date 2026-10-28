using System;
using UnityEngine;

[Serializable]
public sealed class CarryZone
{
    [field: SerializeField, Min(0f)] public float From { get; private set; }
    [field: SerializeField] public string Label { get; private set; }
    [field: SerializeField] public Color Color { get; private set; } = Color.white;
    [field: SerializeField, Min(0f)] public float WalkSpeed { get; private set; } = 1f;
    [field: SerializeField, Min(0f)] public float SprintSpeed { get; private set; } = 1f;
    [field: SerializeField, Min(0f)] public float StaminaCost { get; private set; } = 1f;
    [field: SerializeField, Min(0f)] public float StaminaRegen { get; private set; } = 1f;
    [field: SerializeField] public bool CanSprint { get; private set; } = true;
    [field: SerializeField] public bool CanJump { get; private set; } = true;

    public bool IsUnset => string.IsNullOrEmpty(Label) && WalkSpeed == 0f && SprintSpeed == 0f && StaminaCost == 0f && StaminaRegen == 0f;

    public CarryZone()
    {
    }

    public CarryZone(float from, string label, Color color, float walkSpeed, float sprintSpeed, float staminaCost, float staminaRegen,
        bool canSprint = true, bool canJump = true)
    {
        From = from;
        Label = label;
        Color = color;
        WalkSpeed = walkSpeed;
        SprintSpeed = sprintSpeed;
        StaminaCost = staminaCost;
        StaminaRegen = staminaRegen;
        CanSprint = canSprint;
        CanJump = canJump;
    }

    public void ApplyDefaults()
    {
        Color = Color.white;
        WalkSpeed = 1f;
        SprintSpeed = 1f;
        StaminaCost = 1f;
        StaminaRegen = 1f;
        CanSprint = true;
        CanJump = true;
    }

    public static CarryZone[] Defaults() => new[]
    {
        new CarryZone(0f, "Налегке", new Color(0.36f, 0.74f, 0.36f), 1f, 1f, 1f, 1f),
        new CarryZone(0.5f, "Нагружен", new Color(0.9f, 0.8f, 0.25f), 1f, 1f, 1f, 1f),
        new CarryZone(1f, "Тяжело", new Color(0.95f, 0.55f, 0.2f), 1f, 1f, 1.25f, 1f),
        new CarryZone(1.25f, "Перегружен", new Color(0.85f, 0.25f, 0.2f), 0.9f, 0.8f, 1.5f, 1f),
        new CarryZone(1.5f, "Неподъёмно", new Color(0.55f, 0.1f, 0.1f), 0.5f, 0.5f, 1.5f, 0.75f, false, false)
    };
}
