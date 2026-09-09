using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed class WorldGenBenchStats
{
    public double Min;
    public double Max;
    public double Mean;
    public double P50;
    public double P95;
    public double P99;
    public int Count;

    public static WorldGenBenchStats Of(List<double> values)
    {
        var stats = new WorldGenBenchStats { Count = values.Count };

        if (values.Count == 0)
            return stats;

        var sorted = new List<double>(values);
        sorted.Sort();

        double sum = 0d;

        foreach (double value in sorted)
            sum += value;

        stats.Min = sorted[0];
        stats.Max = sorted[sorted.Count - 1];
        stats.Mean = sum / sorted.Count;
        stats.P50 = Percentile(sorted, 0.50);
        stats.P95 = Percentile(sorted, 0.95);
        stats.P99 = Percentile(sorted, 0.99);
        return stats;
    }

    public static double Percentile(List<double> sorted, double fraction)
    {
        if (sorted.Count == 0)
            return double.NaN;

        if (sorted.Count == 1)
            return sorted[0];

        double position = fraction * (sorted.Count - 1);
        int low = (int)Math.Floor(position);
        int high = (int)Math.Ceiling(position);

        return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
    }

    public void Write(WorldGenBenchJson json, string key)
    {
        json.Object(key)
            .Value("count", Count)
            .Value("min", Count == 0 ? double.NaN : Min)
            .Value("p50", Count == 0 ? double.NaN : P50)
            .Value("p95", Count < 20 ? double.NaN : P95)
            .Value("p99", Count < 100 ? double.NaN : P99)
            .Value("max", Count == 0 ? double.NaN : Max)
            .Value("mean", Count == 0 ? double.NaN : Mean)
            .End();
    }
}

public static class WorldGenBenchSampler
{
    private static int _allocProbe;

    public static bool AllocationCounterWorks
    {
        get
        {
            if (_allocProbe != 0)
                return _allocProbe > 0;

            long before = GC.GetAllocatedBytesForCurrentThread();
            var ballast = new byte[1 << 20];
            ballast[0] = 1;
            long after = GC.GetAllocatedBytesForCurrentThread();

            _allocProbe = after > before ? 1 : -1;
            return _allocProbe > 0;
        }
    }

    public static string Run(WorldGenBenchOp op, WorldGenBenchProfile profile, WorldGenBenchArgs args, int warmup, int samples)
    {
        var context = new WorldGenBenchContext(profile, args);
        var wall = new List<double>();
        var alloc = new List<double>();
        var json = new WorldGenBenchJson();

        json.Text("op", op.Id)
            .Text("profile", profile.Id)
            .Text("args", args.Raw)
            .Text("fixtureHash", profile.Fingerprint)
            .Value("warmup", warmup)
            .Value("requestedSamples", samples);

        string status = "passed";
        string error = null;

        try
        {
            op.Setup?.Invoke(context);

            for (int index = 0; index < warmup + samples; index++)
            {
                bool warming = index < warmup;
                context.Warmup = warming;
                context.Iteration = warming ? index : index - warmup;

                op.Prepare?.Invoke(context);

                WorldGenProbe.BeginRun(false);

                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();

                op.Measure(context);

                long end = Stopwatch.GetTimestamp();
                long after = GC.GetAllocatedBytesForCurrentThread();

                WorldGenProbe.EndRun();

                if (warming)
                    continue;

                wall.Add((end - start) * WorldGenProbe.MsPerTick);
                alloc.Add(after - before);

                if (index == warmup + samples - 1)
                    op.Verify?.Invoke(context);

                Collect(context);
            }
        }
        catch (WorldGenBenchCheckException exception)
        {
            status = "failed";
            error = exception.Message;
        }
        catch (WorldGenBenchBlockedException exception)
        {
            status = exception.Status;
            error = exception.Message;
        }
        catch (Exception exception)
        {
            status = "failed";
            error = exception.GetType().Name + ": " + exception.Message;
        }
        finally
        {
            try
            {
                op.Teardown?.Invoke(context);
            }
            catch (Exception exception)
            {
                context.Note("teardown failed: " + exception.Message);
            }
        }

        json.Text("status", status);

        if (error == null)
            json.Null("error");
        else
            json.Text("error", error);

        WorldGenBenchStats.Of(wall).Write(json, "wallMs");

        if (AllocationCounterWorks)
        {
            WorldGenBenchStats.Of(alloc).Write(json, "allocBytes");
        }
        else
        {
            json.Null("allocBytes");
            alloc.Clear();
            context.Note("managed allocation is unavailable: GC.GetAllocatedBytesForCurrentThread does not move on this runtime, "
                + "so it is reported as null rather than as zero");
        }

        json.Array("samplesMs");

        foreach (double value in wall)
            json.Item(value);

        json.End();

        json.Array("samplesAllocBytes");

        foreach (double value in alloc)
            json.Item(value);

        json.End();

        json.Object("stages");

        for (int index = 0; index < (int)WorldGenStage.Count; index++)
        {
            var stage = (WorldGenStage)index;
            WorldGenStageStat stat = WorldGenProbe.Stat(stage);

            if (stat.Count == 0)
                continue;

            json.Object(stage.ToString())
                .Value("count", stat.Count)
                .Value("totalMs", WorldGenProbe.ToMs(stat.Ticks))
                .Value("maxMs", WorldGenProbe.ToMs(stat.MaxTicks))
                .Value("payload", stat.Payload)
                .End();
        }

        json.End();

        json.Object("work");

        foreach (KeyValuePair<string, double> pair in context.Work)
            json.Value(pair.Key, pair.Value);

        json.End();

        json.Object("fingerprints");

        foreach (KeyValuePair<string, string> pair in context.Fingerprints)
            json.Text(pair.Key, pair.Value);

        json.End();

        json.Array("notes");

        foreach (string note in context.Notes)
            json.Item(note);

        json.End();

        return json.ToString();
    }

    private static void Collect(WorldGenBenchContext context)
    {
        if (context.Args.Bool("collect", false))
            GC.Collect();
    }
}

public sealed class WorldGenBenchBlockedException : Exception
{
    public string Status { get; }

    public WorldGenBenchBlockedException(string status, string message) : base(message)
    {
        Status = status;
    }
}
