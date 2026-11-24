using System;
using System.Collections.Generic;
using NaughtyAttributes;
using R3;
using UnityEngine;
using VContainer;

public sealed class HudPlayerEffectsView : View<HudPlayerEffectsViewModel>
{
    [SerializeField, Required] private EffectSlotView _slotPrefab;
    [SerializeField] private EffectAppearance[] _effects = Array.Empty<EffectAppearance>();

    private readonly List<EffectSlotView> _slots = new();

    [Inject]
    public void Construct(HudPlayerEffectsViewModel viewModel) => Bind(viewModel);

    protected override void BindCore(HudPlayerEffectsViewModel viewModel, CompositeDisposable bindings)
    {
        foreach (EffectAppearance effect in _effects)
        {
            EffectSlotView slot = Instantiate(_slotPrefab, transform);
            _slots.Add(slot);
            slot.SetAppearance(effect.Icon, effect.Background);
            bindings.Add(viewModel.ObserveLabel(effect.Type).Subscribe(slot.SetLabel));
        }
    }

    protected override void OnUnbound()
    {
        foreach (EffectSlotView slot in _slots)
        {
            if (slot == null)
                continue;

            slot.gameObject.SetActive(false);
            Destroy(slot.gameObject);
        }

        _slots.Clear();
    }

    [Serializable]
    private struct EffectAppearance
    {
        [SerializeField] private PlayerEffectType _type;
        [SerializeField] private Sprite _icon;
        [SerializeField] private Sprite _background;

        public PlayerEffectType Type => _type;
        public Sprite Icon => _icon;
        public Sprite Background => _background;
    }
}
