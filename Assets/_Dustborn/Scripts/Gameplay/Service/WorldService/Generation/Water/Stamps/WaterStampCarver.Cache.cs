using System;
using System.Collections.Generic;
using System.Threading;

public static partial class WaterStampCarver
{
    public sealed class CarveCache
    {
        private HeightMap _source;
        private float[][] _signatures;
        private float[][] _lowered;

        private int _reused;
        private int _computed;

        public int Reused => _reused;
        public int Computed => _computed;

        internal void Prepare(HeightMap source, int tiles)
        {
            if (_source == source && _signatures != null && _signatures.Length == tiles)
                return;

            _source = source;
            _signatures = new float[tiles][];
            _lowered = new float[tiles][];
        }

        internal bool TryReuse(int tile, float[] signature, out float[] lowered)
        {
            lowered = _lowered[tile];

            if (lowered == null || !SameBits(_signatures[tile], signature))
            {
                Interlocked.Increment(ref _computed);
                return false;
            }

            Interlocked.Increment(ref _reused);
            return true;
        }

        internal void Store(int tile, float[] signature, float[] lowered)
        {
            _signatures[tile] = signature;
            _lowered[tile] = lowered;
        }
    }

    private static int Lower(float[] lowered, float[] heights, int resolution, int tileMinX, int tileMaxX, int tileMinZ, int tileMaxZ, int tileWidth)
    {
        int count = 0;

        for (int z = tileMinZ; z <= tileMaxZ; z++)
        {
            for (int x = tileMinX; x <= tileMaxX; x++)
            {
                float value = lowered[(z - tileMinZ) * tileWidth + x - tileMinX];

                if (float.IsPositiveInfinity(value))
                    continue;

                int index = z * resolution + x;

                if (value >= heights[index])
                    continue;

                heights[index] = value;
                count++;
            }
        }

        return count;
    }

    private static float[] Signature(List<int> bucket, List<Segment> segments, Sections[] sections)
    {
        var values = new List<float>(bucket.Count * 24);

        foreach (int s in bucket)
        {
            Segment segment = segments[s];
            Sections section = sections[segment.River];

            Add(values, segment.A);
            Add(values, segment.B);
            values.Add(segment.Reach);
            values.Add(segment.Guard);
            values.Add(BitConverter.Int32BitsToSingle(segment.River));
            values.Add(BitConverter.Int32BitsToSingle(section == null ? -1 : segment.Index));

            if (section == null)
                continue;

            for (int i = segment.Index; i <= segment.Index + 1; i++)
            {
                values.Add(section.Reach[i]);
                values.Add(section.Cap[i]);
                values.Add(section.Shoulder[i]);

                for (int k = 0; k < STRIDE; k++)
                {
                    values.Add(section.Left[i * STRIDE + k]);
                    values.Add(section.Right[i * STRIDE + k]);
                }
            }
        }

        return values.ToArray();
    }

    private static void Add(List<float> values, RiverPoint point)
    {
        values.Add(point.Position.x);
        values.Add(point.Position.y);
        values.Add(point.Surface);
        values.Add(point.Width);
    }

    private static bool SameBits(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
                return false;
        }

        return true;
    }
}
