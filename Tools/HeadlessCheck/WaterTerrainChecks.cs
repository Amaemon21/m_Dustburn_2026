using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

static class WaterTerrainChecks
{
    private const float GRID = 8f;
    private const float TOLERANCE = 0.05f;

    private readonly struct Tri
    {
        public readonly Vector3 A, B, C;

        public Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            A = a;
            B = b;
            C = c;
        }

        public bool Height(float x, float z, out float height)
        {
            float area = (B.z - A.z) * (C.x - A.x) - (B.x - A.x) * (C.z - A.z);
            height = float.NegativeInfinity;

            if (Mathf.Abs(area) < 1e-6f)
                return false;

            float u = ((z - A.z) * (C.x - A.x) - (x - A.x) * (C.z - A.z)) / area;
            float v = ((B.z - A.z) * (x - A.x) - (B.x - A.x) * (z - A.z)) / area;

            if (u < -1e-4f || v < -1e-4f || u + v > 1.0001f)
                return false;

            height = A.y + u * (B.y - A.y) + v * (C.y - A.y);
            return true;
        }
    }

    public static void Run(string[] args)
    {
        const string CONTENT = "../../Assets/_Dustborn/Content/Configs/World";
        var config = AssetReader.Load<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");
        var settings = AssetReader.Load<WorldBuildSettings>($"{CONTENT}/WorldBuildSettings.asset");
        var voxels = AssetReader.Load<VoxelConfig>($"{CONTENT}/VoxelConfig.asset");
        var viewer = new Vector2(4223.66f, 2824.0847f);
        int at = Array.IndexOf(args, "--viewer");

        if (at >= 0)
            viewer = new Vector2(float.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[at + 2], System.Globalization.CultureInfo.InvariantCulture));

        string name = "Lake 1 1";
        at = Array.IndexOf(args, "--part");

        if (at >= 0)
            name = args[at + 1];

        HeightMap map = HeightMap.FromRaw16(File.ReadAllBytes("../../Assets/_Dustborn/Generated/HeightMap.bytes"), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
        WaterMap water = WaterMapFormat.Read(File.ReadAllBytes("../../Assets/_Dustborn/Generated/WaterMap.bytes"));
        using var field = new VoxelDensityField(map, voxels, 0f);
        int lodCount = Array.IndexOf(args, "--fine") >= 0 ? 1 : settings.LodCount;
        var plan = new VoxelStreamPlan(voxels, lodCount, settings.NearDistance, config.WorldSize, water);
        var keys = new List<VoxelColumnKey>();
        plan.Around(viewer, keys);
        var probe = new VoxelSurfaceProbe(field.Sampler, plan, viewer, voxels.ChunkSize);

        if (Array.IndexOf(args, "--unrefined") < 0 && lodCount > 1)
        {
            probe = VoxelShorePlan.Refine(field.Sampler, plan, viewer, voxels.ChunkSize, water);
            plan.Around(viewer, keys);
        }
        var budget = new VoxelBudget();

        foreach (VoxelColumnKey key in keys)
        {
            float size = plan.ChunkMetres(key.Lod);
            budget += VoxelBudget.Estimate(plan.VoxelSize(key.Lod), size * size, true);
        }

        Console.WriteLine($"Terrain plan: {keys.Count} columns, {budget.Describe()}");
        if (lodCount > 1)
            WorldBuildBudget.ValidatePlan(plan, viewer, true, settings.MemoryBudget);

        Console.WriteLine($"Baked world: seed {config.Seed}, viewer ({viewer.x:0.00}, {viewer.y:0.00})");
        int failures = 0, checkedParts = 0, totalProbes = 0, totalLodExposed = 0, totalExposed = 0;
        bool analytic = Array.IndexOf(args, "--analytic") >= 0;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        int triangles = 0;

        foreach (WaterMeshPart part in parts)
            triangles += part.Triangles.Count / 3;

        Console.WriteLine($"Water meshes: {triangles} triangles, {clock.Elapsed.TotalSeconds:0.00} s");

        foreach (WaterMeshPart part in parts)
        {
            if (part.Kind == WaterKind.River || name != "all" && part.Name != name)
                continue;

            checkedParts++;
            var vertices = new List<Vector3>(part.Vertices);
            var uv = new List<Vector2>(part.Uv);
            var indices = new List<int>(part.Triangles);
            int before = vertices.Count;
            WaterSkirt.Append(water, vertices, uv, indices);
            int columns = 0;
            var terrain = analytic ? null : Terrain(field, voxels, keys, part, out columns);
            int probes = 0, exposed = 0, missing = 0, lodExposed = 0;
            float worst = float.NegativeInfinity, probeError = 0f;
            Vector3 worstAt = Vector3.zero;

            for (int v = before; v < vertices.Count; v += 4)
            {
                Vector3 a = vertices[v], b = vertices[v + 1];
                float dx = b.x - a.x, dz = b.z - a.z;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dz * dz) / 0.5f));

                for (int k = 0; k <= steps; k++)
                {
                    Vector3 p = a + (b - a) * (k / (float)steps);
                    probes++;
                    float ground = analytic ? probe.MeshHeight(p.x, p.z) : Height(terrain, p.x, p.z);

                    if (float.IsNegativeInfinity(ground))
                    {
                        missing++;
                        continue;
                    }

                    float gap = p.y - ground;
                    probeError = Mathf.Max(probeError, Mathf.Abs(probe.MeshHeight(p.x, p.z) - ground));

                    if (gap > TOLERANCE)
                        exposed++;

                    float fineGap = Mathf.Max(0f, p.y - probe.FineHeight(p.x, p.z));

                    if (gap > fineGap + TOLERANCE)
                        lodExposed++;

                    if (gap <= worst)
                        continue;

                    worst = gap;
                    worstAt = p;
                }
            }

            Console.WriteLine($"{part.Name}: {columns} terrain columns, {probes} shore probes, {exposed} exposed ({lodExposed} from LOD), {missing} missing, gap {worst:0.000} m at ({worstAt.x:0.00}, {worstAt.y:0.00}, {worstAt.z:0.00}), LOD {plan.LodAt(viewer, new Vector2(worstAt.x, worstAt.z))}, probe error {probeError:0.000000} m");
            if (exposed > 0)
            {
                WaterSample sample = water.Sample(worstAt.x, worstAt.z);
                Console.WriteLine($"  Source ground {map.SampleWorldSmooth(worstAt.x, worstAt.z):0.000}, water {sample.Kind} at {sample.Surface:0.000}, cell {water.CellIndex(worstAt.x, worstAt.z)}, detail {water.DetailSlot[water.CellIndex(worstAt.x, worstAt.z)]}");
            }
            failures += (name == "all" ? lodExposed : exposed) + missing + (probeError > 0.002f ? 1 : 0);
            totalProbes += probes;
            totalLodExposed += lodExposed;
            totalExposed += exposed;
        }

        if (checkedParts == 0)
            throw new InvalidOperationException($"No water mesh named {name}");

        Console.WriteLine($"Shore check: {checkedParts} mesh parts, {totalProbes} probes, {totalLodExposed} LOD gaps, {totalExposed} total gaps, {failures} failures");

        if (failures > 0)
            Environment.ExitCode = 1;
    }

    private static Dictionary<(int, int), List<Tri>> Terrain(VoxelDensityField field, VoxelConfig voxels, List<VoxelColumnKey> keys, WaterMeshPart part, out int columns)
    {
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;

        foreach (Vector3 vertex in part.Vertices)
        {
            minX = Mathf.Min(minX, vertex.x);
            maxX = Mathf.Max(maxX, vertex.x);
            minZ = Mathf.Min(minZ, vertex.z);
            maxZ = Mathf.Max(maxZ, vertex.z);
        }

        columns = 0;
        var terrain = new Dictionary<(int, int), List<Tri>>();
        using var mesher = new VoxelChunkMesher(voxels, field);
        using var mesh = new VoxelMesh();

        foreach (VoxelColumnKey key in keys)
        {
            float size = voxels.ChunkMetres * (1 << key.Lod);

            if (key.X * size > maxX + 8f || (key.X + 1) * size < minX - 8f || key.Z * size > maxZ + 8f || (key.Z + 1) * size < minZ - 8f)
                continue;

            columns++;
            float low = float.MaxValue, high = float.MinValue;
            float step = voxels.VoxelSize * (1 << key.Lod);

            for (float z = key.Z * size - step; z <= (key.Z + 1) * size; z += step)
                for (float x = key.X * size - step; x <= (key.X + 1) * size; x += step)
                {
                    float height = field.Surface(x, z);
                    low = Mathf.Min(low, height);
                    high = Mathf.Max(high, height);
                }

            for (int y = Mathf.FloorToInt(low / size) - 1; y <= Mathf.FloorToInt(high / size) + 1; y++)
            {
                mesher.Mesh(key.Lod, key.X, y, key.Z, mesh, key.Seams, key.Morph);

                for (int t = 0; t < mesh.Triangles.Length; t += 3)
                {
                    var tri = new Tri(mesh.Vertices[mesh.Triangles[t]], mesh.Vertices[mesh.Triangles[t + 1]], mesh.Vertices[mesh.Triangles[t + 2]]);
                    int x0 = Mathf.FloorToInt(Mathf.Min(tri.A.x, Mathf.Min(tri.B.x, tri.C.x)) / GRID);
                    int x1 = Mathf.FloorToInt(Mathf.Max(tri.A.x, Mathf.Max(tri.B.x, tri.C.x)) / GRID);
                    int z0 = Mathf.FloorToInt(Mathf.Min(tri.A.z, Mathf.Min(tri.B.z, tri.C.z)) / GRID);
                    int z1 = Mathf.FloorToInt(Mathf.Max(tri.A.z, Mathf.Max(tri.B.z, tri.C.z)) / GRID);

                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            if (!terrain.TryGetValue((x, z), out List<Tri> list))
                                terrain[(x, z)] = list = new List<Tri>();

                            list.Add(tri);
                        }
                }
            }
        }

        return terrain;
    }

    private static float Height(Dictionary<(int, int), List<Tri>> terrain, float x, float z)
    {
        float highest = float.NegativeInfinity;

        if (!terrain.TryGetValue((Mathf.FloorToInt(x / GRID), Mathf.FloorToInt(z / GRID)), out List<Tri> list))
            return highest;

        foreach (Tri tri in list)
            if (tri.Height(x, z, out float height))
                highest = Mathf.Max(highest, height);

        return highest;
    }
}
