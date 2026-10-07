using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public partial class RoadPlanner
{
    private const byte REACHED = 1;
    private const byte LEFT_AS_SOURCE = 2;

    [BurstCompile(FloatPrecision.Standard, FloatMode.Strict, CompileSynchronously = true, DisableSafetyChecks = true)]
    private struct SearchJob : IJob
    {
        [ReadOnly] public NativeArray<float> StepCost;
        [ReadOnly] public NativeArray<float> Grade;
        [ReadOnly] public NativeArray<float> Cross;
        [ReadOnly] public NativeArray<float> TurnCost;
        [ReadOnly] public NativeArray<float> TurnRadius;
        [ReadOnly] public NativeArray<float> StepLength;
        [ReadOnly] public NativeArray<float2> Headings;
        [ReadOnly] public NativeArray<int> StepOffset;
        [ReadOnly] public NativeArray<int> StepX;
        [ReadOnly] public NativeArray<int> StepY;
        [ReadOnly] public NativeArray<float> Penalty;
        [ReadOnly] public NativeArray<bool> Blocked;
        [ReadOnly] public NativeArray<float> Heuristic;
        [ReadOnly] public NativeArray<int> GoalIndex;
        [ReadOnly] public NativeArray<ushort> ApproachMask;
        [ReadOnly] public NativeArray<ushort> RideMask;
        [ReadOnly] public NativeArray<bool> NearPoint;
        [ReadOnly] public NativeArray<byte> CorridorKind;
        [ReadOnly] public NativeArray<float2> CorridorTangent;
        [ReadOnly] public NativeArray<SourceData> Sources;
        [ReadOnly] public NativeArray<GoalData> Goals;

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

        public int Resolution;
        public int SourceCount;
        public float HardGrade;
        public float HardCross;
        public float MinTurnRadius;
        public float StrictTurnRadius;
        public float SinJunction;
        public float ReuseDiscount;
        public float ParallelPenalty;
        public float JunctionPenalty;
        public bool Exact;

        private int _heapCount;

        public void Execute()
        {
            Result[0] = -1;
            Result[1] = -1;

            for (int i = 0; i < Cost.Length; i++)
            {
                Cost[i] = float.MaxValue;
                Previous[i] = -1;
                Closed[i] = false;
            }

            for (int i = 0; i < SourceIndex.Length; i++)
                SourceIndex[i] = -1;

            _heapCount = 0;
            HeapItems.Clear();
            HeapPriorities.Clear();

            for (int i = 0; i < SourceCount; i++)
            {
                SourceData source = Sources[i];
                int state = source.Cell * STATES + source.Heading;

                if (source.Cost >= Cost[state])
                    continue;

                Cost[state] = source.Cost;
                SourceIndex[source.Cell] = i;
                Push(state, source.Cost + Heuristic[source.Cell]);
            }

            BuildStepMask();

            if (!GoalReachable())
                return;

            var turnMask = new NativeArray<ushort>(STATES, Allocator.Temp);
            var strictMask = new NativeArray<ushort>(STATES, Allocator.Temp);
            BuildTurnMask(turnMask, strictMask);

            float bestTotal = float.MaxValue;
            int bestState = -1;
            int bestGoal = -1;

            while (TryPop(out int current))
            {
                if (Closed[current])
                    continue;

                Closed[current] = true;

                int cell = current / STATES;

                if (Cost[current] + Heuristic[cell] >= bestTotal)
                    break;

                int heading = current % STATES;
                int goalIndex = GoalIndex[cell];

                if (goalIndex >= 0 && Previous[current] >= 0)
                {
                    float total = Cost[current] + Terminal(Goals[goalIndex], heading);

                    if (total < bestTotal)
                    {
                        bestTotal = total;
                        bestState = current;
                        bestGoal = goalIndex;
                    }
                }

                int sourceIndex = Previous[current] < 0 ? SourceIndex[cell] : -1;
                int allowed = StepMask[cell] & turnMask[heading];
                int strict = strictMask[heading];

                for (int step = 0; step < HEADINGS; step++)
                {
                    if ((allowed & (1 << step)) == 0)
                        continue;

                    int nextCell = cell + StepOffset[step];
                    int next = nextCell * STATES + step;

                    if (Closed[next])
                        continue;

                    if ((strict & (1 << step)) != 0 && NearPoint[nextCell])
                        continue;

                    int index = cell * HEADINGS + step;
                    bool ride = (RideMask[cell] & (1 << step)) != 0;

                    if (sourceIndex >= 0 && !Sources[sourceIndex].Direct && math.abs(CrossOf(Headings[step], Sources[sourceIndex].Tangent)) < SinJunction)
                        continue;

                    if (Exact && sourceIndex >= 0 && Sources[sourceIndex].Gateway && heading != FREE_HEADING && step != heading)
                        continue;

                    float stepCost = ride
                        ? StepLength[step] * ReuseDiscount * Penalty[nextCell]
                        : CorridorCost(nextCell, step, StepCost[index] * Penalty[nextCell], sourceIndex >= 0);
                    float candidate = Cost[current] + stepCost + TurnCost[heading * HEADINGS + step];

                    if (candidate >= Cost[next])
                        continue;

                    Cost[next] = candidate;
                    Previous[next] = current;
                    Push(next, candidate + Heuristic[nextCell]);
                }
            }

            turnMask.Dispose();
            strictMask.Dispose();

            Result[0] = bestState;
            Result[1] = bestGoal;
        }

        private void BuildStepMask()
        {
            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    int cell = y * Resolution + x;
                    int mask = 0;

                    for (int step = 0; step < HEADINGS; step++)
                    {
                        int nextX = x + StepX[step];
                        int nextY = y + StepY[step];

                        if (nextX < 0 || nextY < 0 || nextX >= Resolution || nextY >= Resolution)
                            continue;

                        int nextCell = nextY * Resolution + nextX;

                        if (Blocked[nextCell] && GoalIndex[nextCell] < 0)
                            continue;

                        if ((ApproachMask[nextCell] & (1 << step)) == 0)
                            continue;

                        int index = cell * HEADINGS + step;

                        if ((Grade[index] > HardGrade || Cross[index] > HardCross) && (RideMask[cell] & (1 << step)) == 0)
                            continue;

                        mask |= 1 << step;
                    }

                    StepMask[cell] = (ushort)mask;
                }
            }
        }

        private void BuildTurnMask(NativeArray<ushort> turnMask, NativeArray<ushort> strictMask)
        {
            for (int heading = 0; heading < STATES; heading++)
            {
                int allowed = 0;
                int strict = 0;

                for (int step = 0; step < HEADINGS; step++)
                {
                    float turn = TurnRadius[heading * HEADINGS + step];

                    if (turn < MinTurnRadius)
                        continue;

                    allowed |= 1 << step;

                    if (turn < StrictTurnRadius)
                        strict |= 1 << step;
                }

                turnMask[heading] = (ushort)allowed;
                strictMask[heading] = (ushort)strict;
            }
        }

        private bool GoalReachable()
        {
            for (int i = 0; i < ReachMark.Length; i++)
                ReachMark[i] = 0;

            int head = 0;
            int tail = 0;

            for (int i = 0; i < SourceCount; i++)
            {
                int cell = Sources[i].Cell;

                if (SourceIndex[cell] < 0 || (ReachMark[cell] & LEFT_AS_SOURCE) != 0)
                    continue;

                ReachMark[cell] = (byte)(ReachMark[cell] | LEFT_AS_SOURCE);
                ReachQueue[tail++] = cell;
            }

            while (head < tail)
            {
                int cell = ReachQueue[head++];
                bool asSource = (ReachMark[cell] & REACHED) == 0;
                int allowed = StepMask[cell];

                for (int step = 0; step < HEADINGS; step++)
                {
                    if ((allowed & (1 << step)) == 0)
                        continue;

                    int nextCell = cell + StepOffset[step];

                    if ((ReachMark[nextCell] & REACHED) != 0 && GoalIndex[nextCell] < 0)
                        continue;

                    if (asSource && !LeavesSource(cell, step))
                        continue;

                    if (GoalIndex[nextCell] >= 0 && Terminal(Goals[GoalIndex[nextCell]], step) < float.MaxValue)
                        return true;

                    if ((ReachMark[nextCell] & REACHED) != 0)
                        continue;

                    ReachMark[nextCell] = (byte)(ReachMark[nextCell] | REACHED);
                    ReachQueue[tail++] = nextCell;
                }
            }

            return false;
        }

        private bool LeavesSource(int cell, int step)
        {
            SourceData source = Sources[SourceIndex[cell]];

            if (!source.Direct && math.abs(CrossOf(Headings[step], source.Tangent)) < SinJunction)
                return false;

            if (!Exact || !source.Gateway)
                return true;

            for (int i = 0; i < SourceCount; i++)
            {
                SourceData other = Sources[i];

                if (other.Cell == cell && (other.Heading == FREE_HEADING || other.Heading == step))
                    return true;
            }

            return false;
        }

        private float Terminal(GoalData goal, int heading)
        {
            if (heading == FREE_HEADING)
                return goal.Direct ? 0f : float.MaxValue;

            if (goal.Direct)
            {
                if (!goal.Tangential)
                    return goal.Cost;

                if (Exact && goal.Gateway && heading != goal.Arrival)
                    return float.MaxValue;

                float turn = TurnRadius[heading * HEADINGS + goal.Arrival];

                return turn < MinTurnRadius || (goal.Gateway && turn < StrictTurnRadius) ? float.MaxValue : goal.Cost + TurnCost[heading * HEADINGS + goal.Arrival];
            }

            return math.abs(CrossOf(Headings[heading], goal.Tangent)) < SinJunction ? float.MaxValue : goal.Cost;
        }

        private float CorridorCost(int cell, int step, float cost, bool leavingSource)
        {
            if (GoalIndex[cell] >= 0 || leavingSource)
                return cost;

            byte kind = CorridorKind[cell];

            if (kind == ON_CORRIDOR)
                return math.abs(CrossOf(Headings[step], CorridorTangent[cell])) < SinJunction ? cost * ParallelPenalty : cost + JunctionPenalty;

            if (kind == NEAR_CORRIDOR)
                return math.abs(CrossOf(Headings[step], CorridorTangent[cell])) < SinJunction ? cost * ParallelPenalty * 0.5f : cost;

            return cost;
        }

        private static float CrossOf(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private void Push(int item, float priority)
        {
            int index = _heapCount++;

            if (index < HeapItems.Length)
            {
                HeapItems[index] = item;
                HeapPriorities[index] = priority;
            }
            else
            {
                HeapItems.Add(item);
                HeapPriorities.Add(priority);
            }

            while (index > 0)
            {
                int parent = (index - 1) / 2;

                if (HeapPriorities[parent] <= HeapPriorities[index])
                    break;

                Swap(parent, index);
                index = parent;
            }
        }

        private bool TryPop(out int item)
        {
            if (_heapCount == 0)
            {
                item = 0;
                return false;
            }

            item = HeapItems[0];
            _heapCount--;

            if (_heapCount == 0)
                return true;

            HeapItems[0] = HeapItems[_heapCount];
            HeapPriorities[0] = HeapPriorities[_heapCount];

            int index = 0;

            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;

                if (left < _heapCount && HeapPriorities[left] < HeapPriorities[smallest])
                    smallest = left;

                if (right < _heapCount && HeapPriorities[right] < HeapPriorities[smallest])
                    smallest = right;

                if (smallest == index)
                    break;

                Swap(smallest, index);
                index = smallest;
            }

            return true;
        }

        private void Swap(int a, int b)
        {
            int item = HeapItems[a];
            HeapItems[a] = HeapItems[b];
            HeapItems[b] = item;

            float priority = HeapPriorities[a];
            HeapPriorities[a] = HeapPriorities[b];
            HeapPriorities[b] = priority;
        }
    }
}
