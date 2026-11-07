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
    public Transform interiorPoint;

    // 03 票：入口门
    public CoffeeShop.Interaction.EntranceDoor door;
    public Vector3 camPosEntry;
    public Vector3 camLookEntry;

    // 07 票 fix：后场操作台（咖啡机以东留空 + 旧木桌）
    public Vector3 camPosBack;
    public Vector3 camLookBack;

    // 07 票：吧台工位与制作界面
    public CoffeeShop.Bar.BarStation barStation;

    IEnumerator Start()
    {
        var cam = Camera.main;
        cam.allowHDR = true;
        // 批处理帧率可达上万，Time.deltaTime 小到 1e-4 会让门扇动画几十帧都转不动，
        // 这里固定成 60fps 步长，截图才拍得到"开到位"的门。
        Time.captureDeltaTime = 1f / 60f;
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
        yield return Capture(cam, rt, camPosBack, camLookBack, "setup_screenshot_backarea.png");
        yield return Capture(cam, rt, camPosStool, camLookStool, "setup_screenshot_stool.png");
        yield return Capture(cam, rt, camPosChair, camLookChair, "setup_screenshot_armchair.png");

        // ---- 02 票验收：第三人称跟随镜头 + 遮挡淡化 ----
        if (followRig != null && player != null)
        {
            // 跟随镜头构图：站到店内空位（出生点现在在门外，不能直接拿来拍店内构图）
            Transform viewAt = interiorPoint != null ? interiorPoint : spawnPoint;
            player.Teleport(viewAt != null ? viewAt.position : player.transform.position,
                            viewAt != null ? viewAt.rotation : player.transform.rotation);
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

        // ---- 03 票验收：门外出生点 / 开门 ----
        if (followRig != null && player != null && spawnPoint != null)
        {
            player.Teleport(spawnPoint.position, spawnPoint.rotation);
            followRig.enabled = true;
            followRig.RestoreDefaultView();
            if (door != null) door.ForceClosed();
            for (int i = 0; i < 12; i++) yield return null;
            yield return CaptureCurrent(cam, rt, "setup_screenshot_entrance.png");

            if (door != null)
            {
                door.Open();
                // 门扇 0.55s 开到位，按 60fps 步长等足 1.2 秒再拍
                for (int i = 0; i < 72; i++) yield return null;
                Debug.Log($"[rig] door state={door.Current} open={door.OpenAmount:F2} bells={door.BellCount}");
                yield return CaptureCurrent(cam, rt, "setup_screenshot_entrance_open.png");
                door.ForceClosed();
                for (int i = 0; i < 4; i++) yield return null;
            }
        }

        // ---- 04 票验收：靠近空座出提示 / 坐进吧台凳 ----
        var seatMgr = Object.FindFirstObjectByType<CoffeeShop.Seating.SeatingManager>();
        var seatAct = Object.FindFirstObjectByType<CoffeeShop.Seating.SeatInteraction>();
        if (seatMgr != null && seatAct != null && player != null && followRig != null)
        {
            var seat = seatMgr.Find("bar-2") ?? (seatMgr.Seats.Count > 0 ? seatMgr.Seats[0] : null);
            if (seat != null)
            {
                player.Teleport(seat.StandPoint, seat.Facing);
                // 座位特写用固定机位：跟随镜头站在人身后，在吧台凳这种狭窄位置会被挤到贴脸
                if (followRig != null) followRig.enabled = false;
                Vector3 seatFwd = seat.Facing * Vector3.forward;
                Vector3 seatLat = Vector3.Cross(Vector3.up, seatFwd).normalized;
                // 机位在座位斜后方：既看得到人坐进凳子里的样子，也看得到靠背
                // （靠背在人背后，机位站得越正越容易被人挡住）
                cam.transform.position = seat.SeatPoint - seatFwd * 1.30f + seatLat * 1.55f + Vector3.up * 1.38f;
                cam.transform.LookAt(seat.SeatPoint + Vector3.up * 0.80f);
                for (int i = 0; i < 12; i++) yield return null;
                yield return CaptureCurrent(cam, rt, "setup_screenshot_seat_prompt.png");

                if (seatAct.TrySit(seat))
                {
                    for (int i = 0; i < 45; i++) yield return null;   // 0.5s 坐下过渡
                    Debug.Log($"[rig] seated={seat.Id} blend={seatAct.Blend:F2} occupied={!seat.IsFree}");
                    yield return CaptureCurrent(cam, rt, "setup_screenshot_seated.png");
                    seatAct.TryStand();
                    for (int i = 0; i < 30; i++) yield return null;
                    Debug.Log($"[rig] stood up, occupied={!seat.IsFree} blend={seatAct.Blend:F2}");
                }
                else
                {
                    Debug.LogWarning($"[rig] trySit 失败: {seat.Id}");
                }
            }
        }

        // ---- 04 票补完：双人沙发一椅两座，只坐左半边，右半边仍空着 ----
        if (seatMgr != null && seatAct != null && player != null)
        {
            // 选多人桌 1 的沙发：它在店中央、灯下最亮，验收图才看得清"一椅两座"（沉浸区那片偏暗）
            var sofaSeat = seatMgr.Find("sofa-g1-a-1") ?? seatMgr.Find("sofa-i1-l-1");
            if (sofaSeat != null)
            {
                player.Teleport(sofaSeat.StandPoint, sofaSeat.Facing);
                if (followRig != null) followRig.enabled = false;
                Vector3 sf = sofaSeat.Facing * Vector3.forward;
                Vector3 sl = Vector3.Cross(Vector3.up, sf).normalized;
                // 机位从桌子另一侧斜看回来：同一张沙发"坐了一个、还空着一个"要同时入画
                cam.transform.position = sofaSeat.SeatPoint + sf * 2.05f + sl * 1.60f + Vector3.up * 1.45f;
                cam.transform.LookAt(sofaSeat.SeatPoint + Vector3.up * 0.72f);
                for (int i = 0; i < 12; i++) yield return null;

                if (seatAct.TrySit(sofaSeat))
                {
                    for (int i = 0; i < 45; i++) yield return null;
                    Debug.Log($"[rig] seated sofa={sofaSeat.Id} occupied={!sofaSeat.IsFree} blend={seatAct.Blend:F2}");
                    yield return CaptureCurrent(cam, rt, "setup_screenshot_seated_sofa.png");
                    seatAct.TryStand();
                    for (int i = 0; i < 30; i++) yield return null;
                }
                else Debug.LogWarning($"[rig] 沙发坐下失败: {sofaSeat.Id}");
            }
        }

        // ---- 07 票验收：吧台工位近景 + 手柄直接操作 ----
        if (barStation != null && barStation.panel != null && barStation.panel.view != null &&
            player != null && followRig != null)
        {
            var panel = barStation.panel;
            Vector3 workDir = barStation.workPoint - barStation.center;
            workDir.y = 0f;
            player.Teleport(barStation.GroundPoint,
                            Quaternion.LookRotation(workDir.sqrMagnitude > 1e-4f ? workDir.normalized : Vector3.forward,
                                                    Vector3.up));
            followRig.enabled = true;
            followRig.RestoreDefaultView();
            // 通道只有 1.2m 宽：镜头站在人正后方会顶进前柜、整屏都是后背。
            // 沿通道轴摆机位，才看得到"人站在工位上 + 吧台 + 咖啡机"。
            Vector3 aisleSide = new Vector3(-workDir.z, 0f, workDir.x).normalized;
            followRig.SetYawPitch(Mathf.Atan2(-aisleSide.x, -aisleSide.z) * Mathf.Rad2Deg, 30f);
            for (int i = 0; i < 10; i++) yield return null;
            yield return CaptureCurrent(cam, rt, "setup_screenshot_bar_prompt.png");

            bool entered = barStation.Enter();
            for (int i = 0; i < 60; i++) yield return null;     // 0.9s 近景过渡（60fps 下 54 帧就到位）
            var craft = panel.Session;
            Debug.Log($"[rig] craft enter={entered} active={barStation.Active} focus={followRig.FocusProgress:F2} " +
                      $"fov={followRig.CurrentFov:F1} dist={followRig.CurrentDistance:F2} " +
                      $"step={(craft != null ? craft.Describe() : "null")}");

            var view = panel.view;
            if (craft != null)
            {
                // 1) 把杯子拖到「台面」= 放错：判偏后只有「重做 / 继续」，没有全屏红闪
                yield return RigCarry(view, 1);
                Debug.Log($"[rig] deviation feedback={panel.LastFeedback} dev={craft.Deviations} " +
                          $"step={craft.StepId} mode={craft.Mode}");
                yield return CaptureCurrent(cam, rt, "setup_screenshot_bar_deviation.png");

                // 2) 重做 → 拖到萃取口 → 绕圈磨豆，转到一半拍近景（粉面正在涨）
                panel.RetryStep();
                yield return null;
                yield return RigCarry(view, 0);

                Vector2 c = view.ToScreen(view.BasketWorld);
                view.PointerDown(c + new Vector2(70f, 0f));
                for (int i = 0; i < 90 && craft.StepId == "grind"; i++)
                {
                    float a = i * 0.22f;
                    view.PointerMove(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 70f);
                    yield return null;
                }
                Debug.Log($"[rig] grind powder={view.PowderLevel:P0} {panel.StateDebug}");
                yield return CaptureCurrent(cam, rt, "setup_screenshot_bar_focus.png");

                // 3) 继续转完 → 歪着压到底：粉饼就是斜的（没有红屏、没有提示）
                for (int i = 90; i < 400 && craft.StepId == "grind"; i++)
                {
                    float a = i * 0.22f;
                    view.PointerMove(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 70f);
                    yield return null;
                }
                view.PointerUp();
                yield return null;

                Vector2 p = view.ToScreen(view.BasketWorld) + new Vector2(0f, 120f);
                view.PointerDown(p);
                for (int i = 0; i < 20; i++)
                {
                    p += new Vector2(6f, -10f);
                    view.PointerMove(p);
                    yield return null;
                }
                for (int i = 0; i < 32; i++) yield return null;
                view.PointerUp();
                // 多等几帧再拍：抬手后压头要退回座子、这一步的道具也要重新收放，
                // 只等一帧的话读回来还是"按着"的那张。
                for (int i = 0; i < 8; i++) yield return null;
                Debug.Log($"[rig] tamp tilt={view.TiltDeg:F1}° step={craft.StepId} {panel.StateDebug}");
                yield return CaptureCurrent(cam, rt, "setup_screenshot_bar_defect.png");
            }

            barStation.Exit();
            for (int i = 0; i < 45; i++) yield return null;      // 0.6s 退出过渡
            Debug.Log($"[rig] craft exit focus={followRig.FocusProgress:F2} fov={followRig.CurrentFov:F1} " +
                      $"dist={followRig.CurrentDistance:F2} motor={player.ControllerEnabled}");
            yield return CaptureCurrent(cam, rt, "setup_screenshot_bar_restored.png");
        }

        // ---- 门口落脚区：阳光区长桌在 12 号凳后收头，门前留出空地 ----
        if (followRig != null) followRig.enabled = false;
        yield return Capture(cam, rt, camPosEntry, camLookEntry, "setup_screenshot_entry_corner.png");
        // 全景前临时隐藏吊顶（完全位于 cutY 之上的渲染器），拍完恢复
        // 静态机位必须先把跟随镜头关掉，否则它会在 LateUpdate 里把相机又拖回角色身后
        if (followRig != null) followRig.enabled = false;
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

    /// <summary>截图脚本用：把杯子拖到某个热点。</summary>
    IEnumerator RigCarry(CoffeeShop.Bar.CraftView view, int spot)
    {
        view.PointerDown(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        yield return null;
        view.PointerMove(view.ToScreen(view.SpotWorld(spot)));
        yield return null;
        view.PointerMove(view.ToScreen(view.SpotWorld(spot)));
        yield return null;
        view.PointerUp();
        yield return null;
    }

    IEnumerator Capture(Camera cam, RenderTexture rt, Vector3 pos, Vector3 look, string file)
    {
        Debug.Log($"[rig] capture {file} from {pos} look {look}");
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

    // 看门狗：2400 帧内未完成也强制退出，避免批处理挂死。
    //     19 张图每张都要 ReadPixels + EncodeToPNG，07 票的近景还要等镜头过渡和手势回合，
    //     900 帧（15s）在加了几段之后就顶不住了 —— 放宽到 40s，真挂死照样会被收掉。
    void Update()
    {
        if (Time.frameCount > 2400)
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
