using System;
using System.Collections.Generic;

internal enum PoiBuildingKind
{
    House
}

internal enum PoiSide
{
    Front,
    Right,
    Back,
    Left
}

internal enum PoiPart
{
    Cube,
    Wall,
    Doorway,
    Window,
    Slab,
    Slope,
    Gable,
    WallPanel,
    SlabHalf,
    OpeningJamb,
    WallSkin,
    OpeningJambSkin,
    OpeningTrimSides,
    OpeningTrimHead,
    WindowSill,
    Baseboard,
    OpeningBaseboard,
    StudyDesk,
    StudyChair,
    StudyLamp,
    StudyClock,
    WallSkinCorner,
    BaseboardCorner,
    ExteriorCorner,
    OpeningRevealSides,
    OpeningRevealHead,
    OpeningSill,
    PorchDeck,
    PorchStairSection,
    PorchGuard,
    PorchStairGuard,
    StairNewel,
    RoofEave,
    RoofRake,
    RoofEaveCorner,
    WindowSash,
    WindowGlass,
    InteriorStairSection,
    StairwellSideGuard,
    StairwellEndGuard,
    InteriorStairRamp,
    InteriorStairRail,
    Plinth,
    PorchLowStairSection,
    PorchPost,
    PorchCanopy,
    RoofFascia,
    GableFascia,
    PorchGuardToWall,
    PorchFoundation,
    RoofFasciaEnd,
    GableFasciaEnd,
    PorchStairEndSection,
    PartitionTopPanel
}

internal enum PoiSection
{
    Foundation,
    Floor,
    Walls,
    Roof,
    Gables,
    Ceiling,
    WallInteriors,
    Trim,
    Wainscot,
    Furniture,
    Porch,
    PorchTimber,
    Glazing,
    UpperFloor,
    Staircase,
    StairwellGuard,
    Partitions
}

internal readonly struct PoiPlacement
{
    public PoiPlacement(PoiPart part, PoiSection section, float x, float y, float z, int yaw = 0)
    {
        Part = part;
        Section = section;
        X = x;
        Y = y;
        Z = z;
        Yaw = yaw;
    }

    public PoiPart Part { get; }
    public PoiSection Section { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public int Yaw { get; }
}

internal sealed class PoiBuildPlan
{
    public PoiBuildPlan(int width, int depth, bool pitched, float height, List<PoiPlacement> parts,
        List<PoiPlacement> wallColumns, List<PoiPlacement> upperWallColumns, PoiStairwell stairwell, int windows)
    {
        Width = width;
        Depth = depth;
        Pitched = pitched;
        Height = height;
        Parts = parts;
        WallColumns = wallColumns;
        UpperWallColumns = upperWallColumns;
        Stairwell = stairwell;
        Windows = windows;
    }

    public int Width { get; }
    public int Depth { get; }
    public bool Pitched { get; }
    public float Height { get; }
    public IReadOnlyList<PoiPlacement> Parts { get; }
    public IReadOnlyList<PoiPlacement> WallColumns { get; }
    public IReadOnlyList<PoiPlacement> UpperWallColumns { get; }
    public PoiStairwell Stairwell { get; }
    public int Windows { get; }
}

[Serializable]
internal struct PoiHouseSettings
{
    public int Width;
    public int Depth;
    public PoiSide Entry;
    public bool Windows;
    public bool Pitched;
    public bool Foundation;
    public bool ModularWalls;
    public bool Wainscot;
    public bool StudyFurniture;
    public bool Porch;
    public bool PorchRails;
    public bool RoofOverhang;
    public bool WindowFrames;
    public bool WindowGlass;
    public bool SecondFloor;

    public static PoiHouseSettings Example => new()
    {
        Width = 6,
        Depth = 8,
        Entry = PoiSide.Front,
        Windows = true,
        Pitched = true,
        ModularWalls = true,
        Porch = true,
        PorchRails = true,
        RoofOverhang = true,
        WindowFrames = true,
        WindowGlass = true
    };
}

internal static class PoiHouseLayout
{
    public const int MIN_SIZE = 4;
    public const int MAX_SIZE = 24;
    public const int WALL_HEIGHT = 3;
    public const float WALL_THICKNESS = 0.25f;
    public const float SLAB_HEIGHT = 0.2f;
    public const float HALF_SLAB_WIDTH = 0.5f;
    public const float SLOPE_THICKNESS = 0.15f;
    public const float PLINTH_THICKNESS = 0.03f;
    private const float PLINTH_TOP_RECESS = 0.006f;

    public static bool Fits(PoiHouseSettings house) =>
        house.Width >= MIN_SIZE && house.Width <= MAX_SIZE &&
        house.Depth >= MIN_SIZE && house.Depth <= MAX_SIZE;

    public static bool FitsPitchedRoof(PoiHouseSettings house) => house.Width % 2 == 0;

    public static PoiBuildPlan Create(PoiHouseSettings house, bool pitched, int ceilingEdgeSide)
    {
        var parts = new List<PoiPlacement>();
        float floorBase = house.Foundation ? 1f : 0f;
        float wallBase = floorBase + SLAB_HEIGHT;
        float wallTop = wallBase + WALL_HEIGHT;

        if (house.Foundation)
            AddGrid(parts, house, PoiPart.Cube, PoiSection.Foundation, 0.5f);

        AddPlinth(parts, house, floorBase);
        AddGrid(parts, house, PoiPart.Slab, PoiSection.Floor, floorBase);
        var wallColumns = new List<PoiPlacement>();
        int windows = AddWalls(wallColumns, house, wallBase, true);
        AddStoreyWalls(parts, wallColumns, house, wallBase);

        if (house.Porch)
            PoiPorchLayout.Add(parts, wallColumns, house, wallBase);

        var upperWallColumns = new List<PoiPlacement>();
        PoiStairwell stairwell = PoiStairwell.None;

        if (house.SecondFloor)
        {
            stairwell = PoiStairwellLayout.Create(house, ceilingEdgeSide);
            AddInteriorSlabs(parts, house, PoiSection.UpperFloor, wallTop - SLAB_HEIGHT, ceilingEdgeSide, stairwell);
            PoiStairwellLayout.Add(parts, stairwell, wallBase, wallTop);
            windows += AddWalls(upperWallColumns, house, wallTop, false);
            AddStoreyWalls(parts, upperWallColumns, house, wallTop);
            AddPartitions(parts, upperWallColumns, house, wallTop, stairwell, true);
            wallTop += WALL_HEIGHT;
        }

        if (house.SecondFloor || !house.StudyFurniture)
            AddPartitions(parts, wallColumns, house, wallBase, stairwell, false);

        if (pitched)
        {
            AddPitchedRoof(parts, house, wallTop);

            if (house.RoofOverhang)
                PoiRoofOverhangLayout.Add(parts, house, wallTop);

            AddInteriorSlabs(parts, house, PoiSection.Ceiling, wallTop - SLAB_HEIGHT, ceilingEdgeSide, PoiStairwell.None);
        }
        else
            AddGrid(parts, house, PoiPart.Slab, PoiSection.Roof, wallTop);

        if (house.StudyFurniture && !house.SecondFloor)
            PoiStudyLayout.AddFurniture(parts, wallColumns, house, wallBase);

        float height = pitched ? wallTop + house.Width / 2f + SLOPE_THICKNESS : wallTop + SLAB_HEIGHT;
        return new PoiBuildPlan(house.Width, house.Depth, pitched, height, parts, wallColumns, upperWallColumns, stairwell, windows);
    }

    private static void AddPlinth(List<PoiPlacement> parts, PoiHouseSettings house, float floorBase)
    {
        float x = house.Width * 0.5f + PLINTH_THICKNESS * 0.5f;
        float z = house.Depth * 0.5f + PLINTH_THICKNESS * 0.5f;
        float y = floorBase - PLINTH_TOP_RECESS;

        for (int i = 0; i < house.Width; i++)
        {
            float center = CellCenter(i, house.Width);
            parts.Add(new PoiPlacement(PoiPart.Plinth, PoiSection.Foundation, center, y, z));
            parts.Add(new PoiPlacement(PoiPart.Plinth, PoiSection.Foundation, center, y, -z, 180));
        }

        for (int i = 0; i < house.Depth; i++)
        {
            float center = CellCenter(i, house.Depth);
            parts.Add(new PoiPlacement(PoiPart.Plinth, PoiSection.Foundation, x, y, center, 90));
            parts.Add(new PoiPlacement(PoiPart.Plinth, PoiSection.Foundation, -x, y, center, -90));
        }
    }

    private static void AddPartitions(List<PoiPlacement> parts, List<PoiPlacement> walls, PoiHouseSettings house,
        float y, PoiStairwell stairwell, bool upperFloor)
    {
        int rows = house.Depth - 1;
        float x = 0f;
        int doorway = rows / 2;

        if (stairwell.HasOpening)
        {
            float side = stairwell.X > 0f ? -1f : 1f;
            x = stairwell.X + side;
            doorway = upperFloor ? stairwell.FirstRow - 1 : stairwell.FirstRow + PoiStairwellLayout.LENGTH;
        }

        int firstRow = MeetsOpening(walls, x, 180) ? 1 : 0;
        int endRow = MeetsOpening(walls, x, 0) ? rows - 1 : rows;
        bool belowUpperFloor = house.SecondFloor && !upperFloor;
        doorway = Math.Max(firstRow, Math.Min(doorway, endRow - 1));

        for (int row = firstRow; row < endRow; row++)
        {
            float z = CellCenter(row, rows);
            var column = new PoiPlacement(row == doorway ? PoiPart.Doorway : PoiPart.Wall, PoiSection.Partitions, x, y, z, 90);
            PoiWallLayout.AddPartitionColumn(parts, column, belowUpperFloor);
        }
    }

    private static bool MeetsOpening(List<PoiPlacement> walls, float x, int yaw)
    {
        foreach (PoiPlacement wall in walls)
        {
            if (wall.Yaw == yaw && wall.Part != PoiPart.Wall &&
                Math.Abs(wall.X - x) < (PoiWallLayout.OPENING_WIDTH + WALL_THICKNESS) * 0.5f)
                return true;
        }

        return false;
    }

    private static void AddStoreyWalls(List<PoiPlacement> parts, List<PoiPlacement> columns, PoiHouseSettings house, float baseY)
    {
        foreach (PoiPlacement column in columns)
            PoiWallLayout.AddColumn(parts, column, house);

        PoiWallLayout.AddExteriorCorners(parts, house, baseY);
    }

    private static void AddGrid(List<PoiPlacement> parts, PoiHouseSettings house, PoiPart part, PoiSection section, float y)
    {
        for (int z = 0; z < house.Depth; z++)
        {
            for (int x = 0; x < house.Width; x++)
                parts.Add(new PoiPlacement(part, section, CellCenter(x, house.Width), y, CellCenter(z, house.Depth)));
        }
    }

    private static int AddWalls(List<PoiPlacement> parts, PoiHouseSettings house, float y, bool hasEntrance)
    {
        int windows = 0;
        float end = EndWallCenter(house.Depth);
        float side = SideWallCenter(house.Width);

        for (int x = 0; x < house.Width; x++)
        {
            float center = CellCenter(x, house.Width);
            windows += AddWall(parts, house.Windows, hasEntrance && house.Entry == PoiSide.Front, x, house.Width, center, y, end, 0);
            windows += AddWall(parts, house.Windows, hasEntrance && house.Entry == PoiSide.Back, house.Width - 1 - x, house.Width, center, y, -end, 180);
        }

        int sideLength = house.Depth - 1;

        for (int z = 0; z < sideLength; z++)
        {
            float center = CellCenter(z, sideLength);
            windows += AddWall(parts, house.Windows, hasEntrance && house.Entry == PoiSide.Right, sideLength - 1 - z, sideLength, side, y, center, 90);
            windows += AddWall(parts, house.Windows, hasEntrance && house.Entry == PoiSide.Left, z, sideLength, -side, y, center, -90);
        }

        return windows;
    }

    private static int AddWall(List<PoiPlacement> parts, bool windows, bool entrance, int index, int length,
        float x, float y, float z, int yaw)
    {
        int center = (length - 1) / 2;
        var part = PoiPart.Wall;

        if (entrance && index == center)
            part = PoiPart.Doorway;
        else if (windows && index > 0 && index < length - 1)
        {
            bool isWindow = length >= 6 ? index == 1 || index == length - 2 : !entrance && index == center;

            if (isWindow)
                part = PoiPart.Window;
        }

        parts.Add(new PoiPlacement(part, PoiSection.Walls, x, y, z, yaw));
        return part == PoiPart.Window ? 1 : 0;
    }

    private static void AddPitchedRoof(List<PoiPlacement> parts, PoiHouseSettings house, float wallTop)
    {
        float end = EndWallCenter(house.Depth);

        for (int step = 0; step < house.Width / 2; step++)
        {
            float left = CellCenter(step, house.Width);
            float y = wallTop + step;

            for (int z = 0; z < house.Depth; z++)
            {
                float center = CellCenter(z, house.Depth);
                parts.Add(new PoiPlacement(PoiPart.Slope, PoiSection.Roof, left, y, center, 90));
                parts.Add(new PoiPlacement(PoiPart.Slope, PoiSection.Roof, -left, y, center, -90));
            }

            AddGableColumn(parts, left, wallTop, end, step, 0);
            AddGableColumn(parts, left, wallTop, -end, step, 0);
            AddGableColumn(parts, -left, wallTop, end, step, 180);
            AddGableColumn(parts, -left, wallTop, -end, step, 180);
        }
    }

    private static void AddGableColumn(List<PoiPlacement> parts, float x, float wallTop, float z, int rise, int yaw)
    {
        for (int level = 0; level < rise; level++)
            parts.Add(new PoiPlacement(PoiPart.WallPanel, PoiSection.Gables, x, wallTop + level, z));

        parts.Add(new PoiPlacement(PoiPart.Gable, PoiSection.Gables, x, wallTop + rise, z, yaw));
    }

    private static void AddInteriorSlabs(List<PoiPlacement> parts, PoiHouseSettings house, PoiSection section,
        float y, int edgeSide, PoiStairwell stairwell)
    {
        int rows = house.Depth - 1;
        int columns = house.Width - 1;
        float edgePivot = (house.Width * 0.5f - WALL_THICKNESS) * edgeSide;

        for (int z = 0; z < rows; z++)
        {
            float centerZ = CellCenter(z, rows);

            for (int x = 0; x < columns; x++)
            {
                if (!stairwell.Contains(x, z))
                    parts.Add(new PoiPlacement(PoiPart.Slab, section, InteriorColumnCenter(x, columns, edgeSide), y, centerZ));
            }

            parts.Add(new PoiPlacement(PoiPart.SlabHalf, section, edgePivot, y, centerZ));
        }
    }

    internal static float CellCenter(int index, int length) => index + 0.5f - length * 0.5f;
    internal static float InteriorColumnCenter(int index, int columns, int edgeSide) =>
        CellCenter(index, columns) - HALF_SLAB_WIDTH * 0.5f * edgeSide;
    internal static float EndWallCenter(int depth) => (depth - 1) * 0.5f + WALL_THICKNESS * 0.5f;
    private static float SideWallCenter(int width) => (width - WALL_THICKNESS) * 0.5f;
}
