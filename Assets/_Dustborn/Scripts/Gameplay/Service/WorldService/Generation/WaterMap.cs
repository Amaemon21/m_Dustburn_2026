using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum WaterKind : byte
{
    None,
    Sea,
    River,
    Lake,
    Pond
}

public struct WaterSample
{
    public WaterKind Kind;
    public float Surface;
    public float Width;
    public Vector2 Flow;
    public int Body;

    public bool IsWater => Kind != WaterKind.None;
}

[Serializable]
public struct WaterBody
{
    public WaterKind Kind;
    public float Surface;
    public float Area;
    public float Depth;
    public Vector2 Center;
}

[Serializable]
public struct RiverPoint
{
    public Vector2 Position;
    public float Surface;
    public float Bed;
    public float Width;
    public float Flow;
    public bool Submerged;
}

public sealed class RiverPath
{
    public readonly List<RiverPoint> Points = new();

    public float Length
    {
        get
        {
            float length = 0f;

            for (int i = 1; i < Points.Count; i++)
                length += Vector2.Distance(Points[i - 1].Position, Points[i].Position);

            return length;
        }
    }
}

[Serializable]
public struct WaterCrossing
{
    public Vector2 Position;
    public Vector2 Direction;
    public float RiverWidth;
    public float WaterHeight;
    public RoadKind RoadKind;
}

public sealed class WaterMap
{
    private const int MAGIC = 0x52544157;
    private const int VERSION = 2;
    private const float INDEX_CELL = 32f;
    private const float FORCED_OUTSIDE = 0.01f;

    public const int SUBDIVISION = 2;
    public const int CELL_SIDE_NODES = SUBDIVISION + 1;
    public const int CELL_NODES = CELL_SIDE_NODES * CELL_SIDE_NODES;
    public const short OWNER_NONE = -1;
    public const short OWNER_SEA = -2;
    public const float MESH_UNDERLAP = 0.1f;

    public int Resolution { get; }
    public float CellSize { get; }
    public float WorldSize { get; }
    public float SeaLevel { get; }
    public float ShoreReach { get; set; }

    public byte[] Kinds { get; }
    public short[] BodyIds { get; }
    public float[] ShoreSurface { get; }
    public float[] ShoreDistance { get; }
    public int[] DetailSlot { get; }

    public int NodeResolution => Resolution * SUBDIVISION + 1;
    public float NodeStep => CellSize / SUBDIVISION;
    public int DetailCount => _detailCells.Count;
    public IReadOnlyList<int> DetailCells => _detailCells;
    public float[] Filled { get; set; }
    public List<float[]> CarveReach { get; set; }
    public HeightMap Uncarved { get; set; }
    public WaterStampLayout Stamps { get; set; }

    public List<WaterBody> Bodies { get; } = new();
    public List<RiverPath> Rivers { get; } = new();
    public List<WaterCrossing> Crossings { get; } = new();

    private Dictionary<long, List<int>> _segments;
    private bool _shoreReady;
    private List<(int River, int Point)> _segmentList = new();
    private readonly object _indexGate = new();
    private readonly List<int> _detailCells = new();
    private short[] _detailOwners = Array.Empty<short>();
    private float[] _detailGround = Array.Empty<float>();

    public WaterMap(int resolution, float cellSize, float worldSize, float seaLevel)
    {
        Resolution = resolution;
        CellSize = cellSize;
        WorldSize = worldSize;
        SeaLevel = seaLevel;

        int cells = resolution * resolution;

        Kinds = new byte[cells];
        BodyIds = new short[cells];
        ShoreSurface = new float[cells];
        ShoreDistance = new float[cells];
        DetailSlot = new int[cells];

        for (int i = 0; i < cells; i++)
        {
            DetailSlot[i] = -1;
            BodyIds[i] = -1;
            ShoreSurface[i] = float.NegativeInfinity;
            ShoreDistance[i] = float.PositiveInfinity;
        }
    }

    public static WaterMap SeaOnly(WorldGenerationConfig config)
    {
        return new WaterMap(1, config.WorldSize, config.WorldSize, config.SeaLevel);
    }

    public static bool Wet(WorldGenerationConfig config, HeightMap map, float x, float z, float ground)
    {
        if (map.Water != null)
            return map.Water.IsWet(x, z, ground, config.ShoreMargin);

        return config.SeaLevel > 0f && ground < config.SeaLevel + config.ShoreMargin;
    }

    public int CellIndex(float x, float z)
    {
        int column = Mathf.Clamp((int)(x / CellSize), 0, Resolution - 1);
        int row = Mathf.Clamp((int)(z / CellSize), 0, Resolution - 1);

        return row * Resolution + column;
    }

    public Vector2 CellCenter(int index)
    {
        return new Vector2((index % Resolution + 0.5f) * CellSize, (index / Resolution + 0.5f) * CellSize);
    }

    public WaterSample Sample(float x, float z)
    {
        if (TryRiver(x, z, out WaterSample river))
            return river;

        if (_shoreReady)
            return Standing(Covers(x, z, out short owner) ? owner : OWNER_SEA);

        int column = Mathf.Clamp((int)(x / CellSize), 0, Resolution - 1);
        int row = Mathf.Clamp((int)(z / CellSize), 0, Resolution - 1);

        int body = -1;
        float surface = float.NegativeInfinity;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int c = column + dx;
                int r = row + dz;

                if (c < 0 || r < 0 || c >= Resolution || r >= Resolution)
                    continue;

                int id = BodyIds[r * Resolution + c];

                if (id < 0 || Bodies[id].Surface <= surface)
                    continue;

                body = id;
                surface = Bodies[id].Surface;
            }
        }

        if (body >= 0 && (SeaLevel <= 0f || surface >= SeaLevel))
            return new WaterSample { Kind = Bodies[body].Kind, Surface = surface, Body = body };

        if (SeaLevel > 0f)
            return new WaterSample { Kind = WaterKind.Sea, Surface = SeaLevel, Body = -1 };

        return new WaterSample { Kind = WaterKind.None, Surface = float.NegativeInfinity, Body = -1 };
    }

    private WaterSample Standing(short owner)
    {
        if (owner >= 0)
            return new WaterSample { Kind = Bodies[owner].Kind, Surface = Bodies[owner].Surface, Body = owner };

        if (SeaLevel > 0f && owner == OWNER_SEA)
            return new WaterSample { Kind = WaterKind.Sea, Surface = SeaLevel, Body = -1 };

        return new WaterSample { Kind = WaterKind.None, Surface = float.NegativeInfinity, Body = -1 };
    }

    public const float RIVER_UNDERLAP = 0.03f;

    public bool RiverOpen(RiverPoint point)
    {
        if (point.Submerged)
            return false;

        return !StandingAt(point.Position.x, point.Position.y, out float surface) || surface < point.Surface - RIVER_UNDERLAP;
    }

    public bool StandingAt(float x, float z, out float surface)
    {
        surface = 0f;

        if (!Covers(x, z, out short owner))
            return false;

        surface = LevelOf(owner);
        return true;
    }

    public float LevelOf(short owner)
    {
        if (owner >= 0)
            return Bodies[owner].Surface;

        return owner == OWNER_SEA && SeaLevel > 0f ? SeaLevel : float.NegativeInfinity;
    }

    public WaterKind KindOf(short owner)
    {
        if (owner >= 0)
            return Bodies[owner].Kind;

        return owner == OWNER_SEA && SeaLevel > 0f ? WaterKind.Sea : WaterKind.None;
    }

    public short CellOwner(int cell)
    {
        if (Kinds[cell] == 0)
            return OWNER_SEA;

        return BodyIds[cell] >= 0 ? BodyIds[cell] : OWNER_SEA;
    }

    public bool IsFull(int cell)
    {
        return DetailSlot[cell] < 0 && Kinds[cell] != 0;
    }

    public short OwnerAt(float x, float z)
    {
        int cell = CellIndex(x, z);
        int slot = DetailSlot[cell];

        if (slot < 0)
            return CellOwner(cell);

        Local(cell, x, z, out int fx, out int fz, out float tx, out float tz);

        return PickOwner(_detailOwners, slot * CELL_NODES, fx, fz, tx, tz);
    }

    public bool Covers(float x, float z, out short owner)
    {
        int cell = CellIndex(x, z);
        int slot = DetailSlot[cell];
        owner = OWNER_NONE;

        if (slot < 0)
        {
            if (Kinds[cell] == 0)
                return false;

            owner = CellOwner(cell);
            return true;
        }

        Local(cell, x, z, out int fx, out int fz, out float tx, out float tz);

        int node = slot * CELL_NODES + fz * CELL_SIDE_NODES + fx;
        int above = node + CELL_SIDE_NODES;

        for (int corner = 0; corner < 4; corner++)
        {
            int index = (corner >> 1 == 0 ? node : above) + (corner & 1);
            short candidate = _detailOwners[index];

            if (float.IsNegativeInfinity(LevelOf(candidate)) || Tested(candidate, corner, node, above))
                continue;

            float a = NodeValue(candidate, _detailOwners[node], _detailGround[node]);
            float b = NodeValue(candidate, _detailOwners[node + 1], _detailGround[node + 1]);
            float c = NodeValue(candidate, _detailOwners[above], _detailGround[above]);
            float d = NodeValue(candidate, _detailOwners[above + 1], _detailGround[above + 1]);

            if (!MarchingCell.Contains(a, b, d, c, tx, tz))
                continue;

            owner = candidate;
            return true;
        }

        return false;
    }

    private bool Tested(short candidate, int corner, int node, int above)
    {
        for (int earlier = 0; earlier < corner; earlier++)
        {
            if (_detailOwners[(earlier >> 1 == 0 ? node : above) + (earlier & 1)] == candidate)
                return true;
        }

        return false;
    }

    public static bool MeshInside(float level, float ground)
    {
        return level + MESH_UNDERLAP - ground >= FORCED_OUTSIDE;
    }

    public float NodeValue(short owner, short nodeOwner, float ground)
    {
        float natural = LevelOf(owner) + MESH_UNDERLAP - ground;

        if (Mathf.Abs(natural) < FORCED_OUTSIDE)
            return -FORCED_OUTSIDE;

        if (nodeOwner == owner || natural <= 0f)
            return natural;

        float own = nodeOwner == OWNER_NONE ? natural : Mathf.Max(0f, LevelOf(nodeOwner) + MESH_UNDERLAP - ground);

        return -Mathf.Max(own, FORCED_OUTSIDE);
    }

    private void Local(int cell, float x, float z, out int fx, out int fz, out float tx, out float tz)
    {
        float step = NodeStep;
        float u = Mathf.Clamp((x - cell % Resolution * CellSize) / step, 0f, SUBDIVISION - 1e-4f);
        float v = Mathf.Clamp((z - cell / Resolution * CellSize) / step, 0f, SUBDIVISION - 1e-4f);

        fx = (int)u;
        fz = (int)v;
        tx = u - fx;
        tz = v - fz;
    }

    public static short PickOwner(short[] owners, int start, int fx, int fz, float tx, float tz)
    {
        short best = OWNER_NONE;
        float nearest = float.MaxValue;
        bool sea = false;

        for (int corner = 0; corner < 4; corner++)
        {
            int cx = corner & 1;
            int cz = corner >> 1;
            short owner = owners[start + (fz + cz) * CELL_SIDE_NODES + fx + cx];

            sea |= owner == OWNER_SEA;

            if (owner < 0)
                continue;

            float dx = tx - cx;
            float dz = tz - cz;
            float distance = dx * dx + dz * dz;

            if (distance >= nearest)
                continue;

            nearest = distance;
            best = owner;
        }

        if (best >= 0)
            return best;

        return sea ? OWNER_SEA : OWNER_NONE;
    }

    public int DetailStart(int cell)
    {
        int slot = DetailSlot[cell];

        return slot < 0 ? -1 : slot * CELL_NODES;
    }

    public short DetailOwner(int index)
    {
        return _detailOwners[index];
    }

    public float DetailGround(int index)
    {
        return _detailGround[index];
    }

    public void SetDetail(int[] cells, short[] owners, float[] ground)
    {
        foreach (int cell in _detailCells)
            DetailSlot[cell] = -1;

        _detailCells.Clear();
        _detailCells.AddRange(cells);
        _detailOwners = owners;
        _detailGround = ground;

        for (int i = 0; i < _detailCells.Count; i++)
            DetailSlot[_detailCells[i]] = i;
    }

    public bool IsWater(float x, float z, float ground)
    {
        if (Remote(CellIndex(x, z), 0f))
            return SeaLevel > 0f && ground < SeaLevel;

        WaterSample sample = Sample(x, z);

        return sample.IsWater && ground < sample.Surface;
    }

    public bool ShoreReady => _shoreReady;

    public void MarkShoreReady()
    {
        _shoreReady = true;
    }

    private bool Remote(int cell, float reach)
    {
        return _shoreReady && ShoreDistance[cell] > reach + 2f * CellSize;
    }

    public WaterKind KindAt(float x, float z, float ground)
    {
        WaterSample sample = Sample(x, z);

        return sample.IsWater && ground < sample.Surface ? sample.Kind : WaterKind.None;
    }

    public float SurfaceHeight(float x, float z)
    {
        return Sample(x, z).Surface;
    }

    public float Depth(float x, float z, float ground)
    {
        WaterSample sample = Sample(x, z);

        return sample.IsWater ? Mathf.Max(0f, sample.Surface - ground) : 0f;
    }

    public Vector2 FlowDirection(float x, float z)
    {
        return Sample(x, z).Flow;
    }

    public bool IsWet(float x, float z, float ground, float margin)
    {
        if (SeaLevel > 0f && ground < SeaLevel + margin)
            return true;

        int cell = CellIndex(x, z);

        if (Remote(cell, ShoreReach))
            return false;

        if (IsWater(x, z, ground + margin))
            return true;

        return ShoreDistance[cell] <= ShoreReach && ground < ShoreSurface[cell] + margin;
    }

    public bool TryRiver(float x, float z, out WaterSample sample)
    {
        sample = default;

        if (Rivers.Count == 0)
            return false;

        EnsureIndex();

        if (!_segments.TryGetValue(Key(Mathf.FloorToInt(x / INDEX_CELL), Mathf.FloorToInt(z / INDEX_CELL)), out List<int> near))
            return false;

        var point = new Vector2(x, z);
        float best = float.MaxValue;

        foreach (int segment in near)
        {
            (int river, int index) = _segmentList[segment];
            List<RiverPoint> points = Rivers[river].Points;

            RiverPoint a = points[index];
            RiverPoint b = points[index + 1];

            Vector2 axis = b.Position - a.Position;
            float length = axis.sqrMagnitude;
            float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a.Position, axis) / length);

            Vector2 closest = a.Position + axis * t;
            float half = 0.5f * Mathf.Lerp(a.Width, b.Width, t);
            float distance = (point - closest).sqrMagnitude;

            if (distance > half * half)
                continue;

            bool openA = RiverOpen(a);
            bool openB = RiverOpen(b);

            if (!openA && !openB || t <= 0f && (!openA || index == 0) || t >= 1f && (!openB || index + 2 == points.Count))
                continue;

            float share = distance / Mathf.Max(half * half, 1e-6f);

            if (share >= best)
                continue;

            best = share;
            sample = new WaterSample
            {
                Kind = WaterKind.River,
                Surface = Mathf.Lerp(a.Surface, b.Surface, t),
                Width = 2f * half,
                Flow = length < 1e-8f ? Vector2.zero : axis / Mathf.Sqrt(length),
                Body = -1
            };
        }

        return best < float.MaxValue;
    }

    public bool InsideEarlierRiver(int river, Vector2 point, float pad, out float surface)
    {
        surface = float.NegativeInfinity;

        if (Rivers.Count == 0)
            return false;

        EnsureIndex();

        if (!_segments.TryGetValue(Key(Mathf.FloorToInt(point.x / INDEX_CELL), Mathf.FloorToInt(point.y / INDEX_CELL)), out List<int> near))
            return false;

        foreach (int segment in near)
        {
            (int other, int index) = _segmentList[segment];

            if (other >= river)
                continue;

            List<RiverPoint> points = Rivers[other].Points;
            RiverPoint a = points[index];
            RiverPoint b = points[index + 1];

            Vector2 axis = b.Position - a.Position;
            float length = axis.sqrMagnitude;
            float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a.Position, axis) / length);
            float half = 0.5f * Mathf.Lerp(a.Width, b.Width, t) + pad;

            if ((point - (a.Position + axis * t)).sqrMagnitude > half * half)
                continue;

            surface = Mathf.Max(surface, Mathf.Lerp(a.Surface, b.Surface, t));
        }

        return !float.IsNegativeInfinity(surface);
    }

    public bool CrossesRiver(Vector2 from, Vector2 to, out float width, out Vector2 flow)
    {
        return CrossesRiver(from, to, out width, out flow, out _, out _);
    }

    public bool CrossesRiver(Vector2 from, Vector2 to, out float width, out Vector2 flow, out Vector2 point, out float surface)
    {
        width = 0f;
        flow = Vector2.zero;
        point = Vector2.zero;
        surface = 0f;

        if (Rivers.Count == 0)
            return false;

        float span = Vector2.Distance(from, to);

        if (Remote(CellIndex(from.x, from.y), span) && Remote(CellIndex(to.x, to.y), span))
            return false;

        EnsureIndex();

        Vector2 min = Vector2.Min(from, to);
        Vector2 max = Vector2.Max(from, to);

        int minX = Mathf.FloorToInt(min.x / INDEX_CELL), maxX = Mathf.FloorToInt(max.x / INDEX_CELL);
        int minZ = Mathf.FloorToInt(min.y / INDEX_CELL), maxZ = Mathf.FloorToInt(max.y / INDEX_CELL);

        for (int cz = minZ; cz <= maxZ; cz++)
        {
            for (int cx = minX; cx <= maxX; cx++)
            {
                if (!_segments.TryGetValue(Key(cx, cz), out List<int> near))
                    continue;

                foreach (int segment in near)
                {
                    (int river, int index) = _segmentList[segment];
                    RiverPoint a = Rivers[river].Points[index];
                    RiverPoint b = Rivers[river].Points[index + 1];

                    if (a.Submerged && b.Submerged)
                        continue;

                    if (!Intersects(from, to, a.Position, b.Position, out float t))
                        continue;

                    float w = Mathf.Lerp(a.Width, b.Width, t);

                    if (w <= width)
                        continue;

                    width = w;
                    flow = (b.Position - a.Position).normalized;
                    point = Vector2.Lerp(a.Position, b.Position, t);
                    surface = Mathf.Lerp(a.Surface, b.Surface, t);
                }
            }
        }

        return width > 0f;
    }

    public int FindCrossings(IReadOnlyList<Road> roads)
    {
        const float SEPARATION = 16f;

        Crossings.Clear();

        foreach (Road road in roads)
        {
            Vector2[] points = road.Points;
            Vector2 last = new(float.MaxValue, float.MaxValue);

            for (int i = 0; i + 1 < points.Length; i++)
            {
                if (!CrossesRiver(points[i], points[i + 1], out float width, out _, out Vector2 point, out float surface))
                    continue;

                if ((point - last).sqrMagnitude < SEPARATION * SEPARATION)
                    continue;

                last = point;

                Crossings.Add(new WaterCrossing
                {
                    Position = point,
                    Direction = (points[i + 1] - points[i]).normalized,
                    RiverWidth = width,
                    WaterHeight = surface,
                    RoadKind = road.Kind
                });
            }
        }

        return Crossings.Count;
    }

    public void InvalidateIndex()
    {
        _segments = null;
    }

    private void EnsureIndex()
    {
        if (System.Threading.Volatile.Read(ref _segments) != null)
            return;

        lock (_indexGate)
        {
            if (_segments != null)
                return;

            var segments = new Dictionary<long, List<int>>();
            var entries = new List<(int River, int Point)>();

            for (int river = 0; river < Rivers.Count; river++)
            {
                List<RiverPoint> points = Rivers[river].Points;

                for (int i = 0; i + 1 < points.Count; i++)
                {
                    RiverPoint a = points[i];
                    RiverPoint b = points[i + 1];
                    float reach = 0.5f * Mathf.Max(a.Width, b.Width);

                    int id = entries.Count;
                    entries.Add((river, i));

                    int minX = Mathf.FloorToInt((Mathf.Min(a.Position.x, b.Position.x) - reach) / INDEX_CELL);
                    int maxX = Mathf.FloorToInt((Mathf.Max(a.Position.x, b.Position.x) + reach) / INDEX_CELL);
                    int minZ = Mathf.FloorToInt((Mathf.Min(a.Position.y, b.Position.y) - reach) / INDEX_CELL);
                    int maxZ = Mathf.FloorToInt((Mathf.Max(a.Position.y, b.Position.y) + reach) / INDEX_CELL);

                    for (int cz = minZ; cz <= maxZ; cz++)
                    {
                        for (int cx = minX; cx <= maxX; cx++)
                        {
                            long key = Key(cx, cz);

                            if (!segments.TryGetValue(key, out List<int> list))
                                segments[key] = list = new List<int>();

                            list.Add(id);
                        }
                    }
                }
            }

            _segmentList = entries;
            System.Threading.Volatile.Write(ref _segments, segments);
        }
    }

    private static long Key(int x, int z)
    {
        return ((long)x << 32) ^ (uint)z;
    }

    private static bool Intersects(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, out float along)
    {
        along = 0f;

        Vector2 r = p2 - p;
        Vector2 s = q2 - q;

        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) < 1e-9f)
            return false;

        Vector2 offset = q - p;

        float t = (offset.x * s.y - offset.y * s.x) / denominator;
        float u = (offset.x * r.y - offset.y * r.x) / denominator;

        if (t < 0f || t > 1f || u < 0f || u > 1f)
            return false;

        along = u;
        return true;
    }

    private static void Block(BinaryWriter writer, Array values, int size)
    {
        var bytes = new byte[values.Length * size];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        writer.Write(bytes);
    }

    private static void Block(BinaryReader reader, Array values, int length)
    {
        byte[] bytes = reader.ReadBytes(length);

        if (bytes.Length != length)
            throw new InvalidDataException("The water map ends early. Regenerate the world.");

        Buffer.BlockCopy(bytes, 0, values, 0, length);
    }

    private static object _cachedSource;
    private static long _cachedLength;
    private static WaterMap _cached;
    private static readonly object CacheGate = new();

    public static WaterMap Load(TextAsset asset)
    {
        if (asset == null)
            return null;

        lock (CacheGate)
        {
            if (_cached != null && ReferenceEquals(_cachedSource, asset) && _cachedLength == asset.dataSize)
                return _cached;

            _cached = FromBytes(asset.bytes);
            _cachedSource = asset;
            _cachedLength = asset.dataSize;

            return _cached;
        }
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(MAGIC);
        writer.Write(VERSION);
        writer.Write(Resolution);
        writer.Write(CellSize);
        writer.Write(WorldSize);
        writer.Write(SeaLevel);
        writer.Write(ShoreReach);

        writer.Write(Kinds);

        Block(writer, BodyIds, sizeof(short));
        Block(writer, ShoreSurface, sizeof(float));
        Block(writer, ShoreDistance, sizeof(float));

        writer.Write(_detailCells.Count);
        Block(writer, _detailCells.ToArray(), sizeof(int));
        Block(writer, _detailOwners, sizeof(short));
        Block(writer, _detailGround, sizeof(float));

        writer.Write(Bodies.Count);

        foreach (WaterBody body in Bodies)
        {
            writer.Write((byte)body.Kind);
            writer.Write(body.Surface);
            writer.Write(body.Area);
            writer.Write(body.Depth);
            writer.Write(body.Center.x);
            writer.Write(body.Center.y);
        }

        writer.Write(Rivers.Count);

        foreach (RiverPath river in Rivers)
        {
            writer.Write(river.Points.Count);

            foreach (RiverPoint point in river.Points)
            {
                writer.Write(point.Position.x);
                writer.Write(point.Position.y);
                writer.Write(point.Surface);
                writer.Write(point.Bed);
                writer.Write(point.Width);
                writer.Write(point.Flow);
                writer.Write(point.Submerged);
            }
        }

        writer.Write(Crossings.Count);

        foreach (WaterCrossing crossing in Crossings)
        {
            writer.Write(crossing.Position.x);
            writer.Write(crossing.Position.y);
            writer.Write(crossing.Direction.x);
            writer.Write(crossing.Direction.y);
            writer.Write(crossing.RiverWidth);
            writer.Write(crossing.WaterHeight);
            writer.Write((int)crossing.RoadKind);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static WaterMap FromBytes(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));

        if (reader.ReadInt32() != MAGIC)
            throw new InvalidDataException("Not a water map");

        int version = reader.ReadInt32();

        if (version != VERSION)
            throw new InvalidDataException($"Water map version {version}, expected {VERSION}. Regenerate the world.");

        int resolution = reader.ReadInt32();
        float cellSize = reader.ReadSingle();
        float worldSize = reader.ReadSingle();
        float seaLevel = reader.ReadSingle();

        var map = new WaterMap(resolution, cellSize, worldSize, seaLevel) { ShoreReach = reader.ReadSingle() };
        int cells = resolution * resolution;

        reader.Read(map.Kinds, 0, cells);

        Block(reader, map.BodyIds, cells * sizeof(short));
        Block(reader, map.ShoreSurface, cells * sizeof(float));
        Block(reader, map.ShoreDistance, cells * sizeof(float));

        int details = reader.ReadInt32();

        if (details < 0 || details > cells)
            throw new InvalidDataException($"The water map holds {details} shore cells for {cells} cells. Regenerate the world.");

        var detailCells = new int[details];
        var detailOwners = new short[details * CELL_NODES];
        var detailGround = new float[details * CELL_NODES];

        Block(reader, detailCells, details * sizeof(int));
        Block(reader, detailOwners, detailOwners.Length * sizeof(short));
        Block(reader, detailGround, detailGround.Length * sizeof(float));

        map.SetDetail(detailCells, detailOwners, detailGround);
        map._shoreReady = true;

        int bodies = reader.ReadInt32();

        for (int i = 0; i < bodies; i++)
        {
            map.Bodies.Add(new WaterBody
            {
                Kind = (WaterKind)reader.ReadByte(),
                Surface = reader.ReadSingle(),
                Area = reader.ReadSingle(),
                Depth = reader.ReadSingle(),
                Center = new Vector2(reader.ReadSingle(), reader.ReadSingle())
            });
        }

        int rivers = reader.ReadInt32();

        for (int i = 0; i < rivers; i++)
        {
            var river = new RiverPath();
            int points = reader.ReadInt32();

            for (int p = 0; p < points; p++)
            {
                river.Points.Add(new RiverPoint
                {
                    Position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    Surface = reader.ReadSingle(),
                    Bed = reader.ReadSingle(),
                    Width = reader.ReadSingle(),
                    Flow = reader.ReadSingle(),
                    Submerged = reader.ReadBoolean()
                });
            }

            map.Rivers.Add(river);
        }

        int crossings = reader.ReadInt32();

        for (int i = 0; i < crossings; i++)
        {
            map.Crossings.Add(new WaterCrossing
            {
                Position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                Direction = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                RiverWidth = reader.ReadSingle(),
                WaterHeight = reader.ReadSingle(),
                RoadKind = (RoadKind)reader.ReadInt32()
            });
        }

        return map;
    }
}
