using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerLevelView : View<PlayerLevelViewModel>
{
    [SerializeField, Required] private Image _fill;
    [SerializeField, Required] private TMP_Text _experienceText;
    [SerializeField, Required] private TMP_Text _levelText;
    [SerializeField] private string _experienceFormat = "{0}/{1}";
    [SerializeField] private string _levelFormat;
    [SerializeField] private string _maxLevelText;

    protected override void BindCore(PlayerLevelViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Experience.CombineLatest(viewModel.RequiredExperience, (experience, required) => (experience, required))
            .Subscribe(value => ShowExperience(value.experience, value.required)));
        string format = string.IsNullOrEmpty(_levelFormat) ? LocalizationService.Text("progression.level") : _levelFormat;
        bindings.Add(viewModel.Level.Subscribe(level => _levelText.text = string.Format(format, level)));
    }

    private void ShowExperience(float experience, int required)
    {
        if (required <= 0)
        {
            _fill.fillAmount = 1f;
            _experienceText.text = string.IsNullOrEmpty(_maxLevelText) ? LocalizationService.Text("progression.maxLevel") : _maxLevelText;
            return;
        }

        int earned = Mathf.Min(Mathf.FloorToInt(experience), required);
        _fill.fillAmount = (float)earned / required;
        _experienceText.text = string.Format(_experienceFormat, earned, required);
    }

    protected override void OnUnbound()
    {
        if (_fill != null)
            _fill.fillAmount = 0f;
        if (_experienceText != null)
            _experienceText.text = string.Empty;
        if (_levelText != null)
            _levelText.text = string.Empty;
    }
}
