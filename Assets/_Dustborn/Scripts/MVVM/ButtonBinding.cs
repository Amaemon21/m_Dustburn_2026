using R3;
using UnityEngine.UI;

public static class ButtonBinding
{
    public static void BindCommand(this Button button, ReactiveCommand<Unit> command, CompositeDisposable bindings)
    {
        bindings.Add(button.OnClickAsObservable().Subscribe(_ => command.Execute(Unit.Default)));
        bindings.Add(command.CanExecute.Subscribe(value => button.interactable = value));
    }
}
