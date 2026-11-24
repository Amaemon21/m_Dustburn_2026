using System;
using System.Collections.Generic;

internal static class PoiWallLayout
{
    public const int DOOR_CELLS = 2;
    public const float PARTITION_TOP_HEIGHT = 0.99f;
    public const int WINDOW_LEVEL = 1;
    public const float SKIN_DEPTH = 0.004f;
    public const float CASING_WIDTH = 0.9f;
    public const float CASING_HEIGHT = 0.045f;
    public const float CASING_DEPTH = 0.03f;
    public const float OPENING_WIDTH = 0.8f;
    public const float REVEAL_THICKNESS = 0.012f;
    public const float REVEAL_DEPTH = PoiHouseLayout.WALL_THICKNESS + SKIN_DEPTH;
    public const float REVEAL_CLEAR_WIDTH = OPENING_WIDTH - 2f * REVEAL_THICKNESS;
    public const float SASH_WIDTH = REVEAL_CLEAR_WIDTH + 0.002f;
    public const float SASH_HEIGHT = 0.975f;
    public const float SASH_DEPTH = 0.061f;
    public const float SASH_SILL_OVERLAP = 0.001f;
    public const float GLAZING_WIDTH = SASH_WIDTH - 0.082f;
    public const float GLAZING_HEIGHT = SASH_HEIGHT - 0.082f;
    public const float GLAZING_DEPTH = 0.006f;
    public const float GLAZING_BOTTOM = 0.041f;
    public const float SILL_WIDTH = 0.96f;
    public const float SILL_HEIGHT = 0.045f;
    public const float SILL_DEPTH = 0.13f;
    public const float OPENING_SILL_DEPTH = 0.45f;
    public const float SILL_BOTTOM = -0.03f;
    public const float BASEBOARD_HEIGHT = 0.1f;
    public const float BASEBOARD_DEPTH = 0.009f;
    public const float CORNER_SKIN_WIDTH = 1f - PoiHouseLayout.WALL_THICKNESS - SKIN_DEPTH;
    public const float CORNER_BASEBOARD_WIDTH = CORNER_SKIN_WIDTH - BASEBOARD_DEPTH;
    public const float CORNER_BOARD_WIDTH = 0.278f;
    public const float CORNER_BOARD_DEPTH = 0.019f;
    private const float WALL_FACE = PoiHouseLayout.WALL_THICKNESS * 0.5f;
    private const float SKIN_CENTER = WALL_FACE + SKIN_DEPTH * 0.5f;
    private const float INTERIOR_CASING_CENTER = WALL_FACE + SKIN_DEPTH + CASING_DEPTH * 0.5f;
    private const float EXTERIOR_CASING_CENTER = WALL_FACE + CASING_DEPTH * 0.5f;
    private const float REVEAL_CENTER = -SKIN_DEPTH * 0.5f;
    private const float BASEBOARD_CENTER = WALL_FACE + SKIN_DEPTH + BASEBOARD_DEPTH * 0.5f;

    public static void AddColumn(List<PoiPlacement> parts, PoiPlacement column, PoiHouseSettings house)
    {
        bool door = column.Part == PoiPart.Doorway;
        bool window = column.Part == PoiPart.Window;
        bool corner = (column.Yaw is 0 or 180) && Math.Abs(column.X) == (house.Width - 1) * 0.5f;
        float inwards = corner ? -Math.Sign(column.X) : 0f;
        float interiorCasing = house.ModularWalls ? INTERIOR_CASING_CENTER : EXTERIOR_CASING_CENTER;

        if (!house.ModularWalls)
            parts.Add(column);

        for (int level = 0; level < PoiHouseLayout.WALL_HEIGHT; level++)
        {
            bool opening = (door && level < DOOR_CELLS) || (window && level == WINDOW_LEVEL);

            if (house.ModularWalls)
            {
                PoiPart structure = opening ? PoiPart.OpeningJamb : PoiPart.WallPanel;
                PoiPart skin = opening ? PoiPart.OpeningJambSkin : corner ? PoiPart.WallSkinCorner : PoiPart.WallSkin;
                PoiSection finish = house.Wainscot && level == 0 ? PoiSection.Wainscot : PoiSection.WallInteriors;
                Add(parts, column, PoiSection.Walls, structure, level, 0f);
                Add(parts, column, finish, skin, level, -SKIN_CENTER, inwards * (1f - CORNER_SKIN_WIDTH) * 0.5f);
            }

            if (opening)
            {
                Add(parts, column, PoiSection.Trim, PoiPart.OpeningRevealSides, level, REVEAL_CENTER);
                Add(parts, column, PoiSection.Trim, PoiPart.OpeningTrimSides, level, -interiorCasing);
                Add(parts, column, PoiSection.Trim, PoiPart.OpeningTrimSides, level, EXTERIOR_CASING_CENTER);
            }
        }

        PoiPart baseboard = door ? PoiPart.OpeningBaseboard : corner ? PoiPart.BaseboardCorner : PoiPart.Baseboard;
        float baseboardShift = inwards * (1f - CORNER_BASEBOARD_WIDTH) * 0.5f;
        Add(parts, column, PoiSection.Trim, baseboard, 0, -BASEBOARD_CENTER, baseboardShift);

        if (house.ModularWalls && house.Wainscot)
            Add(parts, column, PoiSection.Trim, baseboard, 1f - BASEBOARD_HEIGHT, -BASEBOARD_CENTER, baseboardShift);

        if (door || window)
        {
            int head = door ? DOOR_CELLS : WINDOW_LEVEL + 1;
            Add(parts, column, PoiSection.Trim, PoiPart.OpeningRevealHead, head - REVEAL_THICKNESS, REVEAL_CENTER);
            Add(parts, column, PoiSection.Trim, PoiPart.OpeningTrimHead, head, -interiorCasing);
            Add(parts, column, PoiSection.Trim, PoiPart.OpeningTrimHead, head, EXTERIOR_CASING_CENTER);
        }

        if (window)
        {
            Add(parts, column, PoiSection.Trim, PoiPart.OpeningSill, WINDOW_LEVEL, 0f);

            if (house.WindowFrames)
            {
                float sillTop = WINDOW_LEVEL + SILL_BOTTOM + SILL_HEIGHT;
                float sashBottom = sillTop - SASH_SILL_OVERLAP;
                Add(parts, column, PoiSection.Trim, PoiPart.WindowSash, sashBottom, REVEAL_CENTER);

                if (house.WindowGlass)
                    Add(parts, column, PoiSection.Glazing, PoiPart.WindowGlass, sashBottom, REVEAL_CENTER);
            }
        }
    }

    public static void AddExteriorCorners(List<PoiPlacement> parts, PoiHouseSettings house, float y)
    {
        float x = house.Width * 0.5f;
        float z = PoiHouseLayout.EndWallCenter(house.Depth) + WALL_FACE;
        parts.Add(new PoiPlacement(PoiPart.ExteriorCorner, PoiSection.Trim, x, y, z));
        parts.Add(new PoiPlacement(PoiPart.ExteriorCorner, PoiSection.Trim, x, y, -z, 90));
        parts.Add(new PoiPlacement(PoiPart.ExteriorCorner, PoiSection.Trim, -x, y, -z, 180));
        parts.Add(new PoiPlacement(PoiPart.ExteriorCorner, PoiSection.Trim, -x, y, z, -90));
    }

    public static void AddPartitionColumn(List<PoiPlacement> parts, PoiPlacement column, bool belowUpperFloor)
    {
        bool door = column.Part == PoiPart.Doorway;

        for (int level = 0; level < PoiHouseLayout.WALL_HEIGHT; level++)
        {
            bool opening = door && level < DOOR_CELLS;
            bool top = belowUpperFloor && level == PoiHouseLayout.WALL_HEIGHT - 1;
            PoiPart structure = opening ? PoiPart.OpeningJamb : top ? PoiPart.PartitionTopPanel : PoiPart.WallPanel;
            Add(parts, column, PoiSection.Partitions, structure, level, 0f);

            if (opening)
            {
                Add(parts, column, PoiSection.Trim, PoiPart.OpeningRevealSides, level, 0f);
                Add(parts, column, PoiSection.Trim, PoiPart.OpeningTrimSides, level, -EXTERIOR_CASING_CENTER);
                Add(parts, column, PoiSection.Trim, PoiPart.OpeningTrimSides, level, EXTERIOR_CASING_CENTER);
            }
        }

        if (door)
        {
            Add(parts, column, PoiSection.Trim, PoiPart.OpeningRevealHead, DOOR_CELLS - REVEAL_THICKNESS, 0f);
            Add(parts, column, PoiSection.Trim, PoiPart.OpeningTrimHead, DOOR_CELLS, -EXTERIOR_CASING_CENTER);
            Add(parts, column, PoiSection.Trim, PoiPart.OpeningTrimHead, DOOR_CELLS, EXTERIOR_CASING_CENTER);
        }
    }

    private static void Add(List<PoiPlacement> parts, PoiPlacement column, PoiSection section, PoiPart part,
        float level, float normalOffset, float shiftX = 0f)
    {
        float x = column.X + shiftX;
        float z = column.Z;

        switch (column.Yaw)
        {
            case 90: x += normalOffset; break;
            case -90: x -= normalOffset; break;
            case 180: z -= normalOffset; break;
            default: z += normalOffset; break;
        }

        parts.Add(new PoiPlacement(part, section, x, column.Y + level, z, column.Yaw));
    }
}
