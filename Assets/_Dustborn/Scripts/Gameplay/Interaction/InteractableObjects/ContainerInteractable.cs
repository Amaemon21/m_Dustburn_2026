using NaughtyAttributes;
using UnityEngine;

[RequireComponent(typeof(Outline))]
public sealed class ContainerInteractable : MonoBehaviour, IInteractableObject
{
    [SerializeField, Required] private ContainerConfig _config;
    [SerializeField, ReadOnly] private string _containerId;

    private Outline _outline;

    public string InteractKey => _config.InteractableName;

    private void Awake()
    {
        _outline = GetComponent<Outline>();
        _outline.enabled = false;

        if (_config == null)
            Debug.LogError($"ContainerInteractable '{name}' has no ContainerConfig", this);
        if (string.IsNullOrWhiteSpace(_containerId))
            Debug.LogError($"ContainerInteractable '{name}' has no container id, press 'Regenerate Container Id'", this);
    }

    public void ShowOutline() => _outline.enabled = true;
    public void HideOutline() => _outline.enabled = false;
    public bool IsInteractable() => isActiveAndEnabled && _config != null && !string.IsNullOrWhiteSpace(_containerId);
    public void Interact(IInteractionContext context) => context.OpenContainer(_containerId, _config);

#if UNITY_EDITOR
    private void Reset() => RegenerateId();

    [Button("Regenerate Container Id")]
    private void RegenerateId()
    {
        _containerId = System.Guid.NewGuid().ToString("N");
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
