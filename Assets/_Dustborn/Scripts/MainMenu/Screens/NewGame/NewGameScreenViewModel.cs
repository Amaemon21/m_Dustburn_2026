using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public sealed class NewGameScreenViewModel : MainMenuScreenViewModel
{
    private const int MAX_NAME_LENGTH = 40;
    private const string MAP_CREATED = "\u041a\u0430\u0440\u0442\u0430 \u0433\u043e\u0442\u043e\u0432\u0430. \u0417\u0430\u0434\u0430\u0439\u0442\u0435 \u043d\u0430\u0437\u0432\u0430\u043d\u0438\u0435 \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u044f \u0438 \u043d\u0430\u0436\u043c\u0438\u0442\u0435 «\u041d\u0430\u0447\u0430\u0442\u044c \u0438\u0433\u0440\u0443».";
    private readonly IMainMenuExit _exit;
    private readonly WorldCatalog _catalog;
    private readonly GameSlotCatalog _slots;
    private readonly WorldSelection _selection;
    private readonly WorldPreviewCache _previews;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly WorldListViewModel _worlds;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _operationInProgress;

    private bool CanInteract => !_operationInProgress && !_worlds.Busy.CurrentValue;

    public ReactiveProperty<string> Name { get; } = new(string.Empty);

    public ReadOnlyReactiveProperty<IReadOnlyList<ListItemViewModel>> Worlds => _worlds.Items;
    public ReadOnlyReactiveProperty<bool> HasNoWorlds => _worlds.IsEmpty;
    public ReadOnlyReactiveProperty<Texture2D> Preview => _worlds.Preview;
    public ReadOnlyReactiveProperty<string> Footer { get; }
    public ReadOnlyReactiveProperty<string> Usage { get; }
    public ReadOnlyReactiveProperty<bool> IsUnused { get; }
    public ReadOnlyReactiveProperty<string> Count { get; }
    public WorldDetailsViewModel Details { get; }
    public ReactiveCommand<Unit> Start { get; }
    public ReactiveCommand<Unit> Back { get; }
    public ReactiveCommand<Unit> CreateWorld { get; }
    public ReactiveCommand<Unit> DeleteWorld { get; }

    public NewGameScreenViewModel(MainMenuNavigation navigation, WorldCatalog catalog, GameSlotCatalog slots,
        WorldSelection selection, WorldPreviewCache previews, ConfirmDialogViewModel dialog, IMainMenuExit exit)
        : base(navigation, MainMenuScreen.NewGame)
    {
        _catalog = catalog;
        _exit = exit;
        _slots = slots;
        _selection = selection;
        _previews = previews;
        _dialog = dialog;
        _worlds = new WorldListViewModel(catalog, previews);
        Details = new WorldDetailsViewModel(_worlds.Selected);
        Footer = Details.Footer.CombineLatest(_worlds.Message, (details, message) =>
            string.IsNullOrEmpty(message) ? details : message).ToReadOnlyReactiveProperty();
        Count = _worlds.Items.Select(items => items.Count.ToString("D2")).ToReadOnlyReactiveProperty();
        IsUnused = _worlds.Selected.CombineLatest(slots.Slots, (world, saved) => world != null
            && WorldDeletionPolicy.UsageCount(world.Id, saved) == 0).ToReadOnlyReactiveProperty();
        Usage = _worlds.Selected.CombineLatest(slots.Slots, (world, saved) =>
        {
            if (world == null)
                return string.Empty;
            int count = WorldDeletionPolicy.UsageCount(world.Id, saved);
            string usage = count > 0 ? $"\u0418\u0441\u043f\u043e\u043b\u044c\u0437\u0443\u0435\u0442\u0441\u044f \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f\u043c\u0438: {count}" : "\u041d\u0435 \u0438\u0441\u043f\u043e\u043b\u044c\u0437\u0443\u0435\u0442\u0441\u044f \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f\u043c\u0438";
            return world.BuiltIn ? usage + "\n\u0412\u0441\u0442\u0440\u043e\u0435\u043d\u043d\u0430\u044f \u043a\u0430\u0440\u0442\u0430 · \u0443\u0434\u0430\u043b\u0435\u043d\u0438\u0435 \u043d\u0435\u0434\u043e\u0441\u0442\u0443\u043f\u043d\u043e" : usage;
        }).ToReadOnlyReactiveProperty();
        Start = Command();
        Back = NavigationCommand(MainMenuScreen.Main);
        CreateWorld = Command();
        DeleteWorld = Command();
        Disposables.Add(_worlds.Selected.Subscribe(_ => RefreshCommands()));
        Disposables.Add(_worlds.Busy.Subscribe(_ => RefreshCommands()));
        Disposables.Add(Shown.Subscribe(_ => OnShown()));
        Disposables.Add(Start.Executed.Subscribe(_ => StartAsync().Forget()));
        Disposables.Add(CreateWorld.Executed.Subscribe(_ => Navigation.Show(MainMenuScreen.CreateWorld)));
        Disposables.Add(DeleteWorld.Executed.Subscribe(_ => AskDelete()));
    }

    private void OnShown()
    {
        _dialog.Hide();
        if (string.IsNullOrWhiteSpace(Name.Value))
            Name.Value = DefaultName();
        string createdMapId = _selection.TakeCreatedMap();
        _worlds.RefreshAsync(createdMapId, createdMapId == null ? null : MAP_CREATED).Forget();
        _slots.RefreshAsync().Forget();
    }

    private async UniTask StartAsync()
    {
        WorldInfo world = _worlds.Selected.CurrentValue;
        if (!CanInteract || world == null)
            return;
        string name = CleanName(Name.Value);
        _operationInProgress = true;
        RefreshCommands();
        bool started = false;
        try
        {
            WorldInfo available = await _catalog.FindAsync(world.Id, _lifetime.Token);
            if (IsDisposed)
                return;

            if (available == null)
            {
                _dialog.ShowMessage("This map is no longer available. Choose another map.");
                await _worlds.RefreshAsync();
                return;
            }
            string slotId = await _slots.CreateSlotIdAsync();
            if (IsDisposed)
                return;
            _exit.NewGame(slotId, name, world.Id);
            started = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!IsDisposed)
                _dialog.ShowMessage("Could not start the game. Check that the map is available and try again.");
        }
        finally
        {
            if (!started && !IsDisposed)
            {
                _operationInProgress = false;
                RefreshCommands();
            }
        }
    }

    private void AskDelete()
    {
        WorldInfo world = _worlds.Selected.CurrentValue;
        if (!CanInteract || !CanDelete(world))
            return;

        int count = WorldDeletionPolicy.UsageCount(world.Id, _slots.Slots.CurrentValue);
        if (count > 0)
        {
            _dialog.ShowMessage($"\u041a\u0430\u0440\u0442\u0443 \u0438\u0441\u043f\u043e\u043b\u044c\u0437\u0443\u044e\u0442 \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f: {count}. \u0421\u043d\u0430\u0447\u0430\u043b\u0430 \u0443\u0434\u0430\u043b\u0438\u0442\u0435 \u044d\u0442\u0438 \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u044f \u0432 \u0440\u0430\u0437\u0434\u0435\u043b\u0435 «\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c \u0438\u0433\u0440\u0443».");
            return;
        }
        _dialog.Ask($"\u0423\u0434\u0430\u043b\u0438\u0442\u044c \u043a\u0430\u0440\u0442\u0443 «{world.Name}»? \u042d\u0442\u043e \u0434\u0435\u0439\u0441\u0442\u0432\u0438\u0435 \u043d\u0435\u043b\u044c\u0437\u044f \u043e\u0442\u043c\u0435\u043d\u0438\u0442\u044c.", () => DeleteAsync(world).Forget());
    }

    private async UniTask DeleteAsync(WorldInfo world)
    {
        if (!CanInteract)
            return;
        _operationInProgress = true;
        RefreshCommands();
        try
        {
            await _catalog.DeleteAsync(world, _lifetime.Token);
            _previews.Forget(world.Id);
            await _worlds.RefreshAsync();
            await _slots.RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (WorldDeletionException exception)
        {
            if (!IsDisposed)
                _dialog.ShowMessage(exception.HasUnreadableSaves
                    ? "\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0432\u0435\u0440\u0438\u0442\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f. \u0421\u043d\u0430\u0447\u0430\u043b\u0430 \u0432\u043e\u0441\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u0435 \u0438\u043b\u0438 \u0443\u0434\u0430\u043b\u0438\u0442\u0435 \u043f\u043e\u0432\u0440\u0435\u0436\u0434\u0451\u043d\u043d\u044b\u0435 \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u044f."
                    : $"\u041a\u0430\u0440\u0442\u0443 \u0438\u0441\u043f\u043e\u043b\u044c\u0437\u0443\u044e\u0442 \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f: {exception.SaveCount}. \u0421\u043d\u0430\u0447\u0430\u043b\u0430 \u0443\u0434\u0430\u043b\u0438\u0442\u0435 \u044d\u0442\u0438 \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u044f \u0432 \u0440\u0430\u0437\u0434\u0435\u043b\u0435 «\u0417\u0430\u0433\u0440\u0443\u0437\u0438\u0442\u044c \u0438\u0433\u0440\u0443».");
        }
        catch (Exception)
        {
            if (!IsDisposed)
                _dialog.ShowMessage("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0443\u0434\u0430\u043b\u0438\u0442\u044c \u043a\u0430\u0440\u0442\u0443. \u041f\u0440\u043e\u0432\u0435\u0440\u044c\u0442\u0435 \u0434\u043e\u0441\u0442\u0443\u043f \u043a \u0444\u0430\u0439\u043b\u0430\u043c \u0438 \u043f\u043e\u043f\u0440\u043e\u0431\u0443\u0439\u0442\u0435 \u0435\u0449\u0451 \u0440\u0430\u0437.");
        }
        finally
        {
            if (!IsDisposed)
            {
                _operationInProgress = false;
                RefreshCommands();
            }
        }
    }

    private void RefreshCommands()
    {
        WorldInfo world = _worlds.Selected.CurrentValue;
        bool ready = CanInteract;
        Start.SetCanExecute(ready && world != null);
        DeleteWorld.SetCanExecute(ready && CanDelete(world));
        CreateWorld.SetCanExecute(ready);
        Back.SetCanExecute(ready);
    }

    private static bool CanDelete(WorldInfo world) => world != null && !world.BuiltIn && !world.IsSceneWorld;

    public override void GoBack()
    {
        if (CanInteract)
            base.GoBack();
    }

    private string DefaultName() => $"Game {_slots.Slots.CurrentValue.Count + 1}";

    private string CleanName(string name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return DefaultName();
        return trimmed.Length > MAX_NAME_LENGTH ? trimmed.Substring(0, MAX_NAME_LENGTH) : trimmed;
    }

    protected override void OnDisposed()
    {
        base.OnDisposed();
        _lifetime.Cancel();
        _worlds.Dispose();
        Name.Dispose();
        Usage.Dispose();
        IsUnused.Dispose();
        Count.Dispose();
        Footer.Dispose();
        Details.Dispose();
        _lifetime.Dispose();
    }
}
