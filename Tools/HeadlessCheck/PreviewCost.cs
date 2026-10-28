using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

static class PreviewCost
{
    public static void Run(string[] args)
    {
        const string CONTENT = "../../Assets/_Dustborn/Content/Configs/World";
        const string GENERATED = "../../Assets/_Dustborn/Generated";
        var config = AssetReader.Load<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");
        var settings = AssetReader.Load<WorldBuildSettings>($"{CONTENT}/WorldBuildSettings.asset");
        var voxels = AssetReader.Load<VoxelConfig>($"{CONTENT}/VoxelConfig.asset");
        Vector2 viewer = PreviewCenter($"{CONTENT}/WorldBuildSettings.asset");
        bool skipRoads = Array.IndexOf(args, "--no-road-mesh") >= 0;
        bool skipShore = Array.IndexOf(args, "--no-shore") >= 0;

        var clock = Stopwatch.StartNew();
        HeightMap map = HeightMap.FromRaw16(File.ReadAllBytes($"{GENERATED}/HeightMap.bytes"), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
        WaterMap water = WaterMapFormat.Read(File.ReadAllBytes($"{GENERATED}/WaterMap.bytes"));
        (List<Road> regional, List<Road> streets, _) = RoadAudit.Read($"{GENERATED}/RoadNetwork.asset");
        var paved = new List<Road>(regional);
        paved.AddRange(streets);
        Console.WriteLine($"load: {clock.Elapsed.TotalSeconds:0.00} s, {paved.Count} roads, {water.Crossings.Count} crossings, viewer ({viewer.x:0}, {viewer.y:0})");

        clock.Restart();
        using var field = new VoxelDensityField(map, voxels, 0f);
        var plan = new VoxelStreamPlan(voxels, settings.LodCount, settings.NearDistance, config.WorldSize, water);
        var keys = new List<VoxelColumnKey>();
        plan.Around(viewer, keys);
        Console.WriteLine($"plan: {keys.Count} columns, {clock.Elapsed.TotalSeconds:0.00} s");

        if (!skipShore)
        {
            clock.Restart();
            VoxelShorePlan.Refine(field.Sampler, plan, viewer, voxels.ChunkSize, water);
            plan.Around(viewer, keys);
            Console.WriteLine($"shore refine: {keys.Count} columns, fingerprint {Fingerprint(keys):X16}, {clock.Elapsed.TotalSeconds:0.00} s");

            if (Array.IndexOf(args, "--shore-only") >= 0)
                return;
        }

        clock.Restart();
        var roadSurface = skipRoads ? null : new RoadSurfaceMesh(paved, water);
        Console.WriteLine($"road surface index: {clock.Elapsed.TotalSeconds:0.00} s");

        using var mesher = new VoxelChunkMesher(voxels, field);
        using var mesh = new VoxelMesh();
        var meshClock = new Stopwatch();
        var roadClock = new Stopwatch();
        long triangles = 0, roadTriangles = 0;
        int chunks = 0, visited = 0;
        double slowest = 0;
        var perLod = new double[settings.LodCount];
        var perLodTriangles = new long[settings.LodCount];

        foreach (VoxelColumnKey key in keys)
        {
            float size = plan.ChunkMetres(key.Lod);
            float step = plan.VoxelSize(key.Lod);
            field.ChunkRange(key, size, step, out int low, out int high);

            for (int y = low; y <= high; y++)
            {
                meshClock.Start();
                mesher.Mesh(key.Lod, key.X, y, key.Z, mesh, key.Seams, key.Morph);
                meshClock.Stop();

                if (mesh.IsEmpty)
                    continue;

                chunks++;
                triangles += mesh.TriangleCount;

                if (roadSurface == null)
                    continue;

                long before = roadClock.ElapsedTicks;
                roadClock.Start();
                RoadSurfaceMesh.Geometry geometry = roadSurface.Touches(key, size) ? roadSurface.Build(mesh) : new RoadSurfaceMesh.Geometry();
                roadClock.Stop();
                roadTriangles += geometry.Triangles.Count / 3;
                double spent = (roadClock.ElapsedTicks - before) / (double)Stopwatch.Frequency;
                perLod[key.Lod] += spent;
                perLodTriangles[key.Lod] += geometry.Triangles.Count / 3;

                slowest = Math.Max(slowest, spent);

                if (spent > 1.0)
                    Console.WriteLine($"  slow chunk {key} y {y}: {spent:0.00} s, {mesh.TriangleCount} source triangles, {geometry.Triangles.Count / 3} road triangles");
            }

            if (++visited % 250 == 0)
                Console.WriteLine($"  {visited}/{keys.Count} columns: meshing {meshClock.Elapsed.TotalSeconds:0.0} s, road {roadClock.Elapsed.TotalSeconds:0.0} s");
        }

        Console.WriteLine($"meshing: {chunks} chunks, {triangles} triangles, {meshClock.Elapsed.TotalSeconds:0.00} s");
        Console.WriteLine($"road surface: {roadTriangles} triangles, {roadClock.Elapsed.TotalSeconds:0.00} s, slowest chunk {slowest * 1000:0.0} ms");

        for (int lod = 0; lod < perLod.Length; lod++)
            Console.WriteLine($"  lod {lod}: {perLod[lod]:0.00} s, {perLodTriangles[lod]} triangles");

        clock.Restart();
        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        Console.WriteLine($"water meshes: {parts.Count} parts, {clock.Elapsed.TotalSeconds:0.00} s");
    }

    private static ulong Fingerprint(List<VoxelColumnKey> keys)
    {
        ulong hash = 1469598103934665603UL;

        foreach (VoxelColumnKey key in keys)
        {
            foreach (int value in new[] { key.X, key.Z, key.Lod, key.Seams, key.Morph })
            {
                hash ^= (uint)value;
                hash *= 1099511628211UL;
            }
        }

        return hash;
    }

    private static Vector2 PreviewCenter(string path)
    {
        foreach (string line in File.ReadAllLines(path))
        {
            if (!line.Contains("PreviewCenter"))
                continue;

            string[] parts = line.Substring(line.IndexOf('{') + 1).TrimEnd('}').Split(',');
            float x = float.Parse(parts[0].Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
            float y = float.Parse(parts[1].Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
            return new Vector2(x, y);
        }

        return new Vector2(1024f, 1024f);
    }
}
