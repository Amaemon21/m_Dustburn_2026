using NaughtyAttributes;
using R3;
using UnityEngine;
using VContainer;

public sealed class ItemNotificationsView : View<ItemNotificationsViewModel>
{
    [SerializeField, Required] private ItemNotificationView _entryPrefab;
    [SerializeField, Required] private Transform _content;

    [Inject]
    public void Construct(ItemNotificationsViewModel viewModel) => Bind(viewModel);

    protected override void BindCore(ItemNotificationsViewModel viewModel, CompositeDisposable bindings)
    {
        if (_entryPrefab == null || _content == null)
        {
            Debug.LogError($"{nameof(ItemNotificationsView)}: Entry Prefab or Content is not assigned, pickup notifications are off", this);
            return;
        }

        foreach (ItemNotificationViewModel entry in viewModel.Active)
            Spawn(entry);

        bindings.Add(viewModel.Added.Subscribe(Spawn));
    }

    private void Spawn(ItemNotificationViewModel entry)
    {
        ItemNotificationView view = Instantiate(_entryPrefab, _content);
        view.transform.SetAsLastSibling();
        view.Bind(entry);
    }
}
