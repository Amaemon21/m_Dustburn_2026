using System;
using System.Collections.Generic;
using Unity.Profiling;

public sealed class WorldGenBenchFrames : IDisposable
{
    public struct Frame
    {
        public int Index;
        public string Phase;
        public string Segment;
        public double IntervalMs;
        public double CpuMs;
        public double GpuMs;
        public long AllocBytes;
        public int TerrainQueue;
        public int ColliderQueue;
        public int DecorQueue;
    }

    private readonly List<Frame> _frames = new(8192);

    private ProfilerRecorder _cpu;
    private ProfilerRecorder _gpu;
    private ProfilerRecorder _alloc;

    private long _previous;
    private int _index;

    public string Phase { get; set; } = "load";

    public string Segment { get; set; } = string.Empty;

    public IReadOnlyList<Frame> All => _frames;

    public bool CpuAvailable => _cpu.Valid;

    public bool GpuAvailable => _gpu.Valid;

    public bool AllocAvailable => _alloc.Valid;

    public WorldGenBenchFrames()
    {
        _cpu = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        _gpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
        _alloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        _previous = WorldGenProbe.Now;
    }

    public void Sample(int terrainQueue, int colliderQueue, int decorQueue)
    {
        long now = WorldGenProbe.Now;

        var frame = new Frame
        {
            Index = _index++,
            Phase = Phase,
            Segment = Segment,
            IntervalMs = WorldGenProbe.ToMs(now - _previous),
            CpuMs = _cpu.Valid && _cpu.Count > 0 ? _cpu.LastValue * 1e-6 : double.NaN,
            GpuMs = _gpu.Valid && _gpu.Count > 0 ? _gpu.LastValue * 1e-6 : double.NaN,
            AllocBytes = _alloc.Valid && _alloc.Count > 0 ? _alloc.LastValue : -1L,
            TerrainQueue = terrainQueue,
            ColliderQueue = colliderQueue,
            DecorQueue = decorQueue
        };

        _previous = now;
        _frames.Add(frame);
    }

    public void Reset()
    {
        _frames.Clear();
        _index = 0;
        _previous = WorldGenProbe.Now;
    }

    public void Write(WorldGenBenchJson json, string key, string phase)
    {
        var intervals = new List<double>();
        int over16 = 0;
        int over33 = 0;
        int over50 = 0;
        int over100 = 0;
        int streak = 0;
        int worstStreak = 0;
        long alloc = 0L;
        int allocFrames = 0;

        foreach (Frame frame in _frames)
        {
            if (phase != null && frame.Phase != phase)
                continue;

            intervals.Add(frame.IntervalMs);

            if (frame.IntervalMs > 16.67)
                over16++;

            if (frame.IntervalMs > 33.33)
                over33++;

            if (frame.IntervalMs > 50.0)
                over50++;

            if (frame.IntervalMs > 100.0)
                over100++;

            if (frame.IntervalMs > 33.33)
            {
                streak++;
                worstStreak = Math.Max(worstStreak, streak);
            }
            else
            {
                streak = 0;
            }

            if (frame.AllocBytes < 0L)
                continue;

            alloc += frame.AllocBytes;
            allocFrames++;
        }

        json.Object(key);
        WorldGenBenchStats.Of(intervals).Write(json, "intervalMs");

        json.Value("frames", intervals.Count)
            .Value("over16", over16)
            .Value("over33", over33)
            .Value("over50", over50)
            .Value("over100", over100)
            .Value("hitchRatio", intervals.Count == 0 ? double.NaN : over33 / (double)intervals.Count)
            .Value("worstStreak", worstStreak);

        if (allocFrames > 0)
            json.Value("allocBytes", alloc);
        else
            json.Null("allocBytes");

        json.Value("cpuAvailable", CpuAvailable)
            .Value("gpuAvailable", GpuAvailable)
            .Value("allocAvailable", AllocAvailable)
            .End();
    }

    public string Csv(string runId)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("runId,frameIndex,phase,routeSegment,intervalMs,cpuMs,gpuMs,allocBytes,terrainQueue,colliderQueue,decorQueue");

        foreach (Frame frame in _frames)
        {
            text.Append(runId).Append(',')
                .Append(frame.Index).Append(',')
                .Append(frame.Phase).Append(',')
                .Append(frame.Segment).Append(',')
                .Append(WorldGenBenchJson.Number(frame.IntervalMs)).Append(',')
                .Append(WorldGenBenchJson.Number(frame.CpuMs)).Append(',')
                .Append(WorldGenBenchJson.Number(frame.GpuMs)).Append(',')
                .Append(frame.AllocBytes).Append(',')
                .Append(frame.TerrainQueue).Append(',')
                .Append(frame.ColliderQueue).Append(',')
                .Append(frame.DecorQueue)
                .AppendLine();
        }

        return text.ToString();
    }

    public void Dispose()
    {
        if (_cpu.Valid)
            _cpu.Dispose();

        if (_gpu.Valid)
            _gpu.Dispose();

        if (_alloc.Valid)
            _alloc.Dispose();
    }
}
