using System;
using System.IO;
using UnityEngine;

static class RoadSurfacePreview
{
    private const float TILE_LENGTH = 8f;
    private const float EDGE_BAND = 0.08f;
    private const float EDGE_BREAKUP = 0.35f;
    private const float FILL_START = 0.68f;
    private const float FILL_END = 0.78f;
    private const float FILL_SHIFT = 0.86f;
    private const float FILLET_SHIFT = 0.57f;

    private sealed class Texture
    {
        private readonly byte[] _pixels;
        private readonly int _width;
        private readonly int _height;
        private readonly int _channels;

        public Texture(string path)
        {
            _pixels = PngReader.Read(path, out _width, out _height, out _channels);
        }

        public Vector3 Sample(float u, float v, bool strip)
        {
            int x = Math.Clamp((int)((strip ? Math.Clamp(u, 0f, 0.99999f) : u - MathF.Floor(u)) * _width), 0, _width - 1);
            int y = Math.Clamp((int)((1f - (v - MathF.Floor(v))) * _height), 0, _height - 1);
            int index = (y * _width + x) * _channels;
            return new Vector3(_pixels[index] / 255f, _pixels[index + 1] / 255f, _pixels[index + 2] / 255f);
        }
    }

    public static void Write(RoadSurfaceMesh.Geometry geometry, string destination)
    {
        Write(geometry, destination, Vector2.zero, 96f, 960);
    }

    public static void Write(RoadSurfaceMesh.Geometry geometry, string destination, Vector2 origin, float metres, int size)
    {
        string root = Directory.GetCurrentDirectory();

        while (!File.Exists(Path.Combine(root, "Packages", "manifest.json")))
            root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException();

        string folder = Path.Combine(root, "Assets", "_Dustborn", "Content", "Texture", "TerrainTextures");
        var asphalt = new Texture(Path.Combine(folder, "Road_AsphaltStrip.png"));
        var mask = new Texture(Path.Combine(folder, "Road_AsphaltStrip_Mask.png"));
        var gravel = new Texture(Path.Combine(folder, "Road_Gravel.png"));
        float scale = size / metres;
        var pixels = new byte[size * size * 3];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                Pixel(x, y, gravel.Sample(WorldX(x) / 4f, WorldZ(y) / 4f, false) * 0.7f);
        }

        for (int i = 0; i < geometry.Triangles.Count; i += 3)
        {
            int ia = geometry.Triangles[i];
            int ib = geometry.Triangles[i + 1];
            int ic = geometry.Triangles[i + 2];
            Vector2 a = Point(ia);
            Vector2 b = Point(ib);
            Vector2 c = Point(ic);
            float area = Cross(b - a, c - a);

            if (Math.Abs(area) < 0.00001f)
                continue;

            Vector2 min = Vector2.Min(a, Vector2.Min(b, c));
            Vector2 max = Vector2.Max(a, Vector2.Max(b, c));

            for (int y = Math.Max(0, (int)min.y); y <= Math.Min(size - 1, (int)max.y); y++)
            {
                for (int x = Math.Max(0, (int)min.x); x <= Math.Min(size - 1, (int)max.x); x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    float u = Cross(b - point, c - point) / area;
                    float v = Cross(c - point, a - point) / area;
                    float w = 1f - u - v;

                    if (u < 0f || v < 0f || w < 0f)
                        continue;

                    Vector2 uv = geometry.Uv[ia] * u + geometry.Uv[ib] * v + geometry.Uv[ic] * w;
                    Vector3 marks = geometry.Markings[ia] * u + geometry.Markings[ib] * v + geometry.Markings[ic] * w;
                    float width = geometry.Profile[ia].x * u + geometry.Profile[ib].x * v + geometry.Profile[ic].x * w;
                    float style = geometry.Profile[ia].y;
                    float edgeWeight = Mathf.Clamp01(uv.x >= 0f ? marks.y : marks.z);
                    float band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FILL_START, FILL_END, Math.Abs(uv.x)));
                    bool fillet = style < 0.5f;
                    float fill = fillet ? 1f - band : (1f - edgeWeight) * band;
                    float shift = fillet ? FILLET_SHIFT : FILL_SHIFT;
                    float stripU = uv.x * 0.5f + 0.5f;
                    float fillU = (uv.x - (uv.x < 0f ? -shift : shift)) * 0.5f + 0.5f;
                    float stripV = uv.y / TILE_LENGTH;
                    Vector3 masks = Vector3.Lerp(mask.Sample(stripU, stripV, true), mask.Sample(fillU, stripV + 0.37f, true), fill);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - EDGE_BAND, 1f, Mathf.Abs(uv.x))) * edgeWeight;

                    if (1f - edge - EDGE_BREAKUP * (Noise(WorldX(x) * 2.7f, WorldZ(y) * 2.7f) - 0.35f) < 0f)
                        continue;

                    Vector3 color = Vector3.Lerp(asphalt.Sample(stripU, stripV, true), asphalt.Sample(fillU, stripV + 0.37f, true), fill) * (1f - masks.y * 0.15f);
                    float wear = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.224f, 0.524f, Noise(WorldX(x) * 4f, WorldZ(y) * 4f)));
                    float paint = wear * (1f - masks.x);
                    float lateral = uv.x * width;

                    if (style >= 2f && Math.Abs(Math.Abs(lateral) - (width - 0.4f)) < 0.06f)
                        color = Vector3.Lerp(color, new Vector3(0.77f, 0.75f, 0.68f), paint * edgeWeight);

                    if (style >= 2f && Math.Abs(lateral) < 0.06f && (style < 3f || uv.y - MathF.Floor(uv.y / 9f) * 9f < 3f))
                        color = Vector3.Lerp(color, new Vector3(0.72f, 0.48f, 0.08f), paint * Mathf.Clamp01(marks.x));

                    Pixel(x, y, color);
                }
            }
        }

        Png.Write(destination, pixels, size, size);
        Console.WriteLine($"Offline road material preview: {destination}");

        Vector2 Point(int index)
        {
            Vector3 p = geometry.Vertices[index];
            return new Vector2((p.x - origin.x) * scale, size - (p.z - origin.y) * scale);
        }

        float WorldX(int x) => origin.x + (x + 0.5f) / scale;

        float WorldZ(int y) => origin.y + (size - y - 0.5f) / scale;

        void Pixel(int x, int y, Vector3 color)
        {
            int offset = (y * size + x) * 3;
            pixels[offset] = (byte)Math.Clamp(color.x * 255f, 0f, 255f);
            pixels[offset + 1] = (byte)Math.Clamp(color.y * 255f, 0f, 255f);
            pixels[offset + 2] = (byte)Math.Clamp(color.z * 255f, 0f, 255f);
        }
    }

    private static float Noise(float x, float y)
    {
        float cx = MathF.Floor(x);
        float cy = MathF.Floor(y);
        float fx = x - cx;
        float fy = y - cy;
        fx *= fx * (3f - 2f * fx);
        fy *= fy * (3f - 2f * fy);
        return Mathf.Lerp(Mathf.Lerp(Hash(cx, cy), Hash(cx + 1f, cy), fx), Mathf.Lerp(Hash(cx, cy + 1f), Hash(cx + 1f, cy + 1f), fx), fy);
    }

    private static float Hash(float x, float y)
    {
        float value = MathF.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
        return value - MathF.Floor(value);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
