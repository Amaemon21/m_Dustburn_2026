using System.Collections.Generic;
using NaughtyAttributes;
using R3;
using UnityEngine;
using VContainer;

public sealed class HudHotbarView : View<HudHotbarViewModel>
{
    [SerializeField, Required] private HudHotbarSlotView _slotPrefab;
    [SerializeField, Required] private Transform _content;
    private SlotViewPool<HudHotbarSlotView> _pool;

    [Inject]
    public void Construct(HudHotbarViewModel viewModel, IObjectResolver resolver)
    {
        _pool = new SlotViewPool<HudHotbarSlotView>(_slotPrefab, _content, resolver);
        Bind(viewModel);
    }

    protected override void BindCore(HudHotbarViewModel viewModel, CompositeDisposable bindings)
    {
        IReadOnlyList<HudHotbarSlotView> views = _pool.Show(viewModel.Slots.Count);
        for (int i = 0; i < viewModel.Slots.Count; i++)
            views[i].Bind(viewModel.Slots[i]);
    }

    protected override void OnUnbound()
    {
        if (_pool == null)
            return;

        foreach (HudHotbarSlotView slot in _pool.Views)
            slot.Unbind();
    }
}
