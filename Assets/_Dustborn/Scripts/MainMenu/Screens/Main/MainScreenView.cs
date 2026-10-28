using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MainScreenView : View<MainScreenViewModel>
{
    [SerializeField, Required] private Button _continueButton;
    [SerializeField] private TMP_Text _continueDetails;
    [SerializeField, Required] private Button _newGameButton;
    [SerializeField, Required] private Button _loadGameButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField, Required] private Button _testSceneButton;
    [SerializeField, Required] private Button _quitButton;
    [SerializeField] private TMP_Text _notice;

    protected override void BindCore(MainScreenViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.IsVisible.Subscribe(gameObject.SetActive));
        bindings.Add(viewModel.Continue.CanExecute.Subscribe(_continueButton.gameObject.SetActive));

        _continueButton.BindCommand(viewModel.Continue, bindings);
        _newGameButton.BindCommand(viewModel.NewGame, bindings);
        _loadGameButton.BindCommand(viewModel.LoadGame, bindings);
        _testSceneButton.BindCommand(viewModel.TestScene, bindings);
        _quitButton.BindCommand(viewModel.Quit, bindings);

        if (_settingsButton != null)
            _settingsButton.BindCommand(viewModel.Settings, bindings);

        if (_continueDetails != null)
            bindings.Add(viewModel.ContinueDetails.Subscribe(details => _continueDetails.text = details));

        if (_notice != null)
        {
            _notice.text = viewModel.Notice;
            _notice.gameObject.SetActive(!string.IsNullOrEmpty(viewModel.Notice));
        }
    }
}
