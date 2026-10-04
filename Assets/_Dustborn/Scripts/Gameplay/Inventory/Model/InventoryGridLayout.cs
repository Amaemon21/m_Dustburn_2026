using UnityEngine;

public static class InventoryGridLayout
{
    public static int IndexOf(Vector2Int coords, Vector2Int size) => coords.x * size.y + coords.y;

    public static Vector2Int CoordsOf(int index, Vector2Int size) => new(index / size.y, index % size.y);

    public static bool Contains(Vector2Int coords, Vector2Int size)
        => coords.x >= 0 && coords.y >= 0 && coords.x < size.x && coords.y < size.y;
}
