using TMPro;
using UnityEngine;

public class HotbarViewSlot : InventorySlotView
{
    [SerializeField] private TMP_Text _keyLabel;

    public void Bind(InventorySlotViewModel viewModel, int index)
    {
        base.Bind(viewModel);

        if (_keyLabel != null)
        {
            _keyLabel.text = index == 9 ? "0" : (index + 1).ToString();
        }
    }

    protected override void UnbindSlot()
    {
        base.UnbindSlot();
        
        if (_keyLabel != null)
        {
            _keyLabel.text = string.Empty;
        }
    }
}
