using System.Collections.Generic;

internal static class PoiRoofOverhangLayout
{
    public const float SIDE_RUN = 0.35f;
    public const float GABLE_EXTENSION = SIDE_RUN - PoiHouseLayout.WALL_THICKNESS;
    public const float FASCIA_HEIGHT = 0.16f;
    public const float FASCIA_DEPTH = 0.045f;
    public const float FASCIA_TOP_INSET = 0.008f;
    private const float FASCIA_PROJECTION = 0.008f;
    public const float FASCIA_OFFSET = FASCIA_PROJECTION - FASCIA_DEPTH * 0.5f;
    public const float EAVE_FASCIA_END_WIDTH = GABLE_EXTENSION + FASCIA_PROJECTION - FASCIA_DEPTH;
    public const float GABLE_FASCIA_END_WIDTH = SIDE_RUN + FASCIA_PROJECTION;

    public static void Add(List<PoiPlacement> parts, PoiHouseSettings house, float wallTop)
    {
        float edgeZ = (house.Depth + GABLE_EXTENSION) * 0.5f;
        float eaveX = (house.Width + SIDE_RUN) * 0.5f;
        float eaveY = wallTop - SIDE_RUN;
        float fasciaX = house.Width * 0.5f + SIDE_RUN + FASCIA_OFFSET;
        float fasciaZ = house.Depth * 0.5f + GABLE_EXTENSION + FASCIA_OFFSET;
        float fasciaShiftY = PoiHouseLayout.SLOPE_THICKNESS - FASCIA_TOP_INSET - FASCIA_HEIGHT;

        for (int z = 0; z < house.Depth; z++)
        {
            float center = PoiHouseLayout.CellCenter(z, house.Depth);
            AddPair(parts, PoiPart.RoofEave, eaveX, eaveY, center);
            AddEaveFascia(parts, PoiPart.RoofFascia, fasciaX, eaveY + fasciaShiftY, center);
        }

        AddPair(parts, PoiPart.RoofEaveCorner, eaveX, eaveY, -edgeZ);
        AddPair(parts, PoiPart.RoofEaveCorner, eaveX, eaveY, edgeZ);
        float endZ = (house.Depth + EAVE_FASCIA_END_WIDTH) * 0.5f;
        AddEaveFascia(parts, PoiPart.RoofFasciaEnd, fasciaX, eaveY + fasciaShiftY, -endZ);
        AddEaveFascia(parts, PoiPart.RoofFasciaEnd, fasciaX, eaveY + fasciaShiftY, endZ);

        for (int step = 0; step < house.Width / 2; step++)
        {
            float x = -PoiHouseLayout.CellCenter(step, house.Width);
            float y = wallTop + step;
            AddPair(parts, PoiPart.RoofRake, x, y, -edgeZ);
            AddPair(parts, PoiPart.RoofRake, x, y, edgeZ);
            AddGableFascia(parts, PoiPart.GableFascia, x, y + fasciaShiftY, fasciaZ);
        }

        float endX = (house.Width + GABLE_FASCIA_END_WIDTH) * 0.5f;
        AddGableFascia(parts, PoiPart.GableFasciaEnd, endX, wallTop - GABLE_FASCIA_END_WIDTH + fasciaShiftY, fasciaZ);
    }

    private static void AddEaveFascia(List<PoiPlacement> parts, PoiPart part, float x, float y, float z)
    {
        parts.Add(new PoiPlacement(part, PoiSection.Trim, -x, y, z, -90));
        parts.Add(new PoiPlacement(part, PoiSection.Trim, x, y, z, 90));
    }

    private static void AddGableFascia(List<PoiPlacement> parts, PoiPart part, float x, float y, float z)
    {
        parts.Add(new PoiPlacement(part, PoiSection.Trim, -x, y, -z));
        parts.Add(new PoiPlacement(part, PoiSection.Trim, x, y, -z, 180));
        parts.Add(new PoiPlacement(part, PoiSection.Trim, -x, y, z));
        parts.Add(new PoiPlacement(part, PoiSection.Trim, x, y, z, 180));
    }

    private static void AddPair(List<PoiPlacement> parts, PoiPart part, float x, float y, float z)
    {
        parts.Add(new PoiPlacement(part, PoiSection.Roof, -x, y, z, 90));
        parts.Add(new PoiPlacement(part, PoiSection.Roof, x, y, z, -90));
    }
}
