using UnityEngine;

public sealed class BlockPlacementProbe
{
    private const float SURFACE_OFFSET = 0.01f;
    private const float SUPPORT_OFFSET = 0.4f;
    private const float SUPPORT_TOLERANCE = 0.08f;
    private static readonly Vector2[] SUPPORT_POINTS =
    {
        Vector2.zero, new(-SUPPORT_OFFSET, -SUPPORT_OFFSET), new(-SUPPORT_OFFSET, SUPPORT_OFFSET),
        new(SUPPORT_OFFSET, -SUPPORT_OFFSET), new(SUPPORT_OFFSET, SUPPORT_OFFSET)
    };

    private readonly Collider[] _overlaps = new Collider[1];
    private readonly BlockRepository _repository;
    private readonly BlockSupport _support;

    public BlockPlacementProbe(BlockRepository repository, BlockSupport support)
    {
        _repository = repository;
        _support = support;
    }

    public bool TryFind(BlockInventoryItem item, Ray ray, Transform player, int turns, out Vector3Int cell, out float originY,
        out bool allowed, out bool grounded)
    {
        cell = default;
        originY = _repository.Origin.BuildOriginY ?? 0f;
        allowed = false;
        grounded = false;

        if (!Physics.Raycast(ray, out RaycastHit hit, item.Reach, item.SurfaceMask, QueryTriggerInteraction.Ignore))
            return false;

        originY = _repository.Origin.BuildOriginY ?? hit.point.y;
        BlockView block = hit.collider.GetComponentInParent<BlockView>();
        cell = BlockGrid.Cell(hit.point + hit.normal * SURFACE_OFFSET, originY);
        Vector3Int hitCell = BlockGrid.Cell(hit.point - hit.normal * SURFACE_OFFSET, originY);
        int height = Mathf.Max(1, item.Height);

        if (block != null && _repository.TryGetPlaced(hitCell, out PlacedBlockData placed) && placed.Id == block.Id)
            cell = BlockGrid.AdjacentCell(placed, hitCell, hit.normal, height);

        Vector3 center = BlockGrid.Center(cell, originY);

        if ((center - ray.origin).sqrMagnitude > item.Reach * item.Reach ||
            _repository.IsOccupied(cell, height) || hit.collider.transform.IsChildOf(player))
            return true;

        if (block == null && hit.normal.y < 0.7f)
            return true;

        Quaternion rotation = BlockGrid.Rotation(turns);
        BoxCollider[] colliders = item.Prefab.PlacementColliders;

        if (colliders.Length == 0)
            return true;

        foreach (BoxCollider box in colliders)
        {
            Vector3 halfExtents = Vector3.Max(box.size * 0.5f - Vector3.one * SURFACE_OFFSET, Vector3.one * SURFACE_OFFSET);
            int count = Physics.OverlapBoxNonAlloc(center + rotation * box.center, halfExtents, _overlaps,
                rotation, ~((1 << 2) | (1 << 4) | (1 << 5)), QueryTriggerInteraction.Ignore);

            if (count != 0)
                return true;
        }

        grounded = HasGroundSupport(item, cell, originY, rotation);
        allowed = grounded || _support.HasNeighbor(cell, height);
        return true;
    }

    public bool HasGroundSupport(BlockInventoryItem item, Vector3Int cell, float originY, Quaternion rotation)
    {
        if (_repository.IsOccupied(cell + Vector3Int.down))
            return false;

        Vector3 center = BlockGrid.Center(cell, originY);

        foreach (BoxCollider box in item.Prefab.PlacementColliders)
        {
            float bottom = box.center.y - box.size.y * 0.5f;

            if (bottom > -BlockGrid.CELL_SIZE * 0.5f + SUPPORT_TOLERANCE)
                continue;

            foreach (Vector2 offset in SUPPORT_POINTS)
            {
                Vector3 local = new(box.center.x + offset.x * box.size.x, bottom + SUPPORT_TOLERANCE,
                    box.center.z + offset.y * box.size.z);
                Vector3 start = center + rotation * local;

                if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, SUPPORT_TOLERANCE * 2f, item.SurfaceMask,
                        QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.7f && hit.rigidbody == null &&
                    hit.collider.GetComponentInParent<BlockView>() == null)
                    return true;
            }
        }

        return false;
    }
}
