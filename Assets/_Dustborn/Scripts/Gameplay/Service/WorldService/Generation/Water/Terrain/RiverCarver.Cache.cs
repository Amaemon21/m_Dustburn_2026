using System;
using System.Collections.Generic;

public static partial class RiverCarver
{
    public sealed class CarveCache
    {
        private HeightMap _source;
        private float _bankWidth;
        private CarveTile[] _tiles;
        private float[][] _carved;

        private int _reused;
        private int _computed;

        public int Reused => _reused;
        public int Computed => _computed;

        internal void Prepare(HeightMap source, float bankWidth, int tiles)
        {
            if (_source == source && Same(_bankWidth, bankWidth) && _tiles != null && _tiles.Length == tiles)
                return;

            _source = source;
            _bankWidth = bankWidth;
            _tiles = new CarveTile[tiles];
            _carved = new float[tiles][];
        }

        internal bool Reuse(int tile, CarveTile input, float[] heights, int resolution, int minX, int minZ, int width, int maxZ)
        {
            CarveTile cached = _tiles[tile];

            if (cached == null || !cached.Matches(input))
            {
                System.Threading.Interlocked.Increment(ref _computed);
                return false;
            }

            float[] carved = _carved[tile];

            for (int z = minZ; z <= maxZ; z++)
                Array.Copy(carved, (z - minZ) * width, heights, z * resolution + minX, width);

            System.Threading.Interlocked.Increment(ref _reused);
            return true;
        }

        internal void Store(int tile, CarveTile input, float[] heights, int resolution, int minX, int minZ, int width, int maxZ)
        {
            var carved = new float[width * (maxZ - minZ + 1)];

            for (int z = minZ; z <= maxZ; z++)
                Array.Copy(heights, z * resolution + minX, carved, (z - minZ) * width, width);

            _tiles[tile] = input;
            _carved[tile] = carved;
        }
    }

    internal sealed class CarveTile
    {
        private Segment[] _segments;
        private short[] _lakes;
        private byte[] _distances;
        private float[] _surfaces;

        public static CarveTile Describe(List<int> bucket, List<Segment> segments, WaterMap water, short[] nearLake, byte[] lakeDistance, float minX, float minZ, float maxX, float maxZ)
        {
            var tile = new CarveTile { _segments = new Segment[bucket.Count] };

            for (int i = 0; i < bucket.Count; i++)
                tile._segments[i] = segments[bucket[i]];

            int first = water.CellIndex(minX, minZ);
            int last = water.CellIndex(maxX, maxZ);
            int columns = last % water.Resolution - first % water.Resolution + 1;
            int rows = last / water.Resolution - first / water.Resolution + 1;

            tile._lakes = new short[columns * rows];
            tile._surfaces = new float[columns * rows];
            tile._distances = new byte[columns * rows];

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int local = row * columns + column;
                    short lake = nearLake[first + row * water.Resolution + column];
                    tile._lakes[local] = lake;
                    tile._distances[local] = lakeDistance[first + row * water.Resolution + column];
                    tile._surfaces[local] = lake >= 0 ? water.Bodies[lake].Surface : 0f;
                }
            }

            return tile;
        }

        public bool Matches(CarveTile other)
        {
            if (_segments.Length != other._segments.Length || _lakes.Length != other._lakes.Length)
                return false;

            for (int i = 0; i < _lakes.Length; i++)
            {
                if (_lakes[i] != other._lakes[i] || _distances[i] != other._distances[i] || !Same(_surfaces[i], other._surfaces[i]))
                    return false;
            }

            for (int i = 0; i < _segments.Length; i++)
            {
                if (!SameSegment(_segments[i], other._segments[i]))
                    return false;
            }

            return true;
        }
    }

    private static bool SameSegment(Segment a, Segment b)
    {
        return SamePoint(a.A, b.A) && SamePoint(a.B, b.B) && Same(a.Reach, b.Reach) && a.OpenStart == b.OpenStart && a.OpenEnd == b.OpenEnd
            && Same(a.AlongA, b.AlongA) && Same(a.AlongB, b.AlongB) && a.Course.TaperStart == b.Course.TaperStart && a.Course.TaperEnd == b.Course.TaperEnd
            && a.Course.Along.Length == b.Course.Along.Length && (a.Course.Along.Length == 0 || Same(a.Course.Along[^1], b.Course.Along[^1]));
    }

    private static bool SamePoint(RiverPoint a, RiverPoint b)
    {
        return Same(a.Position.x, b.Position.x) && Same(a.Position.y, b.Position.y) && Same(a.Surface, b.Surface) && Same(a.Bed, b.Bed)
            && Same(a.Width, b.Width) && Same(a.Flow, b.Flow) && a.Submerged == b.Submerged;
    }

    private static bool Same(float a, float b)
    {
        return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    }
}
