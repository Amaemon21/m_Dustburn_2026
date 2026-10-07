using System.Collections.Generic;

public sealed partial class WaterStampLakes
{
    private const float SHELF_DEPTH = 0.3f;
    private const float DRY_LIFT = 0.4f;
    private const float CONTAIN_MARGIN = 0.05f;
    private const float MAX_CLIPPED = 0.35f;
    private const float BANK_SLOPE = 0.3f;
    private const float BEACH_SLOPE = 0.1f;
    private const float DRY_LIFT_MAX = 0.25f;
    private const float BANK_REACH_EARTHWORKS = 3f;
    private const float FIELD_STEP = 2f;
    private const float SEED_CELLS = 1.5f;
    private const float MIN_GRADIENT = 1e-4f;

    private const float SCORE_STEP = 16f;
    private const float JITTER = 12f;
    private const float SMALLER = 0.9f;
    private const float OTHER_KIND = 0.3f;
    private const float HIGH_GROUND = 0.5f;
    private const int OTHER_BODY_REACH = 2;

    private readonly WorldGenerationConfig _config;
    private readonly WaterStampLibrary _library;
    private readonly WaterStampSettings _settings;
    private readonly HeightMap _map;
    private readonly HydrologyGrid _grid;
    private readonly WaterMap _water;
    private readonly WaterStampLayout _layout;
    private readonly HashSet<int> _held = new();
    private int[] _crowd;

    private const int FREE = -1;
    private const int SEA = -2;
    private const int MANY = -3;
    private int _basin;

    public WaterStampLakes(WorldGenerationConfig config, WaterStampLibrary library, HeightMap map, HydrologyGrid grid, WaterMap water, WaterStampLayout layout)
    {
        _config = config;
        _library = library;
        _settings = library.Settings;
        _map = map;
        _grid = grid;
        _water = water;
        _layout = layout;
    }

    public void Apply(List<HydrologyBasin> owners)
    {
        if (_library.Lakes.Count + _library.Ponds.Count == 0)
            return;

        foreach (HydrologyBasin owner in owners)
            _held.Add(owner.Id);

        _crowd = Crowding();

        for (int id = 0; id < _water.Bodies.Count; id++)
        {
            _basin = owners[id].Id;

            if (!TryFit(id, owners[id], out Fit best))
            {
                _layout.ProceduralBodies++;
                continue;
            }

            best.Placement.Body = id;
            _layout.Add(best.Placement);
            Carve(id, owners[id], best.Placement);
            Refresh(id, owners[id], best.Placement);
        }
    }
}
