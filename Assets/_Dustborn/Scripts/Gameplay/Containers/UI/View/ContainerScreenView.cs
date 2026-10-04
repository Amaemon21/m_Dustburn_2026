using System;
using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using VContainer;

public sealed class ContainerScreenView : WindowView
{
    [SerializeField, Required] private InventoryView _container;
    [SerializeField, Required] private InventoryView _backpack;
    [SerializeField, Required] private HotbarView _hotbar;
    [SerializeField, Required] private DraggableSlotView _draggableSlot;
    [SerializeField, Required] private TMP_Text _title;
    private UIInputService _input;

    public override string WindowId => ContainerScreenService.CONTAINER_WINDOW;
    public override Type ViewModelType => typeof(ContainerScreenViewModel);

    [Inject]
    public void Construct(UIInputService input)
    {
        _input = input;
    }

    protected override void BindCore(WindowViewModel viewModel, CompositeDisposable bindings)
    {
        ContainerScreenViewModel screen = (ContainerScreenViewModel)viewModel;

        _container.Bind(screen.Container);
        _backpack.Bind(screen.Backpack);
        _hotbar.Bind(screen.Hotbar);
        _draggableSlot.Bind(screen.DragDrop);
        _title.text = screen.Title;
        bindings.Add(_input.Scrolled.Subscribe(direction => screen.DragDrop.ChangeAmount(direction)));
        bindings.Add(_input.TakeAllPressed.Subscribe(_ => screen.TakeAll.Execute(Unit.Default)));

        int openedFrame = Time.frameCount;
        bindings.Add(_input.InteractClosePressed
            .Where(_ => Time.frameCount > openedFrame)
            .Subscribe(_ => screen.Close.Execute(Unit.Default)));
    }

    protected override void OnUnbound()
    {
        _draggableSlot.Unbind();
        _container.Unbind();
        _backpack.Unbind();
        _hotbar.Unbind();
    }
}
