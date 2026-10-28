using UnityEngine;

public interface IWorldObjectHook
{
    void Spawned(GameObject instance, WorldObjectKey key);
}
