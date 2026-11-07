using UnityEngine;

public readonly struct MapLegendEntry
{
    public readonly Color32 Color;
    public readonly string Label;

    public MapLegendEntry(Color32 color, string label)
    {
        Color = color;
        Label = label;
    }
}
