using UnityEngine;

[RequireComponent(typeof(Outline))]
public sealed class PickUpItemInteractable : MonoBehaviour, IInteractableObject
{
    [SerializeField] private InventoryItem _item;
    [SerializeField, Min(1)] private int _amount = 1;

    private Outline _outline;

    public ItemPickup Pickup { get; private set; }
    public string InteractKey => _item != null ? _item.ItemName : string.Empty;

    private void Awake()
    {
        _outline = GetComponent<Outline>();
        _outline.enabled = false;
        if (_item != null)
            Pickup = new ItemPickup(new InventorySlotState(_item.ItemId, _amount));
    }

    public void Setup(InventoryItem item, InventorySlotState stack)
    {
        Pickup?.Dispose();
        _item = item;
        _amount = stack.Amount;
        Pickup = new ItemPickup(stack);
    }

    public void ShowOutline() => _outline.enabled = true;
    public void HideOutline() => _outline.enabled = false;
    public bool IsInteractable() => isActiveAndEnabled && Pickup != null && Pickup.Remaining > 0;

    public void Interact(IInteractionContext context)
    {
        Pickup.PickUp(context);

        if (Pickup.Remaining == 0)
            Destroy(gameObject);
    }

    private void OnDestroy() => Pickup?.Dispose();
}
