using UnityEngine;

public sealed class RaycastInteractionTargetProvider : IInteractionTargetProvider
{
    private readonly ICameraService _cameras;
    private readonly InteractProperty _settings;

    public RaycastInteractionTargetProvider(ICameraService cameras, InteractSettings settings)
    {
        _cameras = cameras;
        _settings = settings.Detection;
    }

    public IInteractableObject FindTarget()
    {
        if (!_cameras.TryGetAimRay(_settings.ViewportPoint, out Ray ray))
            return null;
        if (!Physics.Raycast(ray, out RaycastHit hit, _settings.InteractRange, _settings.HitScanMask, _settings.TriggerInteraction))
            return null;

        return hit.collider.GetComponentInParent<IInteractableObject>();
    }
}
