using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterStampComposer
{
    private void ConnectToTrunk(WaterStampCourse course, WaterStampCourse trunk)
    {
        List<Vector2> points = course.Points;
        int count = points.Count;
        float length = 0f;

        for (int i = 1; i < count; i++)
            length += Vector2.Distance(points[i - 1], points[i]);

        float travelled = 0f;
        int cut = -1;
        Vector2 target = Vector2.zero;

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                travelled += Vector2.Distance(points[i - 1], points[i]);

            if (travelled < 0.4f * length)
                continue;

            Vector2 nearest = NearestOn(trunk, points[i], out float half);

            if ((nearest - points[i]).sqrMagnitude > (half + TOUCH_PAD) * (half + TOUCH_PAD))
                continue;

            cut = i;
            target = nearest;
            break;
        }

        if (cut < 0)
        {
            cut = count - 1;
            target = NearestOn(trunk, points[^1], out _);
        }

        Truncate(course, cut + 1);

        Vector2 end = course.Points[^1];
        float gap = Vector2.Distance(end, target);
        int steps = Mathf.CeilToInt(gap / Hydrology.RESAMPLE_STEP);
        float area = course.Areas[^1];

        for (int i = 1; i <= steps; i++)
            Append(course, Vector2.Lerp(end, target, i / (float)steps), area);

        if (steps > 0)
            BlendTail(course, steps);

        course.HasJoinPoint = true;
        course.JoinPoint = course.Points[^1];
    }

    private void BlendTail(WaterStampCourse course, int added)
    {
        List<Vector2> points = course.Points;
        int join = points.Count - 1 - added;
        int span = Mathf.Min(join, Mathf.Max(2, Mathf.RoundToInt(_settings.BlendDistance / Hydrology.RESAMPLE_STEP)));

        if (span < 2 || join <= 0)
            return;

        int from = join - span;
        Vector2 p0 = points[from];
        Vector2 p1 = points[^1];
        var shares = new float[points.Count - from];

        for (int i = from + 1; i < points.Count; i++)
            shares[i - from] = shares[i - from - 1] + Vector2.Distance(points[i - 1], points[i]);

        float total = shares[^1];
        Vector2 t0 = (points[from + 1] - points[from]).normalized * total;
        Vector2 t1 = (p1 - points[join]).normalized * total;

        for (int i = from + 1; i < points.Count - 1; i++)
            points[i] = Hermite(p0, t0, p1, t1, shares[i - from] / Mathf.Max(1e-3f, total));
    }

    private static void Truncate(WaterStampCourse course, int count)
    {
        if (count >= course.Points.Count)
            return;

        course.Points.RemoveRange(count, course.Points.Count - count);
        course.Areas.RemoveRange(count, course.Areas.Count - count);
        Array.Resize(ref course.WidthScale, count);
        Array.Resize(ref course.DepthScale, count);
        Array.Resize(ref course.Track.Placement, count);
        Array.Resize(ref course.Track.Stamp, count);
    }

    private static void Append(WaterStampCourse course, Vector2 point, float area)
    {
        int count = course.Points.Count;

        course.Points.Add(point);
        course.Areas.Add(area);
        Array.Resize(ref course.WidthScale, count + 1);
        Array.Resize(ref course.DepthScale, count + 1);
        Array.Resize(ref course.Track.Placement, count + 1);
        Array.Resize(ref course.Track.Stamp, count + 1);

        course.WidthScale[count] = course.WidthScale[count - 1];
        course.DepthScale[count] = course.DepthScale[count - 1];
        course.Track.Placement[count] = -1;
    }

    private Vector2 NearestOn(WaterStampCourse trunk, Vector2 point, out float half)
    {
        List<Vector2> line = trunk.Points;
        Vector2 best = line[0];
        float nearest = float.MaxValue;
        int segment = 0;

        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vector2 axis = line[i + 1] - line[i];
            float length = axis.sqrMagnitude;
            float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - line[i], axis) / length);
            Vector2 candidate = line[i] + axis * t;
            float distance = (candidate - point).sqrMagnitude;

            if (distance >= nearest)
                continue;

            nearest = distance;
            best = candidate;
            segment = i;
        }

        half = 0.5f * _hydrology.Width(trunk.Areas[segment]) * trunk.WidthScale[segment];
        return best;
    }

    private (float Arc, Vector2 Point)? NextConfluence(Line line, float cursor)
    {
        (float Arc, Vector2 Point)? next = null;

        for (int t = _path + 1; t < _macros.Count; t++)
        {
            if (_macros[t].JoinPath != _path || _sockets.ContainsKey(t))
                continue;

            Vector2 point = _macros[t].Points[^1];
            float arc = line.Arc[line.Nearest(point)];

            if (arc <= cursor + 1f || next.HasValue && arc >= next.Value.Arc)
                continue;

            next = (arc, point);
        }

        return next;
    }

    private bool PrepareJoin(Vector2 end, Vector2 confluence)
    {
        int index = _library.Find(WaterStampCategory.TributaryJoin);

        if (index < 0)
            return false;

        WaterStampDefinition definition = _library.Get(index);
        WaterStampBranch branch = definition.Branch(WaterStampBranchKind.TributaryIn);

        if (branch == null)
            return false;

        float distance = Vector2.Distance(end, confluence);
        int last = definition.Centerline.Length - 1;

        foreach (float start in JoinStarts)
        {
            float lead = Vector2.Distance(definition.Centerline[Mathf.RoundToInt(start * last)], definition.Centerline[branch.JunctionIndex]);

            if (distance >= lead * definition.MinScale && distance <= lead * definition.MaxScale)
                return true;
        }

        return false;
    }

    private bool TryJoin(Line line, float cursor, float runEnd, Vector2? tangent, ref float runLevel, List<Piece> pieces, out float next, out Vector2 exitTangent)
    {
        next = cursor;
        exitTangent = Vector2.zero;

        int index = _library.Find(WaterStampCategory.TributaryJoin);

        if (index < 0)
            return false;

        WaterStampDefinition definition = _library.Get(index);
        WaterStampBranch branch = definition.Branch(WaterStampBranchKind.TributaryIn);

        if (branch == null)
            return false;

        Vector2 from = line.At(cursor);
        Vector2[] centerline = definition.Centerline;
        int end = centerline.Length - 1;
        Vector2 junction = centerline[branch.JunctionIndex];

        for (int t = _path + 1; t < _macros.Count; t++)
        {
            WaterStampMacro tributary = _macros[t];

            if (tributary.JoinPath != _path || _sockets.ContainsKey(t) || tributary.Points.Count < 4)
                continue;

            Vector2 confluence = tributary.Points[^1];

            foreach (float start in JoinStarts)
            {
                int first = Mathf.RoundToInt(start * end);

                if (first >= branch.JunctionIndex - JOIN_MARGIN)
                    continue;

                float scale = Vector2.Distance(from, confluence) / Vector2.Distance(centerline[first], junction);

                if (scale < definition.MinScale || scale > definition.MaxScale)
                    continue;

                foreach (float stop in JoinStops)
                {
                    int last = Mathf.RoundToInt(stop * end);

                    if (last <= branch.JunctionIndex + JOIN_MARGIN)
                        continue;

                    _layout.Candidates++;

                    if (Join(line, cursor, runEnd, tangent, definition, index, branch, first, last, scale, tributary, t, confluence, ref runLevel, pieces, out next, out exitTangent))
                        return true;
                }
            }
        }

        return false;
    }

    private bool Join(Line line, float cursor, float runEnd, Vector2? tangent, WaterStampDefinition definition, int index, WaterStampBranch branch, int first, int last,
        float scale, WaterStampMacro tributary, int path, Vector2 confluence, ref float runLevel, List<Piece> pieces, out float next, out Vector2 exitTangent)
    {
        next = cursor;
        exitTangent = Vector2.zero;

        Vector2[] centerline = definition.Centerline;
        Vector2 from = line.At(cursor);
        float chord = scale * Vector2.Distance(centerline[first], centerline[last]);
        WaterStampPlacement placement = WaterStampPlacement.Fit(index, definition, false, scale, centerline[first], centerline[branch.JunctionIndex], from, confluence);
        Vector2 exit = placement.World(centerline[last]);
        int landing = line.Nearest(exit, line.Segment(cursor));
        float endArc = line.Arc[landing];

        if (landing >= line.Points.Count - 1 || endArc > runEnd || endArc <= cursor)
            return Reject(confluence, definition, "junction: the trunk run ends first");

        if (Vector2.Distance(line.Points[landing], exit) > Mathf.Max(JOIN_TOLERANCE, 0.06f * chord))
            return Reject(confluence, definition, "junction: the exit leaves the trunk");

        if (tangent.HasValue && WaterStampLibrary.Angle(tangent.Value, Direction(placement, centerline, first)) > _settings.MaxTurnAdjustment)
            return Reject(confluence, definition, "junction: turn at entry");

        if (!Inside(placement, centerline, first, last) || !Inside(placement, branch.Path))
            return Reject(confluence, definition, "junction: outside the world");

        Vector2 socket = placement.World(branch.Path[0]);
        Vector2 inflow = Direction(placement, branch.Path, 0);
        var tributaryLine = new Line(tributary.Points, tributary.Areas);
        int nearest = tributaryLine.Nearest(socket);
        float branchLength = 0f;

        for (int k = 1; k < branch.Path.Length; k++)
            branchLength += Vector2.Distance(branch.Path[k - 1], branch.Path[k]) * scale;

        if (Vector2.Distance(tributaryLine.Points[nearest], socket) > Mathf.Max(SOCKET_TOLERANCE, 0.15f * branchLength))
            return Reject(socket, definition, "junction: the tributary misses the branch socket");

        if (tributaryLine.Length - tributaryLine.Arc[nearest] < 0.5f * branchLength)
            return Reject(socket, definition, "junction: the tributary is too short for the branch");

        if (WaterStampLibrary.Angle(tributaryLine.Tangent(tributaryLine.Arc[nearest]), inflow) > SOCKET_ANGLE)
            return Reject(socket, definition, "junction: the tributary arrives at the wrong angle");

        if (!Evaluate(line, cursor, runEnd, tangent, definition, index, first, last, false, scale, false, runLevel, out Candidate candidate, placement, endArc))
            return false;

        if (!Descends(placement, branch.Path, tributaryLine, nearest))
            return Reject(socket, definition, "tributary branch climbs to the junction");

        placement.River = _path;
        placement.First = first;
        placement.Last = last;
        int placementIndex = _layout.Add(placement);
        Piece stamped = Stamped(definition, placement, placementIndex, line, cursor, endArc, centerline, first, last);
        pieces.Add(stamped);
        Own(stamped);

        Piece branchPiece = Stamped(definition, placement, placementIndex, tributaryLine, tributaryLine.Arc[nearest], tributaryLine.Length, branch.Path, 0, branch.Path.Length - 1);
        _composedArc -= branchLength;

        _sockets[path] = new Socket { Point = socket, Truncate = nearest, Branch = branchPiece.Nodes };
        _layout.TributaryJoins++;

        next = endArc;
        exitTangent = Direction(placement, centerline, last);
        runLevel = candidate.EndLevel;
        return true;
    }

    private bool Descends(WaterStampPlacement placement, Vector2[] path, Line tributary, int from)
    {
        float level = float.PositiveInfinity;
        float excess = 0f;

        foreach (Vector2 stamp in path)
        {
            Vector2 point = placement.World(stamp);
            float ground = _map.SampleWorldSmooth(point.x, point.y);
            level = Mathf.Min(level, ground);
            excess = Mathf.Max(excess, ground - level);
        }

        float macroLevel = float.PositiveInfinity;
        float macroExcess = 0f;

        for (int i = from; i < tributary.Points.Count; i++)
        {
            Vector2 point = tributary.Points[i];
            float ground = _map.SampleWorldSmooth(point.x, point.y);
            macroLevel = Mathf.Min(macroLevel, ground);
            macroExcess = Mathf.Max(macroExcess, ground - macroLevel);
        }

        return excess <= macroExcess + _settings.MaxClimb;
    }

    private void AddFork(WaterStampDefinition definition, WaterStampPlacement placement, int placementIndex, Line line, float endArc)
    {
        WaterStampBranch branch = definition.Branch(WaterStampBranchKind.BranchOut);

        if (branch == null)
            return;

        float area = FORK_AREA_SHARE * line.AreaAt(endArc);
        Piece piece = Stamped(definition, placement, placementIndex, line, endArc, endArc, branch.Path, 0, branch.Path.Length - 1);
        Vector2 end = piece.Nodes[^1].Point;
        Vector2 outward = Direction(placement, branch.Path, branch.Path.Length - 1);

        for (int i = 0; i < piece.Nodes.Count; i++)
        {
            Node node = piece.Nodes[i];
            node.Area = area;
            piece.Nodes[i] = node;
        }

        var nodes = new List<Node>(piece.Nodes);
        Node tail = nodes[^1];
        tail.Point = end + outward * FORK_TAIL;
        tail.Placement = -1;
        nodes.Add(tail);

        WaterStampCourse course = Build(nodes);
        course.Parent = _path;
        _forks.Add(course);
        _layout.Forks++;
    }
}
