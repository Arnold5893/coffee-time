using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 02 票自动化验收入口：打开 Main 场景、挂上 MovementSmokeTest、进 Play 模式。
/// 用法：Unity.exe -batchmode -executeMethod PlayTest.Run（不要加 -quit）
/// </summary>
public static class PlayTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");

        // 截图装置会在 Start 里跑完就退出，测试期间必须关掉
        foreach (var rig in Object.FindObjectsByType<ScreenshotRig>(FindObjectsSortMode.None))
            rig.enabled = false;

        var motor = Object.FindFirstObjectByType<CoffeeShop.Player.PlayerMotor>();
        var router = Object.FindFirstObjectByType<CoffeeShop.Controls.PlayerInputRouter>();
        var camRig = Object.FindFirstObjectByType<CoffeeShop.Cameras.ThirdPersonCameraRig>();
        if (motor == null || router == null || camRig == null)
        {
            Debug.LogError("[SMOKE] missing player/router/camera rig in scene");
            EditorApplication.Exit(2);
            return;
        }

        var shop = GameObject.Find("CoffeeShop");
        var floor = FindRenderer(shop, "Floor");
        var back = FindRenderer(shop, "Back_Counter");
        var front = FindRenderer(shop, "Bar_Counter");

        var test = new GameObject("MovementSmokeTest").AddComponent<CoffeeShop.Testing.MovementSmokeTest>();
        test.verbose = false;
        test.player = motor;
        test.router = router;
        test.rig = camRig;

        // 店面水平范围：地板包围盒往里收 0.25m（墙厚）
        Bounds fb = floor != null ? floor.bounds : new Bounds(Vector3.zero, new Vector3(12, 1, 16));
        test.room = new Bounds(new Vector3(fb.center.x, 0f, fb.center.z),
                               new Vector3(fb.size.x - 0.5f, 2f, fb.size.z - 0.5f));

        test.backCounter = back != null ? back.bounds : new Bounds(Vector3.zero, Vector3.one);
        test.frontCounter = front != null ? front.bounds : test.backCounter;

        // 起点：店内空位（02 票）——出生点现在在门外（03 票），不能直接用作房间内锚点
        var interior = GameObject.Find("InteriorAnchor");
        var spawn = GameObject.Find("PlayerSpawn");
        var candidates = new List<Vector3>();
        if (interior != null) candidates.Add(interior.transform.position);
        else if (spawn != null) candidates.Add(spawn.transform.position);
        if (back != null && front != null)
        {
            var a = back.bounds.center; var b = front.bounds.center;
            candidates.Add(new Vector3((a.x + b.x) * 0.5f, 0.06f, (a.z + b.z) * 0.5f));
        }
        var pairs = new[]
        {
            ("Round_Table_2", "Round_Table_3"),
            ("Round_Table_3", "Round_Table_4"),
            ("Round_Table_1", "Meeting_Table"),
            ("Group_Table_2", "Group_Table_3"),
            ("Round_Table_4", "Divider_Bookshelf"),
        };
        foreach (var (n1, n2) in pairs)
        {
            var r1 = FindRenderer(shop, n1);
            var r2 = FindRenderer(shop, n2);
            if (r1 == null || r2 == null) continue;
            Vector3 p = (r1.bounds.center + r2.bounds.center) * 0.5f;
            candidates.Add(new Vector3(p.x, 0.06f, p.z));
        }
        candidates.Add(new Vector3(fb.center.x, 0.06f, fb.center.z));

        Physics.SyncTransforms();
        foreach (var c in candidates)
        {
            // 若中点压在家具上，就地螺旋外扩找一个空位
            Vector3 chosen = c;
            bool ok = false;
            for (float rad = 0f; rad <= 1.2f && !ok; rad += 0.2f)
            {
                for (int k = 0; k < (rad < 0.01f ? 1 : 8) && !ok; k++)
                {
                    float ang = k * Mathf.PI / 4f;
                    Vector3 p = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rad;
                    Vector3 a = new Vector3(p.x, 0.55f, p.z), b = new Vector3(p.x, 1.35f, p.z);
                    if (Physics.CheckCapsule(a, b, 0.33f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    chosen = new Vector3(p.x, 0.06f, p.z);
                    ok = true;
                }
            }
            if (ok && !test.anchors.Any(q => Vector3.Distance(q, chosen) < 0.8f))
                test.anchors.Add(chosen);
        }
        Debug.Log($"[SMOKE] anchors={test.anchors.Count} room={test.room.size} backMaxZ={test.backCounter.max.z:F2}");

        // 直接进 Play 模式：进程随后由测试自行退出，不经保存，Main.unity 不会被改写
        EditorApplication.EnterPlaymode();
    }

    /// <summary>
    /// 03 票自动化验收入口：进门流程状态机。
    /// 用法：Unity.exe -batchmode -executeMethod PlayTest.RunEntry（不要加 -quit）
    /// </summary>
    public static void RunEntry()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");

        foreach (var rig in Object.FindObjectsByType<ScreenshotRig>(FindObjectsSortMode.None))
            rig.enabled = false;

        var motor = Object.FindFirstObjectByType<CoffeeShop.Player.PlayerMotor>();
        var router = Object.FindFirstObjectByType<CoffeeShop.Controls.PlayerInputRouter>();
        var door = Object.FindFirstObjectByType<CoffeeShop.Interaction.EntranceDoor>();
        var camRig = Object.FindFirstObjectByType<CoffeeShop.Cameras.ThirdPersonCameraRig>();
        var spawn = GameObject.Find("PlayerSpawn");
        if (motor == null || router == null || door == null || spawn == null)
        {
            Debug.LogError($"[ENTRY] missing refs motor={motor} router={router} door={door} spawn={spawn}");
            EditorApplication.Exit(2);
            return;
        }

        var test = new GameObject("EntryFlowTest").AddComponent<CoffeeShop.Testing.EntryFlowTest>();
        test.player = motor;
        test.router = router;
        test.door = door;
        test.followRig = camRig;
        test.spawnPoint = spawn.transform;

        Debug.Log($"[ENTRY] doorCenter={door.doorCenter} interiorDir={door.interiorDir} spawn={spawn.transform.position}");
        EditorApplication.EnterPlaymode();
    }

    static Renderer FindRenderer(GameObject root, string name)
    {
        if (root == null) return null;
        return root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == name);
    }
}
