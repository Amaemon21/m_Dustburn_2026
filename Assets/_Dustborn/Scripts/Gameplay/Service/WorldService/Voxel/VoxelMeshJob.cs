using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct VoxelMeshJob : IJob
{
    private const int NONE = -1;
    private const float SLIVER = 1e-4f;

    public VoxelDensitySampler Field;

    public int ChunkX;
    public int ChunkY;
    public int ChunkZ;
    public int Size;
    public float VoxelSize;
    public float SkirtDepth;
    public int Seams;
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
        BuildSkirt(origin, scale);
    }

    private void BuildSkirt(Vector3 origin, float scale)
    {
        int faces = Seams & Morph;

        if (SkirtDepth <= 0f || faces == 0)
            return;

        float bottom = origin.y;
        float top = origin.y + Size * scale;

        for (int from = 0; from < Size; from += 2)
        {
            int to = math.min(from + 2, Size);

            if ((faces & VoxelColumnKey.FACE_MIN_X) != 0)
                Curtain(Border(origin, scale, 0, to), Border(origin, scale, 0, from), bottom, top);

            if ((faces & VoxelColumnKey.FACE_MAX_X) != 0)
                Curtain(Border(origin, scale, Size, from), Border(origin, scale, Size, to), bottom, top);

            if ((faces & VoxelColumnKey.FACE_MIN_Z) != 0)
                Curtain(Border(origin, scale, from, 0), Border(origin, scale, to, 0), bottom, top);

            if ((faces & VoxelColumnKey.FACE_MAX_Z) != 0)
                Curtain(Border(origin, scale, to, Size), Border(origin, scale, from, Size), bottom, top);
        }
    }

    private Vector3 Border(Vector3 origin, float scale, int slotX, int slotZ)
    {
        var position = new Vector3(origin.x + slotX * scale, 0f, origin.z + slotZ * scale);

        position.y = Surface(position.x, position.z, origin, scale);

        return position;
    }

    private void Curtain(Vector3 near, Vector3 far, float bottom, float top)
    {
        float lowest = math.min(near.y, far.y);

        if (lowest < bottom || lowest >= top)
            return;

        float floor = lowest - SkirtDepth;

        int first = Add(near);
        int second = Add(far);
        int third = Add(new Vector3(far.x, floor, far.z), Normals[second]);
        int fourth = Add(new Vector3(near.x, floor, near.z), Normals[first]);

        Triangle(first, second, third);
        Triangle(first, third, fourth);
    }

    private int Add(Vector3 position)
    {
        return Add(position, Shade(position));
    }

    private int Add(Vector3 position, Vector3 normal)
    {
        float world = Field.CellSize * (Field.Resolution - 1);

        Vertices.Add(position);
        Normals.Add(normal);
        Uv.Add(new Vector2(position.x / world, position.z / world));

        return Vertices.Length - 1;
    }

    private Vector3 Shade(Vector3 position)
    {
        return Field.Normal(position.x, position.y, position.z, VoxelSize, Morph,
            ChunkX * Size * VoxelSize, ChunkZ * Size * VoxelSize, VoxelDensitySampler.MorphSpan(Size, VoxelSize), VoxelSize * 2f);
    }

    private float Surface(float x, float z, Vector3 origin, float scale)
    {
        return Field.Height(x, z, scale, Morph, origin.x, origin.z, VoxelDensitySampler.MorphSpan(Size, scale), scale * 2f);
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

        var position = new Vector3(origin.x + (slotX - 0.5f) * scale, 0f, origin.z + (slotZ - 0.5f) * scale);

        Stitch(ref position, origin, scale, slotX, slotZ);

        position.y = Surface(position.x, position.z, origin, scale);

        return Add(position);
    }

    private void Stitch(ref Vector3 position, Vector3 origin, float scale, int slotX, int slotZ)
    {
        bool edgeX = slotX == 0 || slotX == Size;
        bool edgeZ = slotZ == 0 || slotZ == Size;

        if (!edgeX && !edgeZ)
            return;

        if (edgeX)
        {
            position.x = origin.x + slotX * scale;

            if (!edgeZ)
                position.z = Node(ChunkZ, slotZ, slotX == 0 ? VoxelColumnKey.MORPH_MIN_X : VoxelColumnKey.MORPH_MAX_X) * scale;
        }

        if (edgeZ)
        {
            position.z = origin.z + slotZ * scale;

            if (!edgeX)
                position.x = Node(ChunkX, slotX, slotZ == 0 ? VoxelColumnKey.MORPH_MIN_Z : VoxelColumnKey.MORPH_MAX_Z) * scale;
        }

    }

    private int Node(int chunk, int slot, int morph)
    {
        int node = chunk * Size + slot;

        return (Morph & morph) != 0 ? node & ~1 : node;
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
        if (Collapsed(a, b, c))
            return;

        Triangles.Add(a);
        Triangles.Add(b);
        Triangles.Add(c);
    }

    private bool Collapsed(int a, int b, int c)
    {
        Vector3 first = Vertices[a];
        Vector3 second = Vertices[b] - first;
        Vector3 third = Vertices[c] - first;
        Vector3 across = Vertices[c] - Vertices[b];

        Vector3 normal = Vector3.Cross(second, third);

        float doubled = Square(normal);

        float longest = math.max(Square(second), math.max(Square(third), Square(across)));
        float width = VoxelSize * SLIVER;

        return doubled <= width * width * longest;
    }

    private static float Square(Vector3 value)
    {
        return value.x * value.x + value.y * value.y + value.z * value.z;
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
