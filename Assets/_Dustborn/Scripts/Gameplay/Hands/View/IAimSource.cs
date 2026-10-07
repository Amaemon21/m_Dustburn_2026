using UnityEngine;

public interface IAimSource
{
    Ray Aim { get; }
    Transform Body { get; }
}
