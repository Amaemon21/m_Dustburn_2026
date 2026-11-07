using System.Collections.Generic;
using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WorldMapPreviewView : View<WorldMapPreviewViewModel>
{
    private const float STALE_ALPHA = 0.35f;
    private const string STALE = "\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 \u0438\u0437\u043C\u0435\u043D\u0438\u043B\u0438\u0441\u044C. \u041E\u0431\u043D\u043E\u0432\u0438\u0442\u0435 \u043F\u0440\u0435\u0432\u044C\u044E.";

    [SerializeField, Required, BoxGroup("Map")] private RawImage _map;
    [SerializeField, Required, BoxGroup("Map")] private MapPointerArea _pointer;

    [SerializeField, Foldout("State")] private CanvasGroup _mapGroup;
    [SerializeField, Required, Foldout("State")] private TMP_Text _info;

    [SerializeField, Required, Foldout("Layers")] private Toggle _biomes;
    [SerializeField, Required, Foldout("Layers")] private Toggle _relief;
    [SerializeField, Required, Foldout("Layers")] private Toggle _water;
    [SerializeField, Required, Foldout("Layers")] private Toggle _roads;
    [SerializeField, Required, Foldout("Layers")] private Toggle _settlements;
    [SerializeField, Required, Foldout("Layers")] private Toggle _buildings;

    [SerializeField, Required, Foldout("Legend")] private MapLegendItemView _legendPrefab;
    [SerializeField, Required, Foldout("Legend")] private Transform _legendContent;

    [SerializeField, Required, Foldout("Actions")] private Button _resetButton;

    private readonly List<MapLegendItemView> _legend = new();

    protected override void BindCore(WorldMapPreviewViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Map.Subscribe(ShowMap));
        bindings.Add(viewModel.UvRect.Subscribe(rect => _map.uvRect = rect));
        bindings.Add(viewModel.HasMap.CombineLatest(viewModel.IsStale, (has, stale) => (has, stale)).Subscribe(ShowState));
        bindings.Add(viewModel.Info
            .CombineLatest(viewModel.IsStale, (info, stale) => stale ? $"{info}\n{STALE}" : info)
            .Subscribe(info => _info.text = info));
        bindings.Add(viewModel.Legend.Subscribe(ShowLegend));

        BindToggle(_biomes, viewModel.Biomes, bindings);
        BindToggle(_relief, viewModel.Relief, bindings);
        BindToggle(_water, viewModel.Water, bindings);
        BindToggle(_roads, viewModel.Roads, bindings);
        BindToggle(_settlements, viewModel.Settlements, bindings);
        BindToggle(_buildings, viewModel.Buildings, bindings);

        _resetButton.BindCommand(viewModel.ResetView, bindings);
        bindings.Add(_pointer.Scrolled.Subscribe(scroll => viewModel.Wheel(scroll.Scroll, scroll.Pivot)));
        bindings.Add(_pointer.Dragged.Subscribe(viewModel.Drag));
    }

    private void ShowMap(Texture2D texture)
    {
        _map.texture = texture;
    }

    private void ShowState((bool HasMap, bool Stale) state)
    {
        _map.enabled = state.HasMap;
        _info.gameObject.SetActive(state.HasMap);

        if (_mapGroup != null)
            _mapGroup.alpha = state.Stale ? STALE_ALPHA : 1f;
    }

    private void ShowLegend(IReadOnlyList<MapLegendEntry> entries)
    {
        while (_legend.Count < entries.Count)
            _legend.Add(Instantiate(_legendPrefab, _legendContent));

        for (int i = 0; i < _legend.Count; i++)
        {
            bool used = i < entries.Count;
            _legend[i].gameObject.SetActive(used);

            if (used)
                _legend[i].Show(entries[i]);
        }
    }

    private static void BindToggle(Toggle toggle, ReactiveProperty<bool> value, CompositeDisposable bindings)
    {
        bindings.Add(value.Subscribe(on => toggle.SetIsOnWithoutNotify(on)));
        bindings.Add(toggle.onValueChanged.AsObservable().Subscribe(on => value.Value = on));
    }
}
