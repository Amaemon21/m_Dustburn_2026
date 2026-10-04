using R3;
using UnityEngine;

public interface ICameraService
{
    Camera MainCamera { get; }
    ReadOnlyReactiveProperty<Camera> CurrentCamera { get; }
    void SetMainCamera(Camera camera);
    void ClearMainCamera(Camera camera);
    bool TryGetAimRay(Vector2 viewportPoint, out Ray ray);
}
