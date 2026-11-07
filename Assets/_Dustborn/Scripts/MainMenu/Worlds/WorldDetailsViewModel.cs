using System;
using R3;
using UnityEngine;

public sealed class WorldDetailsViewModel : ViewModel
{
    private readonly MapViewport _viewport = new();
    private bool _hasWorld;
    private readonly ReactiveProperty<Rect> _uvRect = new(new Rect(0f, 0f, 1f, 1f));
    public ReadOnlyReactiveProperty<string> Name { get; }
    public ReadOnlyReactiveProperty<string> Metadata { get; }
    public ReadOnlyReactiveProperty<string> Relief { get; }
    public ReadOnlyReactiveProperty<string> Stats { get; }
    public ReadOnlyReactiveProperty<string> Footer { get; }
    public ReadOnlyReactiveProperty<Rect> UvRect => _uvRect;
    public ReactiveCommand<Unit> ZoomIn { get; } = new();
    public ReactiveCommand<Unit> ZoomOut { get; } = new();

    public WorldDetailsViewModel(Observable<WorldInfo> selected)
    {
        Name = selected.Select(world => world?.Name ?? "\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043a\u0430\u0440\u0442\u0443").ToReadOnlyReactiveProperty();
        Metadata = selected.Select(MainMenuText.MapMetadata).ToReadOnlyReactiveProperty();
        Relief = selected.Select(MainMenuText.MapRelief).ToReadOnlyReactiveProperty();
        Stats = selected.Select(MainMenuText.MapStats).ToReadOnlyReactiveProperty();
        Footer = selected.Select(MainMenuText.MapFooter).ToReadOnlyReactiveProperty();
        Disposables.Add(selected.Subscribe(world =>
        {
            _hasWorld = world != null;
            _viewport.Reset();
            Refresh();
        }));
        Disposables.Add(ZoomIn.Executed.Subscribe(_ => Wheel(1f, new Vector2(0.5f, 0.5f))));
        Disposables.Add(ZoomOut.Executed.Subscribe(_ => Wheel(-1f, new Vector2(0.5f, 0.5f))));
    }

    public void Wheel(float scroll, Vector2 pivot)
    {
        _viewport.Wheel(scroll, pivot);
        Refresh();
    }

    public void Drag(Vector2 delta)
    {
        _viewport.Drag(delta);
        Refresh();
    }

    private void Refresh()
    {
        _uvRect.Value = _viewport.UvRect;
        ZoomIn.SetCanExecute(_hasWorld && _viewport.Zoom < MapViewport.MAX_ZOOM);
        ZoomOut.SetCanExecute(_hasWorld && _viewport.Zoom > MapViewport.MIN_ZOOM);
    }

    protected override void OnDisposed()
    {
        Name.Dispose();
        Metadata.Dispose();
        Relief.Dispose();
        Stats.Dispose();
        Footer.Dispose();
        _uvRect.Dispose();
        ZoomIn.Dispose();
        ZoomOut.Dispose();
    }
}
