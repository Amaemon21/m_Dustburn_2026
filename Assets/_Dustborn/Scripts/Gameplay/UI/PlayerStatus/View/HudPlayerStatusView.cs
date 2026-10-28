using NaughtyAttributes;
using UnityEngine;
using VContainer;

public sealed class HudPlayerStatusView : MonoBehaviour
{
    [SerializeField, Required] private PlayerStatusView _status;

    [Inject]
    public void Construct(PlayerStatusViewModel viewModel)
    {
        _status.Bind(viewModel);
    }
}
