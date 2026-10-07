using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterStampComposer
{
    private void Own(Piece piece)
    {
        float length = 0f;

        for (int i = 1; i < piece.Nodes.Count; i++)
            length += Vector2.Distance(piece.Nodes[i - 1].Point, piece.Nodes[i].Point);

        float arc = _composedArc - length;

        for (int i = 0; i < piece.Nodes.Count; i++)
        {
            if (i > 0)
                arc += Vector2.Distance(piece.Nodes[i - 1].Point, piece.Nodes[i].Point);

            if (i % 2 == 0)
                _own.Add((piece.Nodes[i].Point, arc));
        }
    }

    private bool Overlaps(Vector2 point, float radius)
    {
        float before = _composedArc - _settings.BlendDistance - 4f * radius;
        float limit = 4f * radius * radius;

        foreach ((Vector2 other, float arc) in _own)
        {
            if (arc < before && (other - point).sqrMagnitude < limit)
                return true;
        }

        return false;
    }

    private float ShortestChord()
    {
        float shortest = float.MaxValue;

        foreach (int index in _library.Rivers)
        {
            WaterStampDefinition definition = _library.Get(index);

            if (Special(definition.Category))
                continue;

            foreach ((float from, float to) in Windows)
            {
                Span(definition, from, to, out int first, out int last);
                shortest = Mathf.Min(shortest, Vector2.Distance(definition.Centerline[first], definition.Centerline[last]) * definition.MinScale);
            }
        }

        return shortest == float.MaxValue ? float.MaxValue : shortest;
    }

    private bool Collides(Vector2 point, float radius)
    {
        int cx = Mathf.FloorToInt(point.x / INDEX_CELL), cz = Mathf.FloorToInt(point.y / INDEX_CELL);
        int reach = Mathf.CeilToInt(2f * radius / INDEX_CELL) + 1;
        WaterStampMacro macro = _macros[_path];

        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (!_index.TryGetValue(Key(cx + dx, cz + dz), out List<Entry> entries))
                    continue;

                foreach (Entry entry in entries)
                {
                    if (entry.Path == macro.JoinPath && JoinZone(point, entry.Radius + radius))
                        continue;

                    float limit = entry.Radius + radius;

                    if ((entry.Point - point).sqrMagnitude < limit * limit)
                        return true;
                }
            }
        }

        return false;
    }

    private bool JoinZone(Vector2 point, float reach)
    {
        Vector2 end = _macros[_path].Points[^1];
        float zone = 2f * reach + 50f;

        return (point - end).sqrMagnitude < zone * zone;
    }

    private void Register(WaterStampCourse course, int path)
    {
        if (course == null)
            return;

        for (int i = 0; i < course.Points.Count; i += 2)
        {
            Vector2 point = course.Points[i];
            float radius = 0.5f * _hydrology.Width(course.Areas[i]) * course.WidthScale[i] + COLLISION_PAD;
            long key = Key(Mathf.FloorToInt(point.x / INDEX_CELL), Mathf.FloorToInt(point.y / INDEX_CELL));

            if (!_index.TryGetValue(key, out List<Entry> entries))
                _index[key] = entries = new List<Entry>();

            entries.Add(new Entry { Point = point, Radius = radius, Path = path });
        }
    }

    private static long Key(int x, int z)
    {
        return ((long)x << 32) ^ (uint)z;
    }
}
