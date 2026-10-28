using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class VoxelShorePlan
{
    public const float HEIGHT_TOLERANCE = 0.04f;
    private const float SAMPLE_STEP = 0.5f;

    public static VoxelSurfaceProbe Refine(VoxelDensitySampler sampler, VoxelStreamPlan plan, Vector2 center, int chunkSize, WaterMap water)
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.ShorePlan);

        if (water == null || plan.LodCount <= 1)
            return new VoxelSurfaceProbe(sampler, plan, center, chunkSize);

        return Refine(sampler, plan, center, chunkSize, Samples(water));
    }

    public static VoxelSurfaceProbe Refine(VoxelDensitySampler sampler, VoxelStreamPlan plan, Vector2 center, int chunkSize, List<Vector3> samples)
    {
        var probe = new VoxelSurfaceProbe(sampler, plan, center, chunkSize);

        if (samples == null || samples.Count == 0 || plan.LodCount <= 1)
            return probe;

        var fineGaps = new float[samples.Count];
        var cells = new VoxelSurfaceProbe.Cell[samples.Count];
        var exposed = new bool[samples.Count];
        VoxelSurfaceProbe fine = probe;

        Parallel.For(0, samples.Count, i =>
        {
            Vector3 p = samples[i];
            fineGaps[i] = Mathf.Max(0f, p.y - fine.FineHeight(p.x, p.z));
        });

        for (int pass = 0; pass < plan.LodCount + 3; pass++)
        {
            VoxelSurfaceProbe current = probe;
            bool first = pass == 0;

            Parallel.For(0, samples.Count, i =>
            {
                Vector3 p = samples[i];
                VoxelSurfaceProbe.Cell cell = current.Locate(p.x, p.z);

                if (!first && cell.Equals(cells[i]))
                    return;

                cells[i] = cell;
                exposed[i] = !(p.y - current.MeshHeight(cell, p.x, p.z) <= fineGaps[i] + HEIGHT_TOLERANCE);
            });

            bool changed = false;

            for (int i = 0; i < samples.Count; i++)
            {
                if (!exposed[i])
                    continue;

                Vector3 p = samples[i];
                int lod = cells[i].Lod;
                changed |= plan.ProtectShore(p.x, p.z, Mathf.Max(0, lod - 1), lod == 0 ? 1 : 0);
            }

            if (!changed)
                return probe;

            probe = new VoxelSurfaceProbe(sampler, plan, center, chunkSize);
        }

        return probe;
    }

    public static List<Vector3> Samples(WaterMap water)
    {
        var samples = new List<Vector3>();

        foreach (WaterMeshPart part in WaterMeshes.Build(water))
        {
            if (part.Kind == WaterKind.River)
                continue;

            var vertices = new List<Vector3>(part.Vertices);
            var uv = new List<Vector2>(part.Uv);
            var triangles = new List<int>(part.Triangles);
            int before = vertices.Count;
            WaterSkirt.Append(water, vertices, uv, triangles);

            for (int v = before; v < vertices.Count; v += 4)
            {
                Vector3 a = vertices[v], b = vertices[v + 1];
                float dx = b.x - a.x, dz = b.z - a.z;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dz * dz) / SAMPLE_STEP));

                for (int i = 0; i <= steps; i++)
                    samples.Add(a + (b - a) * (i / (float)steps));
            }
        }

        return samples;
    }
}
