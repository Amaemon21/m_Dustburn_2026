using System;
using System.Collections.Generic;
using UnityEngine;

static class RoadSurfaceMeshChecks
{
    private static readonly float CROSS_RADIUS = 3f / (Mathf.Sqrt(2f) - 1f);
    private static readonly float CROSS_FILLET = CROSS_RADIUS * CROSS_RADIUS * (1f - 4.5f * Mathf.Sin(10f * Mathf.Deg2Rad));

    public static void Run()
    {
        var roads = new List<Road>
        {
            new(new[] { new Vector2(-20f, 48f), new Vector2(48f, 48f), new Vector2(116f, 48f) }, 10f, RoadKind.Highway),
            new(new[] { new Vector2(48f, -20f), new Vector2(48f, 116f) }, 10f, RoadKind.Arterial),
            new(new[] { new Vector2(10f, -20f), new Vector2(10f, 116f) }, 4f, RoadKind.DirtAccess)
        };

        foreach (int cells in new[] { 1, 3, 12, 48 })
        {
            using VoxelMesh source = Plane(cells);
            var planner = new RoadSurfaceMesh(roads);
            RoadSurfaceMesh.Geometry geometry = planner.Build(source);
            Require(Math.Abs(Area(geometry) - 1820f - 4f * CROSS_FILLET) < 0.05f, $"The crossing must have exactly the union area plus four fillets, without duplicated pavement: {Area(geometry):0.000} m2 at {cells} cells.");
            Require(geometry.Vertices.Count == geometry.Uv.Count && geometry.Vertices.Count == geometry.Tangents.Count, "Every vertex must carry surface coordinates and a tangent.");

            foreach (Vector3 point in geometry.Vertices)
                Require(Math.Abs(point.y - Height(point.x, point.z)) < 0.0001f, "Road vertices must be on the original terrain triangle plane.");

            foreach (Vector2 profile in geometry.Profile)
                Require(profile.x == 5f && (profile.y == 0f || profile.y == 2f || profile.y == 3f), "Widths and marking styles must come from the owner road or its fillet.");

            var random = new System.Random(620);

            for (int sample = 0; sample < 1000; sample++)
            {
                var point = new Vector2((float)random.NextDouble() * 96f, (float)random.NextDouble() * 96f);
                int count = Coverage(geometry, point, out Vector3 marking);
                bool paved = Math.Abs(point.x - 48f) < 5f || Math.Abs(point.y - 48f) < 5f;
                Vector2 corner = new(Math.Abs(point.x - 48f) - 5f, Math.Abs(point.y - 48f) - 5f);
                float arc = (corner - new Vector2(CROSS_RADIUS, CROSS_RADIUS)).magnitude;

                if (corner.x > 0f && corner.y > 0f && corner.x < CROSS_RADIUS && corner.y < CROSS_RADIUS && Math.Abs(arc - CROSS_RADIUS) < 0.05f)
                    continue;

                paved |= corner.x > 0f && corner.y > 0f && corner.x < CROSS_RADIUS && corner.y < CROSS_RADIUS && arc > CROSS_RADIUS;
                Require(count == (paved ? 1 : 0), "Crossings and segment caps must have neither holes nor doubled triangles.");

                if (Math.Abs(point.x - 48f) < 3f && Math.Abs(point.y - 48f) < 3f)
                    Require(marking.x < 0.02f, "Markings must be suppressed through a junction.");
            }

            Require(planner.Sample(new Vector2(48.5f, 47f), out Vector2 owner, out _) && Math.Abs(owner.x + 0.2f) < 0.001f, "The highway must own the crossing it shares with an arterial.");
            RoadSurfaceMesh.Geometry repeat = planner.Build(source);
            Require(repeat.Vertices.Count == geometry.Vertices.Count && repeat.Triangles.Count == geometry.Triangles.Count, "Repeat builds must be deterministic.");
            CheckBridge(source, roads);
            Console.WriteLine($"road mesh {cells}x{cells}: {geometry.Triangles.Count / 3} triangles, union {Area(geometry):0.000} m2, coplanar and no overlap");
        }

        CheckCorner();
        CheckCurve();
        CheckTaper();
        CheckTee();
        CheckDenseNetworks();
        using (VoxelMesh preview = Plane(12))
            RoadSurfacePreview.Write(new RoadSurfaceMesh(roads).Build(preview), "road_surface_preview.png");
        Console.WriteLine("Road surface mesh checks passed: terrain LOD, coplanarity, crossing union, styles, dirt exclusion, bridge holes, curved joins, continuous curves, tapered kind changes, tee ownership, dense networks and repeat builds.");
    }

    private static void CheckDenseNetworks()
    {
        var random = new System.Random(4127);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var errors = new List<float>();
        int samples = 0, triangles = 0;

        for (int world = 0; world < 24; world++)
        {
            List<Road> roads = DenseNetwork(random);
            WaterMap water = null;

            if (world % 3 == 0)
            {
                water = new WaterMap(12, 8f, 96f, 0f);
                water.Crossings.Add(new WaterCrossing
                {
                    Center = new Vector2(20f + (float)random.NextDouble() * 56f, 20f + (float)random.NextDouble() * 56f),
                    Direction = new Vector2(1f, 0f), Bridge = true, Span = 16f, RoadWidth = 10f
                });
            }

            var planner = new RoadSurfaceMesh(roads, water);

            foreach (int cells in new[] { 3, 12, 96 })
            {
                using VoxelMesh source = Plane(cells);
                RoadSurfaceMesh.Geometry geometry = planner.Build(source);
                triangles += geometry.Triangles.Count / 3;

                for (int sample = 0; sample < 300; sample++)
                {
                    var point = new Vector2((float)random.NextDouble() * 96f, (float)random.NextDouble() * 96f);

                    if (!RoadSurfaceReference.Expected(planner, point, out bool covered, out Vector2 uv, out Vector3 marking))
                        continue;

                    float gap = RoadSurfaceReference.CorridorGap(roads, point);
                    bool fillet = planner.Sample(point, out _, out _, out Vector2 profile) && profile.y == 0f;
                    Require(!covered || gap < 0.07f || (fillet && gap < 1f), $"Dense network {world}: pavement at ({point.x:0.000}, {point.y:0.000}) lies {gap:0.000} half widths outside every road.");
                    Require(covered || gap > -0.02f || WaterCrossings.OverSpan(water, point), $"Dense network {world}: a hole at ({point.x:0.000}, {point.y:0.000}), {-gap:0.000} half widths inside a road.");
                    int count = RoadSurfaceReference.Locate(geometry.Vertices, geometry.Triangles, geometry.Uv, geometry.Markings, point, out Vector2 actualUv, out Vector3 actualMarking);

                    if (count < 0)
                        continue;

                    samples++;

                    if (count != (covered ? 1 : 0))
                        RoadSurfaceDebug.Dump(geometry.Vertices, geometry.Triangles, geometry.Uv, point);

                    Require(count == (covered ? 1 : 0), $"Dense network {world} at {cells} cells: {count} surface layers at ({point.x:0.000}, {point.y:0.000}), expected {(covered ? 1 : 0)}.");

                    if (!covered)
                        continue;

                    Require(Math.Abs(uv.x - actualUv.x) < 0.01f && Math.Abs(uv.y - actualUv.y) < 0.05f, $"Dense network {world}: the pavement at ({point.x:0.000}, {point.y:0.000}) has uv ({actualUv.x:0.0000}, {actualUv.y:0.0000}), expected ({uv.x:0.0000}, {uv.y:0.0000}).");
                    Vector3 error = marking - actualMarking;
                    errors.Add(Math.Max(Math.Abs(error.x), Math.Max(Math.Abs(error.y), Math.Abs(error.z))));
                }
            }
        }

        errors.Sort();
        float p99 = errors[(int)(errors.Count * 0.99f)];
        Require(p99 < 0.06f, $"Junction marking fades are sampled too coarsely: p99 error {p99:0.000}.");
        Require(clock.Elapsed.TotalSeconds < 60d, $"Dense networks took {clock.Elapsed.TotalSeconds:0.0} s: the surface clipper is fragmenting again.");
        Console.WriteLine($"road mesh dense networks: {samples} samples, {triangles} triangles, marking p99 error {p99:0.000}, {clock.Elapsed.TotalSeconds:0.00} s");
    }

    private static List<Road> DenseNetwork(System.Random random)
    {
        var roads = new List<Road>();

        for (int line = 0; line < 4; line++)
        {
            float offset = 12f + line * 24f + (float)random.NextDouble() * 4f;
            roads.Add(new Road(new[] { new Vector2(-8f, offset), new Vector2(40f, offset + 1f), new Vector2(104f, offset) }, 6f, RoadKind.LocalStreet));
            roads.Add(new Road(new[] { new Vector2(offset, -8f), new Vector2(offset - 1f, 50f), new Vector2(offset, 104f) }, 6f, RoadKind.LocalStreet));
        }

        for (int r = 0; r < 6; r++)
        {
            var points = new List<Vector2>();
            var point = new Vector2((float)random.NextDouble() * 96f, (float)random.NextDouble() * 96f);
            float heading = (float)(random.NextDouble() * Math.PI * 2);

            for (int k = 0; k < 8 + random.Next(16); k++)
            {
                points.Add(point);
                heading += (float)(random.NextDouble() - 0.5) * 0.7f;
                point += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * (2f + (float)random.NextDouble() * 6f);
            }

            RoadKind kind = r % 3 == 0 ? RoadKind.Highway : r % 3 == 1 ? RoadKind.Arterial : RoadKind.DirtAccess;
            roads.Add(new Road(points.ToArray(), 6f + (float)random.NextDouble() * 4f, kind));
        }

        return roads;
    }

    private static void CheckBridge(VoxelMesh source, List<Road> roads)
    {
        var water = new WaterMap(12, 8f, 96f, 0f);
        water.Crossings.Add(new WaterCrossing
        {
            Center = new Vector2(24f, 48f), Direction = new Vector2(1f, 0f), Bridge = true, Span = 16f, RoadWidth = 10f
        });
        var geometry = new RoadSurfaceMesh(roads, water).Build(source);
        Require(Math.Abs(Area(geometry) - 1660f - 4f * CROSS_FILLET) < 0.05f, "A bridge span must remove its exact road area at every terrain LOD.");
        Require(Coverage(geometry, new Vector2(23.123f, 47.723f), out _) == 0, "Terrain pavement must not render beneath a bridge.");
    }

    private static void CheckCorner()
    {
        var points = new[] { new Vector2(4f, 30f), new Vector2(48f, 30f), new Vector2(48f, 84f) };
        var roads = new[] { new Road(points, 10f, RoadKind.Highway) };
        using VoxelMesh source = Plane(12);
        var geometry = new RoadSurfaceMesh(roads).Build(source);
        var random = new System.Random(29);

        for (int sample = 0; sample < 4000; sample++)
        {
            var point = new Vector2(34f + (float)random.NextDouble() * 28f, 16f + (float)random.NextDouble() * 28f);
            float x = Mathf.Clamp(point.x, 4f, 48f);
            float z = Mathf.Clamp(point.y, 30f, 84f);
            float first = (point - new Vector2(x, 30f)).sqrMagnitude;
            float second = (point - new Vector2(48f, z)).sqrMagnitude;
            int count = Coverage(geometry, point, out _);
            Require(count <= 1, "An acute corner must not stack surface triangles.");

            if (Mathf.Min(first, second) < 20f)
                Require(count == 1, "The carriageway must remain continuous through a bend.");
        }
    }

    private static void CheckCurve()
    {
        const float STEP = 0.02f;
        var points = new Vector2[15];

        for (int i = 0; i < points.Length; i++)
            points[i] = new Vector2(8f + 40f * Mathf.Sin(i * 0.15f), 48f - 40f * Mathf.Cos(i * 0.15f));

        var roads = new[] { new Road(points, 8f, RoadKind.Highway) };
        var planner = new RoadSurfaceMesh(roads);
        int samples = 0;

        for (float z = 2f; z < 76f; z += 0.5f)
        {
            for (float x = 2f; x < 56f; x += 0.5f)
            {
                var point = new Vector2(x, z);
                bool covered = planner.Sample(point, out Vector2 uv, out Vector3 marks);
                float gap = RoadSurfaceReference.CorridorGap(roads, point);
                Require(!covered || gap < 0.01f, $"A curve must not pave past its edge: ({x:0.00}, {z:0.00}) is {gap:0.000} half widths out.");
                bool cap = (point - points[0]).magnitude < 4.5f || (point - points[^1]).magnitude < 4.5f;
                Require(covered || cap || gap > -0.01f, $"A curve must not notch its edge at a joint: ({x:0.00}, {z:0.00}) is {-gap:0.000} half widths in.");

                if (!covered)
                    continue;

                samples++;
                Require(marks.x > 0.999f && marks.y > 0.999f && marks.z > 0.999f, "A lone road must carry no junction marks.");

                foreach (Vector2 offset in new[] { new Vector2(STEP, 0f), new Vector2(0f, STEP) })
                {
                    if (!planner.Sample(point + offset, out Vector2 next, out _))
                        continue;

                    Require(Math.Abs(next.x - uv.x) < STEP / 4f * 1.05f + 0.001f, $"The surface must not step sideways at a joint: u jumps {next.x - uv.x:0.0000} near ({x:0.00}, {z:0.00}).");
                    Require(Math.Abs(next.y - uv.y) < STEP * 1.15f + 0.002f, $"The surface must not step along at a joint: v jumps {next.y - uv.y:0.0000} near ({x:0.00}, {z:0.00}).");
                }
            }
        }

        foreach (int cells in new[] { 1, 12 })
        {
            using VoxelMesh source = Plane(cells);
            RoadSurfaceMesh.Geometry geometry = planner.Build(source);
            var random = new System.Random(77);

            for (int sample = 0; sample < 2000; sample++)
            {
                var point = new Vector2(2f + (float)random.NextDouble() * 54f, 2f + (float)random.NextDouble() * 74f);

                if (!RoadSurfaceReference.Expected(planner, point, out bool covered, out Vector2 uv, out _))
                    continue;

                int count = RoadSurfaceReference.Locate(geometry.Vertices, geometry.Triangles, geometry.Uv, geometry.Markings, point, out Vector2 actual, out _);

                if (count < 0)
                    continue;

                if (covered && count == 1 && Math.Abs(actual.y - uv.y) >= 0.1f)
                    RoadSurfaceDebug.Dump(geometry.Vertices, geometry.Triangles, geometry.Uv, point);

                Require(count == (covered ? 1 : 0), $"Curve at {cells} cells: {count} layers at ({point.x:0.000}, {point.y:0.000}).");
                Require(!covered || (Math.Abs(actual.x - uv.x) < 0.01f && Math.Abs(actual.y - uv.y) < 0.1f), $"Curve at {cells} cells: interpolated uv ({actual.x:0.0000}, {actual.y:0.0000}) against ({uv.x:0.0000}, {uv.y:0.0000}) at ({point.x:0.000}, {point.y:0.000}).");
            }
        }

        Console.WriteLine($"road mesh curve: {samples} samples, edge within 1% of the corridor, no uv steps at 8.6 degree joints");
    }

    private static void CheckTaper()
    {
        var roads = new[]
        {
            new Road(new[] { new Vector2(0f, 48f), new Vector2(48f, 48f) }, 8f, RoadKind.Arterial),
            new Road(new[] { new Vector2(96f, 48f), new Vector2(48f, 48f) }, 6f, RoadKind.LocalStreet)
        };
        var planner = new RoadSurfaceMesh(roads);

        foreach (Vector2 point in new[] { new Vector2(30f, 48f), new Vector2(48f, 48f), new Vector2(48.2f, 50.5f), new Vector2(60f, 46f) })
        {
            Require(planner.Sample(point, out _, out Vector3 marks), $"The joined roads must cover {point}.");
            Require(marks.x > 0.999f && marks.y > 0.999f && marks.z > 0.999f, $"A road continuing into another must not read as a junction at {point}: {marks}.");
        }

        Require(planner.Sample(new Vector2(42.8f, 51.95f), out _, out _), "The wider road keeps its width until the taper.");
        Require(planner.Sample(new Vector2(48f, 51.45f), out _, out _) && !planner.Sample(new Vector2(48f, 51.55f), out _, out _), "The joint sits halfway through the taper.");
        Require(!planner.Sample(new Vector2(53.2f, 51.05f), out _, out _), "The narrower road reaches its width after the taper.");

        for (float lateral = -3.4f; lateral <= 3.4f; lateral += 0.4f)
        {
            planner.Sample(new Vector2(47.99f, 48f + lateral), out Vector2 before, out _);
            planner.Sample(new Vector2(48.01f, 48f + lateral), out Vector2 after, out _);
            Require(Math.Abs(after.x - before.x) < 0.01f && Math.Abs(after.y - before.y - 0.02f) < 0.002f, $"The surface must run on through a kind change: {before} then {after}.");
        }

        using VoxelMesh source = Plane(12);
        RoadSurfaceMesh.Geometry geometry = planner.Build(source);

        for (int i = 0; i < geometry.Vertices.Count; i++)
        {
            float x = geometry.Vertices[i].x;
            Require(x > 47.99f || geometry.Profile[i].y == 2f, "An arterial keeps its marking style up to the joint.");
            Require(x < 48.01f || geometry.Profile[i].y == 1f, "A local street keeps its marking style past the joint.");
        }

        Console.WriteLine("road mesh kind change: one surface, tapered from 4 m to 3 m, no junction marks");
    }

    private static void CheckTee()
    {
        var roads = new[]
        {
            new Road(new[] { new Vector2(48f, -20f), new Vector2(48f, 48f) }, 6f, RoadKind.LocalStreet),
            new Road(new[] { new Vector2(-20f, 48f), new Vector2(116f, 48f) }, 8f, RoadKind.LocalStreet)
        };
        var planner = new RoadSurfaceMesh(roads);
        Require(planner.Sample(new Vector2(48.5f, 47f), out Vector2 owner, out Vector3 mouth) && Math.Abs(owner.x + 0.25f) < 0.001f, $"The through road must own the mouth of a tee: {owner}.");
        Require(mouth.z < 0.02f && mouth.y > 0.98f, $"Only the side facing the joining road loses its edge: {mouth}.");
        Require(planner.Sample(new Vector2(60f, 45f), out _, out Vector3 beyond) && beyond.z > 0.98f, $"The edge returns past the mouth: {beyond}.");
        Require(planner.Sample(new Vector2(48f, 30f), out _, out Vector3 side) && side.y > 0.98f && side.z > 0.98f, $"The joining road keeps both edges: {side}.");
        Require(planner.Sample(new Vector2(52f, 43f), out _, out _, out Vector2 fillet) && fillet.y == 0f, "A fillet must pave the corner between the two roads.");
        Require(!planner.Sample(new Vector2(53.5f, 41.5f), out _, out _), "A fillet must stop at its arc.");
        Require(planner.Sample(new Vector2(52f, 45f), out _, out Vector3 handed) && handed.z < 0.05f, $"The through road hands its edge to the fillet: {handed}.");
        Require(planner.Sample(new Vector2(57f, 45f), out _, out Vector3 kept) && kept.z > 0.98f, $"The through road keeps its edge past the fillet: {kept}.");
        Require(planner.Sample(new Vector2(50f, 42f), out _, out Vector3 fading) && fading.z < 0.6f, $"The joining road fades its edge into the fillet: {fading}.");
        Require(planner.Sample(new Vector2(50f, 36f), out _, out Vector3 before) && before.z > 0.98f, $"The joining road keeps its edge before the fillet: {before}.");
        CheckFilletSurface(planner);

        using VoxelMesh source = Plane(12);
        RoadSurfaceMesh.Geometry geometry = planner.Build(source);
        var random = new System.Random(11);

        for (int sample = 0; sample < 3000; sample++)
        {
            var point = new Vector2(30f + (float)random.NextDouble() * 36f, 30f + (float)random.NextDouble() * 26f);

            if (RoadSurfaceReference.Expected(planner, point, out bool covered, out _, out _) && RoadSurfaceReference.Locate(geometry.Vertices, geometry.Triangles, geometry.Uv, geometry.Markings, point, out _, out _) is int count && count >= 0)
                Require(count == (covered ? 1 : 0), $"Tee: {count} layers at ({point.x:0.000}, {point.y:0.000}).");
        }

        Console.WriteLine("road mesh tee: through road owns the mouth, edge suppressed on the joining side only, corners filleted");
    }

    private static void CheckFilletSurface(RoadSurfaceMesh planner)
    {
        const float STEP = 0.02f;

        for (float z = 38f; z < 45f; z += 0.25f)
        {
            for (float x = 50f; x < 57f; x += 0.25f)
            {
                var point = new Vector2(x, z);

                if (!planner.Sample(point, out Vector2 uv, out _, out Vector2 profile) || profile.y != 0f)
                    continue;

                Require(uv.x > 0.35f && uv.x <= 1.0001f, $"A fillet must stay between its gravel band and the clean centre: u {uv.x:0.000} at {point}.");

                foreach (Vector2 offset in new[] { new Vector2(STEP, 0f), new Vector2(0f, STEP) })
                {
                    if (!planner.Sample(point + offset, out Vector2 next, out _, out Vector2 nextProfile) || nextProfile.y != 0f)
                        continue;

                    Require(Math.Abs(next.x - uv.x) < STEP / 3f && Math.Abs(next.y - uv.y) < STEP * 3f, $"A fillet surface must be continuous: {uv} then {next} at {point}.");
                }
            }
        }
    }

    private static VoxelMesh Plane(int cells)
    {
        var mesh = new VoxelMesh();

        for (int z = 0; z <= cells; z++)
        {
            for (int x = 0; x <= cells; x++)
            {
                float px = x * 96f / cells;
                float pz = z * 96f / cells;
                mesh.Vertices.Add(new Vector3(px, Height(px, pz), pz));
                mesh.Normals.Add(new Vector3(-0.1f, 1f, -0.05f));
                mesh.Uv.Add(new Vector2(px, pz));
            }
        }

        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                int a = z * (cells + 1) + x;
                int b = a + 1;
                int c = a + cells + 1;
                int d = c + 1;
                mesh.Triangles.Add(a); mesh.Triangles.Add(c); mesh.Triangles.Add(b);
                mesh.Triangles.Add(b); mesh.Triangles.Add(c); mesh.Triangles.Add(d);
            }
        }

        return mesh;
    }

    private static float Area(RoadSurfaceMesh.Geometry geometry)
    {
        float area = 0f;

        for (int i = 0; i < geometry.Triangles.Count; i += 3)
        {
            Vector3 a = geometry.Vertices[geometry.Triangles[i]];
            Vector3 b = geometry.Vertices[geometry.Triangles[i + 1]];
            Vector3 c = geometry.Vertices[geometry.Triangles[i + 2]];
            area += Vector3.Cross(b - a, c - a).y * 0.5f;
        }

        return area;
    }

    private static int Coverage(RoadSurfaceMesh.Geometry geometry, Vector2 point, out Vector3 marking)
    {
        int count = 0;
        marking = Vector3.zero;

        for (int i = 0; i < geometry.Triangles.Count; i += 3)
        {
            int ia = geometry.Triangles[i];
            int ib = geometry.Triangles[i + 1];
            int ic = geometry.Triangles[i + 2];
            Vector3 av = geometry.Vertices[ia];
            Vector3 bv = geometry.Vertices[ib];
            Vector3 cv = geometry.Vertices[ic];
            var a = new Vector2(av.x, av.z);
            var b = new Vector2(bv.x, bv.z);
            var c = new Vector2(cv.x, cv.z);
            float area = Cross(b - a, c - a);

            if (Math.Abs(area) < 0.00001f)
                continue;

            float u = Cross(b - point, c - point) / area;
            float v = Cross(c - point, a - point) / area;
            float w = 1f - u - v;

            if (u < -0.000001f || v < -0.000001f || w < -0.000001f)
                continue;

            count++;
            marking = geometry.Markings[ia] * u + geometry.Markings[ib] * v + geometry.Markings[ic] * w;
        }

        return count;
    }

    private static float Height(float x, float z) => 10f + x * 0.1f + z * 0.05f;
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
