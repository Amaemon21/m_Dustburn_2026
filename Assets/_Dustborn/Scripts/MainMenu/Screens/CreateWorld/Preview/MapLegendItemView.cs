using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MapLegendItemView : MonoBehaviour
{
    [SerializeField, Required] private Image _swatch;
    [SerializeField, Required] private TMP_Text _label;

    public void Show(MapLegendEntry entry)
    {
        _swatch.color = entry.Color;
        _label.text = entry.Label;
    }
}
