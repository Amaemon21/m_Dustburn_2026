using UnityEngine;

public class VoxelDecorBucket : MonoBehaviour
{
    [SerializeField] private Vector2 _origin;
    [SerializeField] private float _span;
    [SerializeField] private int _lod;
    [SerializeField] private int _size;
    [SerializeField] private float _voxelSize;
    [SerializeField] private int _morph;

    public Vector2 Origin => _origin;
    public float Span => _span;
    public int Lod => _lod;
    public DecorSurface Frame => new(_origin, _size, _voxelSize, _morph);

    public void Setup(Vector2 origin, float span, int lod, DecorSurface frame)
    {
        _origin = origin;
        _span = span;
        _lod = lod;
        _size = frame.Size;
        _voxelSize = frame.VoxelSize;
        _morph = frame.Morph;
    }

    public bool Overlaps(Vector2 min, Vector2 max)
    {
        return min.x < _origin.x + _span && max.x >= _origin.x && min.y < _origin.y + _span && max.y >= _origin.y;
    }
}
