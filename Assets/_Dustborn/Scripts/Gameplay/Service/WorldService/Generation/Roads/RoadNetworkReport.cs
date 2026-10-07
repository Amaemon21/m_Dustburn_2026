using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed class RoadNetworkReport
{
    private const int PROBLEM_CAP = 4000;

    private readonly List<(string Name, double Value)> _metrics = new();
    private readonly Dictionary<string, int> _metricIndex = new();

    public Dictionary<string, int> Violations { get; } = new();
    public List<(Vector2 Point, string Kind)> Problems { get; } = new();

    public IReadOnlyList<(string Name, double Value)> Metrics => _metrics;

    public void Metric(string name, double value)
    {
        if (_metricIndex.TryGetValue(name, out int index))
        {
            _metrics[index] = (name, value);
            return;
        }

        _metricIndex[name] = _metrics.Count;
        _metrics.Add((name, value));
    }

    public double Get(string name)
    {
        return _metricIndex.TryGetValue(name, out int index) ? _metrics[index].Value : double.NaN;
    }

    public void Declare(string name)
    {
        if (!Violations.ContainsKey(name))
            Violations[name] = 0;
    }

    public void Violation(string name, Vector2 point)
    {
        Violations.TryGetValue(name, out int count);
        Violations[name] = count + 1;

        if (Problems.Count < PROBLEM_CAP)
            Problems.Add((point, name));
    }

    public int Count(string name)
    {
        return Violations.TryGetValue(name, out int count) ? count : 0;
    }

    public int Hard
    {
        get
        {
            int total = 0;

            foreach (string name in RoadNetworkDiagnostics.HARD)
                total += Count(name);

            return total;
        }
    }

    public string ToText()
    {
        var text = new StringBuilder();

        foreach ((string name, double value) in _metrics)
            text.Append("  ").Append(name.PadRight(44)).Append(' ').AppendLine(Format(value));

        var names = new List<string>(Violations.Keys);
        names.Sort(StringComparer.Ordinal);

        foreach (string name in names)
            text.Append("  violation ").Append(name.PadRight(34)).Append(' ').Append(Violations[name]).AppendLine(Array.IndexOf(RoadNetworkDiagnostics.HARD, name) >= 0 ? " (hard)" : string.Empty);

        return text.ToString();
    }

    private static string Format(double value)
    {
        return Math.Abs(value - Math.Round(value)) < 1e-9 && Math.Abs(value) < 1e12
            ? ((long)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
