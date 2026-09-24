using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

static class WaterView
{
    private const int TOP_PIXELS = 768;
    private const int VIEW_WIDTH = 1280;
    private const int VIEW_HEIGHT = 720;
    private const float FOAM_DEPTH = 0.35f;
    private const float FIELD_OF_VIEW = 50f;

    public readonly struct Scene
    {
        public readonly string Name;
        public readonly Vector2 Center;
        public readonly float Size;

        public Scene(string name, Vector2 center, float size)
        {
            Name = name;
            Center = center;
            Size = size;
        }
    }

    public static void Render(string directory, HeightMap map, WaterMap water, IEnumerable<Scene> scenes, float lodDistance = 0f)
    {
        Directory.CreateDirectory(directory);

        VoxelDensityField field = null;
        var voxels = new VoxelConfig();

        if (lodDistance > 0f)
        {
            float lowest = float.MaxValue;

            foreach (float h in map.Heights)
                lowest = Mathf.Min(lowest, h);

            field = new VoxelDensityField(map, voxels, lowest * map.MaxHeight);
        }

        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>();

        foreach (WaterMeshPart part in parts)
        {
            for (int t = 0; t < part.Triangles.Count; t += 3)
                triangles.Add((part.Vertices[part.Triangles[t]], part.Vertices[part.Triangles[t + 1]], part.Vertices[part.Triangles[t + 2]]));
        }

        var lines = new List<string>();

        foreach (Scene scene in scenes)
        {
            lines.Add(FormattableString.Invariant($"{scene.Name} {scene.Center.x} {scene.Center.y} {scene.Size}"));

            List<(Vector3 A, Vector3 B, Vector3 C)> local = Clip(triangles, scene.Center, scene.Size * 0.75f);
            Func<float, float, float> ground = map.SampleWorldSmooth;
            string suffix = "";

            if (field != null)
            {
                var viewer = new Vector2(Mathf.Clamp(scene.Center.x + lodDistance, 0f, map.WorldSize), scene.Center.y);

                if (Mathf.Abs(viewer.x - scene.Center.x) < lodDistance * 0.5f)
                    viewer = new Vector2(Mathf.Clamp(scene.Center.x - lodDistance, 0f, map.WorldSize), scene.Center.y);

                var probe = new VoxelSurfaceProbe(field.Sampler, new VoxelStreamPlan(voxels, 6, 96f, map.WorldSize), viewer, voxels.ChunkSize);
                ground = probe.Height;
                suffix = $"_lod{probe.LodAt(scene.Center.x, scene.Center.y)}";
            }

            Png.Write(Path.Combine(directory, scene.Name + suffix + "_top.png"), TopDown(ground, local, scene), TOP_PIXELS, TOP_PIXELS);
            Png.Write(Path.Combine(directory, scene.Name + suffix + "_view.png"), Oblique(ground, local, scene), VIEW_WIDTH, VIEW_HEIGHT);
            Console.WriteLine($"  вид {scene.Name}{suffix}: центр ({scene.Center.x:0}, {scene.Center.y:0}), окно {scene.Size:0} м, {local.Count} треугольников воды");
        }

        field?.Dispose();
        File.WriteAllLines(Path.Combine(directory, "scenes.txt"), lines);
    }

    public static List<Scene> Read(string path)
    {
        var scenes = new List<Scene>();
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        foreach (string line in File.ReadAllLines(path))
        {
            string[] parts = line.Split(' ');

            if (parts.Length == 4)
                scenes.Add(new Scene(parts[0], new Vector2(float.Parse(parts[1], culture), float.Parse(parts[2], culture)), float.Parse(parts[3], culture)));
        }

        return scenes;
    }

    public static List<Scene> Pick(WaterMap water, HeightMap map)
    {
        var scenes = new List<Scene>();
        int largest = -1, smallest = -1, second = -1, steep = -1, flat = -1;
        float steepest = 0f, flattest = float.MaxValue;

        for (int body = 0; body < water.Bodies.Count; body++)
        {
            WaterBody data = water.Bodies[body];

            if (data.Area <= 0f)
                continue;

            if (largest < 0 || data.Area > water.Bodies[largest].Area)
                largest = body;

            if (data.Kind == WaterKind.Pond && (smallest < 0 || data.Area < water.Bodies[smallest].Area))
            {
                second = smallest;
                smallest = body;
            }
            else if (data.Kind == WaterKind.Pond && (second < 0 || data.Area < water.Bodies[second].Area))
            {
                second = body;
            }

            if (data.Kind != WaterKind.Lake)
                continue;

            float slope = ShoreSlope(water, map, body);

            if (slope > steepest)
            {
                steepest = slope;
                steep = body;
            }

            if (slope > 0f && slope < flattest)
            {
                flattest = slope;
                flat = body;
            }
        }

        if (largest >= 0)
            scenes.Add(new Scene("lake_largest", water.Bodies[largest].Center, Mathf.Clamp(Mathf.Sqrt(water.Bodies[largest].Area) * 1.8f, 256f, 1400f)));

        if (smallest >= 0)
            scenes.Add(new Scene("pond_smallest", water.Bodies[smallest].Center, 160f));

        if (second >= 0)
            scenes.Add(new Scene("pond_small", water.Bodies[second].Center, 160f));

        if (steep >= 0)
            scenes.Add(new Scene("lake_steep_shore", water.Bodies[steep].Center, Mathf.Clamp(Mathf.Sqrt(water.Bodies[steep].Area) * 1.6f, 200f, 900f)));

        if (flat >= 0)
            scenes.Add(new Scene("lake_flat_shore", water.Bodies[flat].Center, Mathf.Clamp(Mathf.Sqrt(water.Bodies[flat].Area) * 1.6f, 200f, 900f)));

        AddMouths(water, scenes);
        AddJunction(water, scenes);
        AddLongRiver(water, scenes);

        return scenes;
    }

    private static float ShoreSlope(WaterMap water, HeightMap map, int body)
    {
        float sum = 0f;
        int count = 0;
        float step = water.NodeStep * 2f;

        for (int d = 0; d < water.DetailCount; d++)
        {
            int cell = water.DetailCells[d];

            if (water.BodyIds[cell] != body)
                continue;

            Vector2 at = water.CellCenter(cell);
            float dx = map.SampleWorldSmooth(at.x + step, at.y) - map.SampleWorldSmooth(at.x - step, at.y);
            float dz = map.SampleWorldSmooth(at.x, at.y + step) - map.SampleWorldSmooth(at.x, at.y - step);

            sum += Mathf.Sqrt(dx * dx + dz * dz) / (2f * step);
            count++;
        }

        return count < 12 ? 0f : sum / count;
    }

    private static void AddMouths(WaterMap water, List<Scene> scenes)
    {
        bool lake = false, sea = false;

        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;

            for (int i = 0; i + 1 < points.Count && !(lake && sea); i++)
            {
                if (!water.RiverOpen(points[i]) || water.RiverOpen(points[i + 1]))
                    continue;

                Vector2 at = points[i + 1].Position;
                WaterSample sample = water.Sample(at.x, at.y);

                if (!lake && (sample.Kind == WaterKind.Lake || sample.Kind == WaterKind.Pond) && points[i].Width > 6f)
                {
                    scenes.Add(new Scene("mouth_lake", at, 192f));
                    lake = true;
                }
                else if (!sea && sample.Kind == WaterKind.Sea && points[i].Width > 6f)
                {
                    scenes.Add(new Scene("mouth_sea", at, 256f));
                    sea = true;
                }
            }
        }
    }

    private static void AddJunction(WaterMap water, List<Scene> scenes)
    {
        float widest = 0f;
        Vector2 at = default;

        for (int r = 1; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;
            RiverPoint end = points[^1];

            if (end.Submerged && !water.RiverOpen(points[Mathf.Max(0, points.Count - 6)]) || end.Width <= widest)
                continue;

            if (water.Sample(end.Position.x, end.Position.y).Kind != WaterKind.River)
                continue;

            widest = end.Width;
            at = end.Position;
        }

        if (widest > 0f)
            scenes.Add(new Scene("tributary_junction", at, 192f));
    }

    private static void AddLongRiver(WaterMap water, List<Scene> scenes)
    {
        RiverPath longest = null;

        foreach (RiverPath river in water.Rivers)
        {
            if (longest == null || river.Length > longest.Length)
                longest = river;
        }

        if (longest == null)
            return;

        int middle = longest.Points.Count / 2;

        for (int k = 0; k < longest.Points.Count / 2; k++)
        {
            int i = middle + (k % 2 == 0 ? k / 2 : -k / 2 - 1);

            if (i < 0 || i >= longest.Points.Count || !water.RiverOpen(longest.Points[i]))
                continue;

            scenes.Add(new Scene("river_long", longest.Points[i].Position, 384f));
            return;
        }
    }

    private static List<(Vector3 A, Vector3 B, Vector3 C)> Clip(List<(Vector3 A, Vector3 B, Vector3 C)> triangles, Vector2 center, float reach)
    {
        var local = new List<(Vector3 A, Vector3 B, Vector3 C)>();

        foreach ((Vector3 a, Vector3 b, Vector3 c) in triangles)
        {
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));

            if (maxX < center.x - reach || minX > center.x + reach || maxZ < center.y - reach || minZ > center.y + reach)
                continue;

            local.Add((a, b, c));
        }

        return local;
    }

    private static (float R, float G, float B) Ground(Func<float, float, float> map, float x, float z, float step)
    {
        float here = map(x, z);
        float dx = (map(x + step, z) - map(x - step, z)) / (2f * step);
        float dz = (map(x, z + step) - map(x, z - step)) / (2f * step);

        var normal = Norm(new Vector3(-dx, 1f, -dz));
        float light = Mathf.Clamp01(Vector3.Dot(normal, Norm(new Vector3(-0.45f, 0.8f, -0.4f)))) * 0.75f + 0.25f;
        float slope = Mathf.Sqrt(dx * dx + dz * dz);
        float rock = Mathf.Clamp01((slope - 0.45f) * 3f);

        return (light * Mathf.Lerp(96f, 128f, rock), light * Mathf.Lerp(132f, 118f, rock), light * Mathf.Lerp(58f, 104f, rock));
    }

    private static (float R, float G, float B) Water((float R, float G, float B) ground, float depth)
    {
        float t = Mathf.Clamp01(depth / 3f);
        float alpha = Mathf.Lerp(0.45f, 0.92f, t);
        float r = Mathf.Lerp(ground.R, Mathf.Lerp(40f, 20f, t), alpha);
        float g = Mathf.Lerp(ground.G, Mathf.Lerp(150f, 70f, t), alpha);
        float b = Mathf.Lerp(ground.B, Mathf.Lerp(160f, 110f, t), alpha);

        float foam = 1f - Mathf.Clamp01(depth / FOAM_DEPTH);
        foam *= foam;

        return (Mathf.Lerp(r, 235f, foam * 0.85f), Mathf.Lerp(g, 238f, foam * 0.85f), Mathf.Lerp(b, 228f, foam * 0.85f));
    }

    private static byte[] TopDown(Func<float, float, float> map, List<(Vector3 A, Vector3 B, Vector3 C)> triangles, Scene scene)
    {
        int size = TOP_PIXELS;
        float step = scene.Size / size;
        float x0 = scene.Center.x - scene.Size * 0.5f;
        float z0 = scene.Center.y - scene.Size * 0.5f;
        var top = new float[size * size];

        for (int i = 0; i < top.Length; i++)
            top[i] = float.NegativeInfinity;

        foreach ((Vector3 a, Vector3 b, Vector3 c) in triangles)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - x0) / step));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - x0) / step));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - z0) / step));
            int maxZ = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - z0) / step));

            for (int pz = minZ; pz <= maxZ; pz++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float x = x0 + (px + 0.5f) * step, z = z0 + (pz + 0.5f) * step;

                    if (Barycentric(a, b, c, x, z, out float y))
                        top[pz * size + px] = Mathf.Max(top[pz * size + px], y);
                }
            }
        }

        var pixels = new byte[size * size * 3];

        Parallel.For(0, size, pz =>
        {
            for (int px = 0; px < size; px++)
            {
                float x = x0 + (px + 0.5f) * step, z = z0 + (pz + 0.5f) * step;
                float ground = map(x, z);
                (float r, float g, float b) color = Ground(map, x, z, Mathf.Max(step, 0.5f));
                float water = top[pz * size + px];

                if (water > ground)
                    color = Water(color, water - ground);

                int i = ((size - 1 - pz) * size + px) * 3;
                pixels[i] = (byte)Mathf.Clamp(color.r, 0f, 255f);
                pixels[i + 1] = (byte)Mathf.Clamp(color.g, 0f, 255f);
                pixels[i + 2] = (byte)Mathf.Clamp(color.b, 0f, 255f);
            }
        });

        return pixels;
    }

    private static bool Barycentric(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float y)
    {
        y = 0f;
        float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);

        if (Mathf.Abs(d) < 1e-9f)
            return false;

        float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
        float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
        float w = 1f - u - v;

        if (u < -1e-5f || v < -1e-5f || w < -1e-5f)
            return false;

        y = u * a.y + v * b.y + w * c.y;
        return true;
    }

    private sealed class Camera
    {
        public Vector3 Position, Right, Up, Forward;
        public float Focal;

        public Vector3 View(Vector3 world)
        {
            Vector3 d = world - Position;
            return new Vector3(Vector3.Dot(d, Right), Vector3.Dot(d, Up), Vector3.Dot(d, Forward));
        }

        public Vector2 Screen(Vector3 view)
        {
            return new Vector2(VIEW_WIDTH * 0.5f + Focal * view.x / view.z, VIEW_HEIGHT * 0.5f - Focal * view.y / view.z);
        }
    }

    private static byte[] Oblique(Func<float, float, float> map, List<(Vector3 A, Vector3 B, Vector3 C)> water, Scene scene)
    {
        float centerHeight = map(scene.Center.x, scene.Center.y);
        var target = new Vector3(scene.Center.x, centerHeight, scene.Center.y);
        Vector3 back = Norm(new Vector3(-0.6f, 0f, -0.8f));
        float distance = scene.Size * 0.55f;

        var camera = new Camera();
        camera.Position = target + back * distance + new Vector3(0f, distance * 0.42f, 0f);
        camera.Forward = Norm(target - camera.Position);
        camera.Right = Norm(Vector3.Cross(Vector3.up, camera.Forward));
        camera.Up = Vector3.Cross(camera.Forward, camera.Right);
        camera.Focal = VIEW_HEIGHT * 0.5f / Mathf.Tan(FIELD_OF_VIEW * 0.5f * Mathf.Deg2Rad);

        var depth = new float[VIEW_WIDTH * VIEW_HEIGHT];
        var color = new (float R, float G, float B)[depth.Length];

        for (int i = 0; i < depth.Length; i++)
        {
            depth[i] = float.PositiveInfinity;
            color[i] = (170f, 190f, 210f);
        }

        float half = scene.Size * 0.5f;
        float grid = Mathf.Max(1f, scene.Size / 512f);
        int cells = Mathf.CeilToInt(scene.Size / grid);

        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                float x = scene.Center.x - half + i * grid, z = scene.Center.y - half + j * grid;
                Vector3 p00 = Point(map, x, z), p10 = Point(map, x + grid, z), p01 = Point(map, x, z + grid), p11 = Point(map, x + grid, z + grid);
                (float R, float G, float B) shade = Ground(map, x + grid * 0.5f, z + grid * 0.5f, grid);

                Raster(camera, depth, color, p00, p01, p11, shade, null);
                Raster(camera, depth, color, p00, p11, p10, shade, null);
            }
        }

        foreach ((Vector3 a, Vector3 b, Vector3 c) in water)
            Raster(camera, depth, color, a, b, c, default, map, (scene.Center.x - half, scene.Center.y - half, scene.Center.x + half, scene.Center.y + half));

        var pixels = new byte[depth.Length * 3];

        for (int i = 0; i < depth.Length; i++)
        {
            pixels[i * 3] = (byte)Mathf.Clamp(color[i].R, 0f, 255f);
            pixels[i * 3 + 1] = (byte)Mathf.Clamp(color[i].G, 0f, 255f);
            pixels[i * 3 + 2] = (byte)Mathf.Clamp(color[i].B, 0f, 255f);
        }

        return pixels;
    }

    private static Vector3 Norm(Vector3 v)
    {
        float length = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);

        return length < 1e-9f ? v : new Vector3(v.x / length, v.y / length, v.z / length);
    }

    private static Vector3 Point(Func<float, float, float> map, float x, float z)
    {
        return new Vector3(x, map(x, z), z);
    }

    private const float NEAR = 1f;

    private static void Raster(Camera camera, float[] depth, (float R, float G, float B)[] color, Vector3 a, Vector3 b, Vector3 c,
        (float R, float G, float B) shade, Func<float, float, float> terrain, (float X0, float Z0, float X1, float Z1) window = default)
    {
        Vector3 va = camera.View(a), vb = camera.View(b), vc = camera.View(c);

        if (va.z >= NEAR && vb.z >= NEAR && vc.z >= NEAR)
        {
            Fill(camera, depth, color, a, b, c, va, vb, vc, shade, terrain, window);
            return;
        }

        var world = new List<Vector3>(4);
        var view = new List<Vector3>(4);
        Vector3[] corners = { a, b, c };
        Vector3[] views = { va, vb, vc };

        for (int k = 0; k < 3; k++)
        {
            int next = (k + 1) % 3;
            bool inside = views[k].z >= NEAR, nextInside = views[next].z >= NEAR;

            if (inside)
            {
                world.Add(corners[k]);
                view.Add(views[k]);
            }

            if (inside == nextInside)
                continue;

            float t = (NEAR - views[k].z) / (views[next].z - views[k].z);
            world.Add(corners[k] + (corners[next] - corners[k]) * t);
            view.Add(views[k] + (views[next] - views[k]) * t);
        }

        for (int k = 1; k + 1 < world.Count; k++)
            Fill(camera, depth, color, world[0], world[k], world[k + 1], view[0], view[k], view[k + 1], shade, terrain, window);
    }

    private static void Fill(Camera camera, float[] depth, (float R, float G, float B)[] color, Vector3 a, Vector3 b, Vector3 c,
        Vector3 va, Vector3 vb, Vector3 vc, (float R, float G, float B) shade, Func<float, float, float> terrain, (float X0, float Z0, float X1, float Z1) window)
    {
        Vector2 sa = camera.Screen(va), sb = camera.Screen(vb), sc = camera.Screen(vc);
        float area = (sb.x - sa.x) * (sc.y - sa.y) - (sb.y - sa.y) * (sc.x - sa.x);

        if (Mathf.Abs(area) < 1e-6f)
            return;

        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(sa.x, Mathf.Min(sb.x, sc.x))));
        int maxX = Mathf.Min(VIEW_WIDTH - 1, Mathf.CeilToInt(Mathf.Max(sa.x, Mathf.Max(sb.x, sc.x))));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(sa.y, Mathf.Min(sb.y, sc.y))));
        int maxY = Mathf.Min(VIEW_HEIGHT - 1, Mathf.CeilToInt(Mathf.Max(sa.y, Mathf.Max(sb.y, sc.y))));

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                float x = px + 0.5f, y = py + 0.5f;
                float w0 = ((sb.x - x) * (sc.y - y) - (sb.y - y) * (sc.x - x)) / area;
                float w1 = ((sc.x - x) * (sa.y - y) - (sc.y - y) * (sa.x - x)) / area;
                float w2 = 1f - w0 - w1;

                if (w0 < 0f || w1 < 0f || w2 < 0f)
                    continue;

                float inverse = w0 / va.z + w1 / vb.z + w2 / vc.z;
                float z = 1f / inverse;
                int index = py * VIEW_WIDTH + px;

                if (z >= depth[index])
                    continue;

                if (terrain == null)
                {
                    depth[index] = z;
                    color[index] = shade;
                    continue;
                }

                float wx = (w0 * a.x / va.z + w1 * b.x / vb.z + w2 * c.x / vc.z) * z;
                float wz = (w0 * a.z / va.z + w1 * b.z / vb.z + w2 * c.z / vc.z) * z;
                float wy = (w0 * a.y / va.z + w1 * b.y / vb.z + w2 * c.y / vc.z) * z;
                if (wx < window.X0 || wz < window.Z0 || wx > window.X1 || wz > window.Z1)
                    continue;

                float ground = terrain(wx, wz);

                depth[index] = z;
                color[index] = Water(color[index], Mathf.Max(0f, wy - ground));
            }
        }
    }
}
