using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public sealed class NewGameScreenViewModel : MainMenuScreenViewModel
{
    private const int MAX_NAME_LENGTH = 40;
    private static string MapCreated => LocalizationService.Text("menu.newGame.mapCreated");
    private readonly IMainMenuExit _exit;
    private readonly WorldCatalog _catalog;
    private readonly GameSlotCatalog _slots;
    private readonly WorldSelection _selection;
    private readonly WorldPreviewCache _previews;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly WorldListViewModel _worlds;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SerialDisposable _deleteConfirmation = new();
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
            string usage = count > 0 ? LocalizationService.Format("menu.newGame.saveCount", count) : LocalizationService.Text("menu.newGame.noSaves");
            return world.BuiltIn ? usage + LocalizationService.Text("menu.newGame.builtInMap") : usage;
        }).ToReadOnlyReactiveProperty();
        Start = Command();
        Back = NavigationCommand(MainMenuScreen.Main);
        CreateWorld = Command();
        DeleteWorld = Command();
        Disposables.Add(_deleteConfirmation);
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
        _worlds.RefreshAsync(createdMapId, createdMapId == null ? null : MapCreated).Forget();
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
                _dialog.ShowMessage(LocalizationService.Text("menu.newGame.mapUnavailable"));
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
                _dialog.ShowMessage(LocalizationService.Text("menu.newGame.launchFailed"));
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

        int count = WorldDeletionPolicy.UsageCount(
            world.Id,
            _slots.Slots.CurrentValue);

        if (count > 0)
        {
            _dialog.ShowMessage(
                LocalizationService.Format("menu.newGame.mapInUse", count));
            return;
        }

        _deleteConfirmation.Disposable = _dialog.Ask(LocalizationService.Format("menu.newGame.deleteQuestion", world.Name))
            .Subscribe(_ =>
            {
                if (!IsDisposed)
                    DeleteAsync(world).Forget();
            });
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
                    ? LocalizationService.Text("menu.newGame.corruptSaveList")
                    : LocalizationService.Format("menu.newGame.mapInUse", exception.SaveCount));
        }
        catch (Exception)
        {
            if (!IsDisposed)
                _dialog.ShowMessage(LocalizationService.Text("menu.newGame.deleteFailed"));
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
