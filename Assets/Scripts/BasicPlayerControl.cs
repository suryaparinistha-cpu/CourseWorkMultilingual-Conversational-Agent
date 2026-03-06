using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Basic first-person style player controller.
/// Uses CharacterController for movement + gravity, and Input System actions
/// provided through InputActionProperty fields.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class BasicPlayerControl : MonoBehaviour
{
    private const float MaxReasonableLookDelta = 1000f;
    private const float MaxReasonableMoveDeltaPerFrame = 5f;

    [Header("Movement")]
    [Tooltip("Movement speed in units per second.")]
    public float moveSpeed = 5f;
    [Tooltip("Ignore tiny move input noise under this magnitude.")]
    [Range(0f, 1f)]
    public float moveDeadZone = 0.1f;
    [Tooltip("Gravity acceleration value.")]
    public float gravity = -9.81f;
    [Tooltip("Enable/disable gravity influence.")]
    public bool useGravity = true;
    [Tooltip("Small downward force while grounded to keep the controller snapped to ground.")]
    public float groundedStickForce = -2f;
    [Tooltip("Clamp falling speed to avoid unstable large velocities.")]
    public float maxFallSpeed = 50f;
    [Tooltip("Clamp simulation delta time to avoid big teleport on hitchy frames.")]
    public float maxSimulationDeltaTime = 0.05f;
    [Tooltip("Print diagnostics when CharacterController moves much more than requested.")]
    public bool enableMoveDiagnostics = true;

    [Header("Look")]
    [Tooltip("Mouse/look sensitivity multiplier.")]
    public float lookSensitivity = 1f;
    [Tooltip("Transform of the camera that will receive pitch rotations.")]
    public Transform cameraTransform;
    [Tooltip("Maximum up/down angle in degrees from forward.")]
    public float maxPitchAngle = 85f;

    [Header("Input Actions")]
    [Tooltip("Vector2 action. X=strafe, Y=forward/back.")]
    public InputActionProperty moveAction;
    [Tooltip("Vector2 action. X=yaw, Y=pitch.")]
    public InputActionProperty lookAction;
    [Tooltip("Button action for interaction.")]
    public InputActionProperty interactAction;

    [Header("Events")]
    [Tooltip("Invoked when the interact action is started (button pressed).")]
    public UnityEvent onInteractPressed;
    [Tooltip("Invoked when the interact action is canceled (button released).")]
    public UnityEvent onInteractReleased;

    private CharacterController _controller;

    // gravity & vertical velocity
    private float _verticalVelocity = 0f;

    // current pitch angle (degrees)
    private float _pitch = 0f;
    private bool _warnedAboutInvalidMoveInput = false;
    private bool _hasLoggedStartupSample = false;

    private InputAction Move => moveAction.action;
    private InputAction Look => lookAction.action;
    private InputAction Interact => interactAction.action;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (_controller == null)
            Debug.LogError("BasicPlayerControl requires a CharacterController on the same GameObject.");
        
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            Debug.LogWarning("BasicPlayerControl: Non-kinematic Rigidbody found with CharacterController. This can cause unstable motion. Consider removing Rigidbody or enabling Is Kinematic.");
        }

        if (cameraTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null)
                cameraTransform = childCamera.transform;
        }

        // lock cursor initially for FPS-style control
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnEnable()
    {
        Move?.Enable();
        Look?.Enable();
        if (Interact != null)
        {
            Interact.Enable();
            Interact.started += OnInteractStarted;
            Interact.canceled += OnInteractCanceled;
        }
    }

    private void OnDisable()
    {
        Move?.Disable();
        Look?.Disable();
        if (Interact != null)
        {
            Interact.started -= OnInteractStarted;
            Interact.canceled -= OnInteractCanceled;
            Interact.Disable();
        }
    }

    private void Update()
    {
        // allow unlocking with Escape key for convenience
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        float dt = Mathf.Min(Time.deltaTime, maxSimulationDeltaTime);

        // read movement and look each frame
        Vector2 moveInput = Move != null ? Move.ReadValue<Vector2>() : Vector2.zero;
        Vector2 lookInput = Look != null ? Look.ReadValue<Vector2>() : Vector2.zero;

        // Defensive guard: malformed bindings can sometimes return invalid values.
        if (!IsFinite(moveInput))
        {
            Debug.LogWarning("BasicPlayerControl: Move input returned non-finite value. Input ignored this frame.");
            moveInput = Vector2.zero;
        }
        if (!IsFinite(lookInput))
        {
            Debug.LogWarning("BasicPlayerControl: Look input returned non-finite value. Input ignored this frame.");
            lookInput = Vector2.zero;
        }

        if (!_hasLoggedStartupSample)
        {
            Debug.Log($"BasicPlayerControl startup sample | pos={transform.position} grounded={_controller.isGrounded} move={moveInput} look={lookInput}");
            _hasLoggedStartupSample = true;
        }

        // Guard against wrongly bound move actions (e.g. pointer position).
        if (moveInput.sqrMagnitude > 4f)
        {
            if (!_warnedAboutInvalidMoveInput)
            {
                Debug.LogWarning("BasicPlayerControl: Move action appears to output large values. Ensure it's bound to WASD/stick Vector2, not pointer position.");
                _warnedAboutInvalidMoveInput = true;
            }
            moveInput = Vector2.zero;
        }
        else
        {
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);
            if (moveInput.magnitude < moveDeadZone)
            {
                moveInput = Vector2.zero;
            }
        }

        // horizontal movement
        Vector3 horizontalMove = transform.right * moveInput.x + transform.forward * moveInput.y;

        // gravity
        if (!useGravity)
        {
            _verticalVelocity = 0f;
        }
        else
        {
            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickForce;
            }
            _verticalVelocity += gravity * dt;
            _verticalVelocity = Mathf.Max(_verticalVelocity, -maxFallSpeed);
        }

        Vector3 frameMove =
            (horizontalMove * moveSpeed * dt) +
            (Vector3.up * _verticalVelocity * dt);

        if (!IsFinite(frameMove) || frameMove.magnitude > MaxReasonableMoveDeltaPerFrame)
        {
            Debug.LogWarning($"BasicPlayerControl: Abnormal frameMove detected ({frameMove}). Movement ignored this frame. Check Move/Look bindings and speed settings.");
            frameMove = Vector3.zero;
        }

        Vector3 beforePos = transform.position;
        _controller.Move(frameMove);
        Vector3 actualDelta = transform.position - beforePos;

        if (enableMoveDiagnostics && actualDelta.magnitude > frameMove.magnitude + 0.5f)
        {
            Debug.LogWarning(
                $"BasicPlayerControl: CharacterController displacement larger than requested. " +
                $"requested={frameMove} actual={actualDelta} pos={transform.position} " +
                $"controller(center={_controller.center}, radius={_controller.radius}, height={_controller.height}, skin={_controller.skinWidth}) " +
                $"scale={transform.lossyScale}");
        }

        // look rotation: yaw on body, pitch on camera
        if (Mathf.Abs(lookInput.x) > MaxReasonableLookDelta || Mathf.Abs(lookInput.y) > MaxReasonableLookDelta)
        {
            Debug.LogWarning($"BasicPlayerControl: Abnormal look input detected ({lookInput}). Look ignored this frame.");
            lookInput = Vector2.zero;
        }

        if (lookInput.x != 0f)
        {
            transform.Rotate(Vector3.up, lookInput.x * lookSensitivity);
        }
        if (lookInput.y != 0f && cameraTransform != null)
        {
            // invert y so moving mouse up looks up
            _pitch -= lookInput.y * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch, -maxPitchAngle, maxPitchAngle);
            cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }

    private void OnInteractStarted(InputAction.CallbackContext ctx)
    {
        onInteractPressed?.Invoke();
    }

    private void OnInteractCanceled(InputAction.CallbackContext ctx)
    {
        onInteractReleased?.Invoke();
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
