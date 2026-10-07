using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class StandingWaterRegions
{
    public const int OWNERSHIP_REACH = 2;
    private const float LEAK_MARGIN = 0.05f;
    private const float MIN_FRAGMENT_AREA = 64f;
    private const float SATELLITE_AREA = 800f;
    private const float ISLET_AREA = 8f;
    private const float BAR_PAD = 2f;
    private const float FILL_TOLERANCE = 0.05f;
    private const float OUTLET_TOLERANCE = 0.05f;
    private const float CARVE_EPSILON = 0.05f;
    private const float RIVER_REACH = 10f;
    private const int STEP_ROUNDS = 3;

    private static readonly int[] StepX = { 1, 0, -1, 0 };
    private static readonly int[] StepZ = { 0, 1, 0, -1 };

    private readonly WaterMap _water;
    private readonly HeightMap _map;
    private readonly int _nodes;
    private readonly int _sub;
    private readonly float _step;
    private readonly short[] _owner;
    private readonly bool[] _barred;
    private readonly bool[] _ocean;
    private readonly HashSet<int> _sunk = new();
    private readonly short[] _basins;

    public int Lowered { get; private set; }
    public int Dropped { get; private set; }
    public int Fragments { get; private set; }
    public int Islets { get; private set; }
    public int SeaPuddles { get; private set; }
    public int Stepped { get; private set; }

    private int MinFragment => Mathf.Max(1, Mathf.CeilToInt(MIN_FRAGMENT_AREA / (_step * _step)));
    private int IsletNodes => Mathf.Max(1, Mathf.CeilToInt(ISLET_AREA / (_step * _step)));

    public StandingWaterRegions(WaterMap water, HeightMap map)
    {
        _water = water;
        _map = map;
        _nodes = water.NodeResolution;
        _sub = WaterMap.SUBDIVISION;
        _step = water.NodeStep;
        _owner = new short[_nodes * _nodes];
        _barred = new bool[_nodes * _nodes];
        _ocean = new bool[_nodes * _nodes];
        _basins = (short[])water.BodyIds.Clone();
    }

    public float Ground(int node)
    {
        return _map.SampleWorldSmooth(node % _nodes * _step, node / _nodes * _step);
    }

    public short Owner(int node)
    {
        return _owner[node];
    }

    public bool IsSunk(int node)
    {
        return _sunk.Contains(node);
    }

    public int NearestNode(float x, float z)
    {
        int i = Mathf.Clamp(Mathf.RoundToInt(x / _step), 0, _nodes - 1);
        int j = Mathf.Clamp(Mathf.RoundToInt(z / _step), 0, _nodes - 1);

        return j * _nodes + i;
    }

    public bool NearestBody(float x, float z, float reach, out short body, out float distance)
    {
        body = WaterMap.OWNER_NONE;
        distance = float.MaxValue;

        int minI = Mathf.Max(0, Mathf.FloorToInt((x - reach) / _step)), maxI = Mathf.Min(_nodes - 1, Mathf.CeilToInt((x + reach) / _step));
        int minJ = Mathf.Max(0, Mathf.FloorToInt((z - reach) / _step)), maxJ = Mathf.Min(_nodes - 1, Mathf.CeilToInt((z + reach) / _step));
        float nearest = reach * reach;

        for (int j = minJ; j <= maxJ; j++)
        {
            for (int i = minI; i <= maxI; i++)
            {
                short owner = _owner[j * _nodes + i];

                if (owner < 0)
                    continue;

                float dx = i * _step - x, dz = j * _step - z;
                float squared = dx * dx + dz * dz;

                if (squared >= nearest)
                    continue;

                nearest = squared;
                body = owner;
            }
        }

        if (body < 0)
            return false;

        distance = Mathf.Sqrt(nearest);
        return true;
    }

    public void Build(float shelf)
    {
        Array.Fill(_owner, WaterMap.OWNER_SEA);
        Array.Clear(_barred, 0, _barred.Length);
        _sunk.Clear();
        MarkOcean();

        BarRivers();

        List<int>[] cells = BodyCells();
        bool[] rivers = RiverBodies();

        for (int round = 0; ; round++)
        {
            for (int body = 0; body < _water.Bodies.Count; body++)
                Fill(body, cells[body], rivers[body], shelf);

            if (round == STEP_ROUNDS || !LowerSteps())
                break;

            Array.Fill(_owner, WaterMap.OWNER_SEA);
            _sunk.Clear();
        }

        KeepSeaOffRivers();
        ClearSeaPuddles();
    }

    public bool Ocean(int node)
    {
        return _ocean[node];
    }

    private List<int>[] BodyCells()
    {
        var cells = new List<int>[_water.Bodies.Count];

        for (int body = 0; body < cells.Length; body++)
            cells[body] = new List<int>();

        for (int cell = 0; cell < _basins.Length; cell++)
        {
            int body = _basins[cell];

            if (body >= 0 && body < cells.Length)
                cells[body].Add(cell);
        }

        return cells;
    }

    private bool[] RiverBodies()
    {
        var rivers = new bool[_water.Bodies.Count];
        int resolution = _water.Resolution;
        float cell = _water.CellSize;

        foreach (RiverPath river in _water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                float reach = 0.5f * point.Width + RIVER_REACH + cell;
                int minC = Mathf.Max(0, Mathf.FloorToInt((point.Position.x - reach) / cell));
                int maxC = Mathf.Min(resolution - 1, Mathf.FloorToInt((point.Position.x + reach) / cell));
                int minR = Mathf.Max(0, Mathf.FloorToInt((point.Position.y - reach) / cell));
                int maxR = Mathf.Min(resolution - 1, Mathf.FloorToInt((point.Position.y + reach) / cell));

                for (int r = minR; r <= maxR; r++)
                {
                    for (int c = minC; c <= maxC; c++)
                    {
                        int body = _basins[r * resolution + c];

                        if (body >= 0 && body < rivers.Length)
                            rivers[body] = true;
                    }
                }
            }
        }

        return rivers;
    }

    public void Publish()
    {
        WaterShore.Classify(_water, _map, (cell, i, j) => _owner[j * _nodes + i], _ => true);
    }
}
