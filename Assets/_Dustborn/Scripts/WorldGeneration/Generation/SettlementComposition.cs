using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class SettlementComposition
{
    private readonly List<PoiRequirement> _pending = new();
    private readonly List<Vector2> _claimedAt = new();
    private readonly PoiCensus _claimed = new();

    public PoiCensus Claimed => _claimed;

    public int Planned { get; }

    public int Unmet => _pending.Count;

    public int Met => Planned - _pending.Count;

    public SettlementComposition(SettlementProfile profile, PoiCensus world)
    {
        if (profile?.Composition == null)
            return;

        var sorted = new List<PoiRequirement>(profile.Composition);

        sorted.Sort((first, second) => Area(second).CompareTo(Area(first)));

        var planned = new PoiCensus();

        foreach (PoiRequirement requirement in sorted)
        {
            if (requirement == null || !requirement.IsValid)
                continue;

            for (int i = 0; i < requirement.Count; i++)
            {
                if (!HasRoom(requirement.Definition, world, planned))
                    break;

                planned.Add(requirement.Definition);
                _pending.Add(requirement);
            }
        }

        Planned = _pending.Count;
    }

    public PoiRequirement Claim(DistrictType district, float distance, Vector2 anchor, float spacing)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            if (!_pending[i].Accepts(district, distance))
                continue;

            if (IsCrowded(anchor, spacing))
                return null;

            PoiRequirement claim = _pending[i];

            _pending.RemoveAt(i);

            return claim;
        }

        return null;
    }

    public void Confirm(PoiRequirement claim, Vector2 anchor)
    {
        if (claim == null)
            return;

        _claimedAt.Add(anchor);
        _claimed.Add(claim.Definition);
    }

    public void Return(PoiRequirement claim)
    {
        if (claim == null)
            return;

        _pending.Add(claim);
    }

    public string DescribeUnmet()
    {
        if (_pending.Count == 0)
            return string.Empty;

        var counts = new Dictionary<string, int>();

        foreach (PoiRequirement requirement in _pending)
        {
            string name = $"{requirement.Definition.name} ({requirement.Definition.District})";

            counts.TryGetValue(name, out int count);
            counts[name] = count + 1;
        }

        var text = new StringBuilder();

        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (text.Length > 0)
                text.Append(", ");

            text.Append($"{pair.Key} x{pair.Value}");
        }

        return text.ToString();
    }

    private bool IsCrowded(Vector2 anchor, float spacing)
    {
        float limit = spacing * spacing;

        foreach (Vector2 claimed in _claimedAt)
        {
            if ((claimed - anchor).sqrMagnitude < limit)
                return true;
        }

        return false;
    }

    private static bool HasRoom(PoiDefinition definition, PoiCensus world, PoiCensus planned)
    {
        if (definition.MaxPerSettlement > 0 && planned.Count(definition) >= definition.MaxPerSettlement)
            return false;

        if (definition.MaxPerWorld <= 0)
            return true;

        return world.Count(definition) + planned.Count(definition) < definition.MaxPerWorld;
    }

    private static float Area(PoiRequirement requirement)
    {
        return requirement == null ? 0f : requirement.Area;
    }
}
