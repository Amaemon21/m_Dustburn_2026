using System;
using UnityEngine;

public sealed class PlayerAimSource : IAimSource
{
    private readonly PlayerView _view;

    public PlayerAimSource(PlayerView view)
    {
        _view = view != null ? view : throw new ArgumentNullException(nameof(view));
    }

    public Ray Aim => new(_view.CameraPivot.position, _view.CameraPivot.forward);
    public Transform Body => _view.transform;
}
