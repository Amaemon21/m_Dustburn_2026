using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static partial class RiverCarver
{
    private struct CarveSegment
    {
        public Vector2 A;
        public Vector2 B;
        public float SurfaceA;
        public float SurfaceB;
        public float BedA;
        public float BedB;
        public float WidthA;
        public float WidthB;
        public float Reach;
        public float AlongA;
        public float AlongB;
        public float Length;
        public bool OpenStart;
        public bool OpenEnd;
        public bool TaperStart;
        public bool TaperEnd;

        public static CarveSegment Of(Segment segment)
        {
            return new CarveSegment
            {
                A = segment.A.Position,
                B = segment.B.Position,
                SurfaceA = segment.A.Surface,
                SurfaceB = segment.B.Surface,
                BedA = segment.A.Bed,
                BedB = segment.B.Bed,
                WidthA = segment.A.Width,
                WidthB = segment.B.Width,
                Reach = segment.Reach,
                AlongA = segment.AlongA,
                AlongB = segment.AlongB,
                Length = segment.Course.Along.Length > 0 ? segment.Course.Along[^1] : 0f,
                OpenStart = segment.OpenStart,
                OpenEnd = segment.OpenEnd,
                TaperStart = segment.Course.TaperStart,
                TaperEnd = segment.Course.TaperEnd && segment.Course.Along.Length > 0
            };
        }
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Strict, CompileSynchronously = true, DisableSafetyChecks = true)]
    private struct CarveJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<CarveSegment> Segments;
        [ReadOnly] public NativeArray<int> Members;
        [ReadOnly] public NativeArray<int2> Ranges;
        [ReadOnly] public NativeArray<int4> Bounds;
        [ReadOnly] public NativeArray<int> Offsets;
        [ReadOnly] public NativeArray<short> NearLake;
        [ReadOnly] public NativeArray<byte> NearDistance;
        [ReadOnly] public NativeArray<float> Surfaces;
        [NativeDisableParallelForRestriction] public NativeArray<float> Heights;

        public float Cell;
        public float MaxHeight;
        public float BankWidth;
        public float WaterCell;
        public int WaterResolution;
        public int GuardReach;
        public float2 Left;
        public float2 Right;

        public void Execute(int job)
        {
            int4 bounds = Bounds[job];
            int minX = bounds.x, minZ = bounds.y, maxX = bounds.z, maxZ = bounds.w;
            int width = maxX - minX + 1;
            int count = width * (maxZ - minZ + 1);
            int offset = Offsets[job];

            var bank = new NativeArray<float>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var channel = new NativeArray<float>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var edge = new NativeArray<float>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < count; i++)
            {
                bank[i] = float.PositiveInfinity;
                channel[i] = float.PositiveInfinity;
                edge[i] = float.NegativeInfinity;
            }

            int2 range = Ranges[job];

            for (int m = range.x; m < range.x + range.y; m++)
                Stamp(Segments[Members[m]], minX, minZ, maxX, maxZ, width, offset, bank, channel, edge);

            for (int local = 0; local < count; local++)
            {
                if (float.IsPositiveInfinity(bank[local]))
                    continue;

                float carved = Mathf.Min(Mathf.Max(bank[local], edge[local]), channel[local]) / MaxHeight;

                if (carved < Heights[offset + local])
                    Heights[offset + local] = carved;
            }

            bank.Dispose();
            channel.Dispose();
            edge.Dispose();
        }

        private void Stamp(CarveSegment segment, int tileMinX, int tileMinZ, int tileMaxX, int tileMaxZ, int width, int offset,
            NativeArray<float> bank, NativeArray<float> channel, NativeArray<float> edge)
        {
            Vector2 a = segment.A;
            Vector2 b = segment.B;
            int minX = Mathf.Max(tileMinX, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - segment.Reach) / Cell));
            int maxX = Mathf.Min(tileMaxX, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + segment.Reach) / Cell));
            int minZ = Mathf.Max(tileMinZ, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - segment.Reach) / Cell));
            int maxZ = Mathf.Min(tileMaxZ, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + segment.Reach) / Cell));

            Vector2 axis = b - a;
            float length = axis.sqrMagnitude;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var point = new Vector2(x * Cell, z * Cell);
                    float along = length < 1e-8f ? 0f : Vector2.Dot(point - a, axis) / length;

                    if (segment.OpenStart && along < 0f || segment.OpenEnd && along > 1f)
                        continue;

                    float t = Mathf.Clamp01(along);
                    float distance = Vector2.Distance(point, a + axis * t);

                    if (distance > segment.Reach)
                        continue;

                    int local = (z - tileMinZ) * width + x - tileMinX;
                    float ground = Heights[offset + local] * MaxHeight;
                    float ribbon = 0.5f * Mathf.Lerp(segment.WidthA, segment.WidthB, t);
                    Vector2 foot = a + axis * t;
                    float half = Half(segment, ribbon, foot, Side(axis, point - a), Mathf.Lerp(segment.AlongA, segment.AlongB, t));
                    float surface = Mathf.Lerp(segment.SurfaceA, segment.SurfaceB, t);

                    float target = Profile(distance, half, BankWidth, Mathf.Lerp(segment.BedA, segment.BedB, t), surface, ground);

                    if (distance > half)
                        target = HoldLake(x * Cell, z * Cell, ground, target, surface + LAKE_BANK_GRADE * (distance - half));

                    if (distance <= half)
                        channel[local] = Mathf.Min(channel[local], target);
                    else if (distance <= ribbon + WaterMeshes.RIBBON_PAD + FLOOR_REACH)
                        edge[local] = Mathf.Max(edge[local], Mathf.Min(target, Floor(distance - ribbon - WaterMeshes.RIBBON_PAD, surface)));

                    bank[local] = Mathf.Min(bank[local], target);
                }
            }
        }

        private float Half(CarveSegment segment, float half, Vector2 foot, float side, float along)
        {
            float shaped = Wander(half, foot, side, Left, Right);

            if (segment.TaperStart)
                shaped *= Tip(along, half);

            if (segment.TaperEnd)
                shaped *= Tip(segment.Length - along, half);

            return shaped;
        }

        private float HoldLake(float x, float z, float ground, float target, float bank)
        {
            int column = Mathf.Clamp((int)(x / WaterCell), 0, WaterResolution - 1);
            int row = Mathf.Clamp((int)(z / WaterCell), 0, WaterResolution - 1);
            int lake = NearLake[row * WaterResolution + column];

            if (lake < 0)
                return target;

            float weight = Mathf.Clamp01((GuardReach - GuardDistance(x, z)) / GUARD_FADE_CELLS);

            if (weight <= 0f)
                return target;

            float floor = Mathf.Min(Surfaces[lake], bank) + EDGE_LIFT;
            float held = ground >= floor ? Mathf.Max(target, floor) : ground;

            return Mathf.Lerp(target, held, weight);
        }

        private float GuardDistance(float x, float z)
        {
            int last = WaterResolution - 1;
            float u = Mathf.Clamp(x / WaterCell - 0.5f, 0f, last);
            float v = Mathf.Clamp(z / WaterCell - 0.5f, 0f, last);
            int column = Mathf.Min((int)u, last - 1);
            int row = Mathf.Min((int)v, last - 1);
            int origin = row * WaterResolution + column;
            float low = Mathf.Lerp(NearDistance[origin], NearDistance[origin + 1], u - column);
            float high = Mathf.Lerp(NearDistance[origin + WaterResolution], NearDistance[origin + WaterResolution + 1], u - column);

            return Mathf.Lerp(low, high, v - row);
        }
    }
}
