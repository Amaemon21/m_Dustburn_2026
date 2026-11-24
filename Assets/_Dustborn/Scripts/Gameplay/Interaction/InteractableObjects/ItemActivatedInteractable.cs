using UnityEngine;
using VContainer;

[RequireComponent(typeof(Outline))]
public sealed class ItemActivatedInteractable : MonoBehaviour, IInteractableObject
{
    [Inject] private readonly ItemActivationService _activation;
    [Inject] private readonly LocalPlayerInventory _player;

    [SerializeField] private string _activationId;
    [SerializeField] private InventoryItem _requiredItem;
    [SerializeField] private GameObject _activatedContent;

    [SerializeField] private string _missingItemText;
    [SerializeField] private string _activateText;
    [SerializeField] private string _activatedText;

    private Outline _outline;

    public string InteractKey
    {
        get
        {
            if (_activation.IsActivated(_activationId))
                return _activatedText;

            return _activation.HasRequiredItem(_player.Proxy, _requiredItem.ItemId) ? _activateText : _missingItemText;
        }
    }

    private void Awake()
    {
        _outline = GetComponent<Outline>();
        HideOutline();
    }

    private void Start()
    {
        if (_activation != null)
            RefreshContent();
    }

    public bool IsInteractable() => isActiveAndEnabled && _activation != null;

    public void Interact(IInteractionContext context)
    {
        if (IsInteractable() && _activation.TryActivate(_activationId, _player.Proxy, _requiredItem.ItemId))
            RefreshContent();
    }

    public void ShowOutline()
    {
        if (_outline != null)
            _outline.enabled = true;
    }

    public void HideOutline()
    {
        if (_outline != null)
            _outline.enabled = false;
    }

    private void RefreshContent()
    {
        if (_activatedContent != null)
            _activatedContent.SetActive(_activation.IsActivated(_activationId));
    }
}
