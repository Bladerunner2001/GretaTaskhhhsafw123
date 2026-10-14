using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static, procedurally generated concave ribbon. A concave cross-section is swept along a Catmull-Rom
/// spline through the control points. The mesh is regenerated on enable, so it is never stored in the scene.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class RibbonTrack : MonoBehaviour
{
    [Serializable]
    public struct ControlPoint
    {
        public Vector3 position;
        [Tooltip("Roll around the path direction. Negative banks into right-hand turns.")]
        [Range(-180f, 180f)] public float bankDegrees;

        public ControlPoint(float x, float y, float z, float bank = 0f)
        {
            position = new Vector3(x, y, z);
            bankDegrees = bank;
        }
    }

    public enum FrameMode
    {
        [Tooltip("Ribbon stays upright relative to world up. Best for regular tracks.")]
        WorldUp,
        [Tooltip("Twist-minimising frames. Use for loops, corkscrews and vertical sections.")]
        ParallelTransport,
    }

    [Header("Path (local space)")]
    public List<ControlPoint> controlPoints = DefaultPath();
    public bool closedLoop;
    public FrameMode frameMode = FrameMode.WorldUp;
    [Range(2, 64)] public int samplesPerSegment = 24;

    [Header("Cross Section")]
    [Min(0.5f)] public float width = 7f;
    [Tooltip("Height of the side walls above the centre line.")]
    [Min(0f)] public float depth = 1.6f;
    [Tooltip("1 = V shape, 2 = parabola, higher = flatter floor with steeper walls.")]
    [Range(1f, 8f)] public float wallSharpness = 2.5f;
    [Range(4, 96)] public int crossSegments = 32;

    [Header("Checkerboard")]
    [Tooltip("Number of checker squares across the ribbon. Squares along the path are sized to match.")]
    [Min(1)] public int tilesAcross = 8;

    readonly List<Vector3> samplePositions = new();
    readonly List<Vector3> sampleForwards = new();
    readonly List<Vector3> sampleUps = new();
    readonly List<float> sampleBanks = new();
    readonly List<float> sampleDistances = new();

    Mesh mesh;
    PhysicsMaterial surface;

    public float Length { get; private set; }

    void OnEnable()
    {
        Rebuild();
    }

    void OnDestroy()
    {
        DestroyOwned(mesh);
        DestroyOwned(surface);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled)
                Rebuild();
        };
    }
#endif

    public void Rebuild()
    {
        if (controlPoints == null || controlPoints.Count < 2)
            return;

        SamplePath();
        BuildFrames();
        BuildMesh();
    }

    /// <summary>World-space position and orientation (forward = along path, up = track normal) at a normalised distance.</summary>
    public void GetFrame(float t01, out Vector3 position, out Quaternion rotation)
    {
        if (samplePositions.Count == 0)
            Rebuild();

        float distance = Mathf.Clamp01(t01) * Length;
        int count = samplePositions.Count;
        int i = 0;
        while (i < count - 2 && sampleDistances[i + 1] < distance)
            i++;

        int next = Mathf.Min(i + 1, count - 1);
        float span = sampleDistances[next] - sampleDistances[i];
        float f = span > 1e-5f ? Mathf.Clamp01((distance - sampleDistances[i]) / span) : 0f;

        Vector3 localPos = Vector3.Lerp(samplePositions[i], samplePositions[next], f);
        Vector3 localFwd = Vector3.Slerp(sampleForwards[i], sampleForwards[next], f);
        Vector3 localUp = Vector3.Slerp(sampleUps[i], sampleUps[next], f);

        position = transform.TransformPoint(localPos);
        rotation = Quaternion.LookRotation(transform.TransformDirection(localFwd), transform.TransformDirection(localUp));
    }

    void SamplePath()
    {
        samplePositions.Clear();
        sampleForwards.Clear();
        sampleBanks.Clear();
        sampleDistances.Clear();

        int segmentCount = closedLoop ? controlPoints.Count : controlPoints.Count - 1;
        for (int s = 0; s < segmentCount; s++)
        {
            Vector3 p0 = PointAt(s - 1), p1 = PointAt(s), p2 = PointAt(s + 1), p3 = PointAt(s + 2);
            float bankStart = BankAt(s), bankEnd = BankAt(s + 1);

            bool includeEnd = !closedLoop && s == segmentCount - 1;
            int steps = includeEnd ? samplesPerSegment + 1 : samplesPerSegment;
            for (int k = 0; k < steps; k++)
            {
                float t = k / (float)samplesPerSegment;
                samplePositions.Add(CatmullRom(p0, p1, p2, p3, t));

                Vector3 tangent = CatmullRomTangent(p0, p1, p2, p3, t);
                sampleForwards.Add(tangent.sqrMagnitude > 1e-8f ? tangent.normalized : (p2 - p1).normalized);
                sampleBanks.Add(Mathf.Lerp(bankStart, bankEnd, Mathf.SmoothStep(0f, 1f, t)));
            }
        }

        float total = 0f;
        sampleDistances.Add(0f);
        for (int i = 1; i < samplePositions.Count; i++)
        {
            total += Vector3.Distance(samplePositions[i - 1], samplePositions[i]);
            sampleDistances.Add(total);
        }

        if (closedLoop)
            total += Vector3.Distance(samplePositions[^1], samplePositions[0]);

        Length = total;
    }

    void BuildFrames()
    {
        sampleUps.Clear();
        int count = samplePositions.Count;

        if (frameMode == FrameMode.WorldUp)
        {
            for (int i = 0; i < count; i++)
                sampleUps.Add(OrthogonalUp(Vector3.up, sampleForwards[i]));
        }
        else
        {
            Vector3 up = OrthogonalUp(Vector3.up, sampleForwards[0]);
            sampleUps.Add(up);
            for (int i = 1; i < count; i++)
            {
                up = Quaternion.FromToRotation(sampleForwards[i - 1], sampleForwards[i]) * up;
                up = OrthogonalUp(up, sampleForwards[i]);
                sampleUps.Add(up);
            }

            if (closedLoop)
            {
                // Spread the accumulated twist evenly so the ribbon meets itself at the seam.
                Vector3 wrappedUp = Quaternion.FromToRotation(sampleForwards[^1], sampleForwards[0]) * sampleUps[^1];
                float twist = Vector3.SignedAngle(wrappedUp, sampleUps[0], sampleForwards[0]);
                for (int i = 0; i < count; i++)
                    sampleUps[i] = Quaternion.AngleAxis(twist * i / count, sampleForwards[i]) * sampleUps[i];
            }
        }

        for (int i = 0; i < count; i++)
            sampleUps[i] = Quaternion.AngleAxis(sampleBanks[i], sampleForwards[i]) * sampleUps[i];
    }

    void BuildMesh()
    {
        int sampleCount = samplePositions.Count;
        int rows = closedLoop ? sampleCount + 1 : sampleCount;
        int cols = crossSegments + 1;

        var profile = new Vector2[cols];
        var profileArc = new float[cols];
        for (int j = 0; j < cols; j++)
        {
            float s = Mathf.Lerp(-1f, 1f, j / (float)crossSegments);
            profile[j] = new Vector2(s * width * 0.5f, depth * Mathf.Pow(Mathf.Abs(s), wallSharpness));
            if (j > 0)
                profileArc[j] = profileArc[j - 1] + Vector2.Distance(profile[j - 1], profile[j]);
        }

        float tileSize = profileArc[cols - 1] / tilesAcross;
        int tilesAlong = Mathf.Max(1, Mathf.RoundToInt(Length / tileSize));
        float tileAlong = Length / tilesAlong;

        var vertices = new List<Vector3>(rows * cols);
        var uvs = new List<Vector2>(rows * cols);
        for (int i = 0; i < rows; i++)
        {
            int index = i % sampleCount;
            Vector3 center = samplePositions[index];
            Vector3 up = sampleUps[index];
            Vector3 right = Vector3.Cross(up, sampleForwards[index]).normalized;
            float distance = i == sampleCount ? Length : sampleDistances[index];

            for (int j = 0; j < cols; j++)
            {
                vertices.Add(center + right * profile[j].x + up * profile[j].y);
                uvs.Add(new Vector2(profileArc[j] / tileSize, distance / tileAlong));
            }
        }

        var triangles = new List<int>((rows - 1) * crossSegments * 6);
        for (int i = 0; i < rows - 1; i++)
        {
            for (int j = 0; j < crossSegments; j++)
            {
                int a = i * cols + j;
                int b = a + 1;
                int c = a + cols;
                int d = c + 1;
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
                triangles.Add(a); triangles.Add(d); triangles.Add(b);
            }
        }

        if (mesh == null)
        {
            mesh = new Mesh
            {
                name = "RibbonTrack (generated)",
                hideFlags = HideFlags.DontSave,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        var meshCollider = GetComponent<MeshCollider>();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
        meshCollider.sharedMaterial = GetSurface();
    }

    // Neutral surface: friction 1 / bounce 0 with low-priority Average combine,
    // so the ball's Multiply/Maximum combine makes MarbleBall's constants the effective values.
    PhysicsMaterial GetSurface()
    {
        if (surface == null)
        {
            surface = new PhysicsMaterial("RibbonTrack Surface (generated)")
            {
                hideFlags = HideFlags.DontSave,
                dynamicFriction = 1f,
                staticFriction = 1f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Average,
            };
        }
        return surface;
    }

    Vector3 PointAt(int i)
    {
        int n = controlPoints.Count;
        if (closedLoop)
            return controlPoints[((i % n) + n) % n].position;
        if (i < 0)
            return 2f * controlPoints[0].position - controlPoints[1].position;
        if (i >= n)
            return 2f * controlPoints[n - 1].position - controlPoints[n - 2].position;
        return controlPoints[i].position;
    }

    float BankAt(int i)
    {
        int n = controlPoints.Count;
        return closedLoop ? controlPoints[((i % n) + n) % n].bankDegrees : controlPoints[Mathf.Clamp(i, 0, n - 1)].bankDegrees;
    }

    static Vector3 OrthogonalUp(Vector3 up, Vector3 forward)
    {
        Vector3 projected = Vector3.ProjectOnPlane(up, forward);
        if (projected.sqrMagnitude < 1e-6f)
            projected = Vector3.ProjectOnPlane(Vector3.back, forward);
        return projected.normalized;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        return 0.5f * ((p2 - p0) + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t + 3f * (3f * p1 - p0 - 3f * p2 + p3) * t2);
    }

    static void DestroyOwned(UnityEngine.Object obj)
    {
        if (obj == null)
            return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    static List<ControlPoint> DefaultPath() => new()
    {
        new ControlPoint(0f, 0f, 0f),
        new ControlPoint(0f, -0.5f, 14f),
        new ControlPoint(6f, -2f, 26f, -15f),
        new ControlPoint(18f, -4f, 32f, -20f),
        new ControlPoint(30f, -6f, 28f, -15f),
        new ControlPoint(36f, -8f, 16f, -5f),
        new ControlPoint(32f, -10f, 4f, 15f),
        new ControlPoint(22f, -12f, -2f, 25f),
        new ControlPoint(12f, -15f, 4f, 15f),
        new ControlPoint(8f, -18f, 16f, 5f),
        new ControlPoint(10f, -21f, 30f),
        new ControlPoint(10f, -22f, 42f),
    };

    void OnDrawGizmosSelected()
    {
        if (controlPoints == null)
            return;

        Gizmos.color = Color.magenta;
        for (int i = 0; i < controlPoints.Count; i++)
        {
            Vector3 p = transform.TransformPoint(controlPoints[i].position);
            Gizmos.DrawWireSphere(p, 0.4f);
            if (i > 0)
                Gizmos.DrawLine(transform.TransformPoint(controlPoints[i - 1].position), p);
        }
    }
}
