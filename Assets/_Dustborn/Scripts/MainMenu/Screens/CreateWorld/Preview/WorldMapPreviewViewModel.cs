using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public sealed class WorldMapPreviewViewModel : ViewModel
{
    private readonly ReactiveProperty<Texture2D> _map = new();
    private readonly ReactiveProperty<bool> _hasMap = new();
    private readonly ReactiveProperty<bool> _isStale = new();
    private readonly ReactiveProperty<string> _info = new(string.Empty);
    private readonly ReactiveProperty<IReadOnlyList<MapLegendEntry>> _legend = new(Array.Empty<MapLegendEntry>());
    private readonly ReactiveProperty<Rect> _uvRect = new(new Rect(0f, 0f, 1f, 1f));
    private readonly MapViewport _viewport = new();
    private readonly CancellationTokenSource _lifetime = new();
    private WorldGenerationResult _result;
    private WorldPreviewLayers _layers;
    private WorldPreviewQuality _quality = WorldPreviewQuality.Medium;
    private int _request;
    private int _repaint;

    public ReactiveProperty<bool> Biomes { get; } = new(true);
    public ReactiveProperty<bool> Relief { get; } = new(true);
    public ReactiveProperty<bool> Water { get; } = new(true);
    public ReactiveProperty<bool> Roads { get; } = new(true);
    public ReactiveProperty<bool> Settlements { get; } = new(true);
    public ReactiveProperty<bool> Buildings { get; } = new(true);
    public ReadOnlyReactiveProperty<Texture2D> Map => _map;
    public ReadOnlyReactiveProperty<bool> HasMap => _hasMap;
    public ReadOnlyReactiveProperty<bool> IsStale => _isStale;
    public ReadOnlyReactiveProperty<string> Info => _info;
    public ReadOnlyReactiveProperty<IReadOnlyList<MapLegendEntry>> Legend => _legend;
    public ReadOnlyReactiveProperty<Rect> UvRect => _uvRect;
    public ReactiveCommand<Unit> ResetView { get; } = new();

    public WorldMapPreviewViewModel()
    {
        Disposables.Add(ResetView);
        Disposables.Add(ResetView.Executed.Subscribe(_ => Reset()));

        foreach (ReactiveProperty<bool> layer in new[] { Biomes, Relief, Water, Roads, Settlements, Buildings })
        {
            Disposables.Add(layer);
            Disposables.Add(layer.Skip(1).Subscribe(_ => ComposeAsync().Forget()));
        }
    }

    public WorldPreviewLayer Layers
    {
        get
        {
            return Flag(Biomes, WorldPreviewLayer.Biomes) | Flag(Relief, WorldPreviewLayer.Relief) | Flag(Water, WorldPreviewLayer.Water)
                | Flag(Roads, WorldPreviewLayer.Roads) | Flag(Settlements, WorldPreviewLayer.Settlements) | Flag(Buildings, WorldPreviewLayer.Buildings);
        }
    }

    public void Show(WorldGenerationResult result, string seedText, WorldPreviewQuality quality)
    {
        _result = result;
        _layers = result.Layers;
        _quality = quality;
        _info.Value = InfoText(result, seedText);
        _legend.Value = LegendOf(result.Preview, result.Stats);
        _isStale.Value = false;
        Reset();
        ComposeAsync().Forget();
    }

    public void SetStale(bool stale) => _isStale.Value = stale && _result != null;

    public void Clear()
    {
        _request++;
        _result = null;
        _layers = null;
        _hasMap.Value = false;
        _isStale.Value = false;
        _info.Value = string.Empty;
        _legend.Value = Array.Empty<MapLegendEntry>();
        Texture2D texture = _map.Value;
        _map.Value = null;
        WorldData.Release(texture);
    }

    public void Wheel(float scroll, Vector2 pivot)
    {
        _viewport.Wheel(scroll, pivot);
        _uvRect.Value = _viewport.UvRect;
    }

    public void Drag(Vector2 normalizedDelta)
    {
        _viewport.Drag(normalizedDelta);
        _uvRect.Value = _viewport.UvRect;
    }

    public async UniTask RepaintAsync(WorldPreviewQuality quality)
    {
        if (_result == null || quality == _quality)
        {
            _quality = quality;
            return;
        }

        _quality = quality;
        int repaint = ++_repaint;
        WorldPreviewSource source = _result.Preview;
        int resolution = WorldPreviewQualities.Resolution(quality);

        WorldPreviewLayers layers = await UniTask.RunOnThreadPool(() => WorldPreviewPainter.Layers(source, resolution), cancellationToken: _lifetime.Token);

        if (IsDisposed || repaint != _repaint || source != _result?.Preview)
            return;

        _layers = layers;
        await ComposeAsync();
    }

    private async UniTask ComposeAsync()
    {
        WorldPreviewLayers layers = _layers;
        if (layers == null)
            return;

        int request = ++_request;
        WorldPreviewLayer flags = Layers;
        Color32[] pixels = await UniTask.RunOnThreadPool(() => layers.Compose(flags), cancellationToken: _lifetime.Token);

        if (IsDisposed || request != _request)
            return;

        Texture2D texture = _map.Value;
        if (texture == null || texture.width != layers.Resolution)
        {
            WorldData.Release(texture);
            texture = WorldPreviewImage.Create(layers.Resolution);
        }

        texture.SetPixelData(pixels, 0);
        texture.Apply(false, false);
        _map.Value = texture;
        _hasMap.Value = true;
    }

    private void Reset()
    {
        _viewport.Reset();
        _uvRect.Value = _viewport.UvRect;
    }

    private static WorldPreviewLayer Flag(ReactiveProperty<bool> shown, WorldPreviewLayer layer) => shown.Value ? layer : WorldPreviewLayer.None;

    private static string InfoText(WorldGenerationResult result, string seedText)
    {
        WorldInfo world = result.World;
        string seed = string.IsNullOrWhiteSpace(seedText) || seedText.Trim() == world.Seed.ToString()
            ? world.Seed.ToString()
            : $"«{seedText.Trim()}» → {world.Seed}";

        return $"<b>{world.Name}</b>\n\u0421\u0438\u0434: {seed} · \u0420\u0430\u0437\u043C\u0435\u0440: {MainMenuText.Size(world.WorldSize)}\n{MainMenuText.StatsSummary(result.Stats)}";
    }

    private static IReadOnlyList<MapLegendEntry> LegendOf(WorldPreviewSource source, WorldStats stats)
    {
        var entries = new List<MapLegendEntry>();
        var seen = new HashSet<BiomeType>();

        if (source.Gameplay?.HasSpawn == true)
            entries.Add(new MapLegendEntry(WorldPreviewLayers.StartColor, "\u0421\u0442\u0430\u0440\u0442"));

        for (int i = 0; i < source.BiomeTypes.Length; i++)
        {
            BiomeType type = source.BiomeTypes[i];
            if (!seen.Add(type))
                continue;

            int share = Mathf.RoundToInt(stats.BiomeShare(type) * 100f);
            entries.Add(new MapLegendEntry(source.BiomeColors[i], $"{MainMenuText.Biome(type)} {share}%"));
        }

        return entries;
    }

    protected override void OnDisposed()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        WorldData.Release(_map.Value);
        _map.Dispose();
        _hasMap.Dispose();
        _isStale.Dispose();
        _info.Dispose();
        _legend.Dispose();
        _uvRect.Dispose();
    }
}
