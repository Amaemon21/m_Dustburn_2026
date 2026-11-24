using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LoadingTipsView : MonoBehaviour
{
    [SerializeField] private TMP_Text _tipText;
    [SerializeField] private LoadingTipsConfig _config;

    private LoadingTipRotation _rotation;
    private double _nextTipTime;

    private void OnEnable()
    {
        if (_tipText == null)
            return;

        if (_config == null)
        {
            _tipText.text = string.Empty;
            return;
        }

        _rotation ??= new LoadingTipRotation(_config.Tips);
        ShowNextTip();
    }

    private void Update()
    {
        if (_rotation == null || _rotation.Count < 2 || _tipText == null || _config == null)
            return;

        if (Time.unscaledTimeAsDouble >= _nextTipTime)
            ShowNextTip();
    }

    private void ShowNextTip()
    {
        _tipText.text = _rotation.Next();
        _nextTipTime = Time.unscaledTimeAsDouble + _config.DisplaySeconds;
    }
}
