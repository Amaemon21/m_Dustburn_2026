using System.Collections.Generic;

internal static class PoiPorchLayout
{
    public const float WIDTH = 2f;
    public const float DEPTH = 1.2f;
    public const float DECK_HEIGHT = PoiHouseLayout.SLAB_HEIGHT;
    public const float STAIR_SECTION_WIDTH = 1f;
    public const float STAIR_SECTION_DEPTH = 1f;
    public const float STAIR_SECTION_HEIGHT = 0.8f;
    public const float STAIR_END_DEPTH = 0.5f;
    public const float STAIRS_DEPTH = STAIR_SECTION_DEPTH + STAIR_END_DEPTH;
    public const float STAIRS_HEIGHT = 1f;
    public const float GUARD_HEIGHT = 0.88f;
    public const float GUARD_WIDTH = 0.09f;
    public const float FACADE_LEDGE = 0.5f - PoiHouseLayout.WALL_THICKNESS;
    public const float GUARD_TO_WALL_DEPTH = DEPTH + FACADE_LEDGE;
    public const float STAIR_GUARD_HEIGHT = 1.88f;
    public const float STAIR_GUARD_BOTTOM = 0.2f;
    public const float SUPPORT_WIDTH = 0.14f;
    public const float LOW_STAIRS_DEPTH = 0.6f;
    public const float POST_HEIGHT = 2.36f;
    public const float CANOPY_DEPTH = 1.65f;
    public const float CANOPY_RISE = 0.33f;
    public const float CANOPY_THICKNESS = 0.14f;
    private const float CANOPY_Y = 2.22f;
    private const float GUARD_OFFSET = WIDTH * 0.5f - GUARD_WIDTH * 0.5f - 0.02f;

    public static void Add(List<PoiPlacement> parts, List<PoiPlacement> columns, PoiHouseSettings house, float floor)
    {
        foreach (PoiPlacement entry in columns)
        {
            if (entry.Part != PoiPart.Doorway)
                continue;

            bool hasFacadeLedge = entry.Yaw is 0 or 180;
            float wallFace = PoiHouseLayout.WALL_THICKNESS * 0.5f;
            float slabEdge = wallFace + (hasFacadeLedge ? FACADE_LEDGE : 0f);
            float deckEnd = slabEdge + DEPTH;
            float deckCenter = slabEdge + DEPTH * 0.5f;
            Place(parts, entry, PoiPart.PorchDeck, 0f, floor - DECK_HEIGHT, deckCenter);
            PlacePair(parts, entry, PoiPart.PorchPost, floor, deckEnd - SUPPORT_WIDTH);

            float canopyEdge = wallFace + CANOPY_DEPTH;
            float fasciaY = floor + CANOPY_Y + CANOPY_THICKNESS - PoiRoofOverhangLayout.FASCIA_TOP_INSET - PoiRoofOverhangLayout.FASCIA_HEIGHT;
            float fasciaZ = canopyEdge + PoiRoofOverhangLayout.FASCIA_OFFSET;

            for (int column = 0; column < (int)WIDTH; column++)
            {
                float across = PoiHouseLayout.CellCenter(column, (int)WIDTH);
                Place(parts, entry, PoiPart.PorchCanopy, across, floor + CANOPY_Y, canopyEdge - CANOPY_DEPTH * 0.5f);
                Place(parts, entry, PoiPart.RoofFascia, across, fasciaY, fasciaZ);

                if (house.Foundation)
                {
                    Place(parts, entry, PoiPart.PorchFoundation, across, 0f, deckCenter);
                    Place(parts, entry, PoiPart.PorchStairEndSection, across, 0f, deckEnd + STAIR_END_DEPTH * 0.5f);
                    Place(parts, entry, PoiPart.PorchStairSection, across, 0f, deckEnd + STAIR_END_DEPTH + STAIR_SECTION_DEPTH * 0.5f);
                }
                else
                    Place(parts, entry, PoiPart.PorchLowStairSection, across, 0f, deckEnd + LOW_STAIRS_DEPTH * 0.5f);
            }

            if (house.PorchRails)
            {
                PoiPart guard = hasFacadeLedge ? PoiPart.PorchGuardToWall : PoiPart.PorchGuard;
                PlacePair(parts, entry, guard, floor, (wallFace + deckEnd) * 0.5f);

                if (house.Foundation)
                    PlacePair(parts, entry, PoiPart.PorchStairGuard, 0f, deckEnd + STAIRS_DEPTH * 0.5f);
            }

            return;
        }
    }

    private static void PlacePair(List<PoiPlacement> parts, PoiPlacement entry, PoiPart part, float y, float outwards)
    {
        Place(parts, entry, part, -GUARD_OFFSET, y, outwards);
        Place(parts, entry, part, GUARD_OFFSET, y, outwards);
    }

    private static void Place(List<PoiPlacement> parts, PoiPlacement entry, PoiPart part, float across, float y, float outwards)
    {
        (float x, float z) = entry.Yaw switch
        {
            90 => (outwards, -across),
            -90 => (-outwards, across),
            180 => (-across, -outwards),
            _ => (across, outwards)
        };
        PoiSection section = part switch
        {
            PoiPart.PorchFoundation => PoiSection.Foundation,
            PoiPart.PorchDeck or PoiPart.PorchStairSection or PoiPart.PorchStairEndSection or PoiPart.PorchLowStairSection => PoiSection.Porch,
            PoiPart.PorchCanopy => PoiSection.Roof,
            _ => PoiSection.PorchTimber
        };
        parts.Add(new PoiPlacement(part, section, entry.X + x, y, entry.Z + z, entry.Yaw));
    }
}
