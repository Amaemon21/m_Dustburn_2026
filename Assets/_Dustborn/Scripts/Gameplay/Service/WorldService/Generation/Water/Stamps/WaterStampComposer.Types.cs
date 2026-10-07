using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterStampComposer
{
    private enum Status : byte
    {
        Open,
        Lake,
        Sea
    }

    private struct Node
    {
        public Vector2 Point;
        public float Area;
        public int Placement;
        public Vector2 Stamp;
        public float WidthScale;
        public float DepthScale;
    }

    private sealed class Piece
    {
        public readonly List<Node> Nodes = new();
    }

    private sealed class Line
    {
        public readonly List<Vector2> Points;
        public readonly List<float> Areas;
        public readonly float[] Arc;

        public Line(List<Vector2> points, List<float> areas)
        {
            Points = points;
            Areas = areas;
            Arc = new float[points.Count];

            for (int i = 1; i < points.Count; i++)
                Arc[i] = Arc[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        }

        public float Length => Arc[^1];

        public int Segment(float arc)
        {
            int index = Array.BinarySearch(Arc, arc);

            if (index < 0)
                index = ~index - 1;

            return Mathf.Clamp(index, 0, Points.Count - 2);
        }

        public Vector2 At(float arc)
        {
            int i = Segment(arc);
            float span = Arc[i + 1] - Arc[i];

            return Vector2.Lerp(Points[i], Points[i + 1], span < 1e-6f ? 0f : Mathf.Clamp01((arc - Arc[i]) / span));
        }

        public float AreaAt(float arc)
        {
            int i = Segment(arc);
            float span = Arc[i + 1] - Arc[i];

            return Mathf.Lerp(Areas[i], Areas[i + 1], span < 1e-6f ? 0f : Mathf.Clamp01((arc - Arc[i]) / span));
        }

        public Vector2 Tangent(float arc)
        {
            int i = Segment(arc);
            Vector2 direction = Points[i + 1] - Points[i];

            return direction.sqrMagnitude < 1e-10f ? new Vector2(1f, 0f) : direction.normalized;
        }

        public float ArcAtDistance(float from, float distance)
        {
            Vector2 start = At(from);
            float limit = distance * distance;

            for (int i = Segment(from) + 1; i < Points.Count; i++)
            {
                if ((Points[i] - start).sqrMagnitude < limit)
                    continue;

                float low = Mathf.Max(from, Arc[i - 1]), high = Arc[i];

                for (int step = 0; step < 24; step++)
                {
                    float middle = 0.5f * (low + high);

                    if ((At(middle) - start).sqrMagnitude < limit)
                        low = middle;
                    else
                        high = middle;
                }

                return high;
            }

            return -1f;
        }

        public int Nearest(Vector2 point, int from = 0)
        {
            int best = from;
            float nearest = float.MaxValue;

            for (int i = from; i < Points.Count; i++)
            {
                float distance = (Points[i] - point).sqrMagnitude;

                if (distance >= nearest)
                    continue;

                nearest = distance;
                best = i;
            }

            return best;
        }
    }

    private struct Entry
    {
        public Vector2 Point;
        public float Radius;
        public int Path;
    }

    private sealed class Socket
    {
        public Vector2 Point;
        public int Truncate;
        public List<Node> Branch;
    }

    private struct Candidate
    {
        public int Stamp;
        public int First;
        public int Last;
        public float EndArc;
        public WaterStampPlacement Placement;
        public float Key;
        public float Excess;
        public float EndLevel;
    }
}
