using System;
using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PauseScreenView : WindowView
{
    [SerializeField, Required] private Button _resumeButton;
    [SerializeField, Required] private Button _saveButton;
    [SerializeField, Required] private Button _saveAndExitButton;
    [SerializeField] private TMP_Text _statusLabel;
    [SerializeField] private string _savingText;
    [SerializeField] private string _savedText;
    [SerializeField] private string _saveFailedText;

    private float _previousTimeScale;
    private bool _paused;

    public override Type ViewModelType => typeof(PauseScreenViewModel);
    public override string WindowId => PauseScreenService.WINDOW_ID;

    protected override void BindCore(WindowViewModel viewModel, CompositeDisposable bindings)
    {
        PauseScreenViewModel pause = (PauseScreenViewModel)viewModel;
        _resumeButton.BindCommand(pause.Close, bindings);
        _saveButton.BindCommand(pause.Save, bindings);
        _saveAndExitButton.BindCommand(pause.SaveAndExit, bindings);
        if (_statusLabel != null)
            bindings.Add(pause.Status.Subscribe(ShowStatus));
    }

    protected override void OnUnbound() => SetPaused(false);

    private void OnEnable()
    {
        if (BoundViewModel != null && BoundViewModel.IsOpen.CurrentValue)
            SetPaused(true);
    }

    private void OnDisable() => SetPaused(false);

    private void SetPaused(bool paused)
    {
        if (paused == _paused)
            return;

        _paused = paused;

        if (paused)
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = _previousTimeScale;
        }
    }

    private void ShowStatus(PauseSaveStatus status)
    {
        _statusLabel.text = status switch
        {
            PauseSaveStatus.Saving => _savingText,
            PauseSaveStatus.Saved => _savedText,
            PauseSaveStatus.Failed => _saveFailedText,
            _ => string.Empty
        };
    }
}
