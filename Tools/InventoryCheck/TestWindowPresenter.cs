using System;

internal sealed class TestWindowPresenter : IWindowPresenter
{
    public Type ViewModelType => typeof(WindowViewModel);
    public WindowViewModel Bound { get; private set; }
    public WindowViewModel Rejected { get; set; }
    public void Bind(WindowViewModel viewModel)
    {
        Bound = viewModel;
        if (ReferenceEquals(viewModel, Rejected))
            throw new InvalidOperationException("Binding failed");
    }
    public void Unbind() => Bound = null;
    public void Focus() { }
}
