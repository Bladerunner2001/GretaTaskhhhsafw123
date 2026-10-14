using UnityEngine;

/// <summary>
/// Third-person chase camera. Follows behind and above the ball with a damped offset and always aims
/// exactly at the ball, so the ball stays centred on screen as a fixed visual anchor. A scaled-down,
/// clamped version of the gravity tilt is applied as an orbit around the ball, which makes the world
/// appear to tip without the level geometry ever moving.
/// </summary>
public class MarbleFollowCamera : MonoBehaviour
{
    public Rigidbody target;
    public MarbleTiltController tiltController;

    [Header("Framing")]
    [Min(0f)] public float distance = 6f;
    [Min(0f)] public float height = 2.2f;
    [Tooltip("Smoothing time of the follow offset. The aim is never damped, so the ball stays centred.")]
    [Min(0f)] public float positionSmoothTime = 0.15f;

    [Header("Heading")]
    [Tooltip("How quickly the camera swings behind the direction of travel.")]
    [Min(0f)] public float yawFollowSpeed = 2.5f;
    [Tooltip("Below this horizontal speed the camera keeps its current heading.")]
    [Min(0f)] public float minSpeedForYaw = 1.5f;

    [Header("Visual Tilt")]
    [Tooltip("Fraction of the gravity pitch (forward/back) mirrored by the camera.")]
    [Range(0f, 1f)] public float pitchAmount = 0.6f;
    [Tooltip("Fraction of the gravity roll (left/right) mirrored by the camera.")]
    [Range(0f, 1f)] public float rollAmount = 0.6f;
    [Range(0f, 30f)] public float maxPitchDegrees = 15f;
    [Tooltip("Hard limit on camera roll. Strong roll is the main cause of simulator nausea.")]
    [Range(0f, 15f)] public float maxRollDegrees = 15f;
    [Tooltip("Extra smoothing on the visual tilt, so 50 Hz physics steps don't show up as stepping.")]
    [Min(0f)] public float tiltSmoothTime = 0.08f;

    float yaw;
    Vector3 basePosition;
    Vector3 positionVelocity;
    float pitch;
    float roll;
    float pitchVelocity;
    float rollVelocity;

    public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    public Vector3 PlanarRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

    /// <summary>Current visual tilt in degrees, after scaling and clamping.</summary>
    public float Pitch => pitch;
    public float Roll => roll;

    void Start()
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude > 1e-4f)
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

        basePosition = target != null ? DesiredBasePosition(target.transform.position) : transform.position;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        // The interpolated transform, not Rigidbody.position, or the ball jitters between physics steps.
        Vector3 pivot = target.transform.position;

        UpdateYaw();
        UpdateTilt();

        basePosition = Vector3.SmoothDamp(basePosition, DesiredBasePosition(pivot), ref positionVelocity, positionSmoothTime);

        Quaternion tilt = Quaternion.AngleAxis(roll, PlanarForward) * Quaternion.AngleAxis(-pitch, PlanarRight);
        Vector3 offset = tilt * (basePosition - pivot);
        if (offset.sqrMagnitude < 1e-6f)
            return;

        transform.SetPositionAndRotation(pivot + offset, Quaternion.LookRotation(-offset, tilt * Vector3.up));
    }

    void UpdateYaw()
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(target.linearVelocity, Vector3.up);
        if (planarVelocity.magnitude <= minSpeedForYaw)
            return;

        float targetYaw = Mathf.Atan2(planarVelocity.x, planarVelocity.z) * Mathf.Rad2Deg;
        yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-yawFollowSpeed * Time.deltaTime));
    }

    void UpdateTilt()
    {
        float targetPitch = 0f;
        float targetRoll = 0f;

        if (tiltController != null)
        {
            Vector3 g = tiltController.Gravity;
            float down = -Vector3.Dot(g, Vector3.up);
            if (down > 1e-4f)
            {
                float gravityPitch = Mathf.Atan2(Vector3.Dot(g, PlanarForward), down) * Mathf.Rad2Deg;
                float gravityRoll = Mathf.Atan2(Vector3.Dot(g, PlanarRight), down) * Mathf.Rad2Deg;
                targetPitch = Mathf.Clamp(gravityPitch * pitchAmount, -maxPitchDegrees, maxPitchDegrees);
                targetRoll = Mathf.Clamp(gravityRoll * rollAmount, -maxRollDegrees, maxRollDegrees);
            }
        }

        pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, tiltSmoothTime);
        roll = Mathf.Clamp(Mathf.SmoothDamp(roll, targetRoll, ref rollVelocity, tiltSmoothTime), -maxRollDegrees, maxRollDegrees);
    }

    Vector3 DesiredBasePosition(Vector3 pivot)
    {
        return pivot - PlanarForward * distance + Vector3.up * height;
    }
}
