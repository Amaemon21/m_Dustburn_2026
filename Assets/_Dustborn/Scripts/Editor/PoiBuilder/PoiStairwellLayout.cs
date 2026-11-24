using System.Collections.Generic;

internal readonly struct PoiStairwell
{
    public PoiStairwell(int column, int firstRow, float x, float z)
    {
        Column = column;
        FirstRow = firstRow;
        X = x;
        Z = z;
        HasOpening = true;
    }

    public static PoiStairwell None => default;
    public int Column { get; }
    public int FirstRow { get; }
    public float X { get; }
    public float Z { get; }
    public bool HasOpening { get; }

    public float LowEnd => Z + PoiStairwellLayout.LENGTH * 0.5f;
    public float HighEnd => Z - PoiStairwellLayout.LENGTH * 0.5f;

    public bool Contains(int column, int row) => HasOpening && column == Column &&
        row >= FirstRow && row < FirstRow + PoiStairwellLayout.LENGTH;
}

internal static class PoiStairwellLayout
{
    public const float WIDTH = 1f;
    public const int LENGTH = 4;
    public const int MIN_DEPTH = LENGTH + 3;
    public const int TREADS_PER_SECTION = 4;
    public const int TREADS = LENGTH * TREADS_PER_SECTION;
    public const float SECTION_RISE = (float)PoiHouseLayout.WALL_HEIGHT / LENGTH;
    public const float RISE = SECTION_RISE / TREADS_PER_SECTION;
    public const float GUARD_THICKNESS = 0.08f;
    public const float GUARD_HEIGHT = 0.94f;
    public const float RAIL_TREAD_Y = RISE - 0.006f;
    private const float FLIGHT_RAIL_INSET = 0.005f;
    public const float WALK_WIDTH = WIDTH - 2f * (GUARD_THICKNESS + FLIGHT_RAIL_INSET);
    public const float NEWEL_WIDTH = 0.14f;
    public const float NEWEL_HEIGHT = 1f;
    private const float FLIGHT_RAIL_OFFSET = (WIDTH - GUARD_THICKNESS) * 0.5f - FLIGHT_RAIL_INSET;
    private const float LANDING_GUARD_OFFSET = (WIDTH + GUARD_THICKNESS) * 0.5f;
    private const float NEWEL_INSET = 0.01f;
    private const float NEWEL_EMBED = 0.015f;

    public static PoiStairwell Create(PoiHouseSettings house, int edgeSide)
    {
        int rows = house.Depth - 1;
        int column = 1;
        int firstRow = (rows - LENGTH) / 2;
        float x = PoiHouseLayout.InteriorColumnCenter(column, house.Width - 1, edgeSide);
        float z = PoiHouseLayout.CellCenter(firstRow, rows) + (LENGTH - 1) * 0.5f;
        return new PoiStairwell(column, firstRow, x, z);
    }

    public static void Add(List<PoiPlacement> parts, PoiStairwell stairwell, float lowerFloor, float upperFloor)
    {
        for (int i = 0; i < LENGTH; i++)
        {
            float y = lowerFloor + i * SECTION_RISE;
            float z = stairwell.LowEnd - i - 0.5f;
            parts.Add(new PoiPlacement(PoiPart.InteriorStairSection, PoiSection.Staircase, stairwell.X, y, z));
            parts.Add(new PoiPlacement(PoiPart.InteriorStairRail, PoiSection.StairwellGuard, stairwell.X - FLIGHT_RAIL_OFFSET, y, z));
            parts.Add(new PoiPlacement(PoiPart.InteriorStairRail, PoiSection.StairwellGuard, stairwell.X + FLIGHT_RAIL_OFFSET, y, z));
        }

        float endZ = stairwell.LowEnd + GUARD_THICKNESS * 0.5f;
        float landingPostZ = stairwell.HighEnd - NEWEL_WIDTH * 0.5f + NEWEL_INSET;
        float postBase = upperFloor - NEWEL_EMBED;

        for (int side = -1; side <= 1; side += 2)
        {
            float guardX = stairwell.X + side * LANDING_GUARD_OFFSET;
            float landingPostX = stairwell.X + side * WIDTH * 0.5f;
            parts.Add(new PoiPlacement(PoiPart.StairwellSideGuard, PoiSection.StairwellGuard, guardX, upperFloor, stairwell.Z));
            parts.Add(new PoiPlacement(PoiPart.StairNewel, PoiSection.StairwellGuard, landingPostX, postBase, landingPostZ));
            parts.Add(new PoiPlacement(PoiPart.StairNewel, PoiSection.StairwellGuard, guardX, postBase, endZ));
        }

        parts.Add(new PoiPlacement(PoiPart.StairwellEndGuard, PoiSection.StairwellGuard, stairwell.X, upperFloor, endZ));
    }
}
