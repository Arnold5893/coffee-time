using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 批处理截图入口：打开 Main 场景并进 Play 模式，由场景里的 ScreenshotRig 完成截图后自行退出。
/// 用法：Unity.exe -batchmode -executeMethod PlayCapture.Run（不要加 -quit 和 -nographics）
/// </summary>
public static class PlayCapture
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
