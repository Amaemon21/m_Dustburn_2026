public interface IItemAction
{
    bool CanStart { get; }
    void Start();
    bool Tick(float deltaTime);
    void Cancel();
}
