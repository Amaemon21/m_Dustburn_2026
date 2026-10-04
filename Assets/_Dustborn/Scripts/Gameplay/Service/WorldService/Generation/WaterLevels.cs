using System.Collections.Generic;
using UnityEngine;

public sealed class WaterLevels
{
    private const float TOLERANCE = 1e-3f;

    private readonly List<RiverPoint[]> _rivers = new();
    private readonly float[] _bodies;
    private readonly (bool Start, bool End)[] _loose;

    private WaterLevels(WaterMap water, (bool Start, bool End)[] loose)
    {
        foreach (RiverPath river in water.Rivers)
            _rivers.Add(river.Points.ToArray());

        _bodies = new float[water.Bodies.Count];

        for (int body = 0; body < _bodies.Length; body++)
            _bodies[body] = water.Bodies[body].Surface;

        _loose = loose;
    }

    public static WaterLevels Of(WaterMap water, (bool Start, bool End)[] loose)
    {
        return new WaterLevels(water, loose);
    }

    public int Changes(WaterMap water, (bool Start, bool End)[] loose)
    {
        if (water.Rivers.Count != _rivers.Count || water.Bodies.Count != _bodies.Length || loose.Length != _loose.Length)
            return int.MaxValue;

        int changes = 0;

        for (int body = 0; body < _bodies.Length; body++)
        {
            if (Mathf.Abs(water.Bodies[body].Surface - _bodies[body]) > TOLERANCE)
                changes++;
        }

        for (int river = 0; river < _rivers.Count; river++)
        {
            if (loose[river] != _loose[river])
                changes++;

            RiverPoint[] was = _rivers[river];
            List<RiverPoint> now = water.Rivers[river].Points;

            if (was.Length != now.Count)
            {
                changes += Mathf.Max(was.Length, now.Count);
                continue;
            }

            for (int i = 0; i < was.Length; i++)
            {
                if (Differs(was[i], now[i]))
                    changes++;
            }
        }

        return changes;
    }

    public string Describe(WaterMap water, (bool Start, bool End)[] loose, int limit)
    {
        var examples = new List<string>();

        for (int body = 0; body < _bodies.Length && body < water.Bodies.Count && examples.Count < limit; body++)
        {
            if (Mathf.Abs(water.Bodies[body].Surface - _bodies[body]) > TOLERANCE)
                examples.Add($"body {body} {_bodies[body]:0.00} -> {water.Bodies[body].Surface:0.00}");
        }

        for (int river = 0; river < _rivers.Count && river < water.Rivers.Count && examples.Count < limit; river++)
        {
            if (river < loose.Length && river < _loose.Length && loose[river] != _loose[river])
                examples.Add($"river {river} loose ends {_loose[river]} -> {loose[river]}");

            RiverPoint[] was = _rivers[river];
            List<RiverPoint> now = water.Rivers[river].Points;

            if (was.Length != now.Count)
            {
                examples.Add($"river {river} points {was.Length} -> {now.Count}");
                continue;
            }

            for (int i = 0; i < was.Length && examples.Count < limit; i++)
            {
                if (Differs(was[i], now[i]))
                    examples.Add($"river {river} point {i} at ({now[i].Position.x:0}, {now[i].Position.y:0}) surface {was[i].Surface:0.00} -> {now[i].Surface:0.00}, width {was[i].Width:0.0} -> {now[i].Width:0.0}, submerged {was[i].Submerged} -> {now[i].Submerged}");
            }
        }

        return string.Join("; ", examples);
    }

    private static bool Differs(RiverPoint a, RiverPoint b)
    {
        return a.Submerged != b.Submerged || Mathf.Abs(a.Surface - b.Surface) > TOLERANCE || Mathf.Abs(a.Bed - b.Bed) > TOLERANCE
            || Mathf.Abs(a.Width - b.Width) > TOLERANCE || (a.Position - b.Position).sqrMagnitude > TOLERANCE * TOLERANCE;
    }
}
