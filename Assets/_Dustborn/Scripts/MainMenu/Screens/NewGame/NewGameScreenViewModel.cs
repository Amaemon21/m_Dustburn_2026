using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public sealed class NewGameScreenViewModel : MainMenuScreenViewModel
{
    private const string DEFAULT_NAME = "Игра {0}";
    private const int MAX_NAME_LENGTH = 40;
    private const string DELETE_QUESTION = "Удалить мир «{0}»? Сохранения в нём перестанут загружаться.";

    private readonly GameSlotCatalog _slots;
    private readonly WorldCatalog _worldCatalog;
    private readonly IMainMenuExit _exit;
    private readonly WorldSelection _selection;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly WorldPreviewCache _previews;
    private readonly ReactiveProperty<Texture2D> _preview = new();
    private int _previewRequest;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReactiveProperty<IReadOnlyList<ListItemViewModel>> _worlds = new(Array.Empty<ListItemViewModel>());
    private readonly ReactiveProperty<WorldInfo> _selectedWorld = new();
    private bool _starting;
    private bool _deleting;

    public ReactiveProperty<string> Name { get; } = new(string.Empty);
    public ReadOnlyReactiveProperty<IReadOnlyList<ListItemViewModel>> Worlds => _worlds;
    public ReadOnlyReactiveProperty<bool> HasNoWorlds { get; }
    public ReadOnlyReactiveProperty<Texture2D> Preview => _preview;
    public ReactiveCommand<Unit> Start { get; }
    public ReactiveCommand<Unit> Back { get; }
    public ReactiveCommand<Unit> CreateWorld { get; }
    public ReactiveCommand<Unit> DeleteWorld { get; }

    public NewGameScreenViewModel(MainMenuNavigation navigation, GameSlotCatalog slots, WorldCatalog worldCatalog, IMainMenuExit exit,
        WorldSelection selection, ConfirmDialogViewModel dialog, WorldPreviewCache previews)
        : base(navigation, MainMenuScreen.NewGame)
    {
        _slots = slots;
        _worldCatalog = worldCatalog;
        _exit = exit;
        _selection = selection;
        _dialog = dialog;
        _previews = previews;

        HasNoWorlds = _worlds.Select(worlds => worlds.Count == 0).ToReadOnlyReactiveProperty();
        Start = Command();
        Back = NavigationCommand(MainMenuScreen.Main);
        CreateWorld = NavigationCommand(MainMenuScreen.CreateWorld);
        DeleteWorld = Command();

        Disposables.Add(Name);
        Disposables.Add(HasNoWorlds);
        Disposables.Add(_selectedWorld.Subscribe(_ => RefreshStart()));
        Disposables.Add(_selectedWorld.Subscribe(world => ShowPreviewAsync(world).Forget()));
        Disposables.Add(Shown.Subscribe(_ => OnShown()));
        Disposables.Add(Start.Executed.Subscribe(_ => StartAsync().Forget()));
        Disposables.Add(DeleteWorld.Executed.Subscribe(_ => _dialog.Ask(string.Format(DELETE_QUESTION, _selectedWorld.Value.Name), () => DeleteSelectedAsync().Forget())));
    }

    private void OnShown()
    {
        _dialog.Hide();

        if (string.IsNullOrWhiteSpace(Name.Value))
            Name.Value = DefaultName();

        LoadWorldsAsync(_selection.Take()).Forget();
    }

    private async UniTask LoadWorldsAsync(string preferredId = null)
    {
        IReadOnlyList<WorldInfo> worlds = await _worldCatalog.ListAsync(_lifetime.Token);
        if (IsDisposed)
            return;

        DisposeWorlds();

        var items = new ListItemViewModel[worlds.Count];
        for (int i = 0; i < worlds.Count; i++)
            items[i] = Item(worlds[i]);

        _selectedWorld.Value = Find(worlds, preferredId ?? _selectedWorld.Value?.Id) ?? (worlds.Count > 0 ? worlds[0] : null);
        _worlds.Value = items;
    }

    private ListItemViewModel Item(WorldInfo world)
        => new(world.Name, MainMenuText.WorldDetails(world), _selectedWorld.Select(selected => selected?.Id == world.Id),
            () => Select(world));

    private void Select(WorldInfo world)
    {
        _dialog.Hide();
        _selectedWorld.Value = world;
    }

    private async UniTask DeleteSelectedAsync()
    {
        WorldInfo world = _selectedWorld.Value;
        _deleting = true;
        RefreshStart();

        try
        {
            await _worldCatalog.DeleteAsync(world, _lifetime.Token);
            _previews.Forget(world.Id);
            _selectedWorld.Value = null;
            await LoadWorldsAsync();
        }
        finally
        {
            _deleting = false;
            if (!IsDisposed)
                RefreshStart();
        }
    }

    private async UniTask StartAsync()
    {
        WorldInfo world = _selectedWorld.Value;
        _starting = true;
        RefreshStart();

        bool started = false;
        try
        {
            string slotId = await _slots.CreateSlotIdAsync();
            _exit.NewGame(slotId, CleanName(Name.Value), world.Id);
            started = true;
        }
        finally
        {
            if (!started && !IsDisposed)
            {
                _starting = false;
                RefreshStart();
            }
        }
    }

    private async UniTask ShowPreviewAsync(WorldInfo world)
    {
        int request = ++_previewRequest;
        _preview.Value = null;

        Texture2D preview = await _previews.LoadAsync(world, _lifetime.Token);

        if (!IsDisposed && request == _previewRequest)
            _preview.Value = preview;
    }

    private void RefreshStart()
    {
        WorldInfo world = _selectedWorld.Value;
        Start.SetCanExecute(world != null && !_starting && !_deleting);
        DeleteWorld.SetCanExecute(world != null && !world.BuiltIn && !world.IsSceneWorld && !_starting && !_deleting);
    }

    private string DefaultName() => string.Format(DEFAULT_NAME, _slots.Slots.CurrentValue.Count + 1);

    private string CleanName(string name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return DefaultName();

        return trimmed.Length > MAX_NAME_LENGTH ? trimmed.Substring(0, MAX_NAME_LENGTH) : trimmed;
    }

    private static WorldInfo Find(IReadOnlyList<WorldInfo> worlds, string id)
    {
        if (id == null)
            return null;

        foreach (WorldInfo world in worlds)
        {
            if (world.Id == id)
                return world;
        }

        return null;
    }

    private void DisposeWorlds()
    {
        foreach (ListItemViewModel world in _worlds.Value)
            world.Dispose();
    }

    protected override void OnDisposed()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        DisposeWorlds();
        _worlds.Dispose();
        _selectedWorld.Dispose();
        _preview.Dispose();
    }
}
