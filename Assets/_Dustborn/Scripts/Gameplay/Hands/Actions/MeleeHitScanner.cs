using UnityEngine;

public sealed class MeleeHitScanner
{
    private const int CAPACITY = 16;

    private readonly RaycastHit[] _hits = new RaycastHit[CAPACITY];

    public bool TryHit(IAimSource aim, float reach, float radius, LayerMask mask, out MeleeHit hit)
    {
        Ray ray = aim.Aim;
        int count = Physics.SphereCastNonAlloc(ray.origin, radius, ray.direction, _hits, reach, mask, QueryTriggerInteraction.Ignore);
        int nearest = -1;
        for (int i = 0; i < count; i++)
        {
            if (_hits[i].collider.transform.IsChildOf(aim.Body))
                continue;
            if (nearest < 0 || _hits[i].distance < _hits[nearest].distance)
                nearest = i;
        }

        if (nearest < 0)
        {
            hit = default;
            return false;
        }

        RaycastHit found = _hits[nearest];
        Vector3 point = found.distance > 0f ? found.point : ray.origin;
        hit = new MeleeHit(TargetOf(found.collider), point, -ray.direction);
        return true;
    }

    private static IDamageable TargetOf(Collider collider)
    {
        IDamageableView view = collider.GetComponentInParent<IDamageableView>();
        return view == null ? null : view.Damageable;
    }
}
