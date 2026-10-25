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
    [Tooltip("Speed at which the camera swings at full Yaw Follow Speed. Between Min Speed For Yaw and this, " +
             "the swing fades in gradually instead of switching on.")]
    [Min(0f)] public float fullYawSpeed = 5f;
    [Tooltip("Smoothing time on the travel direction the camera follows; filters out bumps and landings.")]
    [Min(0f)] public float velocitySmoothTime = 0.25f;

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

    [Header("Free Gravity Mode")]
    [Tooltip("Used when the tilt controller is in Free mode: how quickly the camera's up follows the current " +
             "anti-gravity direction. The camera is unclamped there, so the world can appear fully upside down.")]
    [Min(0.001f)] public float freeUpSmoothTime = 0.12f;
    [Tooltip("Gravity changes larger than this in one frame (e.g. respawn) snap the camera instead of swinging.")]
    [Range(30f, 180f)] public float freeSnapAngle = 120f;

    [Header("Occlusion")]
    [Tooltip("Pull the camera in front of level geometry that would block the view of the ball.")]
    public bool avoidOcclusion = true;
    public LayerMask occlusionLayers = ~0;
    [Tooltip("Radius of the probe swept from the ball to the camera; roughly the clearance the camera keeps from walls.")]
    [Min(0.01f)] public float occlusionProbeRadius = 0.25f;
    [Tooltip("Closest the camera may be pulled towards the ball.")]
    [Min(0f)] public float minOcclusionDistance = 0.8f;
    [Tooltip("Time to ease back out once the view is clear. Pulling in is always instant.")]
    [Min(0f)] public float occlusionReturnTime = 0.3f;
    [Tooltip("Sets Physics.queriesHitBackfaces (global) so the probe also detects the ribbon from its underside.")]
    public bool hitBackfaces = true;

    readonly RaycastHit[] occlusionHits = new RaycastHit[16];
    float occlusionDistance = float.PositiveInfinity;
    float occlusionVelocity;

    float yaw;
    Vector3 smoothedVelocity;
    Vector3 smoothedVelocityRate;
    bool freeActive;
    Vector3 freeUp = Vector3.up;
    Vector3 freeHeading = Vector3.forward;
    Vector3 basePosition;
    Vector3 positionVelocity;
    float pitch;
    float roll;
    float pitchVelocity;
    float rollVelocity;

    public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    public Vector3 PlanarRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

    /// <summary>Direction the camera looks along the ground: world-planar in Classic mode, gravity-relative in Free mode.</summary>
    public Vector3 HeadingForward => freeActive ? freeHeading : PlanarForward;

    bool FreeMode => tiltController != null && tiltController.mode == MarbleTiltController.GravityMode.Free;

    /// <summary>Current visual tilt in degrees, after scaling and clamping.</summary>
    public float Pitch => pitch;
    public float Roll => roll;

    void Awake()
    {
        if (hitBackfaces)
            Physics.queriesHitBackfaces = true;
    }

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

        smoothedVelocity = Vector3.SmoothDamp(smoothedVelocity, target.linearVelocity, ref smoothedVelocityRate, velocitySmoothTime);

        if (FreeMode != freeActive)
            SwitchMode(FreeMode);

        if (freeActive)
        {
            UpdateFree(pivot);
            return;
        }

        UpdateYaw();
        UpdateTilt();

        basePosition = Vector3.SmoothDamp(basePosition, DesiredBasePosition(pivot), ref positionVelocity, positionSmoothTime);

        Quaternion tilt = Quaternion.AngleAxis(roll, PlanarForward) * Quaternion.AngleAxis(-pitch, PlanarRight);
        Vector3 offset = tilt * (basePosition - pivot);
        if (offset.sqrMagnitude < 1e-6f)
            return;

        transform.SetPositionAndRotation(ResolveOcclusion(pivot, pivot + offset), Quaternion.LookRotation(-offset, tilt * Vector3.up));
    }

    /// <summary>Moves the camera along the ball-to-camera line so no geometry sits between them.</summary>
    Vector3 ResolveOcclusion(Vector3 pivot, Vector3 cameraPosition)
    {
        Vector3 offset = cameraPosition - pivot;
        float fullDistance = offset.magnitude;
        if (!avoidOcclusion || fullDistance < 1e-4f)
        {
            occlusionDistance = fullDistance;
            occlusionVelocity = 0f;
            return cameraPosition;
        }

        Vector3 direction = offset / fullDistance;
        float allowed = fullDistance;

        int count = Physics.SphereCastNonAlloc(pivot, occlusionProbeRadius, direction, occlusionHits,
            fullDistance, occlusionLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = occlusionHits[i];
            // Distance 0 means the probe started inside that collider (e.g. the ball itself), not a blocker.
            if (hit.distance <= 0f || hit.rigidbody == target)
                continue;
            allowed = Mathf.Min(allowed, hit.distance);
        }

        allowed = Mathf.Max(allowed, Mathf.Min(minOcclusionDistance, fullDistance));

        if (allowed < occlusionDistance)
        {
            occlusionDistance = allowed;
            occlusionVelocity = 0f;
        }
        else
        {
            occlusionDistance = Mathf.SmoothDamp(occlusionDistance, allowed, ref occlusionVelocity, occlusionReturnTime);
        }

        return pivot + direction * Mathf.Min(occlusionDistance, fullDistance);
    }

    void UpdateYaw()
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(smoothedVelocity, Vector3.up);
        float k = YawBlend(planarVelocity.magnitude);
        if (k <= 0f)
            return;

        float targetYaw = Mathf.Atan2(planarVelocity.x, planarVelocity.z) * Mathf.Rad2Deg;
        yaw = Mathf.LerpAngle(yaw, targetYaw, k);
    }

    /// <summary>Per-frame blend factor toward the travel direction, faded in by speed.</summary>
    float YawBlend(float speed)
    {
        float weight = Mathf.InverseLerp(minSpeedForYaw, Mathf.Max(fullYawSpeed, minSpeedForYaw + 0.01f), speed);
        return 1f - Mathf.Exp(-yawFollowSpeed * weight * Time.deltaTime);
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

    void SwitchMode(bool free)
    {
        freeActive = free;
        if (free)
        {
            freeUp = Vector3.up;
            freeHeading = PlanarForward;
            return;
        }

        Vector3 planar = Vector3.ProjectOnPlane(freeHeading, Vector3.up);
        if (planar.sqrMagnitude > 1e-4f)
            yaw = Mathf.Atan2(planar.x, planar.z) * Mathf.Rad2Deg;
        pitch = roll = pitchVelocity = rollVelocity = 0f;
    }

    // Up and heading are carried forward incrementally (each frame rotated by only the change since the
    // last frame), because an absolute rotation from world up is undefined at exactly 180°.
    void UpdateFree(Vector3 pivot)
    {
        Vector3 targetUp = tiltController.Up;
        float dt = Time.deltaTime;

        Vector3 newUp;
        bool snap = Vector3.Angle(freeUp, targetUp) > freeSnapAngle;
        if (snap)
            newUp = targetUp;
        else
            newUp = Vector3.Slerp(freeUp, targetUp, 1f - Mathf.Exp(-dt / freeUpSmoothTime)).normalized;

        freeHeading = Quaternion.FromToRotation(freeUp, newUp) * freeHeading;
        freeUp = newUp;
        freeHeading = Vector3.ProjectOnPlane(freeHeading, freeUp);
        if (freeHeading.sqrMagnitude < 1e-6f)
            freeHeading = Vector3.ProjectOnPlane(transform.forward, freeUp);
        freeHeading.Normalize();

        Vector3 planarVelocity = Vector3.ProjectOnPlane(smoothedVelocity, freeUp);
        float k = YawBlend(planarVelocity.magnitude);
        if (k > 0f)
        {
            float turn = Vector3.SignedAngle(freeHeading, planarVelocity, freeUp);
            freeHeading = Quaternion.AngleAxis(turn * k, freeUp) * freeHeading;
        }

        Vector3 desired = pivot - freeHeading * distance + freeUp * height;
        if (snap)
        {
            basePosition = desired;
            positionVelocity = Vector3.zero;
        }
        else
        {
            basePosition = Vector3.SmoothDamp(basePosition, desired, ref positionVelocity, positionSmoothTime);
        }

        Vector3 toBall = pivot - basePosition;
        if (toBall.sqrMagnitude < 1e-6f)
            return;

        transform.SetPositionAndRotation(ResolveOcclusion(pivot, basePosition), Quaternion.LookRotation(toBall, freeUp));
    }
}
