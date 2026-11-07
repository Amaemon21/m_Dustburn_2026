using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CreateWorldScreenView : View<CreateWorldScreenViewModel>
{
    [SerializeField, Required, Foldout("Basics")] private TMP_InputField _name;
    [SerializeField, Required, Foldout("Basics")] private TMP_InputField _seed;
    [SerializeField, Foldout("Basics")] private TMP_Text _seedNumber;
    [SerializeField, Foldout("Basics")] private Button _randomSeedButton;
    [SerializeField, Required, Foldout("Basics")] private OptionFieldView _size;
    [SerializeField, Required, Foldout("Basics")] private OptionFieldView _previewQuality;

    [SerializeField, Required, Foldout("Features")] private OptionFieldView _towns;
    [SerializeField, Required, Foldout("Features")] private OptionFieldView _wilderness;
    [SerializeField, Required, Foldout("Features")] private OptionFieldView _rivers;
    [SerializeField, Required, Foldout("Features")] private OptionFieldView _lakes;
    [SerializeField, Required, Foldout("Features")] private OptionFieldView _craters;
    [SerializeField, Required, Foldout("Features")] private OptionFieldView _rifts;
    [SerializeField, Required, Foldout("Features")] private OptionFieldView _canyons;

    [SerializeField, Required, Foldout("Terrain")] private OptionFieldView _plains;
    [SerializeField, Required, Foldout("Terrain")] private OptionFieldView _hills;
    [SerializeField, Required, Foldout("Terrain")] private OptionFieldView _mountains;

    [SerializeField, Required, Foldout("Biomes")] private OptionFieldView _pineForest;
    [SerializeField, Required, Foldout("Biomes")] private OptionFieldView _burntForest;
    [SerializeField, Required, Foldout("Biomes")] private OptionFieldView _desert;
    [SerializeField, Required, Foldout("Biomes")] private OptionFieldView _snow;
    [SerializeField, Required, Foldout("Biomes")] private OptionFieldView _wasteland;
    [SerializeField, Required, Foldout("Biomes")] private OptionFieldView _layout;

    [SerializeField, Required, Foldout("Actions")] private Button _generateButton;
    [SerializeField, Required, Foldout("Actions")] private TMP_Text _generateLabel;
    [SerializeField, Required, Foldout("Actions")] private Button _backButton;
    [SerializeField, Required, Foldout("Actions")] private Button _newGameButton;
    [SerializeField, Required, Foldout("Actions")] private TMP_Text _confirmLabel;

    [SerializeField, Required, Foldout("Progress")] private GameObject _progressPanel;
    [SerializeField, Required, Foldout("Progress")] private TMP_Text _stage;
    [SerializeField, Required, Foldout("Progress")] private TMP_Text _elapsedTime;

    [SerializeField, Required, Foldout("Preview")] private WorldMapPreviewView _preview;

    protected override void BindCore(CreateWorldScreenViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.IsVisible.Subscribe(gameObject.SetActive));

        BindText(_name, viewModel.Name, bindings);
        BindText(_seed, viewModel.Seed, bindings);

        if (_seedNumber != null)
            bindings.Add(viewModel.SeedNumber.Subscribe(text => _seedNumber.text = text));

        foreach ((OptionFieldView view, OptionFieldViewModel model) in Options(viewModel))
            view.Bind(model);

        _preview.Bind(viewModel.Preview);

        _generateButton.BindCommand(viewModel.Generate, bindings);
        bindings.Add(viewModel.GenerateLabel.Subscribe(label => _generateLabel.text = label));
        _backButton.BindCommand(viewModel.Back, bindings);
        _newGameButton.BindCommand(viewModel.NewGame, bindings);
        bindings.Add(viewModel.ConfirmLabel.Subscribe(label => _confirmLabel.text = label));

        if (_randomSeedButton != null)
            _randomSeedButton.BindCommand(viewModel.RandomSeed, bindings);

        bindings.Add(viewModel.Status.Subscribe(status => _stage.text = status));
        bindings.Add(viewModel.IsGenerating
            .Select(generating => generating
                ? Observable.EveryUpdate().Select(_ => viewModel.ElapsedSeconds).Prepend(0L)
                : Observable.Return(0L))
            .Switch()
            .DistinctUntilChanged()
            .Subscribe(seconds => _elapsedTime.text = $"{seconds / 60:00}:{seconds % 60:00}"));
        bindings.Add(viewModel.IsGenerating.Subscribe(ShowGenerating));
    }

    private void ShowGenerating(bool generating)
    {
        _name.interactable = !generating;
        _seed.interactable = !generating;
        _generateButton.gameObject.SetActive(!generating);

        _progressPanel.SetActive(generating);
    }

    private (OptionFieldView, OptionFieldViewModel)[] Options(CreateWorldScreenViewModel viewModel)
    {
        return new[]
        {
            (_size, viewModel.Size),
            (_previewQuality, viewModel.PreviewQuality),
            (_towns, viewModel.Towns),
            (_wilderness, viewModel.Wilderness),
            (_rivers, viewModel.Rivers),
            (_lakes, viewModel.Lakes),
            (_craters, viewModel.Craters),
            (_rifts, viewModel.Rifts),
            (_canyons, viewModel.Canyons),
            (_plains, viewModel.Plains),
            (_hills, viewModel.Hills),
            (_mountains, viewModel.Mountains),
            (_pineForest, viewModel.PineForest),
            (_burntForest, viewModel.BurntForest),
            (_desert, viewModel.Desert),
            (_snow, viewModel.Snow),
            (_wasteland, viewModel.Wasteland),
            (_layout, viewModel.Layout)
        };
    }

    private static void BindText(TMP_InputField field, ReactiveProperty<string> value, CompositeDisposable bindings)
    {
        bindings.Add(value.Subscribe(text => field.SetTextWithoutNotify(text)));
        bindings.Add(field.onValueChanged.AsObservable().Subscribe(text => value.Value = text));
    }

    protected override void OnUnbound()
    {
        foreach (OptionFieldView option in new[] { _size, _previewQuality, _towns, _wilderness, _rivers, _lakes, _craters, _rifts, _canyons,
                     _plains, _hills, _mountains, _pineForest, _burntForest, _desert, _snow, _wasteland, _layout })
        {
            if (option != null)
                option.Unbind();
        }

        if (_preview != null)
            _preview.Unbind();
    }
}
