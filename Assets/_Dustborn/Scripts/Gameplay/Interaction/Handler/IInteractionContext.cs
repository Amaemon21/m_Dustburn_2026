public interface IInteractionContext
{
    int PickUp(string itemId, int amount);
    void OpenContainer(string containerId, ContainerConfig config);
}
