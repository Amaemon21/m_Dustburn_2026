using System.Collections.Generic;

public static partial class StandingWaterMesh
{
    private sealed class Builder
    {
        public readonly WaterMap Water;
        public readonly Dictionary<(WaterKind, int, int, bool), WaterMeshPart> Parts;
        public readonly Dictionary<WaterMeshPart, Dictionary<long, int>> Welds = new();
        public readonly int Nodes;
        public readonly float Step;
        public readonly List<int> Polygon = new(8);

        public Builder(WaterMap water, Dictionary<(WaterKind, int, int, bool), WaterMeshPart> parts)
        {
            Water = water;
            Parts = parts;
            Nodes = water.NodeResolution;
            Step = water.NodeStep;
        }
    }

    private readonly struct Block
    {
        public readonly int C0, R0, C1, R1;
        public readonly short Owner;

        public Block(int c0, int r0, int c1, int r1, short owner)
        {
            C0 = c0;
            R0 = r0;
            C1 = c1;
            R1 = r1;
            Owner = owner;
        }
    }
}
