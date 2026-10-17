// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the meat grinder feel good, and does the prison after a loss feel like a reward?
// Date: 2026-10-17

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batch build: Unity.exe -batchmode -quit -projectPath . -executeMethod ProtoBuild.BuildWin64
public static class ProtoBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Prototype/Build Win64")]
    public static void BuildWin64()
    {
        if (!System.IO.File.Exists(ScenePath))
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            camGo.transform.position = new Vector3(0, 0, -10);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.productName = "Inquisitor Prototype";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/Win64/InquisitorPrototype.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Debug.Log("PROTO_BUILD_RESULT: " + report.summary.result + " | size=" + report.summary.totalSize + " | errors=" + report.summary.totalErrors);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
