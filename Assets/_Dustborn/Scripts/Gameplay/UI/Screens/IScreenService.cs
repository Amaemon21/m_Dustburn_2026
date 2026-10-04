using R3;

public interface IScreenService
{
    ReadOnlyReactiveProperty<bool> HasOpenWindows { get; }
    bool HasAnyWindowOpen();
    void Open(string id, WindowViewModel viewModel);
    void Close(string id);
    bool CloseTop();
    void CloseAll();
}
