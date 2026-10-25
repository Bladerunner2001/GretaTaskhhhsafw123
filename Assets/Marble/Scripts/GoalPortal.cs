using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Level goal. When a MarbleBall enters the trigger, the ball loses control, spirals into the portal centre
/// while shrinking, the portal flares up, and the destination scene is loaded. The portal faces along its
/// local +Z axis (the direction the ball travels through it).
/// </summary>
[RequireComponent(typeof(Collider))]
public class GoalPortal : MonoBehaviour
{
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    [Header("Destination")]
    [Tooltip("Scene to load. It must be in the Build Profiles scene list.")]
    public string sceneToLoad = "PlaceholderNextLevel";

    [Header("Pull In")]
    [Tooltip("Seconds from touching the portal until the ball has vanished into it.")]
    [Min(0.05f)] public float pullDuration = 1.2f;

    [Tooltip("Turns the ball makes around the portal axis while being pulled in.")]
    public float spiralTurns = 1.5f;

    [Tooltip("How far behind the portal surface the ball disappears, in portal-local units.")]
    public float pullDepth = 0.8f;

    [Tooltip("Pause after the ball has vanished before the next scene loads.")]
    [Min(0f)] public float delayBeforeLoad = 0.4f;

    [Header("Feedback")]
    [Tooltip("Renderer using the Marble/StarPortal shader. Its intensity ramps up during the pull.")]
    public Renderer portalRenderer;

    [Min(0f)] public float flareIntensity = 3f;

    public AudioClip enterSound;
    [Range(0f, 1f)] public float enterVolume = 1f;

    MaterialPropertyBlock block;
    float baseIntensity = 1f;

    public bool IsTriggered { get; private set; }

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        block = new MaterialPropertyBlock();
        if (portalRenderer != null && portalRenderer.sharedMaterial != null && portalRenderer.sharedMaterial.HasProperty(IntensityId))
            baseIntensity = portalRenderer.sharedMaterial.GetFloat(IntensityId);
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsTriggered)
            return;

        var ball = other.GetComponentInParent<MarbleBall>();
        if (ball == null)
            return;

        if (!Application.CanStreamedLevelBeLoaded(sceneToLoad))
        {
            Debug.LogError($"GoalPortal: scene '{sceneToLoad}' is not in the Build Profiles scene list.", this);
            return;
        }

        StartCoroutine(PullIn(ball));
    }

    IEnumerator PullIn(MarbleBall ball)
    {
        IsTriggered = true;

        if (enterSound != null)
            AudioSource.PlayClipAtPoint(enterSound, transform.position, enterVolume);

        if (ball.TryGetComponent(out MarbleTiltController tilt))
            tilt.enabled = false;

        Rigidbody body = ball.Body;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;

        // Shrinking a collider to zero scale makes PhysX complain, and the ball shouldn't touch anything now anyway.
        foreach (var ballCollider in ball.GetComponentsInChildren<Collider>())
            ballCollider.enabled = false;

        Transform ballTransform = ball.transform;
        Vector3 startScale = ballTransform.localScale;
        Vector3 center = transform.position;
        Vector3 axis = transform.forward;
        Vector3 offset = ballTransform.position - center;
        float startDepth = Vector3.Dot(offset, axis);
        Vector3 radial = offset - axis * startDepth;
        float endDepth = pullDepth * transform.lossyScale.z;

        for (float elapsed = 0f; elapsed < pullDuration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / pullDuration);
            Vector3 swirl = Quaternion.AngleAxis(spiralTurns * 360f * t, axis) * radial * (1f - t);
            ballTransform.position = center + swirl + axis * Mathf.Lerp(startDepth, endDepth, t);
            ballTransform.localScale = startScale * (1f - t);
            ballTransform.Rotate(axis, 720f * Time.deltaTime, Space.World);
            SetIntensity(Mathf.Lerp(baseIntensity, flareIntensity, t));
            yield return null;
        }

        ballTransform.localScale = Vector3.zero;
        SetIntensity(flareIntensity);

        yield return new WaitForSeconds(delayBeforeLoad);
        SceneManager.LoadScene(sceneToLoad);
    }

    void SetIntensity(float value)
    {
        if (portalRenderer == null)
            return;

        portalRenderer.GetPropertyBlock(block);
        block.SetFloat(IntensityId, value);
        portalRenderer.SetPropertyBlock(block);
    }
}
