using UnityEngine;

public interface IMeleeHitScanner
{
    bool TryHit(IAimSource aim, float reach, float radius, LayerMask mask, out MeleeHit hit);
}
