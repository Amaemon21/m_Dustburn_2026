using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public sealed class WorldListViewModel : IDisposable
{
    private readonly WorldCatalog _catalog;
    private readonly WorldPreviewCache _previews;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReactiveProperty<IReadOnlyList<ListItemViewModel>> _items = new(Array.Empty<ListItemViewModel>());
    private readonly ReactiveProperty<WorldInfo> _selected = new();
    private readonly ReactiveProperty<Texture2D> _preview = new();
    private readonly ReactiveProperty<bool> _busy = new();
    private readonly ReactiveProperty<string> _message = new(string.Empty);
    private readonly CompositeDisposable _subscriptions = new();
    private int _request;
    private bool _disposed;

    public ReadOnlyReactiveProperty<IReadOnlyList<ListItemViewModel>> Items => _items;
    public ReadOnlyReactiveProperty<WorldInfo> Selected => _selected;
    public ReadOnlyReactiveProperty<Texture2D> Preview => _preview;
    public ReadOnlyReactiveProperty<bool> Busy => _busy;
    public ReadOnlyReactiveProperty<bool> IsEmpty { get; }
    public ReadOnlyReactiveProperty<string> Message => _message;

    public WorldListViewModel(WorldCatalog catalog, WorldPreviewCache previews)
    {
        _catalog = catalog;
        _previews = previews;
        IsEmpty = _items.CombineLatest(_busy, (items, busy) => items.Count == 0 && !busy).ToReadOnlyReactiveProperty();
        _subscriptions.Add(_selected.Subscribe(world => ShowPreviewAsync(world).Forget()));
    }

    public async UniTask RefreshAsync(string preferredId = null, string message = null)
    {
        if (_disposed || _busy.Value)
            return;
        _busy.Value = true;
        _message.Value = message ?? string.Empty;
        try
        {
            IReadOnlyList<WorldInfo> worlds = await _catalog.ListAsync(_lifetime.Token);
            if (_disposed)
                return;

            string selectedId = preferredId ?? _selected.Value?.Id;
            var items = new ListItemViewModel[worlds.Count];
            WorldInfo selected = null;
            for (int i = 0; i < worlds.Count; i++)
            {
                WorldInfo world = worlds[i];
                items[i] = new ListItemViewModel(world.Name, MainMenuText.WorldDetails(world),
                    _selected.Select(value => value?.Id == world.Id), () => Select(world));
                if (world.Id == selectedId)
                    selected = world;
            }
            DisposeItems();
            _selected.Value = selected ?? (worlds.Count > 0 ? worlds[0] : null);
            _items.Value = items;
            for (int i = 0; i < worlds.Count; i++)
                LoadThumbnailAsync(worlds[i], items[i], _lifetime.Token).Forget();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_disposed)
            {
                _selected.Value = null;
                _message.Value = "\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0447\u0438\u0442\u0430\u0442\u044c \u0441\u043f\u0438\u0441\u043e\u043a \u043a\u0430\u0440\u0442. \u0412\u0435\u0440\u043d\u0438\u0442\u0435\u0441\u044c \u043d\u0430\u0437\u0430\u0434 \u0438 \u043f\u043e\u043f\u0440\u043e\u0431\u0443\u0439\u0442\u0435 \u0435\u0449\u0451 \u0440\u0430\u0437.";
            }
        }
        finally
        {
            if (!_disposed)
                _busy.Value = false;
        }
    }

    private async UniTask LoadThumbnailAsync(WorldInfo world, ListItemViewModel item, CancellationToken token)
    {
        try
        {
            Texture2D texture = await _previews.LoadThumbnailAsync(world, token);
            if (!_disposed)
                item.SetThumbnail(texture);
        }
        catch (OperationCanceledException) { }
    }

    private void Select(WorldInfo world)
    {
        if (_busy.Value)
            return;
        _message.Value = string.Empty;
        _selected.Value = world;
    }

    private async UniTask ShowPreviewAsync(WorldInfo world)
    {
        int request = ++_request;
        _preview.Value = null;
        try
        {
            Texture2D preview = await _previews.LoadAsync(world, _lifetime.Token);
            if (!_disposed && request == _request)
                _preview.Value = preview;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private void DisposeItems()
    {
        foreach (ListItemViewModel item in _items.Value)
            item.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _subscriptions.Dispose();
        DisposeItems();
        IsEmpty.Dispose();
        _items.Dispose();
        _selected.Dispose();
        _preview.Dispose();
        _busy.Dispose();
        _message.Dispose();
        _lifetime.Dispose();
    }
}
