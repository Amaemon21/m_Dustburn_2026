using UnityEngine;

public interface IItemDropService
{
    bool Drop(string ownerId, Vector2Int coordinates, int amount);
}
