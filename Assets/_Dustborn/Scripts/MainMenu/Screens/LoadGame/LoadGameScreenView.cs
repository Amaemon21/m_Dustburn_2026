using NaughtyAttributes;
using R3;
using UnityEngine;
using UnityEngine.UI;

public sealed class LoadGameScreenView : View<LoadGameScreenViewModel>
{
    [SerializeField, Required] private ListItemView _slotPrefab;
    [SerializeField, Required] private Transform _slotContent;
    [SerializeField] private GameObject _emptyList;

    [SerializeField, Required, Foldout("Actions")] private Button _loadButton;
    [SerializeField, Required, Foldout("Actions")] private Button _deleteButton;
    [SerializeField, Required, Foldout("Actions")] private Button _backButton;
    
    private ListItemViews _slots;

    protected override void BindCore(LoadGameScreenViewModel viewModel, CompositeDisposable bindings)
    {
        _slots ??= new ListItemViews(_slotPrefab, _slotContent);

        bindings.Add(viewModel.IsVisible.Subscribe(gameObject.SetActive));
        bindings.Add(viewModel.Items.Subscribe(_slots.Show));

        if (_emptyList != null)
            bindings.Add(viewModel.IsEmpty.Subscribe(_emptyList.SetActive));

        _loadButton.BindCommand(viewModel.Load, bindings);
        _deleteButton.BindCommand(viewModel.Delete, bindings);
        _backButton.BindCommand(viewModel.Back, bindings);
    }

    protected override void OnUnbound()
    {
        _slots?.Unbind();
    }
}
