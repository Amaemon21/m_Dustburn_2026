using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class TerrainCarver
{
    private const int BAND_ROWS = 16;
    private const int APPLY_ROWS = 65536;
    private const int BLUR_PASSES = 3;
    private const float SQUARE_SLACK = 1.0001f;
    private const int RELAX_PASSES = 16;
    private const float GRADE_MARGIN = 0.9f;
    public const float BRIDGE_CLEARANCE = 2.5f;
    public const float CULVERT_CLEARANCE = 1f;
    private const float SHORE_CLEARANCE = 0.5f;
    private const float SHORE_GUARD = 8f;
    private const float SHORE_RIM = 4f;
    private const float SHORE_RIM_SLOPE = 0.8f;
    private const float BRIDGE_APPROACH = 8f;
    private const float JUNCTION_STEP = 0.3f;
    private const float RESHAPE_GRADE_SHARE = 0.9f;
    private const float MIN_BUDGET_SHARE = 0.1f;
    private const float SMOOTHSTEP_PEAK = 1.5f;
    private const float MAX_RESHAPE_RADIUS = 400f;
    private const float RESHAPE_PROBE = 16f;
    private const int RESHAPE_PROBES = 32;
    private const int RESHAPE_PASSES = 3;
    private const float BRIDGE_PAD = 3f;
    private const int BRIDGE_TAPS = 2;
    private const int DECK_PASSES = 3;
    private const float MAX_BRIDGE_COSINE = 0.8f;
    private const float ANCHOR_MASK = 0.99f;
    private const float RAMP_PROBE = 1f;
    private const float OVERRUN_MARGIN = 1f;
    private const float RAMP_EPSILON = 0.05f;
    private const float RAMP_WATER_GAP = 8f;
    private const float RAMP_WATER_FADE = 48f;

    private readonly WorldGenerationConfig _config;
    private readonly WaterMap _water;

    private float[] _carveWeight;
    private float[] _carveTarget;
    private float[] _roadMask;
    private float[] _padDistance;

    private HeightMap _source;
    private float _cellSize;

    public List<(Road Road, Vector2[] Points, float[] Profile, bool[] Anchored)> Profiles { get; set; }
    public List<WaterCrossing> Bridges { get; } = new();

    public TerrainCarver(WorldGenerationConfig config, WaterMap water)
    {
        _config = config;
        _water = water;
    }

    private float PaintReach => _config.RoadVergeWidth + _config.RoadEdgeSoftness * 0.5f;

    public HeightMap CarveSettlements(HeightMap source, IReadOnlyList<SettlementLayout> layouts)
    {
        Begin(source);

        if (layouts != null)
        {
            foreach (SettlementLayout layout in layouts)
                CarveSettlement(layout);
        }

        return Apply();
    }

    public HeightMap CarveStreets(HeightMap source, IReadOnlyList<SettlementLayout> layouts, out float[] roadMask)
    {
        Begin(source);

        _roadMask = new float[source.Heights.Length];

        if (layouts != null)
        {
            var streets = new List<Road>();

            foreach (SettlementLayout layout in layouts)
                streets.AddRange(layout.Streets);

            foreach (Road street in Ordered(streets))
                CarveRoad(street);
        }

        roadMask = _roadMask;

        return Apply();
    }

    public HeightMap CarveHighways(HeightMap source, IReadOnlyList<Road> roads, float[] roadMask)
    {
        Begin(source);

        _roadMask = roadMask ?? new float[source.Heights.Length];

        foreach (Road road in Ordered(roads))
            CarveRoad(road);

        return Apply();
    }

    public HeightMap CarvePads(HeightMap source, IReadOnlyList<PoiPlacement> placements)
    {
        Begin(source);

        _padDistance = new float[source.Heights.Length];
        System.Array.Fill(_padDistance, float.MaxValue);

        foreach (PoiPlacement placement in placements)
            CarvePad(placement);

        return Apply();
    }

    private void Begin(HeightMap source)
    {
        _source = source;
        _cellSize = (float)source.WorldSize / (source.Resolution - 1);

        int cellCount = source.Heights.Length;

        _carveWeight = new float[cellCount];
        _carveTarget = new float[cellCount];
        _roadMask = null;
    }

    private HeightMap Apply()
    {
        var result = new HeightMap(_source.Resolution, _source.WorldSize, _source.MaxHeight);

        float[] source = _source.Heights;
        float[] target = _carveTarget;
        float[] weight = _carveWeight;
        float[] heights = result.Heights;
        int length = heights.Length;
        int rows = (length + APPLY_ROWS - 1) / APPLY_ROWS;

        Parallel.For(0, rows, band =>
        {
            int end = Mathf.Min(length, (band + 1) * APPLY_ROWS);

            for (int i = band * APPLY_ROWS; i < end; i++)
                heights[i] = source[i] + (target[i] - source[i]) * Mathf.Clamp01(weight[i]);
        });

        _carveWeight = null;
        _carveTarget = null;
        _padDistance = null;
        _roadMask = null;
        _source = null;

        return result;
    }

    private List<Road> Ordered(IReadOnlyList<Road> roads)
    {
        var ordered = new List<Road>(roads.Count);

        foreach (Road road in roads)
        {
            if (road?.Points != null && road.Points.Length >= 2)
                ordered.Add(road);
        }

        var rank = new Dictionary<Road, int>(ordered.Count);

        for (int i = 0; i < ordered.Count; i++)
            rank[ordered[i]] = i;

        ordered.Sort((left, right) =>
        {
            int compare = RoadKindProfile.For(_config, left.Kind).CarveOrder.CompareTo(RoadKindProfile.For(_config, right.Kind).CarveOrder);

            return compare != 0 ? compare : rank[left].CompareTo(rank[right]);
        });

        return ordered;
    }

    private float StrictestGrade => Mathf.Min(Mathf.Min(RoadKindProfile.For(_config, RoadKind.Highway).MaxGrade, RoadKindProfile.For(_config, RoadKind.Arterial).MaxGrade),
        Mathf.Min(RoadKindProfile.For(_config, RoadKind.LocalStreet).MaxGrade, RoadKindProfile.For(_config, RoadKind.DirtAccess).MaxGrade));

    private float SampleNormalized(float x, float y)
    {
        int resolution = _source.Resolution;

        int cellX = Mathf.Clamp(Mathf.RoundToInt(x / _cellSize), 0, resolution - 1);
        int cellY = Mathf.Clamp(Mathf.RoundToInt(y / _cellSize), 0, resolution - 1);

        return _source.Get(cellX, cellY);
    }
}
