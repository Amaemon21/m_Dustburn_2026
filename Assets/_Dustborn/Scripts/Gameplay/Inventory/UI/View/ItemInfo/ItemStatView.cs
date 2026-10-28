using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ItemStatView : MonoBehaviour
{
    [SerializeField, Required] private Image _iconImage;
    [SerializeField, Required] private TMP_Text _labelText;
    [SerializeField, Required] private TMP_Text _valueText;

    public void Show(Sprite icon, string label, string value, Color color)
    {
        _iconImage.sprite = icon;
        _iconImage.enabled = icon != null;
        _labelText.text = label;
        _valueText.text = value;
        _valueText.color = color;
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);
}
