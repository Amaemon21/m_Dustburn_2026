using UnityEngine;
using UnityEngine.EventSystems;

public sealed class InventorySelectionBackgroundView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private InventoryItemInfoView _itemInfo;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        _itemInfo.CloseSelection();
    }
}
