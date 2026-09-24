using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class WaterStampMacro
{
    public List<Vector2> Points;
    public List<float> Areas;
    public int JoinPath;
}

public sealed class WaterStampCourse
{
    public List<Vector2> Points;
    public List<float> Areas;
    public float[] WidthScale;
    public float[] DepthScale;
    public WaterStampTrack Track;
    public int Parent = -1;
    public bool HasJoinPoint;
    public Vector2 JoinPoint;
}

public sealed class WaterStampComposer
{
    private const float SAMPLE_STEP = 16f;
    private const float WIDTH_PROFILE_MIN = 0.8f;
    private const float WIDTH_PROFILE_MAX = 1.3f;
    private const float WIDTH_SCALE_MAX = 1.8f;
    private const float DEPTH_SCALE_MIN = 0.6f;
    private const float DEPTH_SCALE_MAX = 1.3f;
    private const float EXIT_TURN_SHARE = 1.5f;
    private const float COLLISION_PAD = 10f;
    private const float INDEX_CELL = 32f;
    private const float MOUTH_REACH = 0.25f;
    private const float SEA_DEPTH = 0.5f;
    private const float JOIN_TOLERANCE = 25f;
    private const float SOCKET_TOLERANCE = 40f;
    private const float SOCKET_ANGLE = 55f;
    private const float TOUCH_PAD = 1f;
    private const float FORK_AREA_SHARE = 0.5f;
    private const float FORK_TAIL = 30f;
    private const float FREEDOM_REACH = 700f;
    private const float STEEP_GRADE = 0.04f;
    private const int FREEDOM_TAPS = 6;
    private const float WINDOW_WEIGHT = 0.6f;
    private const int JOIN_MARGIN = 6;

    private static readonly float[] JoinStarts = { 0f, 0.2f, 0.35f };
    private static readonly float[] JoinStops = { 1f, 0.8f, 0.65f };

    private static readonly (float From, float To)[] Windows =
    {
        (0f, 1f), (0f, 0.66f), (0.34f, 1f), (0.17f, 0.83f), (0f, 0.5f), (0.25f, 0.75f), (0.5f, 1f)
    };

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

    private readonly WorldGenerationConfig _config;
    private readonly WaterStampLibrary _library;
    private readonly WaterStampSettings _settings;
    private readonly HeightMap _map;
    private readonly WaterMap _water;
    private readonly Hydrology _hydrology;
    private readonly WaterStampLayout _layout;
    private readonly float _cell;
    private readonly Action<List<Vector2>, List<float>, int> _procedural;
    private readonly Dictionary<long, List<Entry>> _index = new();
    private readonly Dictionary<int, Socket> _sockets = new();
    private readonly List<WaterStampCourse> _forks = new();
    private readonly List<(Vector2 Point, float Arc)> _own = new();
    private readonly List<(float Arc, Vector2 Point)> _confluences = new();
    private readonly float _referenceDepth;
    private readonly bool _record;

    private List<WaterStampMacro> _macros;
    private WaterStampCourse[] _courses;
    private int _path;
    private int _chunk;
    private float _composedArc;
    private bool _ended;

    public WaterStampComposer(WorldGenerationConfig config, WaterStampLibrary library, HeightMap map, WaterMap water, Hydrology hydrology,
        WaterStampLayout layout, float cell, Action<List<Vector2>, List<float>, int> procedural)
    {
        _config = config;
        _library = library;
        _settings = library.Settings;
        _map = map;
        _water = water;
        _hydrology = hydrology;
        _layout = layout;
        _cell = cell;
        _procedural = procedural;
        _referenceDepth = Mathf.Max(0.1f, Hydrology.DepthAtWidth(library.ReferenceWidth));
        _record = _settings.RecordRejections;
    }

    public IReadOnlyList<WaterStampCourse> Forks => _forks;

    public WaterStampCourse[] Compose(List<WaterStampMacro> macros)
    {
        _macros = macros;
        _courses = new WaterStampCourse[macros.Count];

        foreach (WaterStampMacro macro in macros)
            _layout.MacroRivers.Add(macro.Points.ToArray());

        for (_path = 0; _path < macros.Count; _path++)
        {
            _courses[_path] = ComposePath(macros[_path]);
            Register(_courses[_path], _path);
        }

        return _courses;
    }

    private WaterStampCourse ComposePath(WaterStampMacro macro)
    {
        _chunk = 0;
        _composedArc = 0f;
        _ended = false;
        _own.Clear();

        List<Vector2> points = macro.Points;
        List<float> areas = macro.Areas;
        _sockets.TryGetValue(_path, out Socket socket);

        if (socket != null && socket.Truncate + 1 < points.Count)
        {
            points = points.GetRange(0, socket.Truncate + 1);
            areas = areas.GetRange(0, socket.Truncate + 1);
        }

        var line = new Line(points, areas);
        _confluences.Clear();

        for (int t = _path + 1; t < _macros.Count; t++)
        {
            if (_macros[t].JoinPath == _path && !_sockets.ContainsKey(t))
                _confluences.Add((line.Arc[line.Nearest(_macros[t].Points[^1])], _macros[t].Points[^1]));
        }
        Status[] status = Classify(line);
        var pieces = new List<Piece>();
        float runLevel = float.PositiveInfinity;

        int start = 0;

        while (start < points.Count && !_ended)
        {
            int end = start;

            while (end + 1 < points.Count && status[end + 1] == status[start])
                end++;

            if (status[start] == Status.Open && end > start)
                ComposeRun(macro, line, status, start, end, pieces, ref runLevel);
            else
                pieces.Add(Copy(line, start, Mathf.Min(points.Count - 1, end + 1)));

            start = end + 1;
        }

        if (socket != null)
        {
            pieces.Add(Straight(points[^1], socket.Point, areas[^1]));

            var branch = new Piece();
            branch.Nodes.AddRange(socket.Branch);

            for (int i = 0; i < branch.Nodes.Count; i++)
            {
                Node node = branch.Nodes[i];
                node.Area = areas[^1];
                branch.Nodes[i] = node;
            }

            pieces.Add(branch);
        }

        List<Node> nodes = Assemble(pieces);
        WaterStampCourse course = Build(nodes);

        if (socket != null)
        {
            course.HasJoinPoint = true;
            course.JoinPoint = course.Points[^1];
        }
        else if (macro.JoinPath >= 0 && macro.JoinPath < _path && _courses[macro.JoinPath] != null)
        {
            ConnectToTrunk(course, _courses[macro.JoinPath]);
        }

        return course;
    }

    private Status[] Classify(Line line)
    {
        var status = new Status[line.Points.Count];

        for (int i = 0; i < status.Length; i++)
        {
            Vector2 point = line.Points[i];
            float width = _hydrology.Width(line.Areas[i]);

            if (Hydrology.LakeNear(_water, point.x, point.y, 0.5f * width + Hydrology.RIVER_PAD) >= 0)
                status[i] = Status.Lake;
            else if (_config.SeaLevel > 0f && _map.SampleWorldSmooth(point.x, point.y) < _config.SeaLevel)
                status[i] = Status.Sea;
        }

        return status;
    }

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

    private Piece Stamped(WaterStampDefinition definition, WaterStampPlacement placement, int placementIndex, Line line, float from, float to, Vector2[] path, int first, int last)
    {
        var piece = new Piece();
        float total = 0f;

        for (int k = first + 1; k <= last; k++)
            total += Vector2.Distance(path[k - 1], path[k]);

        float travelled = 0f;
        float nominal = Mathf.Min(WIDTH_SCALE_MAX, definition.NominalChannelWidth / _library.ReferenceWidth);
        float median = Mathf.Max(1e-3f, definition.MedianCoreHalfWidth);
        float depth = Mathf.Clamp(definition.RecommendedDepth / _referenceDepth, DEPTH_SCALE_MIN, DEPTH_SCALE_MAX);

        for (int k = first; k <= last; k++)
        {
            if (k > first)
                travelled += Vector2.Distance(path[k - 1], path[k]);

            float t = total < 1e-6f ? 0f : travelled / total;
            float profile = path == definition.Centerline ? Mathf.Clamp(definition.CoreHalfWidths[k] / median, WIDTH_PROFILE_MIN, WIDTH_PROFILE_MAX) : 1f;

            piece.Nodes.Add(new Node
            {
                Point = placement.World(path[k]),
                Area = line.AreaAt(Mathf.Lerp(from, to, t)),
                Placement = placementIndex,
                Stamp = path[k],
                WidthScale = nominal * profile,
                DepthScale = depth
            });
        }

        _composedArc += total * placement.Scale;
        return piece;
    }

    private static Piece Copy(Line line, int first, int last)
    {
        var piece = new Piece();

        for (int i = first; i <= last; i++)
            piece.Nodes.Add(new Node { Point = line.Points[i], Area = line.Areas[i], Placement = -1, WidthScale = 1f, DepthScale = 1f });

        return piece;
    }

    private static Piece Straight(Vector2 from, Vector2 to, float area)
    {
        var piece = new Piece();
        float length = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / Hydrology.RESAMPLE_STEP));

        for (int i = 0; i <= steps; i++)
            piece.Nodes.Add(new Node { Point = Vector2.Lerp(from, to, i / (float)steps), Area = area, Placement = -1, WidthScale = 1f, DepthScale = 1f });

        return piece;
    }

    private List<Node> Assemble(List<Piece> pieces)
    {
        var nodes = new List<Node>();
        var joins = new List<int>();

        foreach (Piece piece in pieces)
        {
            if (piece.Nodes.Count == 0)
                continue;

            int skip = nodes.Count > 0 && (nodes[^1].Point - piece.Nodes[0].Point).sqrMagnitude < 0.01f ? 1 : 0;

            if (nodes.Count > 0)
                joins.Add(nodes.Count - 1);

            for (int i = skip; i < piece.Nodes.Count; i++)
                nodes.Add(piece.Nodes[i]);
        }

        foreach (int join in joins)
            Blend(nodes, join);

        return nodes;
    }

    private void Blend(List<Node> nodes, int join)
    {
        if (join <= 0 || join >= nodes.Count - 1 || _settings.BlendDistance <= 0f)
            return;

        int before = join, after = join;
        float back = 0f, ahead = 0f;

        while (before > 0 && back < _settings.BlendDistance)
        {
            back += Vector2.Distance(nodes[before - 1].Point, nodes[before].Point);
            before--;
        }

        while (after < nodes.Count - 1 && ahead < _settings.BlendDistance)
        {
            ahead += Vector2.Distance(nodes[after + 1].Point, nodes[after].Point);
            after++;
        }

        if (after - before < 3)
            return;

        float total = back + ahead;
        Vector2 t0 = Tangent(nodes, before) * total;
        Vector2 t1 = Tangent(nodes, after) * total;
        var shares = new float[after - before + 1];

        for (int i = before + 1; i <= after; i++)
            shares[i - before] = shares[i - before - 1] + Vector2.Distance(nodes[i - 1].Point, nodes[i].Point);

        Vector2 p0 = nodes[before].Point;
        Vector2 p1 = nodes[after].Point;

        for (int i = before + 1; i < after; i++)
        {
            Node node = nodes[i];
            node.Point = Hermite(p0, t0, p1, t1, shares[i - before] / Mathf.Max(1e-3f, shares[^1]));
            nodes[i] = node;
        }
    }

    private static Vector2 Hermite(Vector2 p0, Vector2 t0, Vector2 p1, Vector2 t1, float s)
    {
        s = Mathf.Clamp01(s);
        float s2 = s * s, s3 = s2 * s;

        return (2f * s3 - 3f * s2 + 1f) * p0 + (s3 - 2f * s2 + s) * t0 + (-2f * s3 + 3f * s2) * p1 + (s3 - s2) * t1;
    }

    private static Vector2 Tangent(List<Node> nodes, int i)
    {
        Vector2 direction = nodes[Mathf.Min(nodes.Count - 1, i + 1)].Point - nodes[Mathf.Max(0, i - 1)].Point;

        return direction.sqrMagnitude < 1e-10f ? new Vector2(1f, 0f) : direction.normalized;
    }

    private WaterStampCourse Build(List<Node> nodes)
    {
        float step = Hydrology.RESAMPLE_STEP;
        var result = new List<Node> { nodes[0] };
        float carry = 0f;

        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            Node a = nodes[i], b = nodes[i + 1];
            float length = Vector2.Distance(a.Point, b.Point);

            if (length < 1e-4f)
                continue;

            float along = step - carry;

            while (along < length)
            {
                result.Add(Mix(a, b, along / length));
                along += step;
            }

            carry = length - (along - step);
        }

        if (Vector2.Distance(result[^1].Point, nodes[^1].Point) > 1e-3f)
            result.Add(nodes[^1]);

        int count = result.Count;
        var course = new WaterStampCourse
        {
            Points = new List<Vector2>(count),
            Areas = new List<float>(count),
            WidthScale = new float[count],
            DepthScale = new float[count],
            Track = new WaterStampTrack(count)
        };

        float area = 0f;

        for (int i = 0; i < count; i++)
        {
            area = Mathf.Max(area, result[i].Area);
            course.Points.Add(result[i].Point);
            course.Areas.Add(area);
            course.Track.Placement[i] = result[i].Placement;
            course.Track.Stamp[i] = result[i].Stamp;
            course.WidthScale[i] = result[i].WidthScale;
            course.DepthScale[i] = result[i].DepthScale;
        }

        int window = Mathf.Max(1, Mathf.RoundToInt(_settings.BlendDistance / step));
        Smooth(course.WidthScale, window);
        Smooth(course.DepthScale, window);

        return course;
    }

    private static Node Mix(Node a, Node b, float t)
    {
        bool same = a.Placement == b.Placement;
        Node near = t < 0.5f ? a : b;

        return new Node
        {
            Point = Vector2.Lerp(a.Point, b.Point, t),
            Area = Mathf.Lerp(a.Area, b.Area, t),
            Placement = near.Placement,
            Stamp = same ? Vector2.Lerp(a.Stamp, b.Stamp, t) : near.Stamp,
            WidthScale = Mathf.Lerp(a.WidthScale, b.WidthScale, t),
            DepthScale = Mathf.Lerp(a.DepthScale, b.DepthScale, t)
        };
    }

    private static void Smooth(float[] values, int radius)
    {
        var copy = (float[])values.Clone();

        for (int i = 0; i < values.Length; i++)
        {
            float sum = 0f;
            int taps = 0;

            for (int k = -radius; k <= radius; k++)
            {
                int j = i + k;

                if (j < 0 || j >= copy.Length)
                    continue;

                sum += copy[j];
                taps++;
            }

            values[i] = sum / taps;
        }
    }

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
