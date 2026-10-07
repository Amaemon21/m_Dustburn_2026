using System.Collections.Generic;
using UnityEngine;

public sealed partial class RegionalGraphPlanner
{
    private List<Candidate> AddLoops(IReadOnlyList<Hub> hubs, List<Candidate> candidates, int[] degree, List<Candidate> chosen)
    {
        var loops = new List<Candidate>();
        int count = hubs.Count;
        int target = Mathf.FloorToInt(count * _config.TargetLinkRatio);
        var midpoints = new List<Vector2>();

        while (chosen.Count < target)
        {
            float[,] network = NetworkDistances(count, chosen);
            Candidate best = null;
            float bestBenefit = 0f;

            RefusedAngle = RefusedCrossing = RefusedSpacing = RefusedDegree = RefusedDetour = 0;

            foreach (Candidate candidate in candidates)
            {
                if (candidate.Used || candidate.Length > _config.MaxLinkLength || candidate.Water > _config.MaxWaterExposure)
                    continue;

                float ratio = network[candidate.From, candidate.To] / Mathf.Max(1f, candidate.Estimate);

                if (ratio < _config.MinLoopDetour)
                {
                    RefusedDetour++;
                    continue;
                }

                if (degree[candidate.From] >= MaxLinks(hubs[candidate.From]) || degree[candidate.To] >= MaxLinks(hubs[candidate.To]))
                {
                    RefusedDegree++;
                    continue;
                }

                if (IsShallow(hubs, candidate, chosen))
                {
                    RefusedAngle++;
                    continue;
                }

                if (Crosses(hubs, candidate, chosen))
                {
                    RefusedCrossing++;
                    continue;
                }

                Vector2 middle = (hubs[candidate.From].Position + hubs[candidate.To].Position) * 0.5f;

                if (IsCrowded(midpoints, middle))
                {
                    RefusedSpacing++;
                    continue;
                }

                float benefit = (ratio - 1f) * Mathf.Sqrt(Importance(hubs[candidate.From]) * Importance(hubs[candidate.To]));

                if (benefit <= bestBenefit)
                    continue;

                bestBenefit = benefit;
                best = candidate;
            }

            if (best == null)
                break;

            best.Used = true;
            degree[best.From]++;
            degree[best.To]++;
            chosen.Add(best);
            loops.Add(best);
            midpoints.Add((hubs[best.From].Position + hubs[best.To].Position) * 0.5f);
        }

        return loops;
    }

    private static float[,] NetworkDistances(int count, List<Candidate> links)
    {
        var distance = new float[count, count];

        for (int i = 0; i < count; i++)
        {
            for (int j = 0; j < count; j++)
                distance[i, j] = i == j ? 0f : float.MaxValue * 0.25f;
        }

        foreach (Candidate link in links)
        {
            distance[link.From, link.To] = Mathf.Min(distance[link.From, link.To], link.Estimate);
            distance[link.To, link.From] = distance[link.From, link.To];
        }

        for (int k = 0; k < count; k++)
        {
            for (int i = 0; i < count; i++)
            {
                float viaK = distance[i, k];

                for (int j = 0; j < count; j++)
                {
                    float candidate = viaK + distance[k, j];

                    if (candidate < distance[i, j])
                        distance[i, j] = candidate;
                }
            }
        }

        return distance;
    }

    private bool IsShallow(IReadOnlyList<Hub> hubs, Candidate candidate, List<Candidate> links)
    {
        float cosLimit = Mathf.Cos(_config.MinLinkAngle * Mathf.Deg2Rad);

        foreach (Candidate link in links)
        {
            if (Shallow(hubs, candidate.From, candidate.To, link, cosLimit) || Shallow(hubs, candidate.To, candidate.From, link, cosLimit))
                return true;
        }

        return false;
    }

    private static bool Shallow(IReadOnlyList<Hub> hubs, int node, int toward, Candidate link, float cosLimit)
    {
        if (link.From != node && link.To != node)
            return false;

        Vector2 origin = hubs[node].Position;
        Vector2 first = (hubs[toward].Position - origin).normalized;
        Vector2 second = (hubs[link.Other(node)].Position - origin).normalized;

        return Vector2.Dot(first, second) > cosLimit;
    }

    private static bool Crosses(IReadOnlyList<Hub> hubs, Candidate candidate, List<Candidate> links)
    {
        Vector2 a = hubs[candidate.From].Position;
        Vector2 b = hubs[candidate.To].Position;

        foreach (Candidate link in links)
        {
            if (link.From == candidate.From || link.From == candidate.To || link.To == candidate.From || link.To == candidate.To)
                continue;

            if (SegmentsCross(a, b, hubs[link.From].Position, hubs[link.To].Position))
                return true;
        }

        return false;
    }

    private bool IsCrowded(List<Vector2> midpoints, Vector2 middle)
    {
        float limitSqr = _config.LoopSpacing * _config.LoopSpacing;

        foreach (Vector2 other in midpoints)
        {
            if ((other - middle).sqrMagnitude < limitSqr)
                return true;
        }

        return false;
    }

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = Cross(c, d, a);
        float d2 = Cross(c, d, b);
        float d3 = Cross(a, b, c);
        float d4 = Cross(a, b, d);

        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float Cross(Vector2 origin, Vector2 end, Vector2 point)
    {
        return (end.x - origin.x) * (point.y - origin.y) - (end.y - origin.y) * (point.x - origin.x);
    }

    private static float DistanceSqr(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 line = to - from;
        float lengthSqr = line.sqrMagnitude;
        float t = lengthSqr > Mathf.Epsilon ? Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr) : 0f;

        return (point - (from + line * t)).sqrMagnitude;
    }
}
