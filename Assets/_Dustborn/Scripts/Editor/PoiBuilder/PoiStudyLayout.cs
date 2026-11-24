using System;
using System.Collections.Generic;

internal static class PoiStudyLayout
{
    private const float DESK_TOP = 1.009f;
    private const float CLOCK_OFFSET = 0.006f;

    public static void AddFurniture(List<PoiPlacement> parts, List<PoiPlacement> columns, PoiHouseSettings house, float floor)
    {
        bool useFront = house.Entry == PoiSide.Back;
        int direction = useFront ? 1 : -1;
        int inwardYaw = useFront ? 180 : 0;
        float innerWall = direction * (house.Depth - 1) * 0.5f;
        float deskX = -0.55f * direction;
        float deskZ = innerWall - direction * 0.56f;
        Add(parts, PoiPart.StudyDesk, deskX, floor, deskZ, inwardYaw);
        Add(parts, PoiPart.StudyChair, deskX, floor, deskZ - direction * 0.84f, inwardYaw + 180);
        Add(parts, PoiPart.StudyLamp, deskX - direction * 0.48f, floor + DESK_TOP, deskZ + direction * 0.1f, inwardYaw);
        float clockX = FindClockColumn(columns, useFront ? 0 : 180, deskX + direction * 0.62f);
        Add(parts, PoiPart.StudyClock, clockX, floor + 1.94f, innerWall - direction * CLOCK_OFFSET, inwardYaw);
    }

    private static float FindClockColumn(List<PoiPlacement> columns, int yaw, float preferredX)
    {
        float bestDistance = float.PositiveInfinity;
        float clockX = preferredX;

        foreach (PoiPlacement column in columns)
        {
            if (column.Yaw != yaw || column.Part != PoiPart.Wall)
                continue;

            float candidate = Math.Max(column.X - 0.27f, Math.Min(column.X + 0.27f, preferredX));
            float distance = Math.Abs(candidate - preferredX);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            clockX = candidate;
        }

        return clockX;
    }

    private static void Add(List<PoiPlacement> parts, PoiPart part, float x, float y, float z, int yaw) =>
        parts.Add(new PoiPlacement(part, PoiSection.Furniture, x, y, z, yaw));
}
