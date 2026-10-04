public interface IInteractableObject
{
    public string InteractKey { get; }

    public void ShowOutline();
    public void HideOutline();
    public void Interact(IInteractionContext context);
    public bool IsInteractable();
}
