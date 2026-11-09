using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DNF60Prototype;

public static class SceneGenerator
{
    [MenuItem("DNF60/Generate Main Scene")]
    public static void Generate()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5.5f;
        camera.backgroundColor = new Color(0.07f, 0.10f, 0.16f);
        var bootstrap = new GameObject("GameBootstrap");
        bootstrap.AddComponent<GameBootstrap>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("DNF60 Main scene generated successfully.");
    }
}
