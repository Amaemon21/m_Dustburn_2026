using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemNameHoverView : View<ItemNameHoverViewModel>
{
    [SerializeField] private TMP_Text _itemName;
    [SerializeField] private Vector2 _offset = new(0f, 6f);

    protected override void BindCore(ItemNameHoverViewModel viewModel, CompositeDisposable bindings)
    {
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        bindings.Add(viewModel.Anchor.Subscribe(Place));
        bindings.Add(viewModel.Name.Subscribe(Show));
    }

    private void Place(Vector3 anchor)
    {
        Vector3 local = transform.parent.InverseTransformPoint(anchor);
        transform.localPosition = new Vector3(local.x + _offset.x, local.y + _offset.y, 0f);
    }

    private void Show(string itemName)
    {
        bool visible = !string.IsNullOrEmpty(itemName);
        _itemName.text = visible ? itemName : string.Empty;
        gameObject.SetActive(visible);
        if (visible)
            transform.SetAsLastSibling();
    }

    protected override void OnUnbound()
    {
        _itemName.text = string.Empty;
        gameObject.SetActive(false);
    }
}
