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
        }
    }
}
