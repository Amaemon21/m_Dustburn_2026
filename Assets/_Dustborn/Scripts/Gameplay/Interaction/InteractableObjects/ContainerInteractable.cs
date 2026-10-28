using NaughtyAttributes;
using UnityEngine;

public sealed class ContainerInteractable : MonoBehaviour, IInteractableObject
{
    [SerializeField, Required] private ContainerConfig _config;
    [SerializeField, ReadOnly] private string _containerId;

    private Outline _outline;

    public string InteractKey => _config.InteractableName;
    public string ContainerId => _containerId;
    public ContainerConfig Config => _config;

    public void AssignId(string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId))
            throw new System.ArgumentException("A container id is required", nameof(containerId));

        _containerId = containerId;
    }

    private void Awake()
    {
        if (TryGetComponent(out _outline))
            _outline.enabled = false;

        if (_config == null)
            Debug.LogError($"ContainerInteractable '{name}' has no ContainerConfig", this);
        if (string.IsNullOrWhiteSpace(_containerId))
            Debug.LogError($"ContainerInteractable '{name}' has no container id, press 'Regenerate Container Id'", this);
    }

    public void ShowOutline() => Highlight().enabled = true;

    public void HideOutline()
    {
        if (_outline != null)
            _outline.enabled = false;
    }

    public bool IsInteractable() => isActiveAndEnabled && _config != null && !string.IsNullOrWhiteSpace(_containerId);
    public void Interact(IInteractionContext context) => context.OpenContainer(_containerId, _config);

    private Outline Highlight()
    {
        if (_outline == null)
            _outline = gameObject.AddComponent<Outline>();

        return _outline;
    }

#if UNITY_EDITOR
    public void EditorSetup(ContainerConfig config)
    {
        _config = config;
        if (string.IsNullOrWhiteSpace(_containerId))
            RegenerateId();
    }

    private void Reset() => RegenerateId();

    [Button("Regenerate Container Id")]
    private void RegenerateId()
    {
        _containerId = System.Guid.NewGuid().ToString("N");
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
