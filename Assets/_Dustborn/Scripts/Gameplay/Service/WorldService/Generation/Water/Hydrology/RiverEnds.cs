using UnityEngine;

public enum RiverEnd
{
    Standing,
    Border,
    River,
    Loose
}

public static class RiverEnds
{
    private const int STANDING_TAPS = 8;
    private const float SEA_TOLERANCE = 0.05f;

    public static RiverEnd Start(WaterMap water, int river)
    {
        return Classify(water, river, water.Rivers[river].Points[0]);
    }

    public static RiverEnd End(WaterMap water, int river)
    {
        return Classify(water, river, water.Rivers[river].Points[^1]);
    }

    public static (bool Start, bool End)[] Loose(WaterMap water)
    {
        var loose = new (bool Start, bool End)[water.Rivers.Count];

        for (int river = 0; river < loose.Length; river++)
        {
            if (water.Rivers[river].Points.Count < 2)
                continue;

            loose[river] = (Start(water, river) == RiverEnd.Loose, End(water, river) == RiverEnd.Loose);
        }

        return loose;
    }

    public static bool Headwater(WaterMap water, int river, float startArea)
    {
        RiverPath path = water.Rivers[river];

        return path.SourceArea >= startArea && path.SourceDonor < startArea;
    }

    public static void Label(WaterMap water, float startArea)
    {
        for (int river = 0; river < water.Rivers.Count; river++)
        {
            RiverPath path = water.Rivers[river];

            if (path.Points.Count < 2)
                continue;

            path.Source = Source(water, river, path, startArea);

            path.Terminal = Terminal(water, river, path);
        }
    }

    private static RiverSource Source(WaterMap water, int river, RiverPath path, float startArea)
    {
        RiverPoint first = path.Points[0];
        Vector2 p = first.Position;

        if (water.InsideOtherRiver(river, p, water.CellSize))
            return path.Source == RiverSource.Distributary ? RiverSource.Distributary : RiverSource.Unknown;

        if (Headwater(water, river, startArea) && !first.Submerged)
            return RiverSource.Headwater;

        if (InStanding(water, p, first.Width * 0.5f + water.CellSize))
            return water.Covers(p.x, p.y, out short owner) && owner == WaterMap.OWNER_SEA ? RiverSource.Unknown : RiverSource.LakeOutlet;

        float margin = water.CellSize * 4f;

        return p.x <= margin || p.y <= margin || p.x >= water.WorldSize - margin || p.y >= water.WorldSize - margin ? RiverSource.Boundary : RiverSource.Unknown;
    }

    private static RiverTerminal Terminal(WaterMap water, int river, RiverPath path)
    {
        RiverPoint last = path.Points[^1];
        Vector2 p = last.Position;

        if (InStanding(water, p, last.Width * 0.5f + water.CellSize))
            return water.SeaLevel > 0f && last.Surface <= water.SeaLevel + SEA_TOLERANCE ? RiverTerminal.Sea : RiverTerminal.Lake;

        if (path.Parent >= 0 || water.InsideOtherRiver(river, p, water.CellSize))
            return RiverTerminal.Junction;

        float margin = water.CellSize * 4f;

        if (p.x <= margin || p.y <= margin || p.x >= water.WorldSize - margin || p.y >= water.WorldSize - margin)
            return RiverTerminal.Border;

        return RiverTerminal.Unknown;
    }

    private static RiverEnd Classify(WaterMap water, int river, RiverPoint point)
    {
        Vector2 p = point.Position;
        float margin = water.CellSize * 4f;

        if (point.Submerged || InStanding(water, p, point.Width * 0.5f + water.CellSize))
            return RiverEnd.Standing;

        if (p.x <= margin || p.y <= margin || p.x >= water.WorldSize - margin || p.y >= water.WorldSize - margin)
            return RiverEnd.Border;

        if (water.InsideOtherRiver(river, p, water.CellSize))
            return RiverEnd.River;

        return RiverEnd.Loose;
    }

    private static bool InStanding(WaterMap water, Vector2 center, float reach)
    {
        if (Wet(water, center))
            return true;

        for (int k = 0; k < STANDING_TAPS; k++)
        {
            float angle = k * Mathf.PI * 2f / STANDING_TAPS;

            if (Wet(water, center + reach * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))))
                return true;
        }

        return false;
    }

    private static bool Wet(WaterMap water, Vector2 at)
    {
        if (at.x < 0f || at.y < 0f || at.x > water.WorldSize || at.y > water.WorldSize)
            return false;

        return water.StandingAt(at.x, at.y, out _) || water.BodyIds[water.CellIndex(at.x, at.y)] >= 0;
    }
}
