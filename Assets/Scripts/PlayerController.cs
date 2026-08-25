using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private Camera _camera;
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _mouseSensitivity = 0.15f;
    [SerializeField] private float _gravity = -20f;

    private CharacterController _characterController;

    private float _cameraPitch;
    private float _verticalVelocity;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        Move();
        Look();
    }

    private void Move()
    {
        Vector2 input = Keyboard.current != null
            ? new Vector2(
                Keyboard.current.dKey.isPressed ? 1 : Keyboard.current.aKey.isPressed ? -1 : 0,
                Keyboard.current.wKey.isPressed ? 1 : Keyboard.current.sKey.isPressed ? -1 : 0)
            : Vector2.zero;

        Vector3 direction =
            transform.right * input.x +
            transform.forward * input.y;

        direction = Vector3.ClampMagnitude(direction, 1f);

        if (_characterController.isGrounded && _verticalVelocity < 0)
        {
            _verticalVelocity = -2f;
        }

        _verticalVelocity += _gravity * Time.deltaTime;

        Vector3 velocity =
            direction * _moveSpeed +
            Vector3.up * _verticalVelocity;

        _characterController.Move(velocity * Time.deltaTime);
    }

    private void Look()
    {
        if (Mouse.current == null)
            return;

        Vector2 mouseDelta =
            Mouse.current.delta.ReadValue() * _mouseSensitivity;

        transform.Rotate(Vector3.up * mouseDelta.x);

        _cameraPitch -= mouseDelta.y;
        _cameraPitch = Mathf.Clamp(_cameraPitch, -89f, 89f);

        _camera.transform.localRotation =
            Quaternion.Euler(_cameraPitch, 0f, 0f);
    }
}