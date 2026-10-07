using System.Collections.Generic;
using UnityEngine;

public sealed partial class Hydrology
{
    private static readonly float[] SPRING_RADII = { 11f, 8f, 6f };
    private const float SPRING_OFFSET = 0.6f;
    private const float SPRING_DEPTH = 1.6f;
    private const float SPRING_RIM = 0.3f;
    private const float SPRING_DAM = 4f;
    private const float SPRING_DAM_GRADE = 0.5f;
    private const float SPRING_BANK_GRADE = 0.6f;
    private const float SPRING_BANK_REACH = 40f;
    private const float SPRING_MAX_DROP = 8f;
    private const float SPRING_CLEARANCE = 8f;
    private const float LEAD_REACH = 48f;
    private const float LEAD_TOLERANCE = 0.1f;
    private const float SHORTCUT_REACH = 96f;
    private const float POND_WIDTH_SCALE = 1.2f;

    public int SpringPonds { get; private set; }
    public int MouthPonds { get; private set; }

    private void PondRiverEnds(HydrologyGrid grid, WaterMap water)
    {
        for (int river = 0; river < water.Rivers.Count; river++)
        {
            if (water.Rivers[river].Points.Count < 2)
                continue;

            StartInWater(grid, water, river);
            EndInWater(grid, water, river);
        }

        water.InvalidateIndex();
    }

    private void StartInWater(HydrologyGrid grid, WaterMap water, int river)
    {
        RiverPoint first = water.Rivers[river].Points[0];

        if (water.Rivers[river].Source == RiverSource.Distributary || water.InsideOtherRiver(river, first.Position, water.CellSize))
            return;

        int lake = LakeNear(water, first.Position.x, first.Position.y, 0.5f * first.Width + water.CellSize);

        if (lake >= 0 && Mathf.Abs(first.Surface - water.Bodies[lake].Surface) <= LEAD_TOLERANCE)
        {
            ReachBody(grid, water, river, lake, true);
            return;
        }

        if (first.Submerged && lake >= 0)
            return;

        DigPond(grid, water, river, true, -1);
    }

    private void EndInWater(HydrologyGrid grid, WaterMap water, int river)
    {
        RiverPath path = water.Rivers[river];
        RiverPoint last = path.Points[^1];
        float reach = 0.5f * last.Width + water.CellSize;

        if (path.Parent >= 0 || water.InsideOtherRiver(river, last.Position, water.CellSize) || OceanNear(grid, last.Position, reach) || AtBorder(last.Position))
            return;

        int lake = LakeNear(water, last.Position.x, last.Position.y, reach);

        if (lake >= 0 && Mathf.Abs(last.Surface - water.Bodies[lake].Surface) <= LEAD_TOLERANCE)
        {
            ReachBody(grid, water, river, lake, false);
            return;
        }

        if (lake >= 0)
            return;

        DigPond(grid, water, river, false, -1);
    }

    private void ReachBody(HydrologyGrid grid, WaterMap water, int river, int lake, bool start)
    {
        List<RiverPoint> points = water.Rivers[river].Points;
        Vector2 at = start ? points[0].Position : points[^1].Position;

        if (InteriorCell(water, water.CellIndex(at.x, at.y), lake))
            return;

        if (Interior(water, at, lake, LEAD_REACH, out Vector2 inside))
        {
            Lead(water, river, lake, inside, start);
            return;
        }

        DigPond(grid, water, river, start, lake);
    }

    private void DigPond(HydrologyGrid grid, WaterMap water, int river, bool start, int own)
    {
        string refusal = null;
        List<RiverPoint> points = water.Rivers[river].Points;
        float travelled = 0f;

        for (int step = 0; step < points.Count - 2 && travelled <= LEAD_REACH; step++)
        {
            int anchor = start ? step : points.Count - 1 - step;

            if (step > 0)
                travelled += Vector2.Distance(points[anchor].Position, points[start ? anchor - 1 : anchor + 1].Position);

            foreach (float size in SPRING_RADII)
            {
                float radius = Mathf.Max(size, POND_WIDTH_SCALE * points[anchor].Width);

                if (!PondSite(grid, water, river, radius, anchor, start, own, out Vector2 center, out refusal))
                    continue;

                DropBeyond(water, river, center, radius * SPRING_OFFSET, start);

                if (start)
                    AddSpring(grid, water, river, radius, center, own);
                else
                    AddMouthPond(grid, water, river, radius, center, own);

                return;
            }
        }

        Log.Note(HydrologyAction.SpringPond, river, start ? points[0].Position : points[^1].Position, $"course {river} keeps a bare {(start ? "source" : "mouth")}: {refusal}");
    }

    private void AddSpring(HydrologyGrid grid, WaterMap water, int river, float radius, Vector2 center, int own)
    {
        List<RiverPoint> points = water.Rivers[river].Points;
        int outside = 0;

        while (outside < points.Count && (points[outside].Position - center).sqrMagnitude <= radius * radius)
            outside++;

        float surface = points[0].Surface;

        if (outside > 0 && outside < points.Count)
            surface = Mathf.Min(surface, points[outside].Surface + MAX_WATER_GRADE * Vector2.Distance(points[outside - 1].Position, points[outside].Position));

        if (own >= 0)
            surface = water.Bodies[own].Surface;

        int id = Claim(grid, water, center, radius, surface, own);

        for (int i = 0; i < outside; i++)
        {
            RiverPoint point = points[i];
            float level = own >= 0 ? Mathf.Max(surface, point.Surface) : surface;
            point.Bed -= point.Surface - level;
            point.Surface = level;
            point.Submerged = true;
            points[i] = point;
        }

        SpringPonds++;
        Log.Note(HydrologyAction.SpringPond, river, center, $"course {river} starts from a spring pond {id} of {radius:0} m at {surface:0.00} m");
    }

    private void AddMouthPond(HydrologyGrid grid, WaterMap water, int river, float radius, Vector2 center, int own)
    {
        List<RiverPoint> points = water.Rivers[river].Points;
        float surface = own >= 0 ? Mathf.Min(water.Bodies[own].Surface, points[^1].Surface) : points[^1].Surface;
        int id = Claim(grid, water, center, radius, surface, own);

        for (int i = points.Count - 1; i >= 0 && (points[i].Position - center).sqrMagnitude <= radius * radius; i--)
        {
            RiverPoint point = points[i];
            point.Submerged = true;
            points[i] = point;
        }

        MouthPonds++;
        Log.Note(HydrologyAction.SpringPond, river, center, $"course {river} ends in a dug pond {id} of {radius:0} m at {surface:0.00} m");
    }

    private int Claim(HydrologyGrid grid, WaterMap water, Vector2 center, float radius, float surface, int own)
    {
        int id = own >= 0 ? own : water.Bodies.Count;

        DigSpring(center, radius, surface);
        ClaimSpring(grid, water, center, radius, surface, id);

        if (own >= 0)
        {
            WaterBody body = water.Bodies[own];
            body.Area += Mathf.PI * radius * radius;
            water.Bodies[own] = body;
            return id;
        }

        water.Bodies.Add(new WaterBody
        {
            Kind = WaterKind.Pond,
            Surface = surface,
            Area = Mathf.PI * radius * radius,
            Depth = SPRING_DEPTH,
            Center = center,
            SeedRadius = radius
        });

        return id;
    }

    private void Lead(WaterMap water, int river, int lake, Vector2 inside, bool start)
    {
        List<RiverPoint> points = water.Rivers[river].Points;
        CutToNearest(river, points, inside, start);
        RiverPoint end = start ? points[0] : points[^1];
        float length = Vector2.Distance(inside, end.Position);
        int count = Mathf.CeilToInt(length / RESAMPLE_STEP);

        if (count == 0)
            return;

        float surface = start ? Mathf.Max(end.Surface, water.Bodies[lake].Surface) : Mathf.Min(end.Surface, water.Bodies[lake].Surface);
        var lead = new List<RiverPoint>(count);

        for (int i = 0; i < count; i++)
        {
            RiverPoint point = end;
            point.Position = start ? Vector2.Lerp(inside, end.Position, i / (float)count) : Vector2.Lerp(end.Position, inside, (i + 1) / (float)count);
            point.Surface = surface;
            point.Submerged = true;
            lead.Add(point);
        }

        if (start)
            points.InsertRange(0, lead);
        else
            points.AddRange(lead);

        ExtendTrack(river, count, start);
        Log.Note(HydrologyAction.LedIntoLake, river, inside, $"course {river} leads {length:0} m {(start ? "back into" : "on into")} body {lake} it {(start ? "starts" : "ends")} beside");
    }

    private void CutToNearest(int river, List<RiverPoint> points, Vector2 inside, bool start)
    {
        int nearest = start ? 0 : points.Count - 1;
        float best = (points[nearest].Position - inside).sqrMagnitude;
        float walked = 0f;

        for (int step = 1; step < points.Count - 2 && walked <= LEAD_REACH; step++)
        {
            int index = start ? step : points.Count - 1 - step;
            walked += Vector2.Distance(points[index].Position, points[start ? index - 1 : index + 1].Position);
            float distance = (points[index].Position - inside).sqrMagnitude;

            if (distance >= best)
                continue;

            best = distance;
            nearest = index;
        }

        int drop = start ? nearest : points.Count - 1 - nearest;

        if (drop == 0)
            return;

        points.RemoveRange(start ? 0 : nearest + 1, drop);
        TrimTrack(river, drop, start);
    }

    private static bool InteriorCell(WaterMap water, int cell, int lake)
    {
        int resolution = water.Resolution;
        int column = cell % resolution, row = cell / resolution;

        if (column < 1 || row < 1 || column > resolution - 2 || row > resolution - 2)
            return false;

        return water.BodyIds[cell] == lake && water.BodyIds[cell - 1] == lake && water.BodyIds[cell + 1] == lake && water.BodyIds[cell - resolution] == lake && water.BodyIds[cell + resolution] == lake;
    }

    private static bool Interior(WaterMap water, Vector2 from, int lake, float reach, out Vector2 inside)
    {
        inside = from;
        int resolution = water.Resolution;
        int span = Mathf.CeilToInt(reach / water.CellSize);
        int column = Mathf.Clamp((int)(from.x / water.CellSize), 0, resolution - 1);
        int row = Mathf.Clamp((int)(from.y / water.CellSize), 0, resolution - 1);
        float best = reach * reach;
        bool found = false;

        for (int r = Mathf.Max(1, row - span); r <= Mathf.Min(resolution - 2, row + span); r++)
        {
            for (int c = Mathf.Max(1, column - span); c <= Mathf.Min(resolution - 2, column + span); c++)
            {
                if (!InteriorCell(water, r * resolution + c, lake))
                    continue;

                var middle = new Vector2((c + 0.5f) * water.CellSize, (r + 0.5f) * water.CellSize);
                float distance = (middle - from).sqrMagnitude;

                if (distance >= best)
                    continue;

                best = distance;
                inside = middle;
                found = true;
            }
        }

        return found;
    }

    private void ExtendTrack(int river, int count, bool start)
    {
        if (Stamps == null || river >= Stamps.Tracks.Count || Stamps.Tracks[river] == null)
            return;

        WaterStampTrack track = Stamps.Tracks[river];
        var extended = new WaterStampTrack(track.Placement.Length + count);
        int offset = start ? count : 0;
        System.Array.Copy(track.Placement, 0, extended.Placement, offset, track.Placement.Length);
        System.Array.Copy(track.Stamp, 0, extended.Stamp, offset, track.Stamp.Length);
        Stamps.Tracks[river] = extended;
    }

    private bool OceanNear(HydrologyGrid grid, Vector2 at, float reach)
    {
        for (int k = 0; k <= 8; k++)
        {
            float angle = k * Mathf.PI * 0.25f;
            Vector2 probe = k == 8 ? at : at + reach * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            if (OceanAt(probe))
                return true;
        }

        return false;
    }

    private bool AtBorder(Vector2 at)
    {
        float margin = _settings.CellSize * 4f;

        return at.x <= margin || at.y <= margin || at.x >= _config.WorldSize - margin || at.y >= _config.WorldSize - margin;
    }

    private void DropBeyond(WaterMap water, int river, Vector2 center, float keep, bool start)
    {
        List<RiverPoint> points = water.Rivers[river].Points;
        float limit = (keep + RESAMPLE_STEP) * (keep + RESAMPLE_STEP);
        int drop = 0;

        while (drop < points.Count - 2 && (points[start ? drop : points.Count - 1 - drop].Position - center).sqrMagnitude > limit)
            drop++;

        if (drop == 0)
            return;

        Vector2 cut = points[start ? drop : points.Count - 1 - drop].Position;
        points.RemoveRange(start ? 0 : points.Count - drop, drop);
        TrimTrack(river, drop, start);
        Log.Note(HydrologyAction.SpringPond, river, cut, $"course {river} gives up {drop} points at its {(start ? "head" : "mouth")} to fit the pond");
    }

    private void TrimTrack(int river, int count, bool start)
    {
        if (Stamps == null || river >= Stamps.Tracks.Count || Stamps.Tracks[river] == null)
            return;

        WaterStampTrack track = Stamps.Tracks[river];
        var trimmed = new WaterStampTrack(track.Placement.Length - count);
        int offset = start ? count : 0;
        System.Array.Copy(track.Placement, offset, trimmed.Placement, 0, trimmed.Placement.Length);
        System.Array.Copy(track.Stamp, offset, trimmed.Stamp, 0, trimmed.Stamp.Length);
        Stamps.Tracks[river] = trimmed;
    }

    private bool PondSite(HydrologyGrid grid, WaterMap water, int river, float radius, int anchor, bool start, int own, out Vector2 center, out string refusal)
    {
        refusal = null;
        List<RiverPoint> points = water.Rivers[river].Points;
        RiverPoint end = points[anchor];
        bool head = start ? anchor == 0 : anchor == points.Count - 1;
        Vector2 inward = start ? points[0].Position - points[Mathf.Min(points.Count - 1, 3)].Position : points[^1].Position - points[Mathf.Max(0, points.Count - 4)].Position;
        center = end.Position + (head && inward.sqrMagnitude > 1e-6f ? inward.normalized : Vector2.zero) * (radius * SPRING_OFFSET);

        float reach = radius + SPRING_DAM + SPRING_CLEARANCE;

        if (center.x < reach || center.y < reach || center.x > _config.WorldSize - reach || center.y > _config.WorldSize - reach)
        {
            refusal = "too close to the world border";
            return false;
        }

        if (water.InsideOtherRiver(river, end.Position, water.CellSize) || water.InsideOtherRiver(river, center, reach))
        {
            refusal = "another river runs within the pond's reach";
            return false;
        }

        int resolution = grid.Resolution;
        float cut = radius + SPRING_BANK_REACH;
        int span = Mathf.CeilToInt(cut / grid.CellSize);
        int column = Mathf.FloorToInt(center.x / grid.CellSize), row = Mathf.FloorToInt(center.y / grid.CellSize);

        for (int r = row - span; r <= row + span; r++)
        {
            for (int c = column - span; c <= column + span; c++)
            {
                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int cell = r * resolution + c;
                int body = water.BodyIds[cell];

                if (body == own && !grid.Ocean[cell] || body < 0 && !grid.Ocean[cell])
                    continue;

                float nearX = Mathf.Clamp(center.x, c * grid.CellSize, (c + 1) * grid.CellSize) - center.x;
                float nearZ = Mathf.Clamp(center.y, r * grid.CellSize, (r + 1) * grid.CellSize) - center.y;
                float distance = nearX * nearX + nearZ * nearZ;

                if (distance < reach * reach)
                {
                    refusal = "standing water or the sea is within the pond's reach";
                    return false;
                }

                if (body >= 0 && distance < cut * cut && water.Bodies[body].Surface > end.Surface)
                {
                    refusal = $"the pond's banks would cut into higher body {body}";
                    return false;
                }
            }
        }

        return true;
    }

    private void DigSpring(Vector2 center, float radius, float surface)
    {
        int resolution = _map.Resolution;
        float step = _map.WorldSize / (float)(resolution - 1);
        float maxHeight = _map.MaxHeight;
        float[] heights = _map.Heights;
        float reach = radius + Mathf.Max(SPRING_BANK_REACH, SPRING_DAM + (SPRING_RIM + SPRING_MAX_DROP) / SPRING_DAM_GRADE);
        int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / step)), maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.x + reach) / step));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((center.y - reach) / step)), maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.y + reach) / step));

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float distance = Vector2.Distance(new Vector2(x * step, z * step), center);

                if (distance > reach)
                    continue;

                int index = z * resolution + x;
                float ground = heights[index] * maxHeight;
                heights[index] = SpringHeight(distance, radius, ground, surface) / maxHeight;
            }
        }
    }

    private static float SpringHeight(float distance, float radius, float ground, float surface)
    {
        if (distance <= radius)
        {
            float share = distance / radius;
            return Mathf.Min(ground, surface - SPRING_DEPTH * (1f - share * share));
        }

        float beyond = distance - radius;
        float cut = Mathf.Min(ground, surface + SPRING_RIM + SPRING_BANK_GRADE * beyond);
        float dam = surface + SPRING_RIM - SPRING_DAM_GRADE * Mathf.Max(0f, beyond - SPRING_DAM);

        return Mathf.Max(cut, dam);
    }

    private void ClaimSpring(HydrologyGrid grid, WaterMap water, Vector2 center, float radius, float surface, int id)
    {
        int resolution = grid.Resolution;
        int span = Mathf.CeilToInt((radius + grid.CellSize) / grid.CellSize);
        int column = Mathf.FloorToInt(center.x / grid.CellSize), row = Mathf.FloorToInt(center.y / grid.CellSize);

        for (int r = row - span; r <= row + span; r++)
        {
            for (int c = column - span; c <= column + span; c++)
            {
                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int cell = r * resolution + c;
                var middle = new Vector2((c + 0.5f) * grid.CellSize, (r + 0.5f) * grid.CellSize);
                grid.Height[cell] = _map.SampleWorldSmooth(middle.x, middle.y);
                grid.Filled[cell] = Mathf.Max(grid.Filled[cell], surface);

                if ((middle - center).sqrMagnitude <= radius * radius && grid.Height[cell] < surface)
                    water.BodyIds[cell] = (short)id;
            }
        }
    }
}
