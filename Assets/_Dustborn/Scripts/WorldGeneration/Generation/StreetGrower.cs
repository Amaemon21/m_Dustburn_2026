using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class StreetGrower
{
    private const int TURN_CANDIDATES = 5;
    private const float PARALLEL_COS = 0.55f;

    private readonly struct Seed
    {
        public Vector2 Position { get; }
        public Vector2 Direction { get; }
        public int Depth { get; }
        public float Budget { get; }
        public bool FromGap { get; }

        public Seed(Vector2 position, Vector2 direction, int depth, float budget, bool fromGap = false)
        {
            Position = position;
            Direction = direction;
            Depth = depth;
            Budget = budget;
            FromGap = fromGap;
        }
    }

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;
    private readonly RoadProximity _index;

    private readonly Queue<Seed> _seeds = new();
    private readonly List<Seed> _placed = new();
    private readonly List<Vector2> _points = new();
    private readonly List<Vector2> _back = new();

    public int DeadEnds { get; private set; }
    public int Junctions { get; private set; }
    public int Crowded { get; private set; }
    public int Seeded { get; private set; }
    public int TooShort { get; private set; }
    public int Blocked { get; private set; }

    public StreetGrower(WorldGenerationConfig config, HeightMap map, RoadProximity index)
    {
        _config = config;
        _map = map;
        _index = index;
    }

    public void Grow(CityLayout layout, IReadOnlyList<Road> trunks, ref Random random)
    {
        _seeds.Clear();
        _placed.Clear();

        SeedFromTrunks(layout, trunks, ref random);

        if (_seeds.Count == 0)
            SeedFromCenter(layout, ref random);

        for (int pass = 0; pass <= _config.StreetFillPasses; pass++)
        {
            Drain(layout, ref random);

            if (pass < _config.StreetFillPasses)
                SeedGaps(layout, ref random);
        }
    }

    private void Drain(CityLayout layout, ref Random random)
    {
        while (_seeds.Count > 0 && layout.Streets.Count < _config.MaxStreetsPerSettlement)
        {
            Seed seed = _seeds.Dequeue();

            Seeded++;

            Road street = GrowStreet(layout, seed, ref random);

            if (street == null)
                continue;

            layout.Streets.Add(street);
            _index.Add(street);
        }
    }

    private void SeedGaps(CityLayout layout, ref Random random)
    {
        Vector2 center = layout.Hub.Position;
        float reach = layout.Radius * (1f + _config.CityShapeJitter);
        float step = layout.Profile.CoreBlockSize * 0.5f;

        for (float y = -reach; y <= reach; y += step)
        {
            for (float x = -reach; x <= reach; x += step)
            {
                Vector2 sample = center + new Vector2(x, y);

                if (!layout.Contains(sample))
                    continue;

                if (_index.IsWithin(sample, layout.BlockSizeAt(sample) * _config.StreetSpacingFraction))
                    continue;

                Vector2 direction = GapDirection(layout, sample, ref random);

                _seeds.Enqueue(new Seed(sample, direction, 1, Budget(layout, 1, ref random), true));
            }
        }
    }

    private void SeedFromTrunks(CityLayout layout, IReadOnlyList<Road> trunks, ref Random random)
    {
        foreach (Road trunk in trunks)
        {
            if (trunk.Points == null || trunk.Points.Length < 2)
                continue;

            float travelled = 0f;
            float nextAt = layout.Profile.CoreBlockSize * 0.5f;

            for (int i = 0; i < trunk.Points.Length - 1; i++)
            {
                Vector2 from = trunk.Points[i];
                Vector2 to = trunk.Points[i + 1];

                float length = Vector2.Distance(from, to);

                if (length <= Mathf.Epsilon)
                    continue;

                Vector2 direction = (to - from) / length;

                while (travelled + length >= nextAt)
                {
                    Vector2 point = from + direction * (nextAt - travelled);

                    nextAt += layout.BlockSizeAt(point) * random.NextFloat(0.9f, 1.4f);

                    if (!layout.Contains(point))
                        continue;

                    Vector2 normal = new(-direction.y, direction.x);

                    PushRib(layout, point, normal, ref random);
                    PushRib(layout, point, -normal, ref random);
                }

                travelled += length;
            }
        }
    }

    private void PushRib(CityLayout layout, Vector2 point, Vector2 normal, ref Random random)
    {
        Vector2 direction = Rotate(normal, random.NextFloat(-1f, 1f) * _config.StreetBranchJitter * Mathf.Deg2Rad);

        _seeds.Enqueue(new Seed(point, direction, 0, Budget(layout, 0, ref random)));
    }

    private void SeedFromCenter(CityLayout layout, ref Random random)
    {
        Vector2 axis = new(Mathf.Cos(layout.Angle), Mathf.Sin(layout.Angle));
        Vector2 normal = new(-axis.y, axis.x);

        _seeds.Enqueue(new Seed(layout.Hub.Position, axis, 0, Budget(layout, 0, ref random)));
        _seeds.Enqueue(new Seed(layout.Hub.Position, -axis, 0, Budget(layout, 0, ref random)));
        _seeds.Enqueue(new Seed(layout.Hub.Position, normal, 0, Budget(layout, 0, ref random)));
        _seeds.Enqueue(new Seed(layout.Hub.Position, -normal, 0, Budget(layout, 0, ref random)));
    }

    private float Budget(CityLayout layout, int depth, ref Random random)
    {
        float block = layout.BlockSizeAt(layout.Hub.Position);
        float spans = random.NextFloat(_config.StreetSpanMin, _config.StreetSpanMax);

        return block * spans * Mathf.Pow(0.7f, depth);
    }

    private Road GrowStreet(CityLayout layout, Seed seed, ref Random random)
    {
        if (IsCrowded(layout, seed))
        {
            Crowded++;
            return null;
        }

        _placed.Add(seed);

        _points.Clear();

        float travelled = 0f;

        if (seed.FromGap)
        {
            _back.Clear();

            travelled += Walk(layout, seed, -seed.Direction, _back, ref random);

            for (int i = _back.Count - 1; i >= 0; i--)
                _points.Add(_back[i]);
        }

        _points.Add(seed.Position);

        travelled += Walk(layout, seed, seed.Direction, _points, ref random);

        if (travelled < _config.MinStreetLength || _points.Count < 2)
        {
            TooShort++;
            return null;
        }

        return new Road(RoadSmoother.RoundCorners(_points.ToArray(), _config.StreetCornerRadius), _config.StreetHalfWidth * 2f);
    }

    private float Walk(CityLayout layout, Seed seed, Vector2 heading, List<Vector2> points, ref Random random)
    {
        float step = _config.StreetStepLength;
        float parent = _config.RoadHalfWidth + _config.StreetHalfWidth;
        float grace = parent + step * 0.5f;

        Vector2 position = seed.Position;
        Vector2 direction = heading;

        float travelled = 0f;
        float clearUntil = grace;
        float nextBranch = layout.BlockSizeAt(position) * random.NextFloat(0.8f, 1.6f);

        while (travelled < seed.Budget)
        {
            if (!TryStep(layout, position, ref direction, step, ref random, out Vector2 next))
            {
                Blocked++;
                break;
            }

            bool free = travelled >= clearUntil;

            float leash = DistanceUtility.WithinRadius(next, seed.Position, parent + step) ? parent : 0f;

            if (free && _index.Crosses(position, next, seed.Position, leash, out Vector2 hit, out float transverse))
            {
                if (transverse < Mathf.Sin(_config.StreetJunctionAngle * Mathf.Deg2Rad))
                    break;

                points.Add(hit);

                travelled += Vector2.Distance(position, hit);
                position = hit;
                clearUntil = travelled + step * 1.5f;

                Junctions++;

                continue;
            }

            if (free && _index.IsParallelWithin(next, direction, layout.BlockSizeAt(next) * _config.StreetSpacingFraction, PARALLEL_COS, seed.Position, leash))
            {
                DeadEnds++;
                break;
            }

            points.Add(next);

            position = next;
            travelled += step;

            if (seed.Depth >= layout.Profile.StreetBranchDepth || travelled < nextBranch)
                continue;

            nextBranch = travelled + layout.BlockSizeAt(position) * random.NextFloat(0.8f, 1.6f);

            if (random.NextFloat() > _config.StreetBranchChance)
                continue;

            Vector2 normal = new(-direction.y, direction.x);
            float sign = random.NextFloat() < 0.5f ? 1f : -1f;

            PushBranch(layout, position, normal * sign, seed.Depth + 1, ref random);

            if (random.NextFloat() < _config.StreetBranchChance * 0.75f)
                PushBranch(layout, position, normal * -sign, seed.Depth + 1, ref random);
        }

        return travelled;
    }

    private Vector2 GapDirection(CityLayout layout, Vector2 sample, ref Random random)
    {
        float jitter = random.NextFloat(-1f, 1f) * _config.StreetBranchJitter * Mathf.Deg2Rad;

        if (!_index.TryNearestDirection(sample, layout.BlockSizeAt(sample) * 2f, out Vector2 nearest))
            nearest = new Vector2(Mathf.Cos(layout.Angle), Mathf.Sin(layout.Angle));

        if (random.NextFloat() < 0.5f)
            nearest = new Vector2(-nearest.y, nearest.x);

        return Rotate(nearest, jitter);
    }

    private bool IsCrowded(CityLayout layout, Seed seed)
    {
        float spacing = layout.BlockSizeAt(seed.Position) * _config.StreetSpacingFraction;

        if (seed.FromGap)
            return _index.IsWithin(seed.Position, spacing * 1.15f);

        foreach (Seed placed in _placed)
        {
            if (Vector2.Dot(placed.Direction, seed.Direction) < PARALLEL_COS)
                continue;

            Vector2 delta = seed.Position - placed.Position;

            float along = Vector2.Dot(delta, placed.Direction);
            float across = Mathf.Abs(delta.x * placed.Direction.y - delta.y * placed.Direction.x);

            if (across < spacing && Mathf.Abs(along) < spacing * 2f)
                return true;
        }

        return false;
    }

    private void PushBranch(CityLayout layout, Vector2 position, Vector2 normal, int depth, ref Random random)
    {
        Vector2 direction = Rotate(normal, random.NextFloat(-1f, 1f) * _config.StreetBranchJitter * Mathf.Deg2Rad);

        _seeds.Enqueue(new Seed(position, direction, depth, Budget(layout, depth, ref random)));
    }

    private bool TryStep(CityLayout layout, Vector2 position, ref Vector2 direction, float step, ref Random random, out Vector2 next)
    {
        float drift = random.NextFloat(-1f, 1f) * _config.StreetCurveJitter * Mathf.Deg2Rad;
        float turn = _config.StreetMaxTurn * Mathf.Deg2Rad;

        Vector2 best = Vector2.zero;
        Vector2 bestDirection = direction;
        float bestSlope = float.MaxValue;

        float ground = _map.SampleWorld(new Vector3(position.x, 0f, position.y));

        for (int i = 0; i < TURN_CANDIDATES; i++)
        {
            float angle = drift + Mathf.Lerp(-turn, turn, i / (TURN_CANDIDATES - 1f));

            Vector2 candidateDirection = Rotate(direction, angle);
            Vector2 candidate = position + candidateDirection * step;

            if (!IsInsideWorld(candidate) || !layout.Contains(candidate))
                continue;

            float height = _map.SampleWorld(new Vector3(candidate.x, 0f, candidate.y));
            float slope = Mathf.Abs(height - ground) / step;

            if (slope > _config.MaxStreetSlope)
                continue;

            slope += Mathf.Abs(angle) * _config.StreetStraightBias;

            if (slope >= bestSlope)
                continue;

            bestSlope = slope;
            best = candidate;
            bestDirection = candidateDirection;
        }

        next = best;

        if (bestSlope == float.MaxValue)
            return false;

        direction = bestDirection;

        return true;
    }

    private bool IsInsideWorld(Vector2 point)
    {
        return point.x >= 0f && point.y >= 0f && point.x <= _config.WorldSize && point.y <= _config.WorldSize;
    }

    private static Vector2 Rotate(Vector2 direction, float angle)
    {
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);

        return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
    }
}
