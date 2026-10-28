using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CreateWorldScreenView : View<CreateWorldScreenViewModel>
{
    [SerializeField, Required, BoxGroup("Name")] private TMP_InputField _name;
    [SerializeField, Required, BoxGroup("Name")] private TMP_InputField _seed;
    [SerializeField, BoxGroup("Name")] private Button _randomSeedButton;

    [SerializeField, Required, Foldout("Options")] private OptionFieldView _size;
    [SerializeField, Required, Foldout("Options")] private OptionFieldView _towns;
    [SerializeField, Required, Foldout("Options")] private OptionFieldView _farmsteads;
    [SerializeField, Required, Foldout("Options")] private OptionFieldView _rivers;
    [SerializeField, Required, Foldout("Options")] private OptionFieldView _lakes;
    [SerializeField, Required, Foldout("Options")] private OptionFieldView _mountains;
    [SerializeField, Required, Foldout("Options")] private OptionFieldView _relief;
    [SerializeField, Foldout("Options")] private CanvasGroup _options;

    [SerializeField, Required, Foldout("Actions")] private Button _generateButton;
    [SerializeField, Required, Foldout("Actions")] private Button _cancelButton;
    [SerializeField, Required, Foldout("Actions")] private Button _backButton;

    [SerializeField, Foldout("Progress")] private GameObject _progressPanel;
    [SerializeField, Foldout("Progress")] private TMP_Text _status;
    [SerializeField, Foldout("Progress")] private Image _progressFill;

    protected override void BindCore(CreateWorldScreenViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.IsVisible.Subscribe(gameObject.SetActive));

        BindText(_name, viewModel.Name, bindings);
        BindText(_seed, viewModel.Seed, bindings);

        _size.Bind(viewModel.Size);
        _towns.Bind(viewModel.Towns);
        _farmsteads.Bind(viewModel.Farmsteads);
        _rivers.Bind(viewModel.Rivers);
        _lakes.Bind(viewModel.Lakes);
        _mountains.Bind(viewModel.Mountains);
        _relief.Bind(viewModel.Relief);

        _generateButton.BindCommand(viewModel.Generate, bindings);
        _cancelButton.BindCommand(viewModel.Cancel, bindings);
        _backButton.BindCommand(viewModel.Back, bindings);

        if (_randomSeedButton != null)
            _randomSeedButton.BindCommand(viewModel.RandomSeed, bindings);

        bindings.Add(viewModel.IsGenerating.Subscribe(ShowGenerating));

        if (_status != null)
            bindings.Add(viewModel.Status.Subscribe(status => _status.text = status));

        if (_progressFill != null)
            bindings.Add(viewModel.Progress.Subscribe(progress => _progressFill.fillAmount = progress));
    }

    private void ShowGenerating(bool generating)
    {
        _name.interactable = !generating;
        _seed.interactable = !generating;
        _cancelButton.gameObject.SetActive(generating);

        if (_options != null)
            _options.interactable = !generating;

        if (_progressPanel != null)
            _progressPanel.SetActive(generating);
    }

    private static void BindText(TMP_InputField field, ReactiveProperty<string> value, CompositeDisposable bindings)
    {
        bindings.Add(value.Subscribe(text => field.SetTextWithoutNotify(text)));
        bindings.Add(field.onValueChanged.AsObservable().Subscribe(text => value.Value = text));
    }

    protected override void OnUnbound()
    {
        foreach (OptionFieldView option in new[] { _size, _towns, _farmsteads, _rivers, _lakes, _mountains, _relief })
        {
            if (option != null)
                option.Unbind();
        }
    }
}
