using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct VoxelMeshJob : IJob
{
    private const int NONE = -1;

    public VoxelDensitySampler Field;

    public int ChunkX;
    public int ChunkY;
    public int ChunkZ;
    public int Size;
    public float VoxelSize;
    public float SkirtDepth;
    public int Trim;
    public int SkirtFaces;
    public int Morph;

    public NativeArray<float> Columns;
    public NativeArray<float> Density;
    public NativeArray<int> VertexAt;

    public VoxelCellLayout Cells;

    public NativeList<Vector3> Vertices;
    public NativeList<Vector3> Normals;
    public NativeList<Vector2> Uv;
    public NativeList<int> Triangles;

    private int Samples => Size + 2;

    private int Slots => Size + 1;

    public void Execute()
    {
        Vertices.Clear();
        Normals.Clear();
        Uv.Clear();
        Triangles.Clear();

        float scale = VoxelSize;

        var origin = new Vector3(ChunkX * Size * scale, ChunkY * Size * scale, ChunkZ * Size * scale);

        if (!Field.Fill(origin, scale, Samples, Columns, Density, Morph, Size))
            return;

        BuildVertices(origin, scale);
        BuildQuads();
        BuildSkirt();
    }

    private void BuildSkirt()
    {
        if (SkirtDepth <= 0f || SkirtFaces == 0)
            return;

        bool minX = (SkirtFaces & VoxelColumnKey.FACE_MIN_X) != 0;
        bool maxX = (SkirtFaces & VoxelColumnKey.FACE_MAX_X) != 0;
        bool minZ = (SkirtFaces & VoxelColumnKey.FACE_MIN_Z) != 0;
        bool maxZ = (SkirtFaces & VoxelColumnKey.FACE_MAX_Z) != 0;

        for (int step = 0; step < Size; step++)
        {
            if (minX)
                Curtain(Ridge(0, step + 1), Ridge(0, step));

            if (maxX)
                Curtain(Ridge(Size, step), Ridge(Size, step + 1));

            if (minZ)
                Curtain(Ridge(step, 0), Ridge(step + 1, 0));

            if (maxZ)
                Curtain(Ridge(step + 1, Size), Ridge(step, Size));
        }
    }

    private int Ridge(int slotX, int slotZ)
    {
        for (int y = Size; y >= 0; y--)
        {
            int vertex = VertexAt[Slot(slotX, y, slotZ)];

            if (vertex >= 0)
                return vertex;
        }

        return NONE;
    }

    private void Curtain(int from, int to)
    {
        if (from < 0 || to < 0)
            return;

        Vector3 near = Vertices[from];
        Vector3 far = Vertices[to];

        float bottom = math.min(near.y, far.y) - SkirtDepth;

        int first = Add(near, from);
        int second = Add(far, to);
        int third = Add(new Vector3(far.x, bottom, far.z), to);
        int fourth = Add(new Vector3(near.x, bottom, near.z), from);

        Triangle(first, second, third);
        Triangle(first, third, fourth);
    }

    private int Add(Vector3 position, int source)
    {
        Vertices.Add(position);
        Normals.Add(Normals[source]);
        Uv.Add(Uv[source]);

        return Vertices.Length - 1;
    }

    private void BuildVertices(Vector3 origin, float scale)
    {
        for (int z = 0; z < Slots; z++)
        {
            for (int y = 0; y < Slots; y++)
            {
                for (int x = 0; x < Slots; x++)
                    VertexAt[Slot(x, y, z)] = Vertex(origin, scale, x, y, z);
            }
        }
    }

    private int Vertex(Vector3 origin, float scale, int slotX, int slotY, int slotZ)
    {
        if (!Straddles(slotX, slotY, slotZ))
            return NONE;

        float sumX = 0f, sumY = 0f, sumZ = 0f;
        int crossings = 0;

        for (int edge = 0; edge < VoxelCellTables.EDGES; edge++)
        {
            int from = Cells.EdgeFrom[edge];
            int to = Cells.EdgeTo[edge];

            int fromX = Cells.CornerX[from];
            int fromY = Cells.CornerY[from];
            int fromZ = Cells.CornerZ[from];

            int toX = Cells.CornerX[to];
            int toY = Cells.CornerY[to];
            int toZ = Cells.CornerZ[to];

            float here = Density[Sample(slotX + fromX, slotY + fromY, slotZ + fromZ)];
            float there = Density[Sample(slotX + toX, slotY + toY, slotZ + toZ)];

            if (here > 0f == there > 0f)
                continue;

            float t = here / (here - there);

            sumX += fromX + (toX - fromX) * t;
            sumY += fromY + (toY - fromY) * t;
            sumZ += fromZ + (toZ - fromZ) * t;

            crossings++;
        }

        if (crossings == 0)
            return NONE;

        var position = new Vector3(
            origin.x + (slotX - 1 + sumX / crossings) * scale,
            origin.y + (slotY - 1 + sumY / crossings) * scale,
            origin.z + (slotZ - 1 + sumZ / crossings) * scale);

        Stitch(ref position, origin, scale, slotX, slotZ);

        float world = Field.CellSize * (Field.Resolution - 1);

        Vertices.Add(position);
        Normals.Add(Field.Normal(position.x, position.y, position.z, scale, Morph,
            origin.x, origin.z, Size * scale, scale * 2f));
        Uv.Add(new Vector2(position.x / world, position.z / world));

        return Vertices.Length - 1;
    }

    private void Stitch(ref Vector3 position, Vector3 origin, float scale, int slotX, int slotZ)
    {
        bool alongX = slotX == 0 && (Trim & VoxelColumnKey.TRIM_X) != 0;
        bool alongZ = slotZ == 0 && (Trim & VoxelColumnKey.TRIM_Z) != 0;

        if (!alongX && !alongZ)
            return;

        if (alongX)
            position.x = origin.x - Neighbour(scale, VoxelColumnKey.MORPH_MIN_X) * 0.5f;

        if (alongZ)
            position.z = origin.z - Neighbour(scale, VoxelColumnKey.MORPH_MIN_Z) * 0.5f;

        position.y = Field.Height(position.x, position.z, scale, Morph, origin.x, origin.z, Size * scale, scale * 2f);
    }

    private float Neighbour(float scale, int morph)
    {
        return (Morph & morph) != 0 ? scale * 2f : scale * 0.5f;
    }

    private bool Straddles(int slotX, int slotY, int slotZ)
    {
        bool solid = false;
        bool empty = false;

        for (int corner = 0; corner < VoxelCellTables.CORNERS; corner++)
        {
            float density = Density[Sample(
                slotX + Cells.CornerX[corner],
                slotY + Cells.CornerY[corner],
                slotZ + Cells.CornerZ[corner])];

            if (density > 0f)
                solid = true;
            else
                empty = true;

            if (solid && empty)
                return true;
        }

        return false;
    }

    private void BuildQuads()
    {
        for (int z = 0; z < Size; z++)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float here = Density[Sample(x + 1, y + 1, z + 1)];

                    Quad(here, Density[Sample(x + 2, y + 1, z + 1)],
                        Slot(x + 1, y, z), Slot(x + 1, y + 1, z), Slot(x + 1, y + 1, z + 1), Slot(x + 1, y, z + 1));

                    Quad(here, Density[Sample(x + 1, y + 2, z + 1)],
                        Slot(x, y + 1, z), Slot(x, y + 1, z + 1), Slot(x + 1, y + 1, z + 1), Slot(x + 1, y + 1, z));

                    Quad(here, Density[Sample(x + 1, y + 1, z + 2)],
                        Slot(x, y, z + 1), Slot(x + 1, y, z + 1), Slot(x + 1, y + 1, z + 1), Slot(x, y + 1, z + 1));
                }
            }
        }
    }

    private void Quad(float here, float there, int a, int b, int c, int d)
    {
        if (here > 0f == there > 0f)
            return;

        int first = VertexAt[a];
        int second = VertexAt[b];
        int third = VertexAt[c];
        int fourth = VertexAt[d];

        if (first < 0 || second < 0 || third < 0 || fourth < 0)
            return;

        if (here > 0f)
        {
            Triangle(first, second, third);
            Triangle(first, third, fourth);
            return;
        }

        Triangle(first, third, second);
        Triangle(first, fourth, third);
    }

    private void Triangle(int a, int b, int c)
    {
        Triangles.Add(a);
        Triangles.Add(b);
        Triangles.Add(c);
    }

    private int Sample(int x, int y, int z)
    {
        return (z * Samples + y) * Samples + x;
    }

    private int Slot(int x, int y, int z)
    {
        return (z * Slots + y) * Slots + x;
    }
}
