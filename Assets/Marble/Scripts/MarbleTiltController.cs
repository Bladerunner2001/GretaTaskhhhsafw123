using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Super Monkey Ball style "tilt" without moving the level: player input tilts the global gravity vector
/// (Physics.gravity) relative to the camera's heading. The ball is never pushed or given a velocity; it
/// only rolls because the gravity acting on it is no longer straight down.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MarbleTiltController : MonoBehaviour
{
    public enum GravityMode
    {
        [Tooltip("Stick deflection maps to a tilt angle (up to Max Tilt). Releasing the stick levels gravity again.")]
        Classic,
        [Tooltip("Stick rotates gravity relative to its current direction at Free Tilt Speed. Releasing keeps it, " +
                 "so gravity can reach any direction, including fully upside down.")]
        Free,
    }

    [Tooltip("Heading reference for input. Falls back to the main camera.")]
    public MarbleFollowCamera followCamera;

    public GravityMode mode = GravityMode.Classic;

    [Tooltip("Free mode: how fast gravity turns at full stick deflection, in degrees per second.")]
    [Min(0f)] public float freeTiltSpeed = 60f;

    [Tooltip("Magnitude of gravity in m/s². Neutral gravity is (0, -gravityStrength, 0).")]
    [Min(0f)] public float gravityStrength = 9.81f;

    [Tooltip("Angle between the tilted down-vector and true down at full stick deflection, in any direction.")]
    [Range(0f, 180f)] public float maxTiltDegrees = 25f;

    [Header("Input Dampening")]
    [Tooltip("Approximate time for the smoothed input to reach the raw stick value. Higher = heavier, floatier tilt.")]
    [Min(0.001f)] public float inputSmoothTime = 0.12f;

    [Tooltip("Hard cap on how fast the smoothed input may change, in full deflections per second. " +
             "Bounds the tilt rate so a stick flick can't jerk the ball through geometry.")]
    [Min(0.01f)] public float maxInputSpeed = 6f;

    public Vector3 Gravity { get; private set; }

    /// <summary>Smoothed input in [-1, 1]: x = right, y = forward.</summary>
    public Vector2 SmoothedInput => smoothedInput;

    /// <summary>Current tilt in degrees: x = roll (right), y = pitch (forward).</summary>
    public Vector2 Tilt => smoothedInput * maxTiltDegrees;

    Rigidbody body;
    Vector2 rawInput;
    Vector2 smoothedInput;
    Vector2 smoothVelocity;
    Vector3 originalGravity;
    Vector3 freeDown = Vector3.down;
    GravityMode lastMode;

    /// <summary>Direction the player currently perceives as "up" (opposite of gravity).</summary>
    public Vector3 Up => Gravity.sqrMagnitude > 1e-8f ? -Gravity.normalized : Vector3.up;

    Vector3 NeutralGravity => Vector3.down * gravityStrength;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = true;
    }

    void OnEnable()
    {
        originalGravity = Physics.gravity;
        ResetTilt();
        if (TryGetComponent(out MarbleBall ball))
            ball.Respawned += ResetTilt;
    }

    void OnDisable()
    {
        if (TryGetComponent(out MarbleBall ball))
            ball.Respawned -= ResetTilt;
        Physics.gravity = originalGravity;
    }

    void Update()
    {
        rawInput = ReadInput();

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            if (TryGetComponent(out MarbleBall ball))
                ball.Respawn();
            else
                ResetTilt();
        }
    }

    void FixedUpdate()
    {
        smoothedInput = Vector2.SmoothDamp(smoothedInput, rawInput, ref smoothVelocity,
            inputSmoothTime, maxInputSpeed, Time.fixedDeltaTime);

        if (mode != lastMode)
        {
            if (mode == GravityMode.Free)
                freeDown = Gravity.sqrMagnitude > 1e-8f ? Gravity.normalized : Vector3.down;
            lastMode = mode;
        }

        Vector3 gravity = mode == GravityMode.Free
            ? ComputeFreeGravity(smoothedInput, Time.fixedDeltaTime)
            : ComputeGravity(smoothedInput);
        if (gravity != Gravity)
        {
            Gravity = gravity;
            Physics.gravity = gravity;
            // A sleeping body ignores gravity changes, so a ball at rest would never react to the tilt.
            body.WakeUp();
        }
    }

    Vector3 ComputeGravity(Vector2 input)
    {
        float magnitude = Mathf.Min(input.magnitude, 1f);
        if (magnitude < 1e-4f)
            return NeutralGravity;

        Vector3 forward = GetPlanarForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 tiltDirection = (right * input.x + forward * input.y).normalized;

        float angle = magnitude * maxTiltDegrees * Mathf.Deg2Rad;
        Vector3 down = Vector3.down * Mathf.Cos(angle) + tiltDirection * Mathf.Sin(angle);
        return down * gravityStrength;
    }

    Vector3 ComputeFreeGravity(Vector2 input, float deltaTime)
    {
        Vector3 up = -freeDown;
        Vector3 forward = GetHeadingForward(up);
        Vector3 right = Vector3.Cross(up, forward);

        float step = freeTiltSpeed * deltaTime;
        Quaternion turn = Quaternion.AngleAxis(-input.y * step, right) * Quaternion.AngleAxis(input.x * step, forward);
        freeDown = (turn * freeDown).normalized;
        return freeDown * gravityStrength;
    }

    void ResetTilt()
    {
        rawInput = Vector2.zero;
        smoothedInput = Vector2.zero;
        smoothVelocity = Vector2.zero;
        freeDown = Vector3.down;
        lastMode = mode;
        Gravity = NeutralGravity;
        Physics.gravity = Gravity;
    }

    /// <summary>Camera heading flattened onto the plane perpendicular to <paramref name="up"/>.</summary>
    Vector3 GetHeadingForward(Vector3 up)
    {
        Vector3 reference;
        if (followCamera != null)
            reference = followCamera.HeadingForward;
        else if (Camera.main != null)
            reference = Camera.main.transform.forward;
        else
            reference = Vector3.forward;

        Vector3 forward = Vector3.ProjectOnPlane(reference, up);
        if (forward.sqrMagnitude < 1e-6f && Camera.main != null)
            forward = Vector3.ProjectOnPlane(Camera.main.transform.up, up);
        return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
    }

    Vector3 GetPlanarForward()
    {
        if (followCamera != null)
            return followCamera.PlanarForward;

        Camera cam = Camera.main;
        if (cam == null)
            return Vector3.forward;

        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
    }

    /// <summary>Gamepad left stick takes priority; WASD / arrow keys are used when the stick is idle or absent.</summary>
    static Vector2 ReadInput()
    {
        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0f)
                return Vector2.ClampMagnitude(stick, 1f);
        }

        Vector2 value = Vector2.zero;
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) value.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) value.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) value.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) value.x -= 1f;
        }

        return Vector2.ClampMagnitude(value, 1f);
    }
}
