using UnityEditor;
using UnityEditor.Build.Reporting;

/// <summary>
/// 批处理构建入口。
/// 用法：Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod BuildScript.BuildWindows -quit
/// 注意先 export PROGRAMDATA/ALLUSERSPROFILE（本机 shell 缺这两个变量会导致 UPM 崩溃）。
/// </summary>
public static class BuildScript
{
    public static void BuildWindows()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = "Builds/Windows/CoffeeTime.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
