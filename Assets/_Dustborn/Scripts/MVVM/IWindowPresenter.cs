using System;

public interface IWindowPresenter
{
    Type ViewModelType { get; }
    void Bind(WindowViewModel viewModel);
    void Unbind();
    void Focus();
}
