using UnityEngine;

[DisallowMultipleComponent]
public sealed class PooledDecorObject : MonoBehaviour
{
    public VoxelDecorPool Owner { get; private set; }
    public int Layer { get; private set; }
    public bool Retained { get; internal set; }

    public void Setup(VoxelDecorPool owner, int layer)
    {
        Owner = owner;
        Layer = layer;
    }

    private void OnDestroy()
    {
        Owner?.Forget(this);
    }
}
