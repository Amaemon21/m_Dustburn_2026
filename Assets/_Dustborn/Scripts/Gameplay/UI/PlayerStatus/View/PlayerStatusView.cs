using R3;
using UnityEngine;

public sealed class PlayerStatusView : View<PlayerStatusViewModel>
{
    [SerializeField] private VitalBarView _health;
    [SerializeField] private VitalBarView _stamina;
    [SerializeField] private VitalBarView _food;
    [SerializeField] private VitalBarView _water;
    [SerializeField] private VitalBarView _infection;
    [SerializeField] private PlayerLevelView _level;

    protected override void BindCore(PlayerStatusViewModel viewModel, CompositeDisposable bindings)
    {
        BindPart(_health, viewModel.Health);
        BindPart(_stamina, viewModel.Stamina);
        BindPart(_food, viewModel.Food);
        BindPart(_water, viewModel.Water);
        BindPart(_infection, viewModel.Infection);
        
        if (_level != null)
            _level.Bind(viewModel.Level);
    }

    private static void BindPart(VitalBarView view, VitalViewModel viewModel)
    {
        if (view != null)
            view.Bind(viewModel);
    }

    protected override void OnUnbound()
    {
        UnbindPart(_health);
        UnbindPart(_stamina);
        UnbindPart(_food);
        UnbindPart(_water);
        UnbindPart(_infection);
        
        if (_level != null)
            _level.Unbind();
    }

    private static void UnbindPart(VitalBarView view)
    {
        if (view != null)
            view.Unbind();
    }
}
