using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;

public sealed class LoadGameScreenViewModel : MainMenuScreenViewModel
{
    private const string DELETE_QUESTION = "Удалить «{0}»?";

    private readonly GameSlotCatalog _catalog;
    private readonly IMainMenuExit _exit;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly ReactiveProperty<IReadOnlyList<ListItemViewModel>> _items = new(Array.Empty<ListItemViewModel>());
    private readonly ReactiveProperty<GameSlotInfo> _selected = new();
    private bool _deleting;

    public ReadOnlyReactiveProperty<IReadOnlyList<ListItemViewModel>> Items => _items;
    public ReadOnlyReactiveProperty<bool> IsEmpty { get; }
    public ReactiveCommand<Unit> Load { get; }
    public ReactiveCommand<Unit> Delete { get; }
    public ReactiveCommand<Unit> Back { get; }

    public LoadGameScreenViewModel(MainMenuNavigation navigation, GameSlotCatalog catalog, IMainMenuExit exit,
        ConfirmDialogViewModel dialog) : base(navigation, MainMenuScreen.LoadGame)
    {
        _catalog = catalog;
        _exit = exit;
        _dialog = dialog;

        IsEmpty = _items.Select(items => items.Count == 0).ToReadOnlyReactiveProperty();
        Load = Command();
        Delete = Command();
        Back = NavigationCommand(MainMenuScreen.Main);

        Disposables.Add(IsEmpty);
        Disposables.Add(catalog.Slots.Subscribe(Rebuild));
        Disposables.Add(_selected.Subscribe(_ => RefreshCommands()));
        Disposables.Add(Shown.Subscribe(_ => OnShown()));
        Disposables.Add(Load.Executed.Subscribe(_ => LoadSelected()));
        Disposables.Add(Delete.Executed.Subscribe(_ => _dialog.Ask(string.Format(DELETE_QUESTION, _selected.Value.Name), () => DeleteSelectedAsync().Forget())));
    }

    private void OnShown()
    {
        _dialog.Hide();
        _selected.Value = _catalog.Latest.CurrentValue;
    }

    private void LoadSelected()
    {
        Load.SetCanExecute(false);
        _exit.LoadGame(_selected.Value.Id);
    }

    private async UniTask DeleteSelectedAsync()
    {
        _deleting = true;
        RefreshCommands();
        try
        {
            await _catalog.DeleteAsync(_selected.Value.Id);
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
        bool ready = _selected.Value != null && !_deleting;
        Load.SetCanExecute(ready);
        Delete.SetCanExecute(ready);
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
        DisposeItems();
        _items.Dispose();
        _selected.Dispose();
    }
}
