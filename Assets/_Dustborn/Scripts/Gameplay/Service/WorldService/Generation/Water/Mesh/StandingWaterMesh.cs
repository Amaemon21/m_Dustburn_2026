using System.Collections.Generic;

public static partial class StandingWaterMesh
{
    private const float UV_SCALE = 1f / 16f;
    private const float MIN_FACING = 1e-6f;

    private const int NODE_VERTEX = 0;
    private const int EAST_CROSSING = 1;
    private const int NORTH_CROSSING = 2;

    public static void Build(WaterMap water, Dictionary<(WaterKind, int, int, bool), WaterMeshPart> parts)
    {
        var builder = new Builder(water, parts);
        int resolution = water.Resolution;
        var deep = new bool[resolution * resolution];
        var corners = new bool[(resolution + 1) * (resolution + 1)];

        for (int cell = 0; cell < deep.Length; cell++)
        {
            bool detail = water.DetailSlot[cell] >= 0;

            if (!detail && !water.IsFull(cell))
                continue;

            if (!detail && Deep(water, cell, water.CellOwner(cell)))
            {
                deep[cell] = true;
                continue;
            }

            MarkCorners(corners, resolution, cell % resolution, cell / resolution, cell % resolution + 1, cell / resolution + 1);
        }

        List<Block> blocks = Blocks(water, deep);

        foreach (Block block in blocks)
            MarkCorners(corners, resolution, block.C0, block.R0, block.C1, block.R1);

        for (int cell = 0; cell < deep.Length; cell++)
        {
            if (water.DetailSlot[cell] >= 0)
            {
                Detail(builder, cell);
                continue;
            }

            if (water.IsFull(cell) && !deep[cell])
                Fan(builder, cell, water.CellOwner(cell));
        }

        foreach (Block block in blocks)
            Rectangle(builder, corners, block);
    }
}
