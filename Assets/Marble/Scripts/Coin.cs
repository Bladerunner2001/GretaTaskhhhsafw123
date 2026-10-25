using UnityEngine;

/// <summary>
/// Collectable coin. Bobs up and down and spins around its parent's up axis, and is collected
/// when a MarbleBall enters its trigger. Model orientation is preserved, so rotate the coin however
/// it should face and the animation is applied on top.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Coin : MonoBehaviour
{
    [Header("Value")]
    [Min(1)] public int value = 1;

    [Header("Bobbing")]
    [Tooltip("How far the coin moves up and down from its placed position.")]
    [Min(0f)] public float bobHeight = 0.25f;

    [Tooltip("Full up-and-down cycles per second.")]
    [Min(0f)] public float bobSpeed = 1f;

    [Tooltip("Start each coin at a random point in the cycle so a row of coins doesn't move in lockstep.")]
    public bool randomizePhase = true;

    [Header("Spinning")]
    [Tooltip("Degrees per second around the parent's up axis. 0 disables spinning.")]
    public float spinSpeed = 90f;

    [Header("Pickup Feedback")]
    public AudioClip pickupSound;
    [Range(0f, 1f)] public float pickupVolume = 1f;

    [Tooltip("Optional effect (e.g. particle burst) spawned where the coin was collected.")]
    public GameObject pickupEffect;

    [Tooltip("Seconds before the spawned pickup effect is destroyed.")]
    [Min(0f)] public float pickupEffectLifetime = 2f;

    Vector3 startLocalPosition;
    Quaternion startLocalRotation;
    float phase;

    public bool IsCollected { get; private set; }

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        startLocalPosition = transform.localPosition;
        startLocalRotation = transform.localRotation;
        phase = randomizePhase ? Random.value * Mathf.PI * 2f : 0f;
        CoinCounter.Register(this);
    }

    void OnDestroy()
    {
        CoinCounter.Unregister(this);
    }

    void Update()
    {
        float t = Time.time;
        float bob = Mathf.Sin(t * bobSpeed * Mathf.PI * 2f + phase) * bobHeight;
        transform.localPosition = startLocalPosition + Vector3.up * bob;
        transform.localRotation = Quaternion.AngleAxis(t * spinSpeed, Vector3.up) * startLocalRotation;
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsCollected || other.GetComponentInParent<MarbleBall>() == null)
            return;

        IsCollected = true;

        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, transform.position, pickupVolume);

        if (pickupEffect != null)
            Destroy(Instantiate(pickupEffect, transform.position, transform.rotation), pickupEffectLifetime);

        CoinCounter.NotifyCollected(this);
        gameObject.SetActive(false);
    }
}
