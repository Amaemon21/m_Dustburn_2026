using System;
using R3;
using UnityEngine;

public sealed class CameraService : ICameraService, IDisposable
{
    private readonly ReactiveProperty<Camera> _camera;
    public Camera MainCamera => _camera.Value;
    public ReadOnlyReactiveProperty<Camera> CurrentCamera => _camera;

    public CameraService(Camera camera)
    {
        _camera = new ReactiveProperty<Camera>(camera != null ? camera : throw new ArgumentNullException(nameof(camera),
            "Assign the scene output camera in GameplayLifetimeScope"));
    }

    public void SetMainCamera(Camera camera) => _camera.Value = camera;

    public void ClearMainCamera(Camera camera)
    {
        if (_camera.Value == camera)
            _camera.Value = null;
    }

    public bool TryGetAimRay(Vector2 viewportPoint, out Ray ray)
    {
        Camera camera = _camera.Value;
        
        if (camera == null || !camera.isActiveAndEnabled)
        {
            ray = default;
            return false;
        }
        
        ray = camera.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
        return true;
    }

    public void Dispose() => _camera.Dispose();
}
