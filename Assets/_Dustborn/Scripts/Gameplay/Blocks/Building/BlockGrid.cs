using UnityEngine;

public static class BlockGrid
{
    public const float CELL_SIZE = 1f;

    public static Vector3 Center(Vector3Int cell, float originY)
        => (Vector3)cell * CELL_SIZE + new Vector3(0.5f, originY + 0.5f, 0.5f);

    public static Vector3Int Cell(Vector3 point, float originY)
        => Vector3Int.FloorToInt((point - Vector3.up * originY) / CELL_SIZE);

    public static Vector3Int AdjacentCell(PlacedBlockData block, Vector3Int hitCell, Vector3 normal, int height)
    {
        Vector3 absNormal = new(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));

        Vector3Int direction;
        if (absNormal.x > absNormal.y && absNormal.x > absNormal.z)
            direction = normal.x > 0f ? Vector3Int.right : Vector3Int.left;
        else if (absNormal.z > absNormal.y)
            direction = new Vector3Int(0, 0, normal.z > 0f ? 1 : -1);
        else
            direction = normal.y > 0f ? Vector3Int.up : Vector3Int.down;

        int level = direction.y == 0 && height > 1 ? 0 : Mathf.Clamp(hitCell.y - block.Y, 0, block.CellHeight - 1);
        return block.GetCell(level) + direction;
    }

    public static Quaternion Rotation(int turns) => Quaternion.Euler(0f, (turns & 3) * 90f, 0f);
}
