using System.Collections.Generic;
using UnityEngine;

public sealed partial class TileStreetBuilder
{
    private void Chain(SettlementLayout layout, List<HalfStreet> halves)
    {
        foreach (HalfStreet half in halves)
        {
            if (half.Used)
                continue;

            if (half.Pair == null)
                Walk(layout, half, true);
        }

        foreach (HalfStreet half in halves)
        {
            if (half.Used || half.Link != null)
                continue;

            Walk(layout, half, false);
        }

        foreach (HalfStreet half in halves)
        {
            if (!half.Used && half.Tile.Shape == TileShape.Straight)
                Walk(layout, half, true);
        }

        foreach (HalfStreet half in halves)
        {
            if (!half.Used)
                Walk(layout, half, true);
        }
    }

    private void Walk(SettlementLayout layout, HalfStreet start, bool fromCenter)
    {
        var points = new List<Vector2>();
        HalfStreet current = start;
        bool enteringCenter = fromCenter;
        RoadKind kind = start.Kind;

        while (current != null && !current.Used)
        {
            if (current.Kind != kind && points.Count >= 2)
            {
                Flush(layout, points, kind);
                Vector2 joint = points[^1];
                points.Clear();
                points.Add(joint);
                kind = current.Kind;
            }

            current.Used = true;
            AppendHalf(current, enteringCenter, points);

            HalfStreet next;

            if (enteringCenter)
            {
                next = current.Link;
                enteringCenter = false;
            }
            else
            {
                next = current.Pair;
                enteringCenter = true;
            }

            current = next;
        }

        Flush(layout, points, kind);
    }

    private void AppendHalf(HalfStreet half, bool fromCenter, List<Vector2> points)
    {
        Vector2 direction = half.Port - half.Center;
        float length = direction.magnitude;
        Vector2 unit = direction / Mathf.Max(0.001f, length);
        var anchors = new List<float>(half.Anchors);

        anchors.Sort();

        if (!fromCenter)
            anchors.Reverse();

        Add(points, fromCenter ? half.Center : half.Port);

        foreach (float anchor in anchors)
            Add(points, half.Center + unit * anchor);

        Add(points, fromCenter ? half.Port : half.Center);
    }

    private static void Add(List<Vector2> points, Vector2 point)
    {
        if (points.Count > 0 && (points[^1] - point).sqrMagnitude < 1e-4f)
            return;

        points.Add(point);
    }

    private void Flush(SettlementLayout layout, List<Vector2> points, RoadKind kind)
    {
        if (points.Count < 2)
            return;

        AddNode(layout, points[0]);
        AddNode(layout, points[^1]);

        Vector2[] shaped = Fillet(points, RoadKindProfile.For(_config, kind).MinCurveRadius);

        layout.Streets.Add(RoadKindProfile.Create(_config, shaped, kind));
    }

    private static void AddNode(SettlementLayout layout, Vector2 point)
    {
        foreach (Vector2 existing in layout.StreetNodes)
        {
            if ((existing - point).sqrMagnitude < 1e-2f)
                return;
        }

        layout.StreetNodes.Add(point);
    }
}
