using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ListItemView : View<ListItemViewModel>, IPointerClickHandler
{
    [SerializeField, Required] private TMP_Text _name;
    [SerializeField] private TMP_Text _details;
    [SerializeField, Required] private Button _button;
    [SerializeField] private GameObject _selection;

    protected override void BindCore(ListItemViewModel viewModel, CompositeDisposable bindings)
    {
        _name.text = viewModel.Title;
        if (_details != null)
            _details.text = viewModel.Details;

        _button.BindCommand(viewModel.Select, bindings);

        if (_selection != null)
            bindings.Add(viewModel.IsSelected.Subscribe(_selection.SetActive));
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        
    }
}
