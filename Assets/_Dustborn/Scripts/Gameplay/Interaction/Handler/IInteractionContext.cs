public interface IInteractionContext
{
    int PickUp(InventorySlotState stack);
    void OpenContainer(string containerId, ContainerConfig config);
}
