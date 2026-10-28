using UnityEngine;

public interface IItemUseService
{
    bool Use(string ownerId, Vector2Int coordinates);
}
