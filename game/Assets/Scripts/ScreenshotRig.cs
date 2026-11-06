using System.Collections;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 批处理截图装置：挂在场景里，Play 模式下把相机渲染进 RenderTexture 再读回存盘。
/// （URP 不支持 Camera.Render() 立即渲染，批处理截图必须走正常播放循环。
///   不用 WaitForEndOfFrame：渲染循环异常时它会永久阻塞；纯帧数等待 + 看门狗保证必然退出。）
/// </summary>
public class ScreenshotRig : MonoBehaviour
{
    public Vector3 camPosBar;
    public Vector3 camLookBar;
    public Vector3 camPosAll;
    public Vector3 camLookAll;
    public Vector3 camPosEq;
    public Vector3 camLookEq;
    public Vector3 camPosStool;
    public Vector3 camLookStool;
    public Vector3 camPosChair;
    public Vector3 camLookChair;

    // 02 票：玩家与跟随镜头
    public CoffeeShop.Player.PlayerMotor player;
    public CoffeeShop.Cameras.ThirdPersonCameraRig followRig;
    public CoffeeShop.Cameras.CameraOcclusionFader fader;
    public Transform spawnPoint;

    IEnumerator Start()
    {
        var cam = Camera.main;
        cam.allowHDR = true;
        // 静态机位截图期间关掉跟随镜头，免得 LateUpdate 把相机挪走
        if (followRig != null) followRig.enabled = false;
        // HDR 缓冲：URP 只在 HDR 目标上跑后处理（ACES），LDR RT 会被整个跳过
        var rt = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGBHalf);
        cam.targetTexture = rt;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var camData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        Debug.Log($"[rig] renderPostProcessing={camData?.renderPostProcessing} volumeMask={camData?.volumeLayerMask.value}");
        Debug.Log($"[rig] pipeline={(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? "null" : UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name)}");
        var vol = FindObjectOfType<UnityEngine.Rendering.Volume>();
        Debug.Log($"[rig] volume found={vol != null} isGlobal={vol?.isGlobal} profile={vol?.sharedProfile != null}");
#endif

        // 等若干帧让 URP 与纹理流送就绪
        for (int i = 0; i < 15; i++) yield return null;

        yield return Capture(cam, rt, camPosBar, camLookBar, "setup_screenshot_bar.png");
        yield return Capture(cam, rt, camPosEq, camLookEq, "setup_screenshot_equipment.png");
        yield return Capture(cam, rt, camPosStool, camLookStool, "setup_screenshot_stool.png");
        yield return Capture(cam, rt, camPosChair, camLookChair, "setup_screenshot_armchair.png");

        // ---- 02 票验收：第三人称跟随镜头 + 遮挡淡化 ----
        if (followRig != null && player != null)
        {
            // 跟随镜头构图：站回出生点，镜头按默认俯角跟住
            player.Teleport(spawnPoint != null ? spawnPoint.position : player.transform.position,
                            spawnPoint != null ? spawnPoint.rotation : player.transform.rotation);
            followRig.enabled = true;
            followRig.RestoreDefaultView();
            for (int i = 0; i < 10; i++) yield return null;
            yield return CaptureCurrent(cam, rt, "setup_screenshot_playerview.png");

            // 遮挡淡化：角色放双面隔断书架一侧、镜头放另一侧，让书架正好挡在中间
            Transform shelf = null;
            foreach (var r in FindObjectsOfType<Renderer>())
                if (r.name == "Divider_Bookshelf") { shelf = r.transform; break; }
            if (shelf != null)
            {
                Bounds b = shelf.GetComponent<Renderer>().bounds;
                Vector3 c = b.center;
                bool thinX = b.extents.x < b.extents.z;
                Vector3 thin = thinX ? Vector3.right : Vector3.forward;
                float halfThick = thinX ? b.extents.x : b.extents.z;

                followRig.enabled = false;                 // 手动机位，别让跟随覆盖
                var pp = c - thin * (halfThick + 1.35f);
                pp.y = 0.05f;
                player.Teleport(pp, Quaternion.LookRotation(c - pp, Vector3.up));
                cam.transform.position = c + thin * (halfThick + 1.10f) + Vector3.up * 1.55f;
                cam.transform.LookAt(pp + Vector3.up * 1.10f);
                for (int i = 0; i < 10; i++) yield return null;
                yield return CaptureCurrent(cam, rt, "setup_screenshot_occlusion.png");
                followRig.enabled = true;
            }
        }

        // 全景前临时隐藏吊顶（完全位于 cutY 之上的渲染器），拍完恢复
        const float cutY = 2.7f;
        var hidden = new System.Collections.Generic.List<Renderer>();
        foreach (var r in FindObjectsOfType<Renderer>())
        {
            if (r.bounds.min.y > cutY) { r.enabled = false; hidden.Add(r); }
        }
        Debug.Log($"ceiling hidden for overview: {hidden.Count} renderers");
        for (int i = 0; i < 3; i++) yield return null;
        yield return Capture(cam, rt, camPosAll, camLookAll, "setup_screenshot_overview.png");
        foreach (var r in hidden) r.enabled = true;

        Quit(0);
    }

    IEnumerator Capture(Camera cam, RenderTexture rt, Vector3 pos, Vector3 look, string file)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        for (int i = 0; i < 6; i++) yield return null;   // 渲染几帧后再读回
        yield return Readback(rt, file);
    }

    /// <summary>不动相机，只把当前画面读回（供跟随镜头/遮挡淡化验收用）。</summary>
    IEnumerator CaptureCurrent(Camera cam, RenderTexture rt, string file)
    {
        yield return Readback(rt, file);
    }

    IEnumerator Readback(RenderTexture rt, string file)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        File.WriteAllBytes(file, tex.EncodeToPNG());
        Debug.Log("screenshot saved: " + file);
        yield return null;
    }

    // 看门狗：600 帧（约 10 秒渲染循环）内未完成也强制退出，避免批处理挂死
    void Update()
    {
        if (Time.frameCount > 600)
        {
            Debug.LogError("ScreenshotRig watchdog: timeout, force exit");
            Quit(3);
        }
    }

    static void Quit(int code)
    {
#if UNITY_EDITOR
        EditorApplication.Exit(code);
#else
        Application.Quit();
#endif
    }
}
