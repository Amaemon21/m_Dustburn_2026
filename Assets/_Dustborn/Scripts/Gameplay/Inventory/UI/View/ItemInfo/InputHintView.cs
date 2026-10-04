using NaughtyAttributes;
using TMPro;
using UnityEngine;

public sealed class InputHintView : MonoBehaviour
{
    [SerializeField, Required] private TMP_Text _keyText;

    public void SetKey(string key) => _keyText.text = key;

    public void SetVisible(bool visible) => gameObject.SetActive(visible);
}
