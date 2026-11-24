public interface IItemAction
{
    bool TryStart();
    bool Tick(float deltaTime);
    void Cancel();
}
