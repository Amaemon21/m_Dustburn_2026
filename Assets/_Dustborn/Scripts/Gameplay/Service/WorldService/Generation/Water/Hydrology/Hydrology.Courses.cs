using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Hydrology
{
    private sealed class CourseSeed
    {
        public List<int> Cells;
        public int JoinPath;
        public int JoinCell;
        public List<Vector2> Points;
        public List<float> Areas;
        public float MaxWidth = MAX_WIDTH;
        public bool Main;
        public int Index;
    }

    private List<RiverCourse> BuildCourses(HydrologyGrid grid, List<(List<int> Cells, int JoinPath, int JoinCell)> paths, WaterMap water)
    {
        var seeds = new List<CourseSeed>(paths.Count);
        bool main = MainCells > 0;

        for (int p = 0; p < paths.Count; p++)
        {
            Macro(grid, paths[p].Cells, out List<Vector2> points, out List<float> areas);
            seeds.Add(new CourseSeed { Cells = paths[p].Cells, JoinPath = paths[p].JoinPath, JoinCell = paths[p].JoinCell, Points = points, Areas = areas, Index = p, Main = main && p == 0, MaxWidth = main && p == 0 ? MainWidth : MAX_WIDTH });
        }

        return _stamps == null ? Courses(grid, seeds) : StampedCourses(grid, seeds, water);
    }

    private List<RiverCourse> Courses(HydrologyGrid grid, List<CourseSeed> seeds)
    {
        var courses = new List<RiverCourse>(seeds.Count);

        foreach (CourseSeed seed in seeds)
        {
            Meander(seed.Points, seed.Areas, grid.CellSize, seed.Index);
            Settle(seed.Points, seed.Areas, grid.CellSize);
            Bend(seed.Points, seed.Areas, seed.MaxWidth);
            courses.Add(new RiverCourse { Points = seed.Points, Areas = seed.Areas, JoinPath = seed.JoinPath, JoinCell = seed.JoinCell, Cells = seed.Cells, MaxWidth = seed.MaxWidth, Main = seed.Main });
        }

        return courses;
    }

    private List<RiverCourse> StampedCourses(HydrologyGrid grid, List<CourseSeed> seeds, WaterMap water)
    {
        var macros = new List<WaterStampMacro>(seeds.Count);

        foreach (CourseSeed seed in seeds)
            macros.Add(new WaterStampMacro { Points = seed.Points, Areas = seed.Areas, JoinPath = seed.JoinPath });

        var composer = new WaterStampComposer(_config, _stamps, _map, water, this, Stamps, grid.CellSize, (points, areas, index) =>
        {
            Meander(points, areas, grid.CellSize, index);
            Settle(points, areas, grid.CellSize);
        });

        WaterStampCourse[] composed;

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterStamps))
            composed = composer.Compose(macros);

        var courses = new List<RiverCourse>(seeds.Count + composer.Forks.Count);

        for (int p = 0; p < seeds.Count; p++)
        {
            Bend(composed[p].Points, composed[p].Areas, seeds[p].MaxWidth);
            courses.Add(new RiverCourse { Points = composed[p].Points, Areas = composed[p].Areas, JoinPath = seeds[p].JoinPath, JoinCell = seeds[p].JoinCell, Cells = seeds[p].Cells, Stamp = composed[p], MaxWidth = seeds[p].MaxWidth, Main = seeds[p].Main });
        }

        foreach (WaterStampCourse fork in composer.Forks)
        {
            Bend(fork.Points, fork.Areas, MAX_WIDTH);
            courses.Add(new RiverCourse { Points = fork.Points, Areas = fork.Areas, JoinPath = -1, JoinCell = -1, Cells = new List<int>(), Stamp = fork });
        }

        return courses;
    }

    private static int NearestIndex(List<Vector2> line, Vector2 point)
    {
        int best = 0;
        float nearest = float.MaxValue;

        for (int i = 0; i < line.Count; i++)
        {
            float distance = (line[i] - point).sqrMagnitude;

            if (distance >= nearest)
                continue;

            nearest = distance;
            best = i;
        }

        return best;
    }

    private static void Truncate(RiverCourse course, int count)
    {
        course.Points.RemoveRange(count, course.Points.Count - count);
        course.Areas.RemoveRange(count, course.Areas.Count - count);

        if (course.Stamp == null)
            return;

        Array.Resize(ref course.Stamp.WidthScale, count);
        Array.Resize(ref course.Stamp.DepthScale, count);

        if (course.Stamp.Track == null)
            return;

        Array.Resize(ref course.Stamp.Track.Placement, count);
        Array.Resize(ref course.Stamp.Track.Stamp, count);
    }

    private static void Extend(WaterStampCourse stamp, int before, int count)
    {
        if (stamp == null)
            return;

        Array.Resize(ref stamp.WidthScale, count);
        Array.Resize(ref stamp.DepthScale, count);

        for (int i = before; i < count; i++)
        {
            stamp.WidthScale[i] = before > 0 ? stamp.WidthScale[before - 1] : 1f;
            stamp.DepthScale[i] = before > 0 ? stamp.DepthScale[before - 1] : 1f;
        }

        if (stamp.Track == null)
            return;

        Array.Resize(ref stamp.Track.Placement, count);
        Array.Resize(ref stamp.Track.Stamp, count);

        for (int i = before; i < count; i++)
            stamp.Track.Placement[i] = -1;
    }
}
