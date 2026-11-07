using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class NewGameScreenView : View<NewGameScreenViewModel>
{
    [SerializeField, Required] private TMP_InputField _name;
    [SerializeField, Required] private ListItemView _worldPrefab;
    [SerializeField, Required] private Transform _worldContent;
    [SerializeField] private GameObject _noWorlds;
    [SerializeField, Foldout("Details")] private TMP_Text _count;
    [SerializeField, Foldout("Details")] private TMP_Text _mapName;
    [SerializeField, Foldout("Details")] private TMP_Text _metadata;
    [SerializeField, Foldout("Details")] private TMP_Text _relief;
    [SerializeField, Foldout("Details")] private TMP_Text _stats;
    [SerializeField, Foldout("Details")] private TMP_Text _footer;
    [SerializeField, Foldout("Details")] private TMP_Text _usage;
    [SerializeField, Foldout("Details")] private GameObject _unusedMarker;
    [SerializeField, Foldout("Preview")] private RawImage _preview;
    [SerializeField, Foldout("Preview")] private AspectRatioFitter _previewAspect;
    [SerializeField, Foldout("Preview")] private GameObject _noPreview;
    [SerializeField, Foldout("Preview")] private MapPointerArea _pointer;
    [SerializeField, Foldout("Preview")] private Button _zoomInButton;
    [SerializeField, Foldout("Preview")] private Button _zoomOutButton;
    [SerializeField, Required, Foldout("Actions")] private Button _startButton;
    [SerializeField, Required, Foldout("Actions")] private Button _backButton;
    [SerializeField, Required, Foldout("Actions")] private Button _createWorldButton;
    [SerializeField, Required, Foldout("Actions")] private Button _deleteWorldButton;
    private ListItemViews _worlds;

    protected override void BindCore(NewGameScreenViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.IsVisible.Subscribe(gameObject.SetActive));
        bindings.Add(viewModel.Name.Subscribe(name => _name.SetTextWithoutNotify(name)));
        bindings.Add(_name.onValueChanged.AsObservable().Subscribe(name => viewModel.Name.Value = name));
        bindings.Add(_name.onSubmit.AsObservable().Subscribe(_ => viewModel.Start.Execute(Unit.Default)));
        _worlds ??= new ListItemViews(_worldPrefab, _worldContent);
        bindings.Add(viewModel.Worlds.Subscribe(_worlds.Show));
        bindings.Add(viewModel.HasNoWorlds.Subscribe(_noWorlds.SetActive));
        bindings.Add(viewModel.Preview.Subscribe(ShowPreview));
        bindings.Add(viewModel.Details.UvRect.Subscribe(rect => _preview.uvRect = rect));
        bindings.Add(viewModel.Count.Subscribe(text => _count.text = text));
        bindings.Add(viewModel.IsUnused.Subscribe(_unusedMarker.SetActive));
        bindings.Add(viewModel.Usage.Subscribe(text => _usage.text = text));
        bindings.Add(viewModel.Details.Name.Subscribe(text => _mapName.text = text));
        bindings.Add(viewModel.Details.Metadata.Subscribe(text => _metadata.text = text));
        bindings.Add(viewModel.Details.Relief.Subscribe(text => _relief.text = text));
        bindings.Add(viewModel.Details.Stats.Subscribe(text => _stats.text = text));
        bindings.Add(viewModel.Footer.Subscribe(text => _footer.text = text));
        bindings.Add(_pointer.Scrolled.Subscribe(scroll => viewModel.Details.Wheel(scroll.Scroll, scroll.Pivot)));
        bindings.Add(_pointer.Dragged.Subscribe(viewModel.Details.Drag));
        _zoomInButton.BindCommand(viewModel.Details.ZoomIn, bindings);
        _zoomOutButton.BindCommand(viewModel.Details.ZoomOut, bindings);
        _startButton.BindCommand(viewModel.Start, bindings);
        _backButton.BindCommand(viewModel.Back, bindings);
        _createWorldButton.BindCommand(viewModel.CreateWorld, bindings);
        _deleteWorldButton.BindCommand(viewModel.DeleteWorld, bindings);
    }

    private void ShowPreview(Texture2D preview)
    {
        _preview.texture = preview;
        if (preview != null)
            _previewAspect.aspectRatio = (float)preview.width / preview.height;
        _preview.enabled = preview != null;
        _noPreview.SetActive(preview == null);
    }

    protected override void OnUnbound()
    {
        _worlds?.Unbind();
    }
}
