using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;

static class RuntimeStreamingChecks
{
    public static void Run()
    {
        GrassGeometry();
        SwapCoverage();
        Console.WriteLine("PASS: grass job geometry and incremental column swap coverage.");
    }

    private static void GrassGeometry()
    {
        const int COUNT = 257;
        using var instances = new NativeArray<DecorInstance>(COUNT, Allocator.Persistent);
        using var vertices = new NativeArray<GrassMeshVertex>(COUNT * GrassTuft.VERTICES, Allocator.Persistent);
        using var indices = new NativeArray<int>(COUNT * GrassTuft.PLANES * 6, Allocator.Persistent);
        var random = new System.Random(3941);
        var tint = new Color(0.7f, 0.9f, 0.6f, 0.8f);
        var origin = new Vector3(51f, 12f, -24f);
        var job = new GrassMeshJob { Instances = instances, Vertices = vertices, Indices = indices, Tint = tint, Origin = origin };

        for (int i = 0; i < COUNT; i++)
        {
            job.Instances[i] = new DecorInstance
            {
                Position = origin + new Vector3((float)random.NextDouble() * 32f, (float)random.NextDouble() * 12f, (float)random.NextDouble() * 32f),
                Rotation = (float)random.NextDouble() * 360f,
                Scale = 0.2f + (float)random.NextDouble() * 2f,
                Height = 0.1f + (float)random.NextDouble() * 2f,
                Shade = 0.5f + (float)random.NextDouble(),
                Slope = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f)
            };
            job.Execute(i);
        }

        float worst = 0f;

        for (int i = 0; i < COUNT; i++)
        {
            DecorInstance instance = instances[i];

            for (int plane = 0; plane < GrassTuft.PLANES; plane++)
            {
                int first = i * GrassTuft.VERTICES + plane * GrassTuft.CORNERS;

                for (int corner = 0; corner < GrassTuft.CORNERS; corner++)
                {
                    GrassMeshVertex vertex = vertices[first + corner];
                    Vector2 uv = GrassTuft.Corners[corner];
                    Vector3 offset = GrassTuft.Offset(instance.Rotation, plane, uv, new Vector2(instance.Scale, instance.Height), instance.Slope);
                    worst = Math.Max(worst, (vertex.Position - (instance.Position - origin + offset)).magnitude);
                    worst = Math.Max(worst, (vertex.Normal - GrassTuft.Normal(instance.Rotation, plane)).magnitude);
                    Vector3 tangent = GrassTuft.Tangent(instance.Rotation, plane);
                    worst = Math.Max(worst, Math.Abs(vertex.Tangent.x - tangent.x) + Math.Abs(vertex.Tangent.z - tangent.z));
                    Require((vertex.Root + offset).sqrMagnitude < 1e-8f, "Grass root channel changed.");
                    Require(vertex.Corner.x == uv.x && vertex.Corner.y == uv.y && vertex.Tangent.w == -1f, "Grass UV or tangent handedness changed.");
                    Require(Math.Abs(vertex.Color.r - tint.r * instance.Shade) < 1e-6f && vertex.Color.a == tint.a, "Grass tint changed.");
                }

                for (int triangle = 0; triangle < 6; triangle++)
                    Require(indices[i * GrassTuft.PLANES * 6 + plane * 6 + triangle] == first + GrassTuft.Quad[triangle], "Grass triangle winding changed.");
            }
        }

        Require(worst < 1e-4f, $"Grass job differs from the existing geometry by {worst} metres.");
        Require(Marshal.SizeOf<GrassMeshVertex>() == 76, "Grass vertex buffer stride changed.");
        Require(Marshal.OffsetOf<GrassMeshVertex>(nameof(GrassMeshVertex.Color)).ToInt32() == 40
            && Marshal.OffsetOf<GrassMeshVertex>(nameof(GrassMeshVertex.Corner)).ToInt32() == 56
            && Marshal.OffsetOf<GrassMeshVertex>(nameof(GrassMeshVertex.Root)).ToInt32() == 64, "Grass vertex attributes do not match the upload layout.");
        Console.WriteLine($"grass: {vertices.Length} vertices match the existing geometry, worst difference {worst:0.00000000} m");
    }

    private static void SwapCoverage()
    {
        var random = new System.Random(9103);
        List<VoxelColumnKey> before = Partition(random);
        int totalGroups = 0;

        for (int trial = 0; trial < 100; trial++)
        {
            List<VoxelColumnKey> after = Partition(random);
            var oldSet = new HashSet<VoxelColumnKey>(before);
            var newSet = new HashSet<VoxelColumnKey>(after);
            List<VoxelColumnKey> added = after.Where(key => !oldSet.Contains(key)).ToList();
            List<VoxelColumnKey> removed = before.Where(key => !newSet.Contains(key)).ToList();
            List<VoxelColumnSwap> swaps = VoxelColumnSwap.Plan(added, removed);

            Require(swaps.SelectMany(swap => swap.Added).Distinct().Count() == added.Count, "A new column was omitted or repeated.");
            Require(swaps.SelectMany(swap => swap.Removed).Distinct().Count() == removed.Count, "An old column was omitted or repeated.");
            var live = new HashSet<VoxelColumnKey>(before);

            foreach (VoxelColumnSwap swap in swaps)
            {
                foreach (VoxelColumnKey key in swap.Removed)
                    Require(live.Remove(key), "A retired column was not live.");
                foreach (VoxelColumnKey key in swap.Added)
                    Require(live.Add(key), "A replacement column was already live.");
                CheckCoverage(live);
                totalGroups++;
            }

            Require(live.SetEquals(newSet), "Incremental swaps did not reach the target plan.");
            before = after;
        }

        Console.WriteLine($"terrain: {totalGroups} incremental swaps across 100 plan changes preserve exact coverage");
    }

    private static List<VoxelColumnKey> Partition(System.Random random)
    {
        var result = new List<VoxelColumnKey>();
        Split(0, 0, 4, random, result);
        return result;
    }

    private static void Split(int x, int z, int lod, System.Random random, List<VoxelColumnKey> result)
    {
        if (lod == 0 || lod < 4 && random.NextDouble() < 0.55)
        {
            result.Add(new VoxelColumnKey(x, z, lod, random.Next(16), random.Next(16)));
            return;
        }

        for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
                Split(x * 2 + dx, z * 2 + dz, lod - 1, random, result);
    }

    private static void CheckCoverage(IEnumerable<VoxelColumnKey> live)
    {
        var cells = new int[16 * 16];

        foreach (VoxelColumnKey key in live)
        {
            int scale = 1 << key.Lod;
            for (int z = key.Z * scale; z < (key.Z + 1) * scale; z++)
                for (int x = key.X * scale; x < (key.X + 1) * scale; x++)
                    cells[z * 16 + x]++;
        }

        Require(cells.All(count => count == 1), "An incremental swap left a hole or overlapping terrain.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
