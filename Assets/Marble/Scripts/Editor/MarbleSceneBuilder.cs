using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class MarbleSceneBuilder
{
    const string Root = "Assets/Marble";
    const string MaterialsFolder = Root + "/Materials";
    const string ScenePath = "Assets/Scenes/MarbleTrack.unity";
    const string TrackMaterialPath = MaterialsFolder + "/M_TrackChecker.mat";
    const string BallMaterialPath = MaterialsFolder + "/M_BallChecker.mat";
    const string CheckerShaderName = "Marble/Checkerboard";

    const float BallRadius = 0.5f;

    [MenuItem("Tools/Marble/Build Marble Track Scene")]
    public static void Build()
    {
        Shader checker = Shader.Find(CheckerShaderName);
        if (checker == null)
        {
            Debug.LogError($"Shader '{CheckerShaderName}' not found. Let Unity finish importing Assets/Marble/Shaders first.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Material trackMaterial = GetOrCreateCheckerMaterial(TrackMaterialPath, checker,
            Color.white, new Color(0.02f, 0.02f, 0.02f), new Vector2(1f, 1f), CullMode.Off, smoothness: 0.3f, specular: 0.1f);
        Material ballMaterial = GetOrCreateCheckerMaterial(BallMaterialPath, checker,
            new Color(0.95f, 0.18f, 0.25f), Color.white, new Vector2(8f, 4f), CullMode.Back, smoothness: 0.8f, specular: 0.5f);

        SetupLighting();

        var track = CreateTrack(trackMaterial);
        track.GetFrame(0.01f, out Vector3 startPos, out Quaternion startRot);
        Vector3 spawn = startPos + startRot * Vector3.up * (BallRadius + 0.25f);

        var ball = CreateBall(ballMaterial, spawn, track);
        CreateCamera(ball, spawn, startRot * Vector3.forward);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"Marble track scene saved to {ScenePath}. Press Play: WASD / arrows / left stick to tilt, R to respawn.");
    }

    static void SetupLighting()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.64f, 0.78f);
        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.45f, 0.55f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.2f, 0.26f);
        RenderSettings.fog = false;

        var lightObject = new GameObject("Directional Light");
        lightObject.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.intensity = 1.25f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.85f;
        RenderSettings.sun = light;
    }

    static RibbonTrack CreateTrack(Material material)
    {
        var trackObject = new GameObject("Ribbon Track");
        var track = trackObject.AddComponent<RibbonTrack>();
        var renderer = trackObject.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
        track.Rebuild();
        return track;
    }

    static Rigidbody CreateBall(Material material, Vector3 position, RibbonTrack track)
    {
        var ballObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballObject.name = "Marble";
        ballObject.transform.position = position;
        ballObject.transform.localScale = Vector3.one * (BallRadius * 2f);
        ballObject.GetComponent<MeshRenderer>().sharedMaterial = material;

        var body = ballObject.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var marble = ballObject.AddComponent<MarbleBall>();
        float lowestPoint = track.GetComponent<MeshRenderer>().bounds.min.y;
        marble.killHeight = lowestPoint - 25f;

        ballObject.AddComponent<MarbleTiltController>();
        return body;
    }

    static void CreateCamera(Rigidbody ball, Vector3 spawn, Vector3 trackForward)
    {
        Vector3 forward = Vector3.ProjectOnPlane(trackForward, Vector3.up).normalized;

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        cameraObject.transform.position = spawn - forward * 6f + Vector3.up * 2.2f;
        cameraObject.transform.rotation = Quaternion.LookRotation(spawn + forward * 1.5f - cameraObject.transform.position);

        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.78f, 0.76f, 0.92f);
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();

        var follow = cameraObject.AddComponent<MarbleFollowCamera>();
        var tilt = ball.GetComponent<MarbleTiltController>();
        follow.target = ball;
        follow.tiltController = tilt;
        tilt.followCamera = follow;

        cameraObject.AddComponent<MarbleOcclusionCutout>().ball = ball.transform;
    }

    static Material GetOrCreateCheckerMaterial(string path, Shader shader, Color a, Color b, Vector2 tiling,
        CullMode cull, float smoothness, float specular)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            EnsureAssetFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.SetColor("_ColorA", a);
        material.SetColor("_ColorB", b);
        material.SetVector("_Tiling", new Vector4(tiling.x, tiling.y, 0f, 0f));
        material.SetFloat("_Cull", (float)cull);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_SpecularStrength", specular);
        EditorUtility.SetDirty(material);
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
