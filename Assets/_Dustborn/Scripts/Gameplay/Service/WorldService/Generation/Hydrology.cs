using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed class HydrologyGrid
{
    public int Resolution;
    public float CellSize;
    public float[] Height;
    public float[] Filled;
    public int[] Receiver;
    public float[] Accumulation;
    public int[] Order;
    public int[] Basin;
    public bool[] Outlet;

    public float Area(int cell)
    {
        return Accumulation[cell] * CellSize * CellSize;
    }
}

public sealed class HydrologyBasin
{
    public int Id;
    public List<int> Cells = new();
    public float Spill = float.MaxValue;
    public float Depth;
    public bool River;
    public Vector2 Center;
}

public sealed class Hydrology
{
    private const float FLOOD_EPSILON = 2e-3f;
    private const float DEPRESSION = 0.15f;
    private const float LAKE_DROP = 0.1f;
    public const float RESAMPLE_STEP = 4f;
    private const float MIN_RIVER_CELLS = 4;
    private const int RECONCILE_PASSES = 12;
    private const float LOWER_MARGIN = 0.02f;
    private const int BRIDGE_GAP = 6;
    private const float MAX_WATER_GRADE = 0.12f;
    public const float RIVER_PAD = 10f;
    public const float MERGE_TOLERANCE = 0.3f;
    private const int SETTLE_TAPS = 4;
    private const float MAX_JOIN_GRADE = 0.05f;
    private const float OUTFLOW_GRADE = 0.01f;
    private const float OUTFLOW_CLEARANCE = 0.1f;
    private const float SIMPLIFY_CELLS = 0.5f;
    private const int CHAIKIN_PASSES = 3;
    private const int BEND_PASSES = 6;
    private const float BEND_MIN_RADIUS = 10f;
    private const float BEND_WIDTHS = 1.5f;
    private const int SETTLE_SMOOTHING = 3;
    private const float MEANDER_CELLS = 1.2f;
    private const float MEANDER_WIDTHS = 1f;
    private const float MEANDER_WAVE_CELLS = 5f;
    private const float MEANDER_WAVE_WIDTHS = 8f;
    private const float CONFINED_GRADE = 0.15f;
    private const float OPEN_GRADE = 0.03f;
    private const float WIDTH_RAMP = 0.15f;
    private const int JOIN_BLEND_POINTS = 8;
    private const int CONFLUENCE_ROUNDS = 4;
    private const float CONFLUENCE_TOLERANCE = 0.05f;
    private const float STREAM_WIDTH = 2.5f;
    private const float WIDTH_PER_DOUBLING = 3.2f;
    private const float STREAM_DEPTH = 0.5f;
    private const float DEPTH_PER_DOUBLING = 0.45f;

    private static readonly float[] BANK_TAPS = { 0.25f, 0.5f };

    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private readonly WorldGenerationConfig _config;
    private readonly WaterGenerationSettings _settings;
    private readonly HeightMap _map;
    private readonly WaterStampLibrary _stamps;

    private sealed class RiverCourse
    {
        public List<Vector2> Points;
        public List<float> Areas;
        public int JoinPath;
        public int JoinCell;
        public List<int> Cells;
        public WaterStampCourse Stamp;
    }

    public List<HydrologyBasin> Basins { get; } = new();
    public WaterStampLayout Stamps { get; }
    public int Lakes { get; private set; }
    public int Ponds { get; private set; }
    public int Reconciled { get; private set; }

    public Hydrology(WorldGenerationConfig config, HeightMap map, WaterStampLibrary stamps = null)
    {
        _config = config;
        _settings = config.Water;
        _map = map;
        _stamps = stamps;

        if (stamps != null)
            Stamps = new WaterStampLayout();
    }

    public HydrologyGrid Analyze()
    {
        int resolution = Mathf.Max(2, Mathf.CeilToInt(_config.WorldSize / _settings.CellSize));
        float cell = _config.WorldSize / (float)resolution;
        int count = resolution * resolution;

        var grid = new HydrologyGrid
        {
            Resolution = resolution,
            CellSize = cell,
            Height = new float[count],
            Filled = new float[count],
            Receiver = new int[count],
            Accumulation = new float[count],
            Order = new int[count],
            Basin = new int[count],
            Outlet = new bool[count]
        };

        Parallel.For(0, resolution, row =>
        {
            for (int column = 0; column < resolution; column++)
                grid.Height[row * resolution + column] = _map.SampleWorldSmooth((column + 0.5f) * cell, (row + 0.5f) * cell);
        });

        Flood(grid);
        Route(grid);
        Accumulate(grid);
        FindBasins(grid);

        return grid;
    }

    private void Flood(HydrologyGrid grid)
    {
        int resolution = grid.Resolution;
        int count = resolution * resolution;
        var closed = new bool[count];
        var heap = new MinHeap(resolution * 8);
        float sea = _config.SeaLevel;

        for (int i = 0; i < count; i++)
        {
            int column = i % resolution;
            int row = i / resolution;

            bool border = column == 0 || row == 0 || column == resolution - 1 || row == resolution - 1;
            bool drowned = sea > 0f && grid.Height[i] < sea;

            if (!border && !drowned)
                continue;

            grid.Outlet[i] = true;
            grid.Filled[i] = grid.Height[i];
            closed[i] = true;
            heap.Push(i, grid.Height[i]);
        }

        int processed = 0;

        while (heap.TryPop(out int current))
        {
            grid.Order[processed++] = current;

            int column = current % resolution;
            int row = current / resolution;

            for (int direction = 0; direction < 8; direction++)
            {
                int c = column + OffsetX[direction];
                int r = row + OffsetZ[direction];

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int next = r * resolution + c;

                if (closed[next])
                    continue;

                closed[next] = true;
                grid.Filled[next] = Mathf.Max(grid.Height[next], grid.Filled[current] + FLOOD_EPSILON);
                heap.Push(next, grid.Filled[next]);
            }
        }
    }

    private static void Route(HydrologyGrid grid)
    {
        int resolution = grid.Resolution;
        float diagonal = Mathf.Sqrt(2f);

        Parallel.For(0, resolution, row =>
        {
            for (int column = 0; column < resolution; column++)
            {
                int index = row * resolution + column;

                if (grid.Outlet[index])
                {
                    grid.Receiver[index] = -1;
                    continue;
                }

                int best = -1;
                float steepest = 0f;

                for (int direction = 0; direction < 8; direction++)
                {
                    int c = column + OffsetX[direction];
                    int r = row + OffsetZ[direction];

                    if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                        continue;

                    int next = r * resolution + c;
                    float drop = (grid.Filled[index] - grid.Filled[next]) / ((direction & 1) == 1 ? diagonal : 1f);

                    if (drop <= steepest)
                        continue;

                    steepest = drop;
                    best = next;
                }

                grid.Receiver[index] = best;
            }
        });
    }

    private static void Accumulate(HydrologyGrid grid)
    {
        int count = grid.Accumulation.Length;

        for (int i = 0; i < count; i++)
            grid.Accumulation[i] = 1f;

        for (int i = count - 1; i >= 0; i--)
        {
            int cell = grid.Order[i];
            int receiver = grid.Receiver[cell];

            if (receiver >= 0)
                grid.Accumulation[receiver] += grid.Accumulation[cell];
        }
    }

    private void FindBasins(HydrologyGrid grid)
    {
        int resolution = grid.Resolution;
        int count = resolution * resolution;
        float start = RiverStartCells(grid);
        var queue = new Queue<int>();

        for (int i = 0; i < count; i++)
            grid.Basin[i] = -1;

        for (int seed = 0; seed < count; seed++)
        {
            if (grid.Basin[seed] >= 0 || grid.Filled[seed] - grid.Height[seed] <= DEPRESSION)
                continue;

            var basin = new HydrologyBasin { Id = Basins.Count };
            Basins.Add(basin);

            grid.Basin[seed] = basin.Id;
            queue.Enqueue(seed);

            double sumX = 0, sumZ = 0;

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                basin.Cells.Add(cell);
                basin.Spill = Mathf.Min(basin.Spill, grid.Filled[cell]);
                basin.River |= grid.Accumulation[cell] >= start;

                int column = cell % resolution;
                int row = cell / resolution;

                sumX += column;
                sumZ += row;

                for (int direction = 0; direction < 8; direction++)
                {
                    int c = column + OffsetX[direction];
                    int r = row + OffsetZ[direction];

                    if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                        continue;

                    int next = r * resolution + c;

                    if (grid.Basin[next] >= 0 || grid.Filled[next] - grid.Height[next] <= DEPRESSION)
                        continue;

                    grid.Basin[next] = basin.Id;
                    queue.Enqueue(next);
                }
            }

            foreach (int cell in basin.Cells)
                basin.Depth = Mathf.Max(basin.Depth, basin.Spill - grid.Height[cell]);

            basin.Center = new Vector2((float)(sumX / basin.Cells.Count + 0.5) * grid.CellSize, (float)(sumZ / basin.Cells.Count + 0.5) * grid.CellSize);
        }
    }

    private float RiverStartCells(HydrologyGrid grid)
    {
        return Mathf.Max(MIN_RIVER_CELLS, _settings.RiverStartArea * 1e6f / (grid.CellSize * grid.CellSize));
    }

    public WaterMap Build(HydrologyGrid grid)
    {
        var water = new WaterMap(grid.Resolution, grid.CellSize, _config.WorldSize, _config.SeaLevel) { ShoreReach = _settings.ShoreReach, Filled = grid.Filled };
        var owners = new List<HydrologyBasin>();

        SelectLakes(grid, water, owners);

        if (_stamps != null)
        {
            using (WorldGenProbe.Measure(WorldGenStage.MapWaterStampLakes))
                new WaterStampLakes(_config, _stamps, _map, grid, water, Stamps).Apply(owners);
        }

        List<(List<int> Cells, int JoinPath, int JoinCell)> paths = TracePaths(grid);
        List<RiverCourse> courses = _stamps == null ? Courses(grid, paths) : StampedCourses(grid, paths, water);

        JoinTributaries(courses);

        for (int pass = 0; pass < RECONCILE_PASSES; pass++)
        {
            var lowered = new Dictionary<int, float>();

            Profile(grid, water, courses, lowered);

            if (lowered.Count == 0)
                break;

            foreach (KeyValuePair<int, float> pair in lowered)
                Lower(grid, water, owners[pair.Key], pair.Key, pair.Value - LOWER_MARGIN);

            Reconciled++;
        }

        water.InvalidateIndex();
        Merge(water);
        water.Stamps = Stamps;

        return water;
    }

    private List<RiverCourse> Courses(HydrologyGrid grid, List<(List<int> Cells, int JoinPath, int JoinCell)> paths)
    {
        var courses = new List<RiverCourse>(paths.Count);

        for (int p = 0; p < paths.Count; p++)
        {
            Course(grid, paths[p].Cells, p, out List<Vector2> points, out List<float> areas);
            courses.Add(new RiverCourse { Points = points, Areas = areas, JoinPath = paths[p].JoinPath, JoinCell = paths[p].JoinCell, Cells = paths[p].Cells });
        }

        return courses;
    }

    private List<RiverCourse> StampedCourses(HydrologyGrid grid, List<(List<int> Cells, int JoinPath, int JoinCell)> paths, WaterMap water)
    {
        var macros = new List<WaterStampMacro>(paths.Count);

        for (int p = 0; p < paths.Count; p++)
        {
            Macro(grid, paths[p].Cells, out List<Vector2> points, out List<float> areas);
            macros.Add(new WaterStampMacro { Points = points, Areas = areas, JoinPath = paths[p].JoinPath });
        }

        var composer = new WaterStampComposer(_config, _stamps, _map, water, this, Stamps, grid.CellSize, (points, areas, index) =>
        {
            Meander(points, areas, grid.CellSize, index);
            Settle(points, areas, grid.CellSize);
        });

        WaterStampCourse[] composed;

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterStamps))
            composed = composer.Compose(macros);

        var courses = new List<RiverCourse>(paths.Count + composer.Forks.Count);

        for (int p = 0; p < paths.Count; p++)
        {
            Bend(composed[p].Points, composed[p].Areas);
            courses.Add(new RiverCourse { Points = composed[p].Points, Areas = composed[p].Areas, JoinPath = paths[p].JoinPath, JoinCell = paths[p].JoinCell, Cells = paths[p].Cells, Stamp = composed[p] });
        }

        foreach (WaterStampCourse fork in composer.Forks)
        {
            Bend(fork.Points, fork.Areas);
            courses.Add(new RiverCourse { Points = fork.Points, Areas = fork.Areas, JoinPath = -1, JoinCell = -1, Cells = new List<int>(), Stamp = fork });
        }

        return courses;
    }

    private static void JoinTributaries(List<RiverCourse> courses)
    {
        foreach (RiverCourse course in courses)
        {
            List<Vector2> points = course.Points;
            int joinPath = course.JoinPath;

            if (joinPath < 0 || joinPath >= courses.Count || points.Count < 2)
                continue;

            Vector2 end = points[^1];
            Vector2 delta = Nearest(courses[joinPath].Points, end) - end;
            int blend = Mathf.Min(points.Count - 1, JOIN_BLEND_POINTS);

            for (int k = 0; k < blend; k++)
            {
                float t = 1f - k / (float)blend;
                points[points.Count - 1 - k] += delta * (t * t * (3f - 2f * t));
            }
        }
    }

    private static Vector2 Nearest(List<Vector2> line, Vector2 point)
    {
        Vector2 best = line[0];
        float nearest = float.MaxValue;

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
        }

        return best;
    }

    private static void Merge(WaterMap water)
    {
        for (int river = 1; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;

            for (int i = 0; i < points.Count; i++)
            {
                RiverPoint point = points[i];

                if (point.Submerged || !water.InsideEarlierRiver(river, point.Position, 0.5f * point.Width, out float surface) || point.Surface > surface + MERGE_TOLERANCE)
                    continue;

                point.Submerged = true;
                points[i] = point;
            }
        }

        water.InvalidateIndex();
    }

    private void SelectLakes(HydrologyGrid grid, WaterMap water, List<HydrologyBasin> owners)
    {
        float cellArea = grid.CellSize * grid.CellSize;
        var centers = new List<(Vector2 Center, float Spacing)>();

        foreach (HydrologyBasin basin in Basins)
        {
            float surface = basin.Spill - LAKE_DROP - (basin.River ? Mathf.Min(_settings.OutletErosion, basin.Depth * 0.5f) : 0f);

            if (_config.SeaLevel > 0f && surface <= _config.SeaLevel)
                continue;

            int flooded = 0;

            foreach (int cell in basin.Cells)
            {
                if (grid.Height[cell] < surface)
                    flooded++;
            }

            float area = flooded * cellArea;

            if (area < _settings.MinPondArea || basin.Depth < _settings.MinLakeDepth / 3f)
                continue;

            bool big = area >= _settings.MinLakeArea && basin.Depth >= _settings.MinLakeDepth;

            if (!basin.River)
            {
                if (big && area > _settings.MaxLakeArea)
                    continue;

                float roll = Roll(basin.Cells[0]);

                if (roll >= (big ? _settings.LakeDensity : _settings.PondDensity))
                    continue;

                float spacing = big ? _settings.LakeSpacing : _settings.LakeSpacing * 0.5f;

                if (Crowded(centers, basin.Center, spacing))
                    continue;

                centers.Add((basin.Center, spacing));
            }

            int id = water.Bodies.Count;

            water.Bodies.Add(new WaterBody
            {
                Kind = big ? WaterKind.Lake : WaterKind.Pond,
                Surface = surface,
                Area = area,
                Depth = basin.Depth,
                Center = basin.Center
            });

            owners.Add(basin);

            if (big)
                Lakes++;
            else
                Ponds++;

            foreach (int cell in basin.Cells)
            {
                if (grid.Height[cell] < surface)
                    water.BodyIds[cell] = (short)id;
            }
        }
    }

    private static void Lower(HydrologyGrid grid, WaterMap water, HydrologyBasin basin, int id, float surface)
    {
        WaterBody body = water.Bodies[id];

        if (surface >= body.Surface)
            return;

        int flooded = 0;

        foreach (int cell in basin.Cells)
        {
            bool wet = grid.Height[cell] < surface;

            if (water.BodyIds[cell] == id && !wet)
                water.BodyIds[cell] = -1;
            else if (wet)
                water.BodyIds[cell] = (short)id;

            if (wet)
                flooded++;
        }

        body.Surface = surface;
        body.Area = flooded * grid.CellSize * grid.CellSize;
        water.Bodies[id] = body;
    }

    private static bool Crowded(List<(Vector2 Center, float Spacing)> centers, Vector2 center, float spacing)
    {
        foreach ((Vector2 other, float otherSpacing) in centers)
        {
            float gap = Mathf.Min(spacing, otherSpacing);

            if ((other - center).sqrMagnitude < gap * gap)
                return true;
        }

        return false;
    }

    private List<(List<int> Cells, int JoinPath, int JoinCell)> TracePaths(HydrologyGrid grid)
    {
        int count = grid.Accumulation.Length;
        float start = RiverStartCells(grid);
        var river = new bool[count];
        var fed = new bool[count];
        var visited = new int[count];

        for (int i = 0; i < count; i++)
        {
            river[i] = grid.Accumulation[i] >= start && !grid.Outlet[i];
            visited[i] = -1;
        }

        for (int i = 0; i < count; i++)
        {
            int receiver = grid.Receiver[i];

            if (river[i] && receiver >= 0)
                fed[receiver] = true;
        }

        var paths = new List<(List<int> Cells, int JoinPath, int JoinCell)>();

        for (int i = count - 1; i >= 0; i--)
        {
            int source = grid.Order[i];

            if (!river[source] || fed[source])
                continue;

            var cells = new List<int>();
            int current = source;
            int join = -1;
            int path = paths.Count;

            while (current >= 0)
            {
                cells.Add(current);

                if (visited[current] >= 0)
                {
                    join = visited[current];
                    break;
                }

                visited[current] = path;
                current = grid.Receiver[current];
            }

            if (cells.Count < 2)
                continue;

            paths.Add((cells, join, cells[^1]));
        }

        return paths;
    }

    private void Profile(HydrologyGrid grid, WaterMap water, List<RiverCourse> courses, Dictionary<int, float> lowered)
    {
        water.Rivers.Clear();
        Stamps?.Tracks.Clear();

        var surfaces = new Dictionary<int, float>();
        var shapedPaths = new RiverPath[courses.Count];

        for (int c = 0; c < courses.Count; c++)
        {
            RiverCourse course = courses[c];
            WaterStampCourse stamp = course.Stamp;
            RiverPath shaped = Level(water, course.Points, course.Areas, stamp?.WidthScale, stamp?.DepthScale, lowered);

            if (shaped.Points.Count < 2)
                continue;

            if (stamp == null)
            {
                if (course.JoinPath >= 0 && surfaces.TryGetValue(course.JoinCell, out float joined))
                    Join(shaped, joined);

                foreach (int cell in course.Cells)
                {
                    if (!surfaces.ContainsKey(cell))
                        surfaces[cell] = SurfaceNear(shaped, grid.CellSize * (cell % grid.Resolution + 0.5f), grid.CellSize * (cell / grid.Resolution + 0.5f));
                }
            }
            else
            {
                if (course.JoinPath >= 0 && course.JoinPath < c && shapedPaths[course.JoinPath] != null)
                    Join(shaped, SurfaceNear(shapedPaths[course.JoinPath], shaped.Points[^1].Position.x, shaped.Points[^1].Position.y), stamp.DepthScale);

                if (stamp.Parent >= 0 && shapedPaths[stamp.Parent] != null)
                    Cap(shaped, SurfaceNear(shapedPaths[stamp.Parent], shaped.Points[0].Position.x, shaped.Points[0].Position.y));
            }

            shapedPaths[c] = shaped;
            water.Rivers.Add(shaped);
            Stamps?.Tracks.Add(stamp?.Track);
        }

        if (Stamps != null)
            Confluences(courses, shapedPaths);
    }

    private void Confluences(List<RiverCourse> courses, RiverPath[] shapedPaths)
    {
        for (int round = 0; round < CONFLUENCE_ROUNDS; round++)
        {
            bool changed = false;

            for (int c = 0; c < courses.Count; c++)
            {
                RiverCourse course = courses[c];

                if (course.Stamp == null || course.JoinPath < 0 || course.JoinPath >= c || shapedPaths[c] == null || shapedPaths[course.JoinPath] == null)
                    continue;

                RiverPath tributary = shapedPaths[c];
                RiverPath trunk = shapedPaths[course.JoinPath];
                RiverPoint end = tributary.Points[^1];
                int at = NearestPoint(trunk, end.Position);
                float level = trunk.Points[at].Surface;

                if (trunk.Points[at].Submerged)
                    continue;

                if (end.Surface > level + MERGE_TOLERANCE)
                {
                    Join(tributary, level, course.Stamp.DepthScale);
                    changed = true;
                }
                else if (end.Surface < level - CONFLUENCE_TOLERANCE)
                {
                    changed |= Drawdown(trunk, at, end.Surface);
                }
            }

            if (!changed)
                break;
        }
    }

    private static bool Drawdown(RiverPath trunk, int from, float level)
    {
        List<RiverPoint> points = trunk.Points;
        int last = from;

        while (last + 1 < points.Count && !points[last + 1].Submerged)
            last++;

        if (last + 1 < points.Count && points[last + 1].Surface > level)
            return false;

        for (int i = from; i <= last; i++)
        {
            RiverPoint point = points[i];

            if (point.Surface <= level)
                break;

            point.Bed -= point.Surface - level;
            point.Surface = level;
            points[i] = point;
        }

        for (int i = from - 1; i >= 0; i--)
        {
            RiverPoint point = points[i];
            float limit = points[i + 1].Surface + MAX_WATER_GRADE * Vector2.Distance(point.Position, points[i + 1].Position);

            if (point.Surface <= limit)
                break;

            point.Bed -= point.Surface - limit;
            point.Surface = limit;
            points[i] = point;
        }

        return true;
    }

    private static int NearestPoint(RiverPath path, Vector2 position)
    {
        int best = 0;
        float nearest = float.MaxValue;

        for (int i = 0; i < path.Points.Count; i++)
        {
            float distance = (path.Points[i].Position - position).sqrMagnitude;

            if (distance >= nearest)
                continue;

            nearest = distance;
            best = i;
        }

        return best;
    }

    private static void Cap(RiverPath path, float level)
    {
        for (int i = 0; i < path.Points.Count; i++)
        {
            RiverPoint point = path.Points[i];

            if (point.Surface <= level)
                continue;

            point.Bed -= point.Surface - level;
            point.Surface = level;
            path.Points[i] = point;
        }
    }

    private void Join(RiverPath path, float joined, float[] depthScale = null)
    {
        float level = joined;

        for (int i = path.Points.Count - 1; i >= 0; i--)
        {
            RiverPoint point = path.Points[i];

            if (point.Surface <= level)
                break;

            point.Surface = level;
            point.Bed = Mathf.Min(point.Bed, level - (depthScale == null ? Depth(point.Flow) : Depth(point.Flow) * depthScale[i]));
            path.Points[i] = point;

            level += MAX_JOIN_GRADE * (i > 0 ? Vector2.Distance(path.Points[i - 1].Position, point.Position) : 0f);
        }
    }

    private static float SurfaceNear(RiverPath path, float x, float z)
    {
        float best = float.MaxValue;
        float surface = 0f;
        var point = new Vector2(x, z);

        foreach (RiverPoint candidate in path.Points)
        {
            float distance = (candidate.Position - point).sqrMagnitude;

            if (distance >= best)
                continue;

            best = distance;
            surface = candidate.Surface;
        }

        return surface;
    }

    private void Course(HydrologyGrid grid, List<int> cells, int index, out List<Vector2> points, out List<float> areas)
    {
        Macro(grid, cells, out points, out areas);
        Meander(points, areas, grid.CellSize, index);
        Settle(points, areas, grid.CellSize);
        Bend(points, areas);
    }

    private void Macro(HydrologyGrid grid, List<int> cells, out List<Vector2> points, out List<float> areas)
    {
        int resolution = grid.Resolution;
        var positions = new List<Vector2>(cells.Count);
        var flows = new List<float>(cells.Count);

        foreach (int cell in cells)
        {
            positions.Add(new Vector2((cell % resolution + 0.5f) * grid.CellSize, (cell / resolution + 0.5f) * grid.CellSize));
            flows.Add(grid.Area(cell));
        }

        Simplify(positions, flows, SIMPLIFY_CELLS * grid.CellSize);

        for (int pass = 0; pass < CHAIKIN_PASSES; pass++)
            Chaikin(positions, flows);

        Resample(positions, flows, Mathf.Min(RESAMPLE_STEP, grid.CellSize * 0.5f), out points, out areas);
    }

    private static void Simplify(List<Vector2> positions, List<float> flows, float tolerance)
    {
        int count = positions.Count;

        if (count < 3)
            return;

        var keep = new bool[count];
        var stack = new Stack<(int From, int To)>();

        keep[0] = true;
        keep[count - 1] = true;
        stack.Push((0, count - 1));

        while (stack.Count > 0)
        {
            (int from, int to) = stack.Pop();
            Vector2 a = positions[from];
            Vector2 axis = positions[to] - a;
            float length = axis.sqrMagnitude;
            int farthest = -1;
            float worst = tolerance * tolerance;

            for (int i = from + 1; i < to; i++)
            {
                float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(positions[i] - a, axis) / length);
                float distance = (positions[i] - (a + axis * t)).sqrMagnitude;

                if (distance <= worst)
                    continue;

                worst = distance;
                farthest = i;
            }

            if (farthest < 0)
                continue;

            keep[farthest] = true;
            stack.Push((from, farthest));
            stack.Push((farthest, to));
        }

        int write = 0;

        for (int i = 0; i < count; i++)
        {
            if (!keep[i])
                continue;

            positions[write] = positions[i];
            flows[write] = flows[i];
            write++;
        }

        positions.RemoveRange(write, count - write);
        flows.RemoveRange(write, count - write);
    }

    private void Bend(List<Vector2> points, List<float> areas)
    {
        for (int pass = 0; pass < BEND_PASSES; pass++)
        {
            for (int i = 1; i + 1 < points.Count; i++)
            {
                Vector2 before = points[i] - points[i - 1];
                Vector2 after = points[i + 1] - points[i];
                float first = before.magnitude, second = after.magnitude;
                float chord = 0.5f * (first + second);
                float turn = first * second < 1e-8f ? 0f : Mathf.Acos(Mathf.Clamp(Vector2.Dot(before, after) / (first * second), -1f, 1f));

                if (turn < 1e-4f || chord / turn >= Mathf.Max(BEND_MIN_RADIUS, BEND_WIDTHS * Width(areas[i])))
                    continue;

                points[i] = Vector2.Lerp(points[i], 0.5f * (points[i - 1] + points[i + 1]), 0.5f);
            }
        }
    }

    private void Settle(List<Vector2> points, List<float> areas, float cell)
    {
        int count = points.Count;

        if (count < 3)
            return;

        var offsets = new float[count];
        var normals = new Vector2[count];

        for (int i = 1; i + 1 < count; i++)
        {
            Vector2 tangent = (points[i + 1] - points[i - 1]).normalized;
            normals[i] = new Vector2(-tangent.y, tangent.x);

            float reach = Mathf.Min(0.6f * cell, Mathf.Max(3f, 0.75f * Width(areas[i])));
            float best = float.MaxValue;

            for (int k = -SETTLE_TAPS; k <= SETTLE_TAPS; k++)
            {
                float offset = reach * k / SETTLE_TAPS;
                Vector2 probe = points[i] + normals[i] * offset;
                float ground = _map.SampleWorldSmooth(probe.x, probe.y) + Mathf.Abs(offset) * 0.01f;

                if (ground >= best)
                    continue;

                best = ground;
                offsets[i] = offset;
            }
        }

        var smoothed = new float[count];

        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 1; i + 1 < count; i++)
            {
                float sum = 0f;
                int taps = 0;

                for (int k = -SETTLE_SMOOTHING; k <= SETTLE_SMOOTHING; k++)
                {
                    int j = i + k;

                    if (j <= 0 || j >= count - 1)
                        continue;

                    sum += offsets[j];
                    taps++;
                }

                smoothed[i] = sum / taps;
            }

            Array.Copy(smoothed, offsets, count);
        }

        for (int i = 1; i + 1 < count; i++)
        {
            float taper = Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / 3f);
            points[i] += normals[i] * (offsets[i] * taper);
        }
    }

    private RiverPath Level(WaterMap water, List<Vector2> points, List<float> areas, float[] widthScale, float[] depthScale, Dictionary<int, float> lowered)
    {
        var path = new RiverPath();
        float level = float.PositiveInfinity;
        float outflow = float.NaN;
        float sinceOutlet = 0f;
        var bodies = new List<int>(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 position = points[i];
            float depth = depthScale == null ? Depth(areas[i]) : Depth(areas[i]) * depthScale[i];
            float width = widthScale == null ? Width(areas[i]) : Width(areas[i]) * widthScale[i];

            Vector2 before = points[Mathf.Max(0, i - 1)];
            Vector2 after = points[Mathf.Min(points.Count - 1, i + 1)];
            Vector2 tangent = (after - before).normalized;
            var normal = new Vector2(-tangent.y, tangent.x);

            float ground = _map.SampleWorldSmooth(position.x, position.y);
            float banks = ground;

            foreach (float share in BANK_TAPS)
            {
                Vector2 left = position + normal * (width * share + 1f);
                Vector2 right = position - normal * (width * share + 1f);

                banks = Mathf.Min(banks, Mathf.Min(_map.SampleWorldSmooth(left.x, left.y), _map.SampleWorldSmooth(right.x, right.y)));
            }

            bool sea = _config.SeaLevel > 0f && ground < _config.SeaLevel;
            int body = LakeNear(water, position.x, position.y, 0.5f * width + RIVER_PAD);
            bool lake = body >= 0;
            bodies.Add(body);

            if (lake)
            {
                float surface = water.Bodies[body].Surface;

                if (level < surface)
                {
                    lowered[body] = lowered.TryGetValue(body, out float current) ? Mathf.Min(current, level) : level;
                    surface = level;
                }

                level = Mathf.Min(level, surface);
                outflow = level;
                sinceOutlet = 0f;
            }
            else if (sea)
            {
                level = Mathf.Min(level, _config.SeaLevel);
                outflow = float.NaN;
            }
            else
            {
                float natural = banks - Freeboard(depth);

                if (!float.IsNaN(outflow))
                {
                    sinceOutlet += Vector2.Distance(before, position);
                    float held = Mathf.Min(outflow - OUTFLOW_GRADE * sinceOutlet, banks - OUTFLOW_CLEARANCE);

                    if (held > natural)
                        natural = held;
                    else
                        outflow = float.NaN;
                }

                level = Mathf.Min(level, natural);

                if (_config.SeaLevel > 0f && level <= _config.SeaLevel)
                {
                    level = _config.SeaLevel;
                    sea = true;
                }
            }

            path.Points.Add(new RiverPoint
            {
                Position = position,
                Surface = level,
                Bed = level - depth,
                Width = width,
                Flow = areas[i],
                Submerged = lake || sea
            });
        }

        Ramp(path.Points);

        if (widthScale != null)
            Taper(path.Points);

        Bridge(path.Points);
        Ease(path.Points);
        Drain(water, path.Points, bodies, lowered);

        return path;
    }

    private static void Drain(WaterMap water, List<RiverPoint> points, List<int> bodies, Dictionary<int, float> lowered)
    {
        for (int i = 0; i < points.Count; i++)
        {
            int body = bodies[i];

            if (body < 0 || points[i].Surface >= water.Bodies[body].Surface - LOWER_MARGIN)
                continue;

            lowered[body] = lowered.TryGetValue(body, out float current) ? Mathf.Min(current, points[i].Surface) : points[i].Surface;
        }
    }

    private static void Ramp(List<RiverPoint> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            RiverPoint point = points[i];
            float limit = points[i - 1].Width + WIDTH_RAMP * Vector2.Distance(points[i - 1].Position, point.Position);

            if (point.Width <= limit)
                continue;

            point.Width = limit;
            points[i] = point;
        }
    }

    private static void Taper(List<RiverPoint> points)
    {
        for (int i = points.Count - 2; i >= 0; i--)
        {
            RiverPoint point = points[i];
            float limit = points[i + 1].Width + WIDTH_RAMP * Vector2.Distance(points[i + 1].Position, point.Position);

            if (point.Width <= limit)
                continue;

            point.Width = limit;
            points[i] = point;
        }
    }

    private static void Ease(List<RiverPoint> points)
    {
        for (int i = points.Count - 2; i >= 0; i--)
        {
            RiverPoint point = points[i];
            float limit = points[i + 1].Surface + MAX_WATER_GRADE * Vector2.Distance(point.Position, points[i + 1].Position);

            if (point.Surface <= limit)
                continue;

            point.Bed -= point.Surface - limit;
            point.Surface = limit;
            points[i] = point;
        }
    }

    private static void Bridge(List<RiverPoint> points)
    {
        int last = -1;

        for (int i = 0; i < points.Count; i++)
        {
            if (!points[i].Submerged)
                continue;

            if (last >= 0 && i - last - 1 <= BRIDGE_GAP)
            {
                for (int j = last + 1; j < i; j++)
                {
                    RiverPoint point = points[j];
                    point.Submerged = true;
                    point.Surface = Mathf.Min(point.Surface, points[last].Surface);
                    points[j] = point;
                }
            }

            last = i;
        }
    }

    public static int LakeNear(WaterMap water, float x, float z, float reach)
    {
        int resolution = water.Resolution;
        int column = Mathf.Clamp((int)(x / water.CellSize), 0, resolution - 1);
        int row = Mathf.Clamp((int)(z / water.CellSize), 0, resolution - 1);

        int own = water.BodyIds[row * resolution + column];

        if (own >= 0)
            return own;

        int found = -1;
        float lowest = float.MaxValue;

        int span = Mathf.Max(1, Mathf.CeilToInt(reach / water.CellSize));

        for (int dz = -span; dz <= span; dz++)
        {
            for (int dx = -span; dx <= span; dx++)
            {
                int c = column + dx, r = row + dz;

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                float nearX = Mathf.Clamp(x, c * water.CellSize, (c + 1) * water.CellSize) - x;
                float nearZ = Mathf.Clamp(z, r * water.CellSize, (r + 1) * water.CellSize) - z;

                if (nearX * nearX + nearZ * nearZ > reach * reach)
                    continue;

                int id = water.BodyIds[r * resolution + c];

                if (id < 0 || water.Bodies[id].Surface >= lowest)
                    continue;

                lowest = water.Bodies[id].Surface;
                found = id;
            }
        }

        return found;
    }

    public float Width(float area)
    {
        float ratio = Mathf.Max(1f, area / (_settings.RiverStartArea * 1e6f));

        return _settings.RiverWidthScale * (STREAM_WIDTH + WIDTH_PER_DOUBLING * Mathf.Log(ratio, 2f));
    }

    public float Depth(float area)
    {
        float ratio = Mathf.Max(1f, area / (_settings.RiverStartArea * 1e6f));

        return _settings.RiverDepthScale * (STREAM_DEPTH + DEPTH_PER_DOUBLING * Mathf.Log(ratio, 2f));
    }

    public static float DepthAtWidth(float width)
    {
        return STREAM_DEPTH + DEPTH_PER_DOUBLING * Mathf.Max(0f, width - STREAM_WIDTH) / WIDTH_PER_DOUBLING;
    }

    private static float Freeboard(float depth)
    {
        return 0.15f + 0.2f * depth;
    }

    private static void Chaikin(List<Vector2> positions, List<float> flows)
    {
        if (positions.Count < 3)
            return;

        var smoothed = new List<Vector2>(positions.Count * 2) { positions[0] };
        var carried = new List<float>(positions.Count * 2) { flows[0] };

        for (int i = 0; i + 1 < positions.Count; i++)
        {
            Vector2 a = positions[i];
            Vector2 b = positions[i + 1];

            if (i > 0)
            {
                smoothed.Add(Vector2.Lerp(a, b, 0.25f));
                carried.Add(Mathf.Lerp(flows[i], flows[i + 1], 0.25f));
            }

            if (i + 2 < positions.Count)
            {
                smoothed.Add(Vector2.Lerp(a, b, 0.75f));
                carried.Add(Mathf.Lerp(flows[i], flows[i + 1], 0.75f));
            }
        }

        smoothed.Add(positions[^1]);
        carried.Add(flows[^1]);

        positions.Clear();
        positions.AddRange(smoothed);
        flows.Clear();
        flows.AddRange(carried);
    }

    private void Meander(List<Vector2> points, List<float> areas, float cell, int index)
    {
        int count = points.Count;

        if (_settings.Meander <= 0f || count < 4)
            return;

        var random = new Unity.Mathematics.Random((uint)(_config.Seed * 7919 + index * 104729) | 1u);
        var along = new float[count];

        for (int i = 1; i < count; i++)
            along[i] = along[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        var knots = new List<(float At, float Value)> { (0f, random.NextFloat(-1f, 1f)) };

        while (knots[^1].At < along[count - 1])
        {
            float width = Width(areas[Mathf.Min(count - 1, Index(along, knots[^1].At))]);
            float spacing = Mathf.Max(MEANDER_WAVE_CELLS * cell, MEANDER_WAVE_WIDTHS * width) * random.NextFloat(0.6f, 1.4f);

            knots.Add((knots[^1].At + spacing, random.NextFloat(-1f, 1f)));
        }

        var freedom = new float[count];

        for (int i = 1; i + 1 < count; i++)
            freedom[i] = Freedom(points[i], (points[i + 1] - points[i - 1]).normalized, Width(areas[i]), cell);

        Blur(freedom, 4);

        var offsets = new Vector2[count];
        int knot = 0;

        for (int i = 1; i + 1 < count; i++)
        {
            while (knot + 2 < knots.Count && knots[knot + 1].At < along[i])
                knot++;

            float t = Mathf.Clamp01((along[i] - knots[knot].At) / Mathf.Max(1e-3f, knots[knot + 1].At - knots[knot].At));
            float wave = Mathf.Lerp(knots[knot].Value, knots[knot + 1].Value, t * t * (3f - 2f * t));

            Vector2 tangent = (points[i + 1] - points[i - 1]).normalized;
            float amplitude = _settings.Meander * freedom[i] * Mathf.Min(MEANDER_CELLS * cell, MEANDER_WIDTHS * Width(areas[i]) + 2f);
            float taper = Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / 6f);

            offsets[i] = new Vector2(-tangent.y, tangent.x) * (amplitude * taper * wave);
        }

        for (int i = 1; i + 1 < count; i++)
            points[i] += offsets[i];
    }

    private static int Index(float[] along, float at)
    {
        int index = Array.BinarySearch(along, at);

        return index >= 0 ? index : Mathf.Max(0, ~index - 1);
    }

    public float Freedom(Vector2 point, Vector2 tangent, float width, float cell)
    {
        var normal = new Vector2(-tangent.y, tangent.x);
        float reach = cell + width;
        float center = _map.SampleWorldSmooth(point.x, point.y);
        Vector2 left = point + normal * reach;
        Vector2 right = point - normal * reach;
        float rise = Mathf.Min(_map.SampleWorldSmooth(left.x, left.y), _map.SampleWorldSmooth(right.x, right.y)) - center;
        float t = Mathf.Clamp01((rise / reach - OPEN_GRADE) / (CONFINED_GRADE - OPEN_GRADE));

        return 1f - t * t * (3f - 2f * t);
    }

    private static void Blur(float[] values, int radius)
    {
        var copy = (float[])values.Clone();

        for (int i = 0; i < values.Length; i++)
        {
            float sum = 0f;
            int taps = 0;

            for (int k = -radius; k <= radius; k++)
            {
                int j = i + k;

                if (j < 0 || j >= values.Length)
                    continue;

                sum += copy[j];
                taps++;
            }

            values[i] = sum / taps;
        }
    }

    private static void Resample(List<Vector2> positions, List<float> flows, float step, out List<Vector2> points, out List<float> areas)
    {
        points = new List<Vector2> { positions[0] };
        areas = new List<float> { flows[0] };

        float carry = 0f;

        for (int i = 0; i + 1 < positions.Count; i++)
        {
            Vector2 a = positions[i];
            Vector2 b = positions[i + 1];
            float length = Vector2.Distance(a, b);

            if (length < 1e-4f)
                continue;

            float along = step - carry;

            while (along < length)
            {
                float t = along / length;

                points.Add(Vector2.Lerp(a, b, t));
                areas.Add(Mathf.Lerp(flows[i], flows[i + 1], t));
                along += step;
            }

            carry = length - (along - step);
        }

        if (Vector2.Distance(points[^1], positions[^1]) > 1e-3f)
        {
            points.Add(positions[^1]);
            areas.Add(flows[^1]);
        }
    }

    private float Roll(int cell)
    {
        unchecked
        {
            uint hash = (uint)_config.Seed * 2654435761u ^ (uint)(cell + 1) * 2246822519u;

            hash ^= hash >> 15;
            hash *= 3266489917u;
            hash ^= hash >> 13;

            return (hash & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
