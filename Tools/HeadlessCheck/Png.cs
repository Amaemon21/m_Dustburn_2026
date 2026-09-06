using System;
using System.IO;
using System.IO.Compression;

static class Png
{
    static readonly uint[] Crc = BuildCrc();

    public static void Write(string path, byte[] rgb, int width, int height)
    {
        using var fs = new FileStream(path, FileMode.Create);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var ihdr = new MemoryStream();
        WriteInt(ihdr, width);
        WriteInt(ihdr, height);
        ihdr.WriteByte(8);
        ihdr.WriteByte(2);
        ihdr.WriteByte(0);
        ihdr.WriteByte(0);
        ihdr.WriteByte(0);
        Chunk(fs, "IHDR", ihdr.ToArray());

        var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgb, y * width * 3, width * 3);
        }

        byte[] rawBytes = raw.ToArray();

        var z = new MemoryStream();
        z.WriteByte(0x78);
        z.WriteByte(0x01);
        using (var deflate = new DeflateStream(z, CompressionLevel.Optimal, true))
            deflate.Write(rawBytes, 0, rawBytes.Length);
        WriteInt(z, (int)Adler32(rawBytes));

        Chunk(fs, "IDAT", z.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static void Chunk(Stream fs, string type, byte[] data)
    {
        WriteInt(fs, data.Length);

        byte[] typeBytes = { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        fs.Write(typeBytes);
        fs.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte b in typeBytes) crc = Crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data) crc = Crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        WriteInt(fs, (int)(crc ^ 0xFFFFFFFF));
    }

    static void WriteInt(Stream s, int value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)value);
    }

    static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte x in data)
        {
            a = (a + x) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    static uint[] BuildCrc()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}

class Canvas
{
    public readonly int Width, Height;
    public readonly byte[] Pixels;

    public Canvas(int width, int height)
    {
        Width = width; Height = height;
        Pixels = new byte[width * height * 3];
    }

    public void Set(int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        int i = ((Height - 1 - y) * Width + x) * 3;
        Pixels[i] = r; Pixels[i + 1] = g; Pixels[i + 2] = b;
    }

    public void Blend(int x, int y, byte r, byte g, byte b, float alpha)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        int i = ((Height - 1 - y) * Width + x) * 3;
        Pixels[i] = (byte)(Pixels[i] * (1 - alpha) + r * alpha);
        Pixels[i + 1] = (byte)(Pixels[i + 1] * (1 - alpha) + g * alpha);
        Pixels[i + 2] = (byte)(Pixels[i + 2] * (1 - alpha) + b * alpha);
    }

    public void Line(float x0, float y0, float x1, float y1, byte r, byte g, byte b, float thickness = 1f)
    {
        float dx = x1 - x0, dy = y1 - y0;
        int steps = (int)(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) * 2f) + 1;
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float px = x0 + dx * t, py = y0 + dy * t;
            int half = (int)MathF.Ceiling(thickness / 2f);
            for (int oy = -half; oy <= half; oy++)
            for (int ox = -half; ox <= half; ox++)
                Set((int)px + ox, (int)py + oy, r, g, b);
        }
    }

    public void Save(string path) => Png.Write(path, Pixels, Width, Height);
}
