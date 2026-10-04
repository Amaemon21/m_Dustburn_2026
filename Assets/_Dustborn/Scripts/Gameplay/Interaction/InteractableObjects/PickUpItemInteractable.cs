using UnityEngine;

[RequireComponent(typeof(Outline))]
public sealed class PickUpItemInteractable : MonoBehaviour, IInteractableObject
{
    [SerializeField] private InventoryItem _item;
    [SerializeField, Min(1)] private int _amount = 1;

    private Outline _outline;
    private ItemPickup _pickup;

    public string InteractKey => $"{_item.ItemName}";

    private void Awake()
    {
        _outline = GetComponent<Outline>();
        _outline.enabled = false;
        _pickup = new ItemPickup(_item.ItemId, _amount);
    }

    public void ShowOutline() => _outline.enabled = true;
    public void HideOutline() => _outline.enabled = false;
    public bool IsInteractable() => isActiveAndEnabled && _pickup.Remaining > 0;

    public void Interact(IInteractionContext context)
    {
        _pickup.PickUp(context);
        
        if (_pickup.Remaining == 0)
            Destroy(gameObject);
    }
}
