using System.Collections.Generic;
using UnityEngine;

public sealed partial class RegionalGraphPlanner
{
    private const float SAMPLE_STEP = 48f;
    private const int NEAREST = 3;
    private const int EARTHWORK_WINDOW = 4;
    private const float INTRUSION_PENALTY = 4f;
    private const float IMPORTANCE_EXPONENT = 0.25f;

    private sealed class Candidate
    {
        public int From;
        public int To;
        public float Length;
        public float Estimate;
        public float Water;
        public bool Used;

        public int Other(int node)
        {
            return node == From ? To : From;
        }
    }

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;
    private readonly WaterMap _water;

    public int Candidates { get; private set; }
    public int Backbone { get; private set; }
    public int Loops { get; private set; }
    public int RefusedAngle { get; private set; }
    public int RefusedCrossing { get; private set; }
    public int RefusedSpacing { get; private set; }
    public int RefusedDegree { get; private set; }
    public int RefusedDetour { get; private set; }

    public RegionalGraphPlanner(WorldGenerationConfig config, HeightMap map, WaterMap water)
    {
        _config = config;
        _map = map;
        _water = water;
    }

    public List<RegionalLink> Plan(IReadOnlyList<Hub> hubs)
    {
        var links = new List<RegionalLink>();
        int count = hubs.Count;

        if (count < 2)
            return links;

        List<Candidate> candidates = Collect(hubs);
        Candidates = candidates.Count;

        var degree = new int[count];
        var chosen = new List<Candidate>();

        BuildBackbone(hubs, candidates, degree, chosen);
        Backbone = chosen.Count;

        float[] weights = TreeWeights(hubs, chosen);
        var ordered = new List<(Candidate Link, float Priority, bool Backbone)>();

        for (int i = 0; i < chosen.Count; i++)
            ordered.Add((chosen[i], weights[i], true));

        ordered.Sort((left, right) =>
        {
            int compare = right.Priority.CompareTo(left.Priority);
            return compare != 0 ? compare : left.Link.Estimate.CompareTo(right.Link.Estimate);
        });

        List<Candidate> loops = AddLoops(hubs, candidates, degree, chosen);
        Loops = loops.Count;

        for (int i = 0; i < loops.Count; i++)
            ordered.Add((loops[i], -1f - i, false));

        foreach ((Candidate link, float priority, bool backbone) in ordered)
            links.Add(new RegionalLink(link.From, link.To, backbone, link.Estimate, priority));

        Debug.Log($"Regional graph: {Candidates} candidate links, {Backbone} backbone and {Loops} loops over {count} settlements ({links.Count / (float)count:F2} links per settlement). Loops refused: {RefusedDetour} saving too little, {RefusedAngle} too shallow, {RefusedCrossing} crossing, {RefusedSpacing} too close to another loop, {RefusedDegree} over the link cap");

        return links;
    }

    private int MaxLinks(Hub hub)
    {
        SettlementTypeProfile profile = _config.Profile(hub.Type);

        return profile == null ? 2 : Mathf.Max(1, profile.MaxLinks);
    }

    private float Importance(Hub hub)
    {
        SettlementTypeProfile profile = _config.Profile(hub.Type);

        return profile == null ? 1f : Mathf.Max(0.1f, profile.Importance);
    }

    private static long Key(int a, int b)
    {
        int low = Mathf.Min(a, b);
        int high = Mathf.Max(a, b);

        return ((long)low << 32) | (uint)high;
    }
}
