using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterStampComposer
{
    private void ComposeRun(WaterStampMacro macro, Line line, Status[] status, int first, int last, List<Piece> pieces, ref float runLevel)
    {
        float runStart = line.Arc[first];
        float runEnd = line.Arc[last];
        float cursor = runStart;
        bool source = first == 0;
        bool afterLake = first > 0 && status[first - 1] == Status.Lake;
        bool seaEnd = last + 1 < status.Length && status[last + 1] == Status.Sea;
        Vector2? tangent = source ? null : line.Tangent(runStart);
        float shortest = ShortestChord();

        if (float.IsPositiveInfinity(runLevel))
            runLevel = _map.SampleWorldSmooth(line.Points[first].x, line.Points[first].y);

        while (runEnd - cursor >= shortest)
        {
            bool opening = cursor <= runStart + 1e-3f;
            bool placed = TryJoin(line, cursor, runEnd, tangent, ref runLevel, pieces, out float next, out Vector2 exitTangent);

            if (!placed)
                placed = TryChunk(macro, line, cursor, runEnd, tangent, opening && source, opening && afterLake, seaEnd, ref runLevel, pieces, out next, out exitTangent);

            if (placed)
            {
                cursor = next;
                tangent = exitTangent;
                _chunk++;
                continue;
            }

            _layout.FailedChunks++;
            float step = Mathf.Min(runEnd - cursor, Mathf.Max(shortest * 0.5f, 4f * _cell));
            Fallback(line, cursor, cursor + step, pieces, ref runLevel);
            cursor += step;
            tangent = line.Tangent(cursor - 1e-3f);
            _chunk++;
        }

        if (runEnd - cursor > 1e-3f)
            Fallback(line, cursor, runEnd, pieces, ref runLevel);
    }

    private void Fallback(Line line, float from, float to, List<Piece> pieces, ref float runLevel)
    {
        int a = line.Segment(from);
        int b = Mathf.Min(line.Points.Count - 1, line.Segment(to) + 1);
        var points = new List<Vector2> { line.At(from) };
        var areas = new List<float> { line.AreaAt(from) };

        for (int i = a + 1; i < b; i++)
        {
            if (line.Arc[i] <= from || line.Arc[i] >= to)
                continue;

            points.Add(line.Points[i]);
            areas.Add(line.Areas[i]);
        }

        points.Add(line.At(to));
        areas.Add(line.AreaAt(to));

        if (points.Count >= 4)
            _procedural?.Invoke(points, areas, _path * 7919 + _chunk);

        var piece = new Piece();

        for (int i = 0; i < points.Count; i++)
        {
            piece.Nodes.Add(new Node { Point = points[i], Area = areas[i], Placement = -1, WidthScale = 1f, DepthScale = 1f });
            runLevel = Mathf.Min(runLevel, _map.SampleWorldSmooth(points[i].x, points[i].y));
        }

        pieces.Add(piece);
        _layout.FallbackChunks++;
        _layout.FallbackPoints.Add(line.At(0.5f * (from + to)));
        _composedArc += to - from;
        Own(piece);
    }

    private bool TryChunk(WaterStampMacro macro, Line line, float cursor, float runEnd, Vector2? tangent, bool source, bool afterLake, bool seaEnd,
        ref float runLevel, List<Piece> pieces, out float next, out Vector2 exitTangent)
    {
        next = cursor;
        exitTangent = Vector2.zero;

        Terrain(line, cursor, out float freedom, out float grade);
        float width = _hydrology.Width(line.AreaAt(cursor));
        Vector2 start = line.At(cursor);

        Candidate best = default;
        best.Key = float.MaxValue;
        int tried = 0;
        (float Arc, Vector2 Point)? confluence = NextConfluence(line, cursor);

        foreach (int index in _library.Rivers)
        {
            WaterStampDefinition definition = _library.Get(index);
            float affinity = Affinity(definition, freedom, grade, source, afterLake, seaEnd, width);

            if (affinity <= 0f)
                continue;

            float baseScale = Mathf.Clamp(width / definition.NominalChannelWidth, definition.MinScale, definition.MaxScale);
            float spread = _settings.ScaleVariation * (definition.MaxScale - definition.MinScale);

            for (int window = 0; window < Windows.Length; window++)
            {
                (float from, float to) = Windows[window];

                if (!Allowed(definition.Category, from, to))
                    continue;

                Span(definition, from, to, out int first, out int last);
                float share = window == 0 ? 1f : WINDOW_WEIGHT * (to - from);

                for (int mirror = 0; mirror < (_library.Mirrors(definition) ? 2 : 1); mirror++)
                {
                    for (int step = 0; step < 3; step++)
                    {
                        float scale = Mathf.Min(definition.MaxScale, baseScale + spread * step * 0.5f);
                        int id = ((index * Windows.Length + window) * 2 + mirror) * 3 + step;
                        tried++;

                        if (!Evaluate(line, cursor, runEnd, tangent, definition, index, first, last, mirror == 1, scale, seaEnd, runLevel, out Candidate candidate))
                            continue;

                        float bonus = 1f;

                        if (confluence.HasValue && confluence.Value.Arc > candidate.EndArc && PrepareJoin(candidate.Placement.World(definition.Centerline[last]), confluence.Value.Point))
                            bonus = 2.5f;

                        float weight = definition.Weight * affinity * share * bonus * Mathf.Exp(-0.5f * candidate.Excess);
                        candidate.Key = WaterStampLibrary.Race(weight, WaterStampLibrary.Hash01(_config.Seed, _path, _chunk, id));

                        if (candidate.Key < best.Key)
                            best = candidate;
                    }
                }
            }
        }

        _layout.Candidates += tried;

        if (best.Key == float.MaxValue)
        {
            _layout.Reject(_record, start, "chunk", "no stamp fits the drainage here");
            return false;
        }

        WaterStampDefinition chosen = _library.Get(best.Stamp);
        best.Placement.River = _path;
        best.Placement.First = best.First;
        best.Placement.Last = best.Last;
        int placement = _layout.Add(best.Placement);
        Piece stamped = Stamped(chosen, best.Placement, placement, line, cursor, best.EndArc, chosen.Centerline, best.First, best.Last);
        pieces.Add(stamped);
        Own(stamped);

        if (seaEnd && best.EndArc >= runEnd - 1e-3f && chosen.Category is WaterStampCategory.RiverMouth or WaterStampCategory.RiverFork)
            _ended = true;

        if (chosen.Category == WaterStampCategory.RiverFork)
            AddFork(chosen, best.Placement, placement, line, best.EndArc);

        next = best.EndArc;
        exitTangent = Direction(best.Placement, chosen.Centerline, best.Last);
        runLevel = best.EndLevel;
        return true;
    }

    private float Affinity(WaterStampDefinition definition, float freedom, float grade, bool source, bool afterLake, bool seaEnd, float width)
    {
        float confined = 1f - freedom;
        float steep = Mathf.Clamp01(grade / STEEP_GRADE);

        switch (definition.Category)
        {
            case WaterStampCategory.RiverSource:
                return source ? 6f : 0f;
            case WaterStampCategory.LakeOutlet:
                return afterLake ? 6f : 0f;
            case WaterStampCategory.RiverMouth:
                return seaEnd ? 8f : 0f;
            case WaterStampCategory.RiverFork:
                return seaEnd && _library.Get(_library.Find(WaterStampCategory.RiverFork)).Branch(WaterStampBranchKind.BranchOut) != null ? 3f : 0f;
            case WaterStampCategory.TributaryJoin:
                return 0f;
            case WaterStampCategory.StraightRiver:
                return 1f + confined;
            case WaterStampCategory.GentleCurve:
                return 1f;
            case WaterStampCategory.StrongCurve:
                return 0.5f + freedom;
            case WaterStampCategory.SMeander:
            case WaterStampCategory.NarrowMeander:
                return 0.3f + 1.5f * freedom * (1f - 0.5f * steep);
            case WaterStampCategory.WideMeander:
                return (0.2f + 2f * freedom * (1f - steep)) * Mathf.Clamp(width / definition.NominalChannelWidth + 0.5f, 0.5f, 1.5f);
            case WaterStampCategory.CanyonRiver:
                return 0.2f + 3f * confined * confined + 2f * steep;
            default:
                return 0.5f;
        }
    }

    private void Terrain(Line line, float cursor, out float freedom, out float grade)
    {
        float end = Mathf.Min(line.Length, cursor + FREEDOM_REACH);
        float sum = 0f;

        for (int tap = 0; tap < FREEDOM_TAPS; tap++)
        {
            float arc = Mathf.Lerp(cursor, end, (tap + 0.5f) / FREEDOM_TAPS);
            sum += _hydrology.Freedom(line.At(arc), line.Tangent(arc), _hydrology.Width(line.AreaAt(arc)), _cell);
        }

        freedom = sum / FREEDOM_TAPS;

        Vector2 a = line.At(cursor), b = line.At(end);
        grade = end - cursor < 1f ? 0f : Mathf.Max(0f, _map.SampleWorldSmooth(a.x, a.y) - _map.SampleWorldSmooth(b.x, b.y)) / (end - cursor);
    }

    private bool Evaluate(Line line, float cursor, float runEnd, Vector2? tangent, WaterStampDefinition definition, int index, int first, int last, bool mirror,
        float scale, bool seaEnd, float runLevel, out Candidate candidate, WaterStampPlacement? forced = null, float forcedEnd = -1f)
    {
        candidate = default;

        Vector2[] centerline = definition.Centerline;
        float chord = scale * Vector2.Distance(centerline[first], centerline[last]);
        float endArc = forced.HasValue ? forcedEnd : line.ArcAtDistance(cursor, chord);
        bool mouth = definition.Category is WaterStampCategory.RiverMouth or WaterStampCategory.RiverFork;

        if (endArc < 0f || endArc > runEnd + 1e-3f)
        {
            if (!mouth || !seaEnd)
                return false;

            endArc = -1f;
        }

        if (mouth && endArc >= 0f && endArc < runEnd - MOUTH_REACH * chord)
            return false;

        Vector2 from = line.At(cursor);
        Vector2 to = endArc >= 0f ? line.At(endArc) : from + line.Tangent(runEnd) * chord;

        if (endArc < 0f)
        {
            Vector2 heading = (line.At(runEnd) - from).normalized;

            if (heading.sqrMagnitude < 0.5f)
                return false;

            to = from + heading * chord;
            endArc = runEnd;
        }

        WaterStampPlacement placement = forced ?? WaterStampPlacement.Fit(index, definition, mirror, scale, centerline[first], centerline[last], from, to);

        Vector2 entryDirection = Direction(placement, centerline, first);

        if (tangent.HasValue && WaterStampLibrary.Angle(tangent.Value, entryDirection) > _settings.MaxTurnAdjustment)
            return Reject(from, definition, "turn at entry");

        Vector2 exitDirection = Direction(placement, centerline, last);

        if (!mouth && endArc < runEnd - 1f && WaterStampLibrary.Angle(line.Tangent(endArc), exitDirection) > _settings.MaxTurnAdjustment * EXIT_TURN_SHARE)
            return Reject(from, definition, "turn at exit");

        if (!Inside(placement, centerline, first, last))
            return Reject(from, definition, "outside the world");

        float width = _hydrology.Width(line.AreaAt(cursor)) * definition.NominalChannelWidth / _library.ReferenceWidth;
        float radius = 0.5f * width + COLLISION_PAD;
        int stride = Mathf.Max(1, Mathf.RoundToInt(SAMPLE_STEP / (WaterStampTracer.STEP * scale)));
        float level = runLevel;
        float excess = 0f;
        int count = last - first + 1;

        for (int k = first; k <= last; k += stride)
        {
            Vector2 point = placement.World(centerline[k]);
            bool tail = mouth ? k - first > count * 0.5f : k > last - stride;

            if (!tail && Standing(point, width))
                return Reject(point, definition, "enters a lake or the sea");

            if (Collides(point, radius))
                return Reject(point, definition, "runs into another river");

            if (Overlaps(point, radius))
                return Reject(point, definition, "runs into its own course");

            float ground = _map.SampleWorldSmooth(point.x, point.y);
            level = Mathf.Min(level, ground);
            excess = Mathf.Max(excess, ground - level);
        }

        float macroLevel = runLevel;
        float macroExcess = 0f;

        for (float arc = cursor; arc <= endArc; arc += SAMPLE_STEP)
        {
            Vector2 point = line.At(arc);
            float ground = _map.SampleWorldSmooth(point.x, point.y);
            macroLevel = Mathf.Min(macroLevel, ground);
            macroExcess = Mathf.Max(macroExcess, ground - macroLevel);
        }

        if (excess > macroExcess + _settings.MaxClimb)
            return Reject(from, definition, $"climbs {excess - macroExcess:0.0} m out of the valley");

        if (!Meets(placement, centerline, first, last, cursor, endArc, width))
            return Reject(from, definition, "misses a confluence");

        if (definition.Category == WaterStampCategory.RiverFork && !Fork(placement, definition))
            return Reject(from, definition, "fork branch does not reach the sea");

        candidate = new Candidate
        {
            Stamp = index,
            First = first,
            Last = last,
            EndArc = endArc,
            Placement = placement,
            Excess = Mathf.Max(0f, excess - macroExcess),
            EndLevel = level
        };

        return true;
    }

    private bool Meets(WaterStampPlacement placement, Vector2[] centerline, int first, int last, float from, float to, float width)
    {
        float reach = Mathf.Max(JOIN_TOLERANCE, 1.5f * width);

        foreach ((float arc, Vector2 point) in _confluences)
        {
            if (arc <= from || arc > to)
                continue;

            bool met = false;

            for (int k = first; k <= last && !met; k++)
                met = (placement.World(centerline[k]) - point).sqrMagnitude <= reach * reach;

            if (!met)
                return false;
        }

        return true;
    }

    private bool Reject(Vector2 point, WaterStampDefinition definition, string reason)
    {
        _layout.Reject(_record, point, definition.Name, reason);
        return false;
    }

    private bool Fork(WaterStampPlacement placement, WaterStampDefinition definition)
    {
        WaterStampBranch branch = definition.Branch(WaterStampBranchKind.BranchOut);

        if (branch == null)
            return false;

        Vector2 socket = placement.World(branch.Path[^1]);
        Vector2 exit = placement.World(definition.Centerline[^1]);

        return Sea(socket) && Sea(exit) && Inside(placement, branch.Path);
    }

    private bool Sea(Vector2 point)
    {
        return _config.SeaLevel > 0f && _map.SampleWorldSmooth(point.x, point.y) < _config.SeaLevel - SEA_DEPTH;
    }

    private bool Standing(Vector2 point, float width)
    {
        if (Hydrology.LakeNear(_water, point.x, point.y, 0.5f * width + Hydrology.RIVER_PAD) >= 0)
            return true;

        return _config.SeaLevel > 0f && _map.SampleWorldSmooth(point.x, point.y) < _config.SeaLevel;
    }

    private bool Inside(WaterStampPlacement placement, Vector2[] line)
    {
        return Inside(placement, line, 0, line.Length - 1);
    }

    private bool Inside(WaterStampPlacement placement, Vector2[] line, int first, int last)
    {
        float margin = 2f * _cell;
        float limit = _config.WorldSize - margin;

        for (int k = first; k <= last; k++)
        {
            Vector2 point = placement.World(line[k]);

            if (point.x < margin || point.y < margin || point.x > limit || point.y > limit || !float.IsFinite(point.x) || !float.IsFinite(point.y))
                return false;
        }

        return true;
    }

    private static Vector2 Direction(WaterStampPlacement placement, Vector2[] line, int i)
    {
        Vector2 a = placement.World(line[Mathf.Max(0, i - 1)]);
        Vector2 b = placement.World(line[Mathf.Min(line.Length - 1, i + 1)]);
        Vector2 direction = b - a;

        return direction.sqrMagnitude < 1e-10f ? new Vector2(1f, 0f) : direction.normalized;
    }

    private static void Span(WaterStampDefinition definition, float from, float to, out int first, out int last)
    {
        int end = definition.Centerline.Length - 1;

        first = Mathf.Clamp(Mathf.RoundToInt(from * end), 0, end - 1);
        last = Mathf.Clamp(Mathf.RoundToInt(to * end), first + 1, end);
    }

    private static bool Allowed(WaterStampCategory category, float from, float to)
    {
        bool whole = from <= 0f && to >= 1f;

        return category switch
        {
            WaterStampCategory.RiverSource or WaterStampCategory.LakeOutlet => from <= 0f,
            WaterStampCategory.RiverMouth => to >= 1f,
            WaterStampCategory.RiverFork or WaterStampCategory.TributaryJoin => whole,
            _ => true
        };
    }

    private static bool Special(WaterStampCategory category)
    {
        return category is WaterStampCategory.RiverSource or WaterStampCategory.RiverMouth or WaterStampCategory.LakeOutlet
            or WaterStampCategory.TributaryJoin or WaterStampCategory.RiverFork;
    }
}
