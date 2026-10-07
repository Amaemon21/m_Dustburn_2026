using UnityEngine;

public static class DropPlacement
{
    private const float SETTLE_DISTANCE = 500f;
    private const float SKIN = 0.02f;

    public static void Settle(Transform item, LayerMask mask)
    {
        Physics.SyncTransforms();
        if (!TryGetBounds(item, out Bounds bounds))
            return;

        if (!Physics.BoxCast(bounds.center, bounds.extents, Vector3.down, out RaycastHit hit, Quaternion.identity, SETTLE_DISTANCE,
                mask, QueryTriggerInteraction.Ignore))
            return;

        item.position += Vector3.down * Mathf.Max(0f, hit.distance - SKIN);
        Physics.SyncTransforms();
    }

    private static bool TryGetBounds(Transform item, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Collider collider in item.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger)
                continue;

            if (found)
            {
                bounds.Encapsulate(collider.bounds);
                continue;
            }

            bounds = collider.bounds;
            found = true;
        }

        return found;
    }
}
