using UnityEngine;

public interface IDecorObjectHook
{
    bool IsRemoved(DecorKey key);
    void Spawned(GameObject copy, DecorKey key);
}
