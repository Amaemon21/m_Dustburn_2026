using System;

public interface IHeldItemPresenter : IDisposable
{
    bool Show(HeldItemConfig config);
    void Hide();
    void Play(HandsMotion motion);
}
