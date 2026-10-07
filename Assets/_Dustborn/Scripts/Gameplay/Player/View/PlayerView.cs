using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerView : MonoBehaviour
{
    [field: SerializeField, Required, FormerlySerializedAs("_cameraTransform"), BoxGroup("Camera"), HorizontalLine(2f, EColor.Blue)]
    public Transform CameraPivot { get; private set; }
    [field: SerializeField, BoxGroup("Camera")] public Transform TiltRoot { get; private set; }
    [field: SerializeField, Required, BoxGroup("Hands"), HorizontalLine(2f, EColor.Violet)]
    public Animator HandsAnimator { get; private set; }
    [field: SerializeField, Required, BoxGroup("Hands")] public Transform HandsSocket { get; private set; }
    [field: SerializeField, BoxGroup("Hands")] public Renderer[] HandsRenderers { get; private set; }
    [field: SerializeField, BoxGroup("Hands")] public HeldItemConfig UnarmedHands { get; private set; }
    [field: SerializeField, Required, BoxGroup("Configs"), HorizontalLine(2f, EColor.Green)]
    public PlayerMovementConfig Movement { get; private set; }
    [field: SerializeField, Required, BoxGroup("Configs")] public PlayerCameraConfig CameraMotion { get; private set; }

    private CharacterController _controller;

    public CharacterController Controller => _controller != null ? _controller : _controller = GetComponent<CharacterController>();
}
