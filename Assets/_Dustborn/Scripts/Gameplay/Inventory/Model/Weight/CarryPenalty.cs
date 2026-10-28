using System.Collections.Generic;
using UnityEngine;

public readonly struct CarryPenalty
{
    public CarryZone Zone { get; }
    public float WalkSpeed { get; }
    public float SprintSpeed { get; }
    public float StaminaCost { get; }
    public float StaminaRegen { get; }
    public bool CanSprint { get; }
    public bool CanJump { get; }

    public static CarryPenalty None => new(null, 1f, 1f, 1f, 1f, true, true);

    private CarryPenalty(CarryZone zone, float walkSpeed, float sprintSpeed, float staminaCost, float staminaRegen, bool canSprint, bool canJump)
    {
        Zone = zone;
        WalkSpeed = walkSpeed;
        SprintSpeed = sprintSpeed;
        StaminaCost = staminaCost;
        StaminaRegen = staminaRegen;
        CanSprint = canSprint;
        CanJump = canJump;
    }

    public static CarryZone ZoneAt(IReadOnlyList<CarryZone> zones, float load)
    {
        CarryZone current = null;
        if (zones == null)
            return null;

        foreach (CarryZone zone in zones)
            if (zone != null && zone.From <= load && (current == null || zone.From > current.From))
                current = zone;
        return current;
    }

    public static CarryPenalty At(IReadOnlyList<CarryZone> zones, float load)
    {
        CarryZone current = ZoneAt(zones, load);
        if (current == null)
            return None;

        CarryZone next = NextAfter(zones, current);
        float t = next == null ? 0f : Mathf.Clamp01((load - current.From) / (next.From - current.From));
        CarryZone target = next ?? current;
        return new CarryPenalty(current,
            Mathf.Lerp(current.WalkSpeed, target.WalkSpeed, t),
            Mathf.Lerp(current.SprintSpeed, target.SprintSpeed, t),
            Mathf.Lerp(current.StaminaCost, target.StaminaCost, t),
            Mathf.Lerp(current.StaminaRegen, target.StaminaRegen, t),
            current.CanSprint,
            current.CanJump);
    }

    private static CarryZone NextAfter(IReadOnlyList<CarryZone> zones, CarryZone current)
    {
        CarryZone next = null;
        foreach (CarryZone zone in zones)
            if (zone != null && zone.From > current.From && (next == null || zone.From < next.From))
                next = zone;
        return next;
    }
}
