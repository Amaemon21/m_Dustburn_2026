using System.Collections.Generic;

public partial class TerrainCarver
{
    private sealed class TileGrid
    {
        private readonly int _minI;
        private readonly int _minJ;
        private readonly int _width;
        private readonly int _height;
        private readonly bool[] _tiles;
        private readonly float[] _corners;

        public TileGrid(SettlementLayout layout, Dictionary<long, float> corners)
        {
            int minI = int.MaxValue, minJ = int.MaxValue, maxI = int.MinValue, maxJ = int.MinValue;

            foreach (SettlementTile tile in layout.Tiles)
            {
                minI = System.Math.Min(minI, tile.I);
                minJ = System.Math.Min(minJ, tile.J);
                maxI = System.Math.Max(maxI, tile.I);
                maxJ = System.Math.Max(maxJ, tile.J);
            }

            _minI = minI - 1;
            _minJ = minJ - 1;
            _width = maxI - minI + 4;
            _height = maxJ - minJ + 4;
            _tiles = new bool[_width * _height];
            _corners = new float[_width * _height];

            foreach (SettlementTile tile in layout.Tiles)
                _tiles[Index(tile.I, tile.J)] = true;

            foreach (KeyValuePair<long, float> corner in corners)
            {
                int i = (int)(corner.Key >> 32) - 32768;
                int j = (int)(corner.Key & 0xffffffffL) - 32768;
                _corners[Index(i, j)] = corner.Value;
            }
        }

        public bool Has(int i, int j)
        {
            int x = i - _minI;
            int y = j - _minJ;

            return x >= 0 && y >= 0 && x < _width && y < _height && _tiles[y * _width + x];
        }

        public float Corner(int i, int j)
        {
            return _corners[Index(i, j)];
        }

        private int Index(int i, int j)
        {
            return (j - _minJ) * _width + i - _minI;
        }
    }
}
