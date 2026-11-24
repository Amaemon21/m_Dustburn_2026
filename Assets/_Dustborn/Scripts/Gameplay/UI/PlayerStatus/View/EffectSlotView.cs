using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EffectSlotView : MonoBehaviour
{
    [SerializeField, Required] private Image _background;
    [SerializeField, Required] private Image _icon;
    [SerializeField, Required] private TMP_Text _label;

    public void SetAppearance(Sprite icon, Sprite background)
    {
        _icon.sprite = icon;
        _background.sprite = background;
    }

    public void SetLabel(string label)
    {
        _label.text = label;
        bool visible = label.Length > 0;

        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }
}
