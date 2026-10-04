using NaughtyAttributes;
using TMPro;
using UnityEngine;

public sealed class ItemStatView : MonoBehaviour
{
    [SerializeField] private ItemStatType _type;
    [SerializeField, Required] private TMP_Text _valueText;
    [SerializeField] private string _format = "{0:0.#}";

    public ItemStatType Type => _type;

    public void Show(float value)
    {
        _valueText.text = string.Format(_format, value);
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);
}
