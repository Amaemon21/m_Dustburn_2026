public class EquipmentSlotView : InteractiveItemSlotView<InventorySlotViewModel>
{
    protected override InventorySlotViewModel DraggableSlot => ViewModel;
}
