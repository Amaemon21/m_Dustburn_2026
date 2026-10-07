using System;
using Unity.Collections;
using Unity.Mathematics;

public partial class RoadPlanner
{
    private const byte NO_CORRIDOR = 0;
    private const byte ON_CORRIDOR = 1;
    private const byte NEAR_CORRIDOR = 2;

    private struct SourceData
    {
        public int Cell;
        public int Heading;
        public float Cost;
        public bool Direct;
        public bool Gateway;
        public float2 Tangent;
    }

    private struct GoalData
    {
        public bool Direct;
        public bool Gateway;
        public bool Tangential;
        public int Arrival;
        public float Cost;
        public float2 Tangent;
    }

    private readonly struct SearchPlan
    {
        public readonly int Level;
        public readonly bool Exact;
        public readonly float HardGrade;
        public readonly float HardCross;

        public SearchPlan(int level, bool exact, float hardGrade, float hardCross)
        {
            Level = level;
            Exact = exact;
            HardGrade = hardGrade;
            HardCross = hardCross;
        }
    }

    private sealed class SearchSpace : IDisposable
    {
        public NativeArray<float> StepCost;
        public NativeArray<float> Grade;
        public NativeArray<float> Cross;
        public NativeArray<float> TurnCost;
        public NativeArray<float> TurnRadius;
        public NativeArray<float> StepLength;
        public NativeArray<float2> Headings;
        public NativeArray<int> StepOffset;
        public NativeArray<int> StepX;
        public NativeArray<int> StepY;

        public NativeArray<float> Penalty;
        public NativeArray<bool> Blocked;

        public NativeArray<float> Heuristic;
        public NativeArray<int> GoalIndex;
        public NativeArray<ushort> ApproachMask;
        public NativeArray<ushort> RideMask;
        public NativeArray<bool> NearPoint;
        public NativeArray<byte> CorridorKind;
        public NativeArray<float2> CorridorTangent;
        public NativeArray<SourceData> Sources;
        public NativeArray<GoalData> Goals;

        public NativeArray<float> Cost;
        public NativeArray<int> Previous;
        public NativeArray<bool> Closed;
        public NativeList<int> HeapItems;
        public NativeList<float> HeapPriorities;
        public NativeArray<int> SourceIndex;
        public NativeArray<ushort> StepMask;
        public NativeArray<byte> ReachMark;
        public NativeArray<int> ReachQueue;
        public NativeArray<int> Result;

        public SearchSpace(int cellCount)
        {
            Penalty = new NativeArray<float>(cellCount, Allocator.Persistent);
            Blocked = new NativeArray<bool>(cellCount, Allocator.Persistent);
            Heuristic = new NativeArray<float>(cellCount, Allocator.Persistent);
            GoalIndex = new NativeArray<int>(cellCount, Allocator.Persistent);
            ApproachMask = new NativeArray<ushort>(cellCount, Allocator.Persistent);
            RideMask = new NativeArray<ushort>(cellCount, Allocator.Persistent);
            NearPoint = new NativeArray<bool>(cellCount, Allocator.Persistent);
            CorridorKind = new NativeArray<byte>(cellCount, Allocator.Persistent);
            CorridorTangent = new NativeArray<float2>(cellCount, Allocator.Persistent);
            Sources = new NativeArray<SourceData>(1, Allocator.Persistent);
            Goals = new NativeArray<GoalData>(1, Allocator.Persistent);

            Cost = new NativeArray<float>(cellCount * STATES, Allocator.Persistent);
            Previous = new NativeArray<int>(cellCount * STATES, Allocator.Persistent);
            Closed = new NativeArray<bool>(cellCount * STATES, Allocator.Persistent);
            HeapItems = new NativeList<int>(cellCount / 2, Allocator.Persistent);
            HeapPriorities = new NativeList<float>(cellCount / 2, Allocator.Persistent);
            SourceIndex = new NativeArray<int>(cellCount, Allocator.Persistent);
            StepMask = new NativeArray<ushort>(cellCount, Allocator.Persistent);
            ReachMark = new NativeArray<byte>(cellCount, Allocator.Persistent);
            ReachQueue = new NativeArray<int>(cellCount * 2, Allocator.Persistent);
            Result = new NativeArray<int>(2, Allocator.Persistent);
        }

        public void Statics(float[] stepCost, float[] grade, float[] cross, float[] turnCost, float[] turnRadius, float[] stepLength, float2[] headings, int[] stepOffset, int[] stepX, int[] stepY)
        {
            StepCost = new NativeArray<float>(stepCost, Allocator.Persistent);
            Grade = new NativeArray<float>(grade, Allocator.Persistent);
            Cross = new NativeArray<float>(cross, Allocator.Persistent);
            TurnCost = new NativeArray<float>(turnCost, Allocator.Persistent);
            TurnRadius = new NativeArray<float>(turnRadius, Allocator.Persistent);
            StepLength = new NativeArray<float>(stepLength, Allocator.Persistent);
            Headings = new NativeArray<float2>(headings, Allocator.Persistent);
            StepOffset = new NativeArray<int>(stepOffset, Allocator.Persistent);
            StepX = new NativeArray<int>(stepX, Allocator.Persistent);
            StepY = new NativeArray<int>(stepY, Allocator.Persistent);
        }

        public void Endpoints(SourceData[] sources, GoalData[] goals)
        {
            Sources.Dispose();
            Goals.Dispose();
            Sources = new NativeArray<SourceData>(sources.Length > 0 ? sources : new SourceData[1], Allocator.Persistent);
            Goals = new NativeArray<GoalData>(goals.Length > 0 ? goals : new GoalData[1], Allocator.Persistent);
        }

        public void Dispose()
        {
            NativeBuffer.Release(ref StepCost);
            NativeBuffer.Release(ref Grade);
            NativeBuffer.Release(ref Cross);
            NativeBuffer.Release(ref TurnCost);
            NativeBuffer.Release(ref TurnRadius);
            NativeBuffer.Release(ref StepLength);
            NativeBuffer.Release(ref Headings);
            NativeBuffer.Release(ref StepOffset);
            NativeBuffer.Release(ref StepX);
            NativeBuffer.Release(ref StepY);
            NativeBuffer.Release(ref Penalty);
            NativeBuffer.Release(ref Blocked);
            NativeBuffer.Release(ref Heuristic);
            NativeBuffer.Release(ref GoalIndex);
            NativeBuffer.Release(ref ApproachMask);
            NativeBuffer.Release(ref RideMask);
            NativeBuffer.Release(ref NearPoint);
            NativeBuffer.Release(ref CorridorKind);
            NativeBuffer.Release(ref CorridorTangent);
            NativeBuffer.Release(ref Sources);
            NativeBuffer.Release(ref Goals);
            NativeBuffer.Release(ref Cost);
            NativeBuffer.Release(ref Previous);
            NativeBuffer.Release(ref Closed);
            NativeBuffer.Release(ref SourceIndex);
            NativeBuffer.Release(ref StepMask);
            NativeBuffer.Release(ref ReachMark);
            NativeBuffer.Release(ref ReachQueue);
            NativeBuffer.Release(ref Result);

            if (HeapItems.IsCreated)
                HeapItems.Dispose();

            if (HeapPriorities.IsCreated)
                HeapPriorities.Dispose();
        }
    }
}
