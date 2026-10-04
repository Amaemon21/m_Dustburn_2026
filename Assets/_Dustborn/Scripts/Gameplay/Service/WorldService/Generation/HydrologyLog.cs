using System.Collections.Generic;
using UnityEngine;

public enum HydrologyAction
{
    Captured,
    StubDropped,
    Shortened,
    Dropped,
    Extended,
    BodyCrowded,
    BodyPerched,
    MergedHead
}

public readonly struct HydrologyEvent
{
    public readonly HydrologyAction Action;
    public readonly int Course;
    public readonly Vector2 At;
    public readonly string Detail;

    public HydrologyEvent(HydrologyAction action, int course, Vector2 at, string detail)
    {
        Action = action;
        Course = course;
        At = at;
        Detail = detail;
    }

    public override string ToString()
    {
        string owner = Course >= 0 ? $"course {Course}" : "body";
        return $"{Action} {owner} at ({At.x:0}, {At.y:0}): {Detail}";
    }
}

public sealed class HydrologyLog
{
    private readonly List<HydrologyEvent> _events = new();

    public IReadOnlyList<HydrologyEvent> Events => _events;

    public void Note(HydrologyAction action, int course, Vector2 at, string detail)
    {
        _events.Add(new HydrologyEvent(action, course, at, detail));
    }

    public int Count(HydrologyAction action)
    {
        int count = 0;

        foreach (HydrologyEvent entry in _events)
        {
            if (entry.Action == action)
                count++;
        }

        return count;
    }

    public IEnumerable<HydrologyEvent> Near(Vector2 point, float reach)
    {
        foreach (HydrologyEvent entry in _events)
        {
            if ((entry.At - point).sqrMagnitude <= reach * reach)
                yield return entry;
        }
    }
}
