using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RibbonTrack))]
public class RibbonTrackEditor : Editor
{
    static readonly Color PointColor = new(1f, 0.25f, 0.85f);
    static readonly Color SelectedColor = new(1f, 0.85f, 0.1f);

    int selectedIndex = -1;

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Scene view: click a control point to select it, then drag the move handle. Esc deselects.", MessageType.Info);
        DrawDefaultInspector();
    }

    void OnSceneGUI()
    {
        var track = (RibbonTrack)target;
        if (track.controlPoints == null)
            return;

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && selectedIndex >= 0)
        {
            selectedIndex = -1;
            e.Use();
        }

        Transform t = track.transform;
        Quaternion handleRotation = Tools.pivotRotation == PivotRotation.Local ? t.rotation : Quaternion.identity;

        for (int i = 0; i < track.controlPoints.Count; i++)
        {
            Vector3 world = t.TransformPoint(track.controlPoints[i].position);
            float size = HandleUtility.GetHandleSize(world);

            Handles.color = i == selectedIndex ? SelectedColor : PointColor;
            if (Handles.Button(world, Quaternion.identity, size * 0.08f, size * 0.12f, Handles.SphereHandleCap))
            {
                selectedIndex = i;
                Repaint();
            }

            Handles.Label(world + Vector3.up * size * 0.2f, i.ToString(), EditorStyles.boldLabel);

            if (i != selectedIndex)
                continue;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, handleRotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(track, "Move Track Control Point");
                var point = track.controlPoints[i];
                point.position = t.InverseTransformPoint(moved);
                track.controlPoints[i] = point;
                track.Rebuild();
                EditorUtility.SetDirty(track);
            }

            DrawBankHandle(track, i, world, size);
        }
    }

    // Disc keeps its own drag-start rotation, so it must be fed the current bank, not a per-frame delta.
    static void DrawBankHandle(RibbonTrack track, int index, Vector3 world, float size)
    {
        Vector3 axis = track.transform.TransformDirection(TangentAt(track, index));
        if (axis.sqrMagnitude < 1e-8f)
            return;
        axis.Normalize();

        Vector3 reference = Vector3.ProjectOnPlane(Vector3.up, axis);
        if (reference.sqrMagnitude < 1e-6f)
            reference = Vector3.ProjectOnPlane(Vector3.forward, axis);
        reference.Normalize();

        float bank = track.controlPoints[index].bankDegrees;
        Quaternion current = Quaternion.AngleAxis(bank, axis);
        float radius = size * 1.4f;

        Handles.color = new Color(0.2f, 0.85f, 1f);
        Handles.DrawLine(world, world + current * reference * radius, 2f);

        EditorGUI.BeginChangeCheck();
        Quaternion rotated = Handles.Disc(current, world, axis, radius, false, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(track, "Bank Track Control Point");
            var point = track.controlPoints[index];
            point.bankDegrees = Vector3.SignedAngle(reference, rotated * reference, axis);
            track.controlPoints[index] = point;
            track.Rebuild();
            EditorUtility.SetDirty(track);
        }

        Handles.Label(world + current * reference * radius, $"{bank:0}°", EditorStyles.miniBoldLabel);
    }

    static Vector3 TangentAt(RibbonTrack track, int i)
    {
        var points = track.controlPoints;
        int n = points.Count;
        if (n < 2)
            return Vector3.forward;

        if (track.closedLoop)
            return points[(i + 1) % n].position - points[(i - 1 + n) % n].position;

        Vector3 next = points[Mathf.Min(i + 1, n - 1)].position;
        Vector3 previous = points[Mathf.Max(i - 1, 0)].position;
        return next - previous;
    }
}
