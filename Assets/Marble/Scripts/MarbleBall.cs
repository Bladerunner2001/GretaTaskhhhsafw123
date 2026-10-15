using UnityEngine;

/// <summary>
/// Rolling sphere player. Owns every physical constant of the ball (friction, bounce, mass, damping)
/// and builds a PhysicsMaterial from them at runtime, so the values below are the single source of truth.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class MarbleBall : MonoBehaviour
{
    public const float DefaultDynamicFriction = 0.6f;
    public const float DefaultStaticFriction = 0.6f;
    public const float DefaultBounciness = 0.15f;
    public const float DefaultMass = 1f;
    public const float DefaultAirDrag = 0.05f;
    public const float DefaultRollingResistance = 0.2f;
    public const float DefaultMaxAngularVelocity = 150f;

    [Header("Surface Contact")]
    [Tooltip("Friction while sliding. Track surfaces use friction 1 + Average combine, so with Multiply this value is the effective friction.")]
    [Range(0f, 1f)] public float dynamicFriction = DefaultDynamicFriction;

    [Tooltip("Friction needed to start sliding from rest.")]
    [Range(0f, 1f)] public float staticFriction = DefaultStaticFriction;

    [Tooltip("0 = no bounce, 1 = perfectly elastic. Track surfaces use bounce 0, so with Maximum this value is the effective bounce.")]
    [Range(0f, 1f)] public float bounciness = DefaultBounciness;

    public PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Multiply;
    public PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Maximum;

    [Header("Body")]
    [Min(0.01f)] public float mass = DefaultMass;

    [Tooltip("Linear damping: slows the ball everywhere, like air resistance.")]
    [Min(0f)] public float airDrag = DefaultAirDrag;

    [Tooltip("Angular damping. PhysX does not model rolling resistance, so this is what slows a rolling ball on flat ground.")]
    [Min(0f)] public float rollingResistance = DefaultRollingResistance;

    [Min(1f)] public float maxAngularVelocity = DefaultMaxAngularVelocity;

    [Header("Respawn")]
    [Tooltip("On play, derive the kill zone from all RibbonTracks: respawn when the ball leaves their combined " +
             "bounds by more than Kill Margin in any direction (also catches falling 'up' under flipped gravity).")]
    public bool autoKillHeight = true;

    [Tooltip("How far outside the tracks' bounds the ball may travel before respawning (auto mode).")]
    [Min(0f)] public float killMargin = 25f;

    [Tooltip("Falling below this world height resets the ball to where it started. Overwritten when Auto Kill Height is on.")]
    public float killHeight = -50f;

    Rigidbody body;
    SphereCollider sphere;
    PhysicsMaterial material;
    Vector3 spawnPosition;
    Quaternion spawnRotation;
    Bounds playBounds;
    bool hasPlayBounds;

    public Rigidbody Body => body;

    public event System.Action Respawned;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        ApplyPhysicsSettings();
    }

    void Start()
    {
        if (!autoKillHeight)
            return;

        var tracks = FindObjectsByType<RibbonTrack>(FindObjectsSortMode.None);
        if (tracks.Length == 0)
            return;

        Bounds bounds = tracks[0].GetComponent<MeshRenderer>().bounds;
        foreach (var track in tracks)
            bounds.Encapsulate(track.GetComponent<MeshRenderer>().bounds);
        bounds.Expand(killMargin * 2f);

        playBounds = bounds;
        hasPlayBounds = true;
        killHeight = bounds.min.y;
    }

    void OnValidate()
    {
        if (Application.isPlaying && body != null)
            ApplyPhysicsSettings();
    }

    void FixedUpdate()
    {
        if (body.position.y < killHeight || (hasPlayBounds && !playBounds.Contains(body.position)))
            Respawn();
    }

    void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }

    public void ApplyPhysicsSettings()
    {
        if (material == null)
            material = new PhysicsMaterial("MarbleBall (runtime)");

        material.dynamicFriction = dynamicFriction;
        material.staticFriction = staticFriction;
        material.bounciness = bounciness;
        material.frictionCombine = frictionCombine;
        material.bounceCombine = bounceCombine;
        sphere.sharedMaterial = material;

        body.mass = mass;
        body.linearDamping = airDrag;
        body.angularDamping = rollingResistance;
        body.maxAngularVelocity = maxAngularVelocity;
    }

    public void SetSpawn(Vector3 position)
    {
        spawnPosition = position;
    }

    public void Respawn()
    {
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = spawnPosition;
        body.rotation = spawnRotation;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        Respawned?.Invoke();
    }
}
