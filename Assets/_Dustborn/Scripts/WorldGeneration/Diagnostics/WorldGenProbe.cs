using System;
using System.Diagnostics;
using Unity.Profiling;

public struct WorldGenStageStat
{
    public int Count;
    public long Ticks;
    public long MaxTicks;
    public long Payload;
}

public struct WorldGenSpanEvent
{
    public int Stage;
    public int Generation;
    public long Start;
    public long End;
    public long Payload;
}

public struct WorldGenQueueEvent
{
    public int Kind;
    public int Phase;
    public int Generation;
    public int WorkId;
    public long Key;
    public long Stamp;
    public int Depth;
}

public static class WorldGenProbe
{
    public const int SPAN_CAPACITY = 262144;
    public const int QUEUE_CAPACITY = 262144;

    private static readonly string[] NAMES = BuildNames();
    private static readonly ProfilerMarker[] MARKERS = BuildMarkers();
    private static readonly WorldGenStageStat[] STATS = new WorldGenStageStat[(int)WorldGenStage.Count];
    private static readonly long[] MILESTONES = new long[(int)WorldGenMilestone.Count];

    private static WorldGenSpanEvent[] _spans = Array.Empty<WorldGenSpanEvent>();
    private static WorldGenQueueEvent[] _queue = Array.Empty<WorldGenQueueEvent>();

    private static int _spanCount;
    private static int _queueCount;
    private static int _generation;
    private static int _workId;
    private static long _runStart;

    public static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

    public static bool Capture { get; private set; }

    public static bool Overflow { get; private set; }

    public static int Generation => _generation;

    public static int SpanCount => _spanCount;

    public static int QueueCount => _queueCount;

    public static long RunStart => _runStart;

    public static long Now => Stopwatch.GetTimestamp();

    public static double ToMs(long ticks)
    {
        return ticks * MsPerTick;
    }

    public static string Name(WorldGenStage stage)
    {
        return NAMES[(int)stage];
    }

    public static void BeginRun(bool capture)
    {
        _generation++;
        _runStart = Stopwatch.GetTimestamp();
        _spanCount = 0;
        _queueCount = 0;
        _workId = 0;
        Overflow = false;

        Array.Clear(STATS, 0, STATS.Length);

        for (int index = 0; index < MILESTONES.Length; index++)
            MILESTONES[index] = 0L;

        MILESTONES[(int)WorldGenMilestone.RunStart] = _runStart;

        if (capture && _spans.Length != SPAN_CAPACITY)
            _spans = new WorldGenSpanEvent[SPAN_CAPACITY];

        if (capture && _queue.Length != QUEUE_CAPACITY)
            _queue = new WorldGenQueueEvent[QUEUE_CAPACITY];

        Capture = capture;
    }

    public static void EndRun()
    {
        Capture = false;
    }

    public static int NextWorkId()
    {
        return ++_workId;
    }

    public readonly struct Span : IDisposable
    {
        private readonly int _stage;
        private readonly long _start;

        internal Span(int stage, long start)
        {
            _stage = stage;
            _start = start;
        }

        public void Dispose()
        {
            Complete(_stage, _start, 0L);
        }

        public void Finish(long payload)
        {
            Complete(_stage, _start, payload);
        }
    }

    public static Span Measure(WorldGenStage stage)
    {
        int index = (int)stage;
        MARKERS[index].Begin();
        return new Span(index, Stopwatch.GetTimestamp());
    }

    private static void Complete(int stage, long start, long payload)
    {
        long end = Stopwatch.GetTimestamp();
        MARKERS[stage].End();
        Store(stage, start, end, payload);
    }

    public static void Record(WorldGenStage stage, long start, long end, long payload)
    {
        Store((int)stage, start, end, payload);
    }

    private static void Store(int stage, long start, long end, long payload)
    {
        long ticks = end - start;
        STATS[stage].Count++;
        STATS[stage].Ticks += ticks;
        STATS[stage].Payload += payload;

        if (ticks > STATS[stage].MaxTicks)
            STATS[stage].MaxTicks = ticks;

        if (!Capture)
            return;

        if (_spanCount >= _spans.Length)
        {
            Overflow = true;
            return;
        }

        _spans[_spanCount].Stage = stage;
        _spans[_spanCount].Generation = _generation;
        _spans[_spanCount].Start = start;
        _spans[_spanCount].End = end;
        _spans[_spanCount].Payload = payload;
        _spanCount++;
    }

    public static WorldGenStageStat Stat(WorldGenStage stage)
    {
        return STATS[(int)stage];
    }

    public static void Mark(WorldGenMilestone milestone)
    {
        int index = (int)milestone;

        if (MILESTONES[index] == 0L)
            MILESTONES[index] = Stopwatch.GetTimestamp();
    }

    public static void Remark(WorldGenMilestone milestone)
    {
        MILESTONES[(int)milestone] = Stopwatch.GetTimestamp();
    }

    public static void Forget(WorldGenMilestone milestone)
    {
        MILESTONES[(int)milestone] = 0L;
    }

    public static bool Reached(WorldGenMilestone milestone)
    {
        return MILESTONES[(int)milestone] != 0L;
    }

    public static double MilestoneMs(WorldGenMilestone milestone)
    {
        long stamp = MILESTONES[(int)milestone];
        return stamp == 0L ? double.NaN : (stamp - _runStart) * MsPerTick;
    }

    public static void Queue(WorldGenQueueKind kind, WorldGenQueuePhase phase, int workId, long key, int depth)
    {
        if (!Capture)
            return;

        if (_queueCount >= _queue.Length)
        {
            Overflow = true;
            return;
        }

        _queue[_queueCount].Kind = (int)kind;
        _queue[_queueCount].Phase = (int)phase;
        _queue[_queueCount].Generation = _generation;
        _queue[_queueCount].WorkId = workId;
        _queue[_queueCount].Key = key;
        _queue[_queueCount].Stamp = Stopwatch.GetTimestamp();
        _queue[_queueCount].Depth = depth;
        _queueCount++;
    }

    public static WorldGenSpanEvent SpanAt(int index)
    {
        return _spans[index];
    }

    public static WorldGenQueueEvent QueueAt(int index)
    {
        return _queue[index];
    }

    public static long ColumnKey(int lod, int x, int y, int z, int seams, int morph)
    {
        long key = lod & 0xFL;
        key |= (seams & 0xFL) << 4;
        key |= (morph & 0xFL) << 8;
        key |= (y & 0xFFFL) << 12;
        key |= (x & 0xFFFFFL) << 24;
        key |= (z & 0xFFFFFL) << 44;
        return key;
    }

    private static string[] BuildNames()
    {
        var names = new string[(int)WorldGenStage.Count];

        for (int index = 0; index < names.Length; index++)
            names[index] = Prefix((WorldGenStage)index) + (WorldGenStage)index;

        return names;
    }

    private static ProfilerMarker[] BuildMarkers()
    {
        var markers = new ProfilerMarker[(int)WorldGenStage.Count];

        for (int index = 0; index < markers.Length; index++)
            markers[index] = new ProfilerMarker(NAMES[index]);

        return markers;
    }

    private static string Prefix(WorldGenStage stage)
    {
        if (stage <= WorldGenStage.MapTotal)
            return "WorldGen.Map.";

        if (stage <= WorldGenStage.BakeTotal)
            return "WorldGen.Bake.";

        if (stage <= WorldGenStage.PreviewClear)
            return "WorldGen.Preview.";

        if (stage <= WorldGenStage.RuntimeDemolish)
            return "WorldGen.Runtime.";

        if (stage <= WorldGenStage.VoxelSpawn)
            return "WorldGen.Voxel.";

        if (stage <= WorldGenStage.ColliderAttach)
            return "WorldGen.Collider.";

        if (stage <= WorldGenStage.DecorInstantiate)
            return "WorldGen.Decor.";

        if (stage <= WorldGenStage.PoiBatch)
            return "WorldGen.POI.";

        return "WorldGen.Cleanup.";
    }
}
