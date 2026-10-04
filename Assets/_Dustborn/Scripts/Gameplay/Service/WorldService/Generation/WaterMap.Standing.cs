using UnityEngine;

public sealed partial class WaterMap
{
    public WaterSample Sample(float x, float z)
    {
        if (TryRiver(x, z, out WaterSample river))
        {
            if (_shoreReady && Covers(x, z, out short under) && river.Surface <= LevelOf(under) + WaterMeshes.MOUTH_BLEND)
                return Standing(under);

            return river;
        }

        if (_shoreReady)
            return Covers(x, z, out short owner) ? Standing(owner) : new WaterSample { Kind = WaterKind.None, Surface = float.NegativeInfinity, Body = -1 };

        int column = Mathf.Clamp((int)(x / CellSize), 0, Resolution - 1);
        int row = Mathf.Clamp((int)(z / CellSize), 0, Resolution - 1);

        int body = -1;
        float surface = float.NegativeInfinity;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int c = column + dx;
                int r = row + dz;

                if (c < 0 || r < 0 || c >= Resolution || r >= Resolution)
                    continue;

                int id = BodyIds[r * Resolution + c];

                if (id < 0 || Bodies[id].Surface <= surface)
                    continue;

                body = id;
                surface = Bodies[id].Surface;
            }
        }

        if (body >= 0 && (SeaLevel <= 0f || surface >= SeaLevel))
            return new WaterSample { Kind = Bodies[body].Kind, Surface = surface, Body = body };

        if (SeaLevel > 0f)
            return new WaterSample { Kind = WaterKind.Sea, Surface = SeaLevel, Body = -1 };

        return new WaterSample { Kind = WaterKind.None, Surface = float.NegativeInfinity, Body = -1 };
    }

    private WaterSample Standing(short owner)
    {
        if (owner >= 0)
            return new WaterSample { Kind = Bodies[owner].Kind, Surface = Bodies[owner].Surface, Body = owner };

        if (SeaLevel > 0f && owner == OWNER_SEA)
            return new WaterSample { Kind = WaterKind.Sea, Surface = SeaLevel, Body = -1 };

        return new WaterSample { Kind = WaterKind.None, Surface = float.NegativeInfinity, Body = -1 };
    }

    public bool StandingAt(float x, float z, out float surface)
    {
        surface = 0f;

        if (!Covers(x, z, out short owner))
            return false;

        surface = LevelOf(owner);
        return true;
    }

    public float LevelOf(short owner)
    {
        if (owner >= 0)
            return Bodies[owner].Surface;

        return owner == OWNER_SEA && SeaLevel > 0f ? SeaLevel : float.NegativeInfinity;
    }

    public WaterKind KindOf(short owner)
    {
        if (owner >= 0)
            return Bodies[owner].Kind;

        return owner == OWNER_SEA && SeaLevel > 0f ? WaterKind.Sea : WaterKind.None;
    }

    public short CellOwner(int cell)
    {
        if (Kinds[cell] == 0)
            return OWNER_SEA;

        return BodyIds[cell] >= 0 ? BodyIds[cell] : OWNER_SEA;
    }

    public bool IsFull(int cell)
    {
        return DetailSlot[cell] < 0 && Kinds[cell] != 0;
    }

    public short OwnerAt(float x, float z)
    {
        int cell = CellIndex(x, z);
        int slot = DetailSlot[cell];

        if (slot < 0)
            return CellOwner(cell);

        Local(cell, x, z, out int fx, out int fz, out float tx, out float tz);

        return PickOwner(_detailOwners, slot * CELL_NODES, fx, fz, tx, tz);
    }

    public bool Covers(float x, float z, out short owner)
    {
        int cell = CellIndex(x, z);
        int slot = DetailSlot[cell];
        owner = OWNER_NONE;

        if (slot < 0)
        {
            if (Kinds[cell] == 0)
                return false;

            owner = CellOwner(cell);
            return true;
        }

        Local(cell, x, z, out int fx, out int fz, out float tx, out float tz);

        int node = slot * CELL_NODES + fz * CELL_SIDE_NODES + fx;
        int above = node + CELL_SIDE_NODES;

        for (int corner = 0; corner < 4; corner++)
        {
            int index = (corner >> 1 == 0 ? node : above) + (corner & 1);
            short candidate = _detailOwners[index];

            if (float.IsNegativeInfinity(LevelOf(candidate)) || Tested(candidate, corner, node, above))
                continue;

            float a = NodeValue(candidate, _detailOwners[node], _detailGround[node]);
            float b = NodeValue(candidate, _detailOwners[node + 1], _detailGround[node + 1]);
            float c = NodeValue(candidate, _detailOwners[above], _detailGround[above]);
            float d = NodeValue(candidate, _detailOwners[above + 1], _detailGround[above + 1]);

            if (!MarchingCell.Contains(a, b, d, c, tx, tz))
                continue;

            owner = candidate;
            return true;
        }

        return false;
    }

    private bool Tested(short candidate, int corner, int node, int above)
    {
        for (int earlier = 0; earlier < corner; earlier++)
        {
            if (_detailOwners[(earlier >> 1 == 0 ? node : above) + (earlier & 1)] == candidate)
                return true;
        }

        return false;
    }

    public static bool MeshInside(float level, float ground)
    {
        return level + MESH_UNDERLAP - ground >= FORCED_OUTSIDE;
    }

    public float NodeValue(short owner, short nodeOwner, float ground)
    {
        float natural = LevelOf(owner) + MESH_UNDERLAP - ground;

        if (Mathf.Abs(natural) < FORCED_OUTSIDE)
            return -FORCED_OUTSIDE;

        if (nodeOwner == owner || natural <= 0f)
            return natural;

        float own = nodeOwner == OWNER_NONE ? natural : Mathf.Max(0f, LevelOf(nodeOwner) + MESH_UNDERLAP - ground);

        return -Mathf.Max(own, FORCED_OUTSIDE);
    }

    private void Local(int cell, float x, float z, out int fx, out int fz, out float tx, out float tz)
    {
        float step = NodeStep;
        float u = Mathf.Clamp((x - cell % Resolution * CellSize) / step, 0f, SUBDIVISION - 1e-4f);
        float v = Mathf.Clamp((z - cell / Resolution * CellSize) / step, 0f, SUBDIVISION - 1e-4f);

        fx = (int)u;
        fz = (int)v;
        tx = u - fx;
        tz = v - fz;
    }

    public static short PickOwner(short[] owners, int start, int fx, int fz, float tx, float tz)
    {
        short best = OWNER_NONE;
        float nearest = float.MaxValue;
        bool sea = false;

        for (int corner = 0; corner < 4; corner++)
        {
            int cx = corner & 1;
            int cz = corner >> 1;
            short owner = owners[start + (fz + cz) * CELL_SIDE_NODES + fx + cx];

            sea |= owner == OWNER_SEA;

            if (owner < 0)
                continue;

            float dx = tx - cx;
            float dz = tz - cz;
            float distance = dx * dx + dz * dz;

            if (distance >= nearest)
                continue;

            nearest = distance;
            best = owner;
        }

        if (best >= 0)
            return best;

        return sea ? OWNER_SEA : OWNER_NONE;
    }

    public bool IsWater(float x, float z, float ground)
    {
        if (Remote(CellIndex(x, z), 0f))
            return SeaLevel > 0f && ground < SeaLevel;

        WaterSample sample = Sample(x, z);

        return sample.IsWater && ground < sample.Surface;
    }

    private bool Remote(int cell, float reach)
    {
        return _shoreReady && ShoreDistance[cell] > reach + 2f * CellSize;
    }

    public WaterKind KindAt(float x, float z, float ground)
    {
        WaterSample sample = Sample(x, z);

        return sample.IsWater && ground < sample.Surface ? sample.Kind : WaterKind.None;
    }

    public float SurfaceHeight(float x, float z)
    {
        return Sample(x, z).Surface;
    }

    public float Depth(float x, float z, float ground)
    {
        WaterSample sample = Sample(x, z);

        return sample.IsWater ? Mathf.Max(0f, sample.Surface - ground) : 0f;
    }

    public Vector2 FlowDirection(float x, float z)
    {
        return Sample(x, z).Flow;
    }

    public bool IsWet(float x, float z, float ground, float margin)
    {
        if (SeaLevel > 0f && ground < SeaLevel + margin)
            return true;

        int cell = CellIndex(x, z);

        if (Remote(cell, ShoreReach))
            return false;

        if (IsWater(x, z, ground + margin))
            return true;

        return ShoreDistance[cell] <= ShoreReach && ground < ShoreSurface[cell] + margin;
    }

    public bool TrySurface(float x, float z, out float surface)
    {
        if (TryRiver(x, z, out WaterSample river))
        {
            surface = river.Surface;
            return true;
        }

        return StandingAt(x, z, out surface);
    }
}
