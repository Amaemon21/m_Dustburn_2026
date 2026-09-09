using System;
using System.Collections.Generic;

public sealed class WorldGenBenchContext
{
    public WorldGenBenchProfile Profile { get; }

    public WorldGenBenchArgs Args { get; }

    public Dictionary<string, object> State { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> Work { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Fingerprints { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Notes { get; } = new();

    public int Iteration { get; set; }

    public bool Warmup { get; set; }

    public WorldGenBenchContext(WorldGenBenchProfile profile, WorldGenBenchArgs args)
    {
        Profile = profile;
        Args = args;
    }

    public T Get<T>(string key) where T : class
    {
        return State.TryGetValue(key, out object value) ? value as T : null;
    }

    public void Put(string key, object value)
    {
        State[key] = value;
    }

    public void Count(string key, double value)
    {
        Work[key] = value;
    }

    public void Add(string key, double value)
    {
        Work[key] = Work.TryGetValue(key, out double previous) ? previous + value : value;
    }

    public void Print(string key, string fingerprint)
    {
        Fingerprints[key] = fingerprint;
    }

    public void Note(string text)
    {
        if (!Notes.Contains(text))
            Notes.Add(text);
    }

    public void Require(bool condition, string message)
    {
        if (!condition)
            throw new WorldGenBenchCheckException(message);
    }
}

public sealed class WorldGenBenchCheckException : Exception
{
    public WorldGenBenchCheckException(string message) : base(message)
    {
    }
}

public sealed class WorldGenBenchOp
{
    public string Id { get; }

    public Action<WorldGenBenchContext> Setup { get; set; }

    public Action<WorldGenBenchContext> Prepare { get; set; }

    public Action<WorldGenBenchContext> Measure { get; set; }

    public Action<WorldGenBenchContext> Verify { get; set; }

    public Action<WorldGenBenchContext> Teardown { get; set; }

    public string Requires { get; set; }

    public WorldGenBenchOp(string id, Action<WorldGenBenchContext> measure)
    {
        Id = id;
        Measure = measure;
    }
}
