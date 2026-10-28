using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

static class DecorGroundChecks
{
    const string CONTENT = "../../Assets/_Dustborn/Content/Configs/World";
    const string GENERATED = "../../Assets/_Dustborn/Generated";
    const int COLUMNS_PER_LOD = 12;
    const int POINTS_PER_COLUMN = 160;
    const float FLOATING = 0.05f;
    const float DRAWN_TOLERANCE = 0.01f;

    public struct Triangle
    {
        public Vector3 A;
        public Vector3 B;
        public Vector3 C;
    }

    public static void Run(string[] args)
    {
        var config = AssetReader.Load<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");
        var settings = AssetReader.Load<WorldBuildSettings>($"{CONTENT}/WorldBuildSettings.asset");
        var voxels = AssetReader.Load<VoxelConfig>($"{CONTENT}/VoxelConfig.asset");
        HeightMap map = HeightMap.FromRaw16(File.ReadAllBytes($"{GENERATED}/HeightMap.bytes"), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
        WaterMap water = WaterMapFormat.Read(File.ReadAllBytes($"{GENERATED}/WaterMap.bytes"));
        Vector2 viewer = ParseViewer(args) ?? new Vector2(config.WorldSize * 0.5f, config.WorldSize * 0.5f);

        using var field = new VoxelDensityField(map, voxels, 0f);
        var plan = new VoxelStreamPlan(voxels, settings.LodCount, settings.NearDistance, config.WorldSize, water);

        if (Array.IndexOf(args, "--no-shore") < 0)
            VoxelShorePlan.Refine(field.Sampler, plan, viewer, voxels.ChunkSize, water);

        var keys = new List<VoxelColumnKey>();
        plan.Around(viewer, keys);

        Console.WriteLine($"decor on the drawn ground: {keys.Count} columns around ({viewer.x:0}, {viewer.y:0})");
        Console.WriteLine("  lod  voxel  columns  points | old: mean   p95    max    floating  sunk | drawn: max");

        using var mesh = new VoxelMesh();
        using var mesher = new VoxelChunkMesher(voxels, field);

        int failures = 0;

        for (int lod = 0; lod < plan.LodCount; lod++)
        {
            List<VoxelColumnKey> sample = Sample(keys, lod);

            if (sample.Count == 0)
            {
                Console.WriteLine($"  {lod,3}  no columns at this level around the viewer");
                continue;
            }

            var errors = new List<float>();
            float drawnWorst = 0f;
            int floating = 0, sunk = 0;

            foreach (VoxelColumnKey key in sample)
            {
                List<Triangle> triangles = MeshColumn(mesher, mesh, field, key, plan.ChunkMetres(key.Lod), plan.VoxelSize(key.Lod));

                if (triangles.Count == 0)
                    continue;

                float span = plan.ChunkMetres(key.Lod);
                var frame = new DecorSurface(new Vector2(key.X * span, key.Z * span), voxels.ChunkSize, plan.VoxelSize(key.Lod), key.Morph);
                var random = new System.Random(key.X * 7919 + key.Z * 104729 + key.Lod);

                for (int i = 0; i < POINTS_PER_COLUMN; i++)
                {
                    float x = frame.Origin.x + (float)random.NextDouble() * span;
                    float z = frame.Origin.y + (float)random.NextDouble() * span;

                    if (!TryHeight(triangles, x, z, out float ground))
                        continue;

                    float old = field.Surface(x, z, frame) - ground;
                    float drawn = Math.Abs(field.Drawn(x, z, frame) - ground);

                    errors.Add(Math.Abs(old));
                    drawnWorst = Math.Max(drawnWorst, drawn);

                    if (old > FLOATING)
                        floating++;
                    else if (old < -FLOATING)
                        sunk++;
                }
            }

            if (errors.Count == 0)
            {
                Console.WriteLine($"  {lod,3}  {sample.Count} columns, no point landed on their meshes");
                continue;
            }

            errors.Sort();

            float mean = 0f;

            foreach (float error in errors)
                mean += error / errors.Count;

            float p95 = errors[(int)(errors.Count * 0.95f)];
            float max = errors[^1];

            Console.WriteLine($"  {lod,3}  {plan.VoxelSize(lod),4:0} м  {sample.Count,7}  {errors.Count,6} | {mean,9:0.000}  {p95,5:0.000}  {max,6:0.000}  "
                + $"{100f * floating / errors.Count,6:0.0}%  {100f * sunk / errors.Count,5:0.0}% | {drawnWorst,10:0.0000}");

            if (drawnWorst > DRAWN_TOLERANCE)
                failures++;
        }

        if (failures > 0)
        {
            Console.WriteLine($"  ПРОВАЛ: на {failures} уровнях LOD высота Drawn расходится с мешем больше {DRAWN_TOLERANCE * 100f:0} см");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"  Drawn совпадает с нарисованным мешем на всех уровнях с точностью {DRAWN_TOLERANCE * 100f:0} см");
    }

    static List<VoxelColumnKey> Sample(List<VoxelColumnKey> keys, int lod)
    {
        var level = keys.FindAll(key => key.Lod == lod);
        var sample = new List<VoxelColumnKey>();

        if (level.Count == 0)
            return sample;

        var morphed = level.FindAll(key => key.Morph != 0);
        var plain = level.FindAll(key => key.Morph == 0);

        Take(morphed, COLUMNS_PER_LOD / 2, sample);
        Take(plain, COLUMNS_PER_LOD - sample.Count, sample);

        return sample;
    }

    static void Take(List<VoxelColumnKey> from, int count, List<VoxelColumnKey> into)
    {
        if (from.Count == 0 || count <= 0)
            return;

        int stride = Math.Max(1, from.Count / count);

        for (int i = 0; i < from.Count && into.Count < COLUMNS_PER_LOD && count > 0; i += stride, count--)
            into.Add(from[i]);
    }

    public static List<Triangle> MeshColumn(VoxelChunkMesher mesher, VoxelMesh mesh, VoxelDensityField field, VoxelColumnKey key, float size, float voxelSize)
    {
        var triangles = new List<Triangle>();

        field.ChunkRange(key, size, voxelSize, out int low, out int high);

        for (int y = low; y <= high; y++)
        {
            mesher.Mesh(key.Lod, key.X, y, key.Z, mesh, key.Seams, key.Morph);

            for (int i = 0; i + 2 < mesh.Triangles.Length; i += 3)
            {
                var triangle = new Triangle
                {
                    A = mesh.Vertices[mesh.Triangles[i]],
                    B = mesh.Vertices[mesh.Triangles[i + 1]],
                    C = mesh.Vertices[mesh.Triangles[i + 2]]
                };

                Vector3 normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);

                if (Math.Abs(normal.y) > 1e-6f)
                    triangles.Add(triangle);
            }
        }

        return triangles;
    }

    public static bool TryHeight(List<Triangle> triangles, float x, float z, out float height)
    {
        bool found = false;

        height = float.MinValue;

        foreach (Triangle triangle in triangles)
        {
            if (!OnTriangle(triangle, x, z, out float y))
                continue;

            found = true;
            height = Math.Max(height, y);
        }

        return found;
    }

    static bool OnTriangle(Triangle t, float x, float z, out float height)
    {
        height = 0f;

        double area = (double)(t.B.x - t.A.x) * (t.C.z - t.A.z) - (double)(t.C.x - t.A.x) * (t.B.z - t.A.z);

        if (Math.Abs(area) < 1e-9)
            return false;

        double towardB = ((x - t.A.x) * (double)(t.C.z - t.A.z) - (t.C.x - t.A.x) * (double)(z - t.A.z)) / area;
        double towardC = ((t.B.x - t.A.x) * (double)(z - t.A.z) - (x - t.A.x) * (double)(t.B.z - t.A.z)) / area;
        double towardA = 1.0 - towardB - towardC;

        if (towardA < -1e-6 || towardB < -1e-6 || towardC < -1e-6)
            return false;

        height = (float)(towardA * t.A.y + towardB * t.B.y + towardC * t.C.y);

        return true;
    }

    static Vector2? ParseViewer(string[] args)
    {
        int index = Array.IndexOf(args, "--viewer");

        if (index < 0 || index + 2 >= args.Length)
            return null;

        return new Vector2(float.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(args[index + 2], System.Globalization.CultureInfo.InvariantCulture));
    }
}
