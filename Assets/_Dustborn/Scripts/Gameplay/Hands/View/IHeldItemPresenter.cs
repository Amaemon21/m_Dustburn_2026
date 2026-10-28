using System;

public interface IHeldItemPresenter : IDisposable
{
    bool Show(HeldItemConfig config);
    void Hide();
    bool Play(HandsMotion motion);
    void Impact(float shake);
    void Tick(float deltaTime);
}
