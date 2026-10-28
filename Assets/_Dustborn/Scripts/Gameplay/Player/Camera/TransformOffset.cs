using System;
using UnityEngine;

public sealed class TransformOffset
{
    private readonly Transform _target;
    private readonly Vector3 _position;
    private readonly Quaternion _rotation;

    public TransformOffset(Transform target)
    {
        _target = target != null ? target : throw new ArgumentNullException(nameof(target));
        _position = target.localPosition;
        _rotation = target.localRotation;
    }

    public void Apply(Vector3 position, Vector3 euler)
    {
        _target.localPosition = _position + position;
        _target.localRotation = _rotation * Quaternion.Euler(euler);
    }

    public static TransformOffset Of(Transform target) => target != null ? new TransformOffset(target) : null;
}
