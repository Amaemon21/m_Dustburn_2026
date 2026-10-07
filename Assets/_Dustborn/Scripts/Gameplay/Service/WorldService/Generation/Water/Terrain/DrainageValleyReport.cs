using System.Collections.Generic;

public sealed class DrainageValleyReport
{
    public int Outlets;
    public int Mouths;
    public int DryEnds;
    public int DryCells;
    public int TerminalLakes;
    public int Valleys;
    public int Junctions;
    public int LakePoints;
    public int CutLimited;
    public int ChannelCells;
    public int LiftedCells;
    public int Inlets;
    public float LargestCatchment;
    public float Length;
    public float DeepestCut;
    public float HighestFill;
    public readonly List<DrainageValley> Paths = new();
    public DrainageValley Main;
    public MainRiverPlan MainPlan;

    public string Describe()
    {
        return $"Drainage valleys: {Valleys} valleys over {Length / 1000f:0.0} km from {Mouths} mouths into {Outlets} outlet cells ({DryEnds} valleys run into the {DryCells} dry cells, {TerminalLakes} of them end in a terminal basin short of it), {Junctions} junctions, {ChannelCells} channel cells, largest catchment {LargestCatchment / 1e6f:0.0} km2, "
            + $"deepest cut {DeepestCut:0.0} m ({CutLimited} floor points held up by the cut limit), highest fill {HighestFill:0.0} m, {LakePoints} floor points left open for lakes, {Inlets} pockets under the sea opened and {LiftedCells} height cells lifted after the cut; {(MainPlan == null ? "no main river asked for" : MainPlan.Describe())}";
    }
}
