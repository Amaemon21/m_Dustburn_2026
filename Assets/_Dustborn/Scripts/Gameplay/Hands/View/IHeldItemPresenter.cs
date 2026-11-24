using System;
using UnityEngine;

public interface IHeldItemPresenter : IDisposable
{
    bool Show(HeldItemConfig config);
    void Hide();
    bool Play(HandsMotion motion);
    void Impact(float shake, Material particles, in MeleeImpact impact);
    void Tick(float deltaTime);
}
