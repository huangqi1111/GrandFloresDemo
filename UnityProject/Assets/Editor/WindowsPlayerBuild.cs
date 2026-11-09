using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WindowsPlayerBuild
{
    const string MainScene = "Assets/Scenes/Main.unity";
    const string ProductName = "横版动作游戏Demo";

    [MenuItem("DNF Demo/Build Windows Player")]
    public static void BuildFromMenu()
    {
        Build(ResolveOutputPath());
    }

    public static void RunBatch()
    {
        try
        {
            Build(ResolveOutputPath());
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void Build(string executablePath)
    {
        if (!File.Exists(MainScene))
            throw new FileNotFoundException("Startup scene is missing.", MainScene);

        var scene = new EditorBuildSettingsScene(MainScene, true);
        EditorBuildSettings.scenes = new[] { scene };
        PlayerSettings.companyName = "HuangQi";
        PlayerSettings.productName = ProductName;
        PlayerSettings.bundleVersion = "0.1.0";

        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("Invalid Windows build output path.");
        Directory.CreateDirectory(directory);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { MainScene },
            locationPathName = executablePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.CleanBuildCache
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException(
                $"Windows build failed: {report.summary.result}, errors={report.summary.totalErrors}");

        Debug.Log($"WINDOWS_PLAYER_BUILD_PASS: {executablePath}, size={report.summary.totalSize}");
    }

    static string ResolveOutputPath()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            throw new InvalidOperationException("Could not resolve project directory.");

        var workspaceRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "..", "2026-09-29", "new-chat"));
        return Path.Combine(workspaceRoot, "output", "game-build", "GrandFloresDemo.exe");
    }
}
