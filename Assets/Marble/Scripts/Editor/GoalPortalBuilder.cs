using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class GoalPortalBuilder
{
    const string Root = "Assets/Marble";
    const string PortalMaterialPath = Root + "/Materials/M_StarPortal.mat";
    const string SparkleMaterialPath = Root + "/Materials/M_PortalSparkle.mat";
    const string DiscMeshPath = Root + "/Meshes/PortalDisc.asset";
    const string PlaceholderSceneName = "PlaceholderNextLevel";
    const string PlaceholderScenePath = "Assets/Scenes/" + PlaceholderSceneName + ".unity";
    const string PortalShaderName = "Marble/StarPortal";
    const string SparkleShaderName = "Marble/PortalSparkle";

    [MenuItem("Tools/Marble/Create Goal Portal")]
    public static void Create()
    {
        Shader portalShader = Shader.Find(PortalShaderName);
        Shader sparkleShader = Shader.Find(SparkleShaderName);
        if (portalShader == null || sparkleShader == null)
        {
            Debug.LogError("Portal shaders not found. Let Unity finish importing Assets/Marble/Shaders first.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(activeScene.path))
        {
            Debug.LogError("Save the current scene first so it can be added to the build scene list.");
            return;
        }

        Material portalMaterial = GetOrCreateMaterial(PortalMaterialPath, portalShader);
        Material sparkleMaterial = GetOrCreateMaterial(SparkleMaterialPath, sparkleShader);
        Mesh disc = GetOrCreateDiscMesh();

        EnsurePlaceholderScene(activeScene);
        AddToBuildScenes(activeScene.path, PlaceholderScenePath);

        GameObject portal = BuildPortal(portalMaterial, sparkleMaterial, disc);
        Undo.RegisterCreatedObjectUndo(portal, "Create Goal Portal");
        Selection.activeGameObject = portal;
        EditorSceneManager.MarkSceneDirty(activeScene);

        Debug.Log($"Goal portal created. It loads '{PlaceholderSceneName}'; change Scene To Load on the GoalPortal to point elsewhere.", portal);
    }

    static GameObject BuildPortal(Material portalMaterial, Material sparkleMaterial, Mesh disc)
    {
        var track = Object.FindAnyObjectByType<RibbonTrack>();
        float radius = track != null ? track.width * 0.38f : 2.5f;

        Vector3 position;
        Quaternion rotation;
        if (track != null)
        {
            track.GetFrame(0.985f, out Vector3 trackPoint, out rotation);
            position = trackPoint + rotation * Vector3.up * radius;
        }
        else
        {
            var view = SceneView.lastActiveSceneView;
            position = view != null ? view.pivot : Vector3.zero;
            rotation = Quaternion.identity;
        }

        var root = new GameObject("Goal Portal");
        root.transform.SetPositionAndRotation(position, rotation);
        root.transform.localScale = Vector3.one * radius;

        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(1.9f, 1.9f, 0.5f);

        var vortex = new GameObject("Vortex");
        vortex.transform.SetParent(root.transform, false);
        vortex.AddComponent<MeshFilter>().sharedMesh = disc;
        var vortexRenderer = vortex.AddComponent<MeshRenderer>();
        vortexRenderer.sharedMaterial = portalMaterial;
        vortexRenderer.shadowCastingMode = ShadowCastingMode.Off;
        vortexRenderer.receiveShadows = false;

        CreateStarSwirl(root.transform, sparkleMaterial);

        var lightObject = new GameObject("Glow Light");
        lightObject.transform.SetParent(root.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 0f, -0.3f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.7f, 0.35f, 1f);
        light.intensity = 4f;
        light.range = radius * 3f;
        light.shadows = LightShadows.None;

        var goal = root.AddComponent<GoalPortal>();
        goal.sceneToLoad = PlaceholderSceneName;
        goal.portalRenderer = vortexRenderer;
        return root;
    }

    static void CreateStarSwirl(Transform parent, Material material)
    {
        var swirlObject = new GameObject("Star Swirl");
        swirlObject.transform.SetParent(parent, false);
        var particles = swirlObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.5f, 1f), new Color(1f, 0.92f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 400;

        var emission = particles.emission;
        emission.rateOverTime = 80f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1.1f;
        shape.radiusThickness = 0.15f;

        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.4f);
        velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(2.5f);
        velocity.radial = new ParticleSystem.MinMaxCurve(-0.6f);

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        var color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = fade;

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        var renderer = swirlObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        particles.Play();
    }

    /// <summary>Unit disc in the XY plane, finely tessellated so the shader can bend it into a funnel.</summary>
    static Mesh GetOrCreateDiscMesh()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(DiscMeshPath);
        if (mesh != null)
            return mesh;

        const int rings = 24;
        const int segments = 96;

        var vertices = new List<Vector3> { Vector3.zero };
        var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
        for (int ring = 1; ring <= rings; ring++)
        {
            float r = ring / (float)rings;
            for (int s = 0; s < segments; s++)
            {
                float angle = s / (float)segments * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);
                vertices.Add(p);
                uvs.Add(new Vector2(p.x * 0.5f + 0.5f, p.y * 0.5f + 0.5f));
            }
        }

        int RingVertex(int ring, int s) => 1 + (ring - 1) * segments + s % segments;

        var triangles = new List<int>();
        for (int s = 0; s < segments; s++)
            triangles.AddRange(new[] { 0, RingVertex(1, s + 1), RingVertex(1, s) });

        for (int ring = 1; ring < rings; ring++)
        {
            for (int s = 0; s < segments; s++)
            {
                int a = RingVertex(ring, s), b = RingVertex(ring, s + 1);
                int c = RingVertex(ring + 1, s), d = RingVertex(ring + 1, s + 1);
                triangles.AddRange(new[] { a, b, c, b, d, c });
            }
        }

        mesh = new Mesh { name = "PortalDisc" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.SetNormals(Enumerable.Repeat(Vector3.back, vertices.Count).ToList());
        // The vertex shader pushes the centre up to 3 units along +Z (max Funnel Depth) and ripples the rim outward.
        mesh.bounds = new Bounds(new Vector3(0f, 0f, 1.5f), new Vector3(2.5f, 2.5f, 3.2f));

        EnsureAssetFolder(Path.GetDirectoryName(DiscMeshPath).Replace('\\', '/'));
        AssetDatabase.CreateAsset(mesh, DiscMeshPath);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    static void EnsurePlaceholderScene(Scene returnTo)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaceholderScenePath) != null)
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.03f, 0.16f);
        cameraObject.AddComponent<AudioListener>();
        SceneManager.MoveGameObjectToScene(cameraObject, scene);

        var textObject = new GameObject("Placeholder Text");
        var text = textObject.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        text.text = "Next level\ncoming soon";
        text.fontSize = 96;
        text.characterSize = 0.08f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.85f, 0.7f, 1f);
        textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        SceneManager.MoveGameObjectToScene(textObject, scene);

        EditorSceneManager.SaveScene(scene, PlaceholderScenePath);
        EditorSceneManager.CloseScene(scene, true);
        SceneManager.SetActiveScene(returnTo);
    }

    static void AddToBuildScenes(params string[] paths)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        foreach (string path in paths)
        {
            if (!scenes.Any(s => s.path == path))
                scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static Material GetOrCreateMaterial(string path, Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        EnsureAssetFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
