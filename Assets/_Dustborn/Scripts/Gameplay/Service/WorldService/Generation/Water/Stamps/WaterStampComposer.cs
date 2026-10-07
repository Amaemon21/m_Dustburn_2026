using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterStampComposer
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
}
