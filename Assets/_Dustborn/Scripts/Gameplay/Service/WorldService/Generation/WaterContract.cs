using System.Collections.Generic;
using UnityEngine;

public enum WaterViolationKind
{
    UnjustifiedSource,
    UnjustifiedTerminal,
    BorderOutflow,
    DetachedJunction,
    JunctionStep,
    ParentCycle,
    RisingSurface,
    LosingFlow,
    WideSource,
    NarrowingDownstream,
    WaterInDryLand
}

public readonly struct WaterViolation
{
    public readonly WaterViolationKind Kind;
    public readonly int River;
    public readonly Vector2 At;
    public readonly float Amount;

    public WaterViolation(WaterViolationKind kind, int river, Vector2 at, float amount)
    {
        Kind = kind;
        River = river;
        At = at;
        Amount = amount;
    }

    public override string ToString()
    {
        return $"{Kind} river {River} at ({At.x:0}, {At.y:0}) {Amount:0.###}";
    }
}

public sealed class WaterContract
{
    public const float RISE_TOLERANCE = 1e-3f;
    public const float FLOW_TOLERANCE = 1e-3f;
    public const float JOIN_REACH = 4f;
    public const float MAX_JOIN_STEP = 0.5f;
    public const float MAX_SOURCE_WIDTH = 3f;
    public const float NARROWING_TOLERANCE = 0.05f;

    public readonly List<WaterViolation> Violations = new();
    public readonly int[] Sources = new int[5];
    public readonly int[] Terminals = new int[5];
    public int Rivers;
    public int Junctions;

    public int Count(WaterViolationKind kind)
    {
        int count = 0;

        foreach (WaterViolation violation in Violations)
        {
            if (violation.Kind == kind)
                count++;
        }

        return count;
    }

    public static WaterContract Check(WaterMap water, float startArea, bool border)
    {
        var contract = new WaterContract();

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            RiverPath path = water.Rivers[river];

            if (path.Points.Count < 2)
                continue;

            contract.Rivers++;
            contract.Sources[(int)path.Source]++;
            contract.Terminals[(int)path.Terminal]++;
            contract.Ends(water, river, path, startArea, border);
            contract.Profile(river, path);
        }

        contract.Cycles(water);
        contract.DryLand(water);
        return contract;
    }

    private void Ends(WaterMap water, int river, RiverPath path, float startArea, bool border)
    {
        RiverPoint first = path.Points[0], last = path.Points[^1];

        if (path.Source == RiverSource.Unknown)
            Violations.Add(new WaterViolation(WaterViolationKind.UnjustifiedSource, river, first.Position, path.SourceArea / Mathf.Max(1f, startArea)));

        if (path.Source == RiverSource.Headwater && first.Width > MAX_SOURCE_WIDTH)
            Violations.Add(new WaterViolation(WaterViolationKind.WideSource, river, first.Position, first.Width));

        if (path.Terminal == RiverTerminal.Unknown)
            Violations.Add(new WaterViolation(WaterViolationKind.UnjustifiedTerminal, river, last.Position, 0f));

        if (path.Terminal == RiverTerminal.Border && !border)
            Violations.Add(new WaterViolation(WaterViolationKind.BorderOutflow, river, last.Position, 0f));

        if (path.Terminal != RiverTerminal.Junction)
            return;

        Junctions++;

        if (path.Parent < 0 || path.Parent >= water.Rivers.Count || path.Parent == river)
        {
            Violations.Add(new WaterViolation(WaterViolationKind.DetachedJunction, river, last.Position, -1f));
            return;
        }

        RiverPath parent = water.Rivers[path.Parent];
        int nearest = Nearest(parent, last.Position, out float distance);
        float reach = 0.5f * parent.Points[nearest].Width + JOIN_REACH;

        if (distance > reach)
            Violations.Add(new WaterViolation(WaterViolationKind.DetachedJunction, river, last.Position, distance - reach));

        float step = last.Surface - parent.Points[nearest].Surface;

        if (step > MAX_JOIN_STEP || step < -MAX_JOIN_STEP)
            Violations.Add(new WaterViolation(WaterViolationKind.JunctionStep, river, last.Position, step));
    }

    private void Profile(int river, RiverPath path)
    {
        float worstRise = 0f, worstLoss = 0f, worstNarrowing = 0f, widest = 0f;
        Vector2 rise = default, loss = default, narrowing = default;

        for (int i = 1; i < path.Points.Count; i++)
        {
            RiverPoint before = path.Points[i - 1], after = path.Points[i];
            widest = Mathf.Max(widest, before.Width);

            if (after.Surface - before.Surface > worstRise)
            {
                worstRise = after.Surface - before.Surface;
                rise = after.Position;
            }

            if (before.Flow - after.Flow > worstLoss)
            {
                worstLoss = before.Flow - after.Flow;
                loss = after.Position;
            }

            if (!before.Submerged && !after.Submerged && before.Width - after.Width > worstNarrowing)
            {
                worstNarrowing = before.Width - after.Width;
                narrowing = after.Position;
            }
        }

        if (worstRise > RISE_TOLERANCE)
            Violations.Add(new WaterViolation(WaterViolationKind.RisingSurface, river, rise, worstRise));

        if (worstLoss > FLOW_TOLERANCE * Mathf.Max(1f, path.Points[^1].Flow))
            Violations.Add(new WaterViolation(WaterViolationKind.LosingFlow, river, loss, worstLoss));

        if (worstNarrowing > NARROWING_TOLERANCE * Mathf.Max(1f, widest))
            Violations.Add(new WaterViolation(WaterViolationKind.NarrowingDownstream, river, narrowing, worstNarrowing));
    }

    private void DryLand(WaterMap water)
    {
        for (int river = 0; river < water.Rivers.Count; river++)
        {
            foreach (RiverPoint point in water.Rivers[river].Points)
            {
                if (!water.Dry[water.CellIndex(point.Position.x, point.Position.y)] || !water.RiverOpen(point))
                    continue;

                Violations.Add(new WaterViolation(WaterViolationKind.WaterInDryLand, river, point.Position, point.Width));
                break;
            }
        }

        var reported = new HashSet<short>();

        for (int cell = 0; cell < water.BodyIds.Length; cell++)
        {
            short body = water.BodyIds[cell];

            if (body >= 0 && water.Dry[cell] && reported.Add(body))
                Violations.Add(new WaterViolation(WaterViolationKind.WaterInDryLand, -1 - body, water.CellCenter(cell), water.Bodies[body].Area));
        }
    }

    private void Cycles(WaterMap water)
    {
        for (int river = 0; river < water.Rivers.Count; river++)
        {
            int current = river;

            for (int steps = 0; current >= 0 && current < water.Rivers.Count; steps++)
            {
                if (steps > water.Rivers.Count)
                {
                    Violations.Add(new WaterViolation(WaterViolationKind.ParentCycle, river, water.Rivers[river].Points[^1].Position, steps));
                    break;
                }

                current = water.Rivers[current].Terminal == RiverTerminal.Junction ? water.Rivers[current].Parent : -1;
            }
        }
    }

    private static int Nearest(RiverPath path, Vector2 point, out float distance)
    {
        int best = 0;
        float nearest = float.MaxValue;

        for (int i = 0; i < path.Points.Count; i++)
        {
            float d = (path.Points[i].Position - point).sqrMagnitude;

            if (d >= nearest)
                continue;

            nearest = d;
            best = i;
        }

        distance = Mathf.Sqrt(nearest);
        return best;
    }

    public string Describe()
    {
        var counts = new List<string>();

        for (int kind = 0; kind <= (int)WaterViolationKind.WaterInDryLand; kind++)
        {
            int count = Count((WaterViolationKind)kind);

            if (count > 0)
                counts.Add($"{(WaterViolationKind)kind} {count}");
        }

        return $"river graph: {Rivers} rivers, sources headwater {Sources[(int)RiverSource.Headwater]} / lake outlet {Sources[(int)RiverSource.LakeOutlet]} / boundary {Sources[(int)RiverSource.Boundary]} / distributary {Sources[(int)RiverSource.Distributary]} / unknown {Sources[(int)RiverSource.Unknown]}, "
            + $"terminals sea {Terminals[(int)RiverTerminal.Sea]} / lake {Terminals[(int)RiverTerminal.Lake]} / junction {Terminals[(int)RiverTerminal.Junction]} / border {Terminals[(int)RiverTerminal.Border]} / unknown {Terminals[(int)RiverTerminal.Unknown]}; "
            + (counts.Count == 0 ? "no violations" : "violations: " + string.Join(", ", counts));
    }
}
