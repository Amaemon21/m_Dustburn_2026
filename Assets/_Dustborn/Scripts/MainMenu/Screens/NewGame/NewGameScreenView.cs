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

    [SerializeField, Foldout("Preview")] private RawImage _preview;
    [SerializeField, Foldout("Preview")] private GameObject _noPreview;

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

        if (_worldPrefab != null && _worldContent != null)
        {
            _worlds ??= new ListItemViews(_worldPrefab, _worldContent);
            bindings.Add(viewModel.Worlds.Subscribe(_worlds.Show));
        }

        if (_noWorlds != null)
            bindings.Add(viewModel.HasNoWorlds.Subscribe(_noWorlds.SetActive));

        bindings.Add(viewModel.Preview.Subscribe(ShowPreview));

        _startButton.BindCommand(viewModel.Start, bindings);
        _backButton.BindCommand(viewModel.Back, bindings);

        if (_createWorldButton != null)
            _createWorldButton.BindCommand(viewModel.CreateWorld, bindings);

        if (_deleteWorldButton != null)
            _deleteWorldButton.BindCommand(viewModel.DeleteWorld, bindings);
    }

    private void ShowPreview(Texture2D preview)
    {
        if (_preview != null)
        {
            _preview.texture = preview;
            _preview.enabled = preview != null;
        }

        if (_noPreview != null)
            _noPreview.SetActive(preview == null);
    }

    protected override void OnUnbound()
    {
        _worlds?.Unbind();
    }
}
