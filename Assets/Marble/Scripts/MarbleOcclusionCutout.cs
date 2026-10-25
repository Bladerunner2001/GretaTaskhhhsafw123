using UnityEngine;

/// <summary>
/// Feeds the ball position to the Checkerboard shader, which dithers away any geometry between the camera
/// and the ball inside a screen-space circle. The camera never has to move to keep the ball visible.
/// </summary>
public class MarbleOcclusionCutout : MonoBehaviour
{
    [Tooltip("Ball to keep visible. Taken from MarbleFollowCamera.target on this object when empty.")]
    public Transform ball;

    [Tooltip("Hole radius in metres, measured at the ball's distance (the ball itself has radius 0.5).")]
    [Min(0f)] public float radius = 1.5f;

    [Tooltip("Fraction of the radius used for the soft dithered edge.")]
    [Range(0f, 1f)] public float edgeSoftness = 0.4f;

    [Tooltip("Geometry closer than this to the ball (along the view line) is never cut, so the floor under the ball stays.")]
    [Min(0f)] public float depthMargin = 1.5f;

    [Tooltip("Distance over which the cut fades in in front of Depth Margin.")]
    [Min(0.01f)] public float depthFade = 1f;

    static readonly int PositionId = Shader.PropertyToID("_MarbleCutoutPosition");
    static readonly int ParamsId = Shader.PropertyToID("_MarbleCutoutParams");

    void Awake()
    {
        if (ball == null && TryGetComponent(out MarbleFollowCamera follow) && follow.target != null)
            ball = follow.target.transform;
    }

    void LateUpdate()
    {
        if (ball == null)
        {
            Shader.SetGlobalVector(PositionId, Vector4.zero);
            return;
        }

        Vector3 p = ball.position;
        Shader.SetGlobalVector(PositionId, new Vector4(p.x, p.y, p.z, 1f));
        Shader.SetGlobalVector(ParamsId, new Vector4(radius, edgeSoftness, depthMargin, depthFade));
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(PositionId, Vector4.zero);
    }
}
