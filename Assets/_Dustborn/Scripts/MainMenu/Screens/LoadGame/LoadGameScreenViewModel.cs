using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using System.Threading;

public sealed class LoadGameScreenViewModel : MainMenuScreenViewModel
{
    private const string DELETE_QUESTION = "\u0423\u0434\u0430\u043b\u0438\u0442\u044c \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u0435 «{0}»? \u041a\u0430\u0440\u0442\u0430 \u043e\u0441\u0442\u0430\u043d\u0435\u0442\u0441\u044f \u0434\u043e\u0441\u0442\u0443\u043f\u043d\u043e\u0439 \u0434\u043b\u044f \u043d\u043e\u0432\u043e\u0439 \u0438\u0433\u0440\u044b.";

    private readonly CancellationTokenSource _lifetime = new();
    private readonly GameSlotThumbnailCache _thumbnails;
    private readonly GameSlotCatalog _catalog;
    private readonly GameSlotLauncher _launcher;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly ReactiveProperty<IReadOnlyList<ListItemViewModel>> _items = new(Array.Empty<ListItemViewModel>());
    private readonly ReactiveProperty<GameSlotInfo> _selected = new();
    private bool _deleting;

    public ReadOnlyReactiveProperty<IReadOnlyList<ListItemViewModel>> Items => _items;
    public ReadOnlyReactiveProperty<bool> IsEmpty { get; }
    public ReadOnlyReactiveProperty<string> SelectionDetails { get; }
    public ReactiveCommand<Unit> Load { get; }
    public ReactiveCommand<Unit> Delete { get; }
    public ReactiveCommand<Unit> Back { get; }

    public LoadGameScreenViewModel(MainMenuNavigation navigation, GameSlotCatalog catalog, GameSlotLauncher launcher,
        ConfirmDialogViewModel dialog, GameSlotThumbnailCache thumbnails) : base(navigation, MainMenuScreen.LoadGame)
    {
        _thumbnails = thumbnails;
        _catalog = catalog;
        _launcher = launcher;
        _dialog = dialog;

        SelectionDetails = _selected.Select(MainMenuText.SlotDescription).ToReadOnlyReactiveProperty();
        IsEmpty = _items.Select(items => items.Count == 0).ToReadOnlyReactiveProperty();
        Load = Command();
        Delete = Command();
        Back = NavigationCommand(MainMenuScreen.Main);

        Disposables.Add(_launcher.Busy.Subscribe(_ => RefreshCommands()));
        Disposables.Add(catalog.Slots.Subscribe(Rebuild));
        Disposables.Add(_selected.Subscribe(_ => RefreshCommands()));
        Disposables.Add(_catalog.Error.Subscribe(_ => RefreshCommands()));
        Disposables.Add(Shown.Subscribe(_ => OnShown()));
        Disposables.Add(Load.Executed.Subscribe(_ => LoadSelected()));
        Disposables.Add(Delete.Executed.Subscribe(_ => AskDelete()));
    }

    private void OnShown()
    {
        _dialog.Hide();
        _catalog.RefreshAsync().Forget();
    }

    private void LoadSelected()
    {
        _launcher.LoadAsync(_selected.Value?.Id).Forget();
    }

    private void AskDelete()
    {
        GameSlotInfo slot = _selected.Value;
        _dialog.Ask(string.Format(DELETE_QUESTION, slot.Name), () => DeleteSelectedAsync(slot.Id).Forget());
    }

    private async UniTask DeleteSelectedAsync(string id)
    {
        if (_deleting)
            return;
        _deleting = true;
        RefreshCommands();
        try
        {
            await _catalog.DeleteAsync(id);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!IsDisposed)
                _dialog.ShowMessage("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0443\u0434\u0430\u043b\u0438\u0442\u044c \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u0435. \u041f\u043e\u043f\u0440\u043e\u0431\u0443\u0439\u0442\u0435 \u0435\u0449\u0451 \u0440\u0430\u0437.");
        }
        finally
        {
            _deleting = false;
            if (!IsDisposed)
                RefreshCommands();
        }
    }

    private void Rebuild(IReadOnlyList<GameSlotInfo> slots)
    {
        DisposeItems();

        ListItemViewModel[] items = new ListItemViewModel[slots.Count];
        for (int i = 0; i < slots.Count; i++)
            items[i] = Item(slots[i]);

        _selected.Value = Find(slots, _selected.Value?.Id) ?? (slots.Count > 0 ? slots[0] : null);
        _items.Value = items;
        for (int i = 0; i < slots.Count; i++)
            ShowThumbnailAsync(slots[i], items[i], _lifetime.Token).Forget();
    }

    private async UniTask ShowThumbnailAsync(GameSlotInfo slot, ListItemViewModel item, CancellationToken token)
    {
        try
        {
            var texture = await _thumbnails.LoadAsync(slot, token);
            if (!IsDisposed)
                item.SetThumbnail(texture);
        }
        catch (OperationCanceledException) { }
    }

    private ListItemViewModel Item(GameSlotInfo slot)
        => new(slot.Name, MainMenuText.SlotDetails(slot), _selected.Select(selected => selected?.Id == slot.Id), () => Select(slot));

    private void Select(GameSlotInfo slot)
    {
        _dialog.Hide();
        _selected.Value = slot;
    }

    private void RefreshCommands()
    {
        bool ready = _selected.Value != null && !_deleting && !_launcher.Busy.CurrentValue;
        Load.SetCanExecute(ready && _selected.Value.CanLoad && string.IsNullOrEmpty(_catalog.Error.CurrentValue));
        Delete.SetCanExecute(ready);
        Back.SetCanExecute(!_deleting && !_launcher.Busy.CurrentValue);
    }

    public override void GoBack()
    {
        if (!_deleting && !_launcher.Busy.CurrentValue)
            base.GoBack();
    }

    private static GameSlotInfo Find(IReadOnlyList<GameSlotInfo> slots, string id)
    {
        if (id == null)
            return null;

        foreach (GameSlotInfo slot in slots)
        {
            if (slot.Id == id)
                return slot;
        }

        return null;
    }

    private void DisposeItems()
    {
        foreach (ListItemViewModel item in _items.Value)
            item.Dispose();
    }

    protected override void OnDisposed()
    {
        base.OnDisposed();
        _lifetime.Cancel();
        _lifetime.Dispose();
        DisposeItems();
        _items.Dispose();
        _selected.Dispose();
        IsEmpty.Dispose();
        SelectionDetails.Dispose();
    }
}
