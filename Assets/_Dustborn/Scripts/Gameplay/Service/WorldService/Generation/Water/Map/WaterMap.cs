using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterMap
{
    private const float FORCED_OUTSIDE = 0.01f;

    public const int SUBDIVISION = 2;
    public const int CELL_SIDE_NODES = SUBDIVISION + 1;
    public const int CELL_NODES = CELL_SIDE_NODES * CELL_SIDE_NODES;
    public const short OWNER_NONE = -1;
    public const short OWNER_SEA = -2;
    public const float MESH_UNDERLAP = 0.3f;
    public const float LAGOON_BAR = 0.3f;

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
    public bool[] Frozen { get; }
    public bool[] Dry { get; }

    public int NodeResolution => Resolution * SUBDIVISION + 1;
    public float NodeStep => CellSize / SUBDIVISION;
    public int DetailCount => _detailCells.Count;
    public IReadOnlyList<int> DetailCells => _detailCells;
    internal short[] DetailOwners => _detailOwners;
    internal float[] DetailGrounds => _detailGround;
    public float[] Filled { get; set; }
    public List<float[]> CarveReach { get; set; }
    public HeightMap Uncarved { get; set; }
    public WaterStampLayout Stamps { get; set; }

    public List<WaterBody> Bodies { get; } = new();
    public List<RiverPath> Rivers { get; } = new();
    public List<WaterCrossing> Crossings { get; } = new();

    private RiverSegmentIndex _index;
    private bool _shoreReady;
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
        Frozen = new bool[cells];
        Dry = new bool[cells];

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

    public static bool Wet(WorldGenerationConfig config, WaterMap water, float x, float z, float ground)
    {
        if (water != null)
            return water.IsWet(x, z, ground, config.ShoreMargin);

        return config.SeaLevel > 0f && ground < config.SeaLevel + config.ShoreMargin;
    }

    public void ApplyClimate(WaterClimate climate)
    {
        if (climate == null)
            return;

        for (int cell = 0; cell < Frozen.Length; cell++)
        {
            Vector2 center = CellCenter(cell);
            Frozen[cell] = climate.Frozen(center.x, center.y);
            Dry[cell] = climate.Dry(center.x, center.y);
        }
    }

    public bool DryNear(float x, float z, float reach)
    {
        int span = Mathf.CeilToInt(reach / CellSize);
        int column = Mathf.Clamp((int)(x / CellSize), 0, Resolution - 1);
        int row = Mathf.Clamp((int)(z / CellSize), 0, Resolution - 1);

        for (int r = Mathf.Max(0, row - span); r <= Mathf.Min(Resolution - 1, row + span); r++)
        {
            for (int c = Mathf.Max(0, column - span); c <= Mathf.Min(Resolution - 1, column + span); c++)
            {
                if (Dry[r * Resolution + c])
                    return true;
            }
        }

        return false;
    }

    public bool FrozenAt(float x, float z)
    {
        return Frozen[CellIndex(x, z)];
    }

    public bool FrozenOwner(short owner, int cell)
    {
        if (owner == OWNER_SEA)
            return false;

        if (owner < 0 || owner >= Bodies.Count)
            return Frozen[cell];

        Vector2 center = Bodies[owner].Center;
        return FrozenAt(center.x, center.y);
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

    public (byte[] Kinds, short[] BodyIds) Ownership()
    {
        return ((byte[])Kinds.Clone(), (short[])BodyIds.Clone());
    }

    public void ResetOwnership((byte[] Kinds, short[] BodyIds) ownership)
    {
        Array.Copy(ownership.Kinds, Kinds, Kinds.Length);
        Array.Copy(ownership.BodyIds, BodyIds, BodyIds.Length);
        SetDetail(Array.Empty<int>(), Array.Empty<short>(), Array.Empty<float>());
        Array.Fill(ShoreSurface, float.NegativeInfinity);
        Array.Fill(ShoreDistance, float.PositiveInfinity);
        _shoreReady = false;
        InvalidateIndex();
    }

    public bool ShoreReady => _shoreReady;

    public void MarkShoreReady()
    {
        _shoreReady = true;
    }
}
