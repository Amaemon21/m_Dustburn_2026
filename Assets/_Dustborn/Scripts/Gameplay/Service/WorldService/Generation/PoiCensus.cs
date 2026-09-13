using System.Collections.Generic;

public class PoiCensus
{
    private readonly Dictionary<PoiDefinition, int> _counts = new();

    public void Clear()
    {
        _counts.Clear();
    }

    public int Count(PoiDefinition definition)
    {
        if (definition == null)
            return 0;

        return _counts.TryGetValue(definition, out int count) ? count : 0;
    }

    public void Add(PoiDefinition definition, int amount = 1)
    {
        if (definition == null)
            return;

        _counts.TryGetValue(definition, out int count);
        _counts[definition] = count + amount;
    }

    public void Merge(PoiCensus other)
    {
        if (other == null)
            return;

        foreach (KeyValuePair<PoiDefinition, int> pair in other._counts)
            Add(pair.Key, pair.Value);
    }

    public bool HasRoom(PoiDefinition definition, int cap)
    {
        return cap <= 0 || Count(definition) < cap;
    }
}
