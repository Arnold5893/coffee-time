using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ProjectSetup
{
    // 前柜组：整体向通道外侧平移，使前后柜通道 >= 1.20m（06 票结论）
    static readonly string[] FrontGroup = { "Bar_Counter", "Bar_Stool_1", "Bar_Stool_2", "Bar_Stool_3", "Bar_Rack" };
    const float TargetAisle = 1.20f;

    public static void Run()
    {
        var report = new StringBuilder();
        try
        {
            // 1. URP 管线资产（重建：旧资产缺默认 Renderer——Create() 生成的 RendererData 必须随主资产持久化）
            const string rpPath = "Assets/Settings/URP.asset";
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath) != null)
                AssetDatabase.DeleteAsset(rpPath);
            Directory.CreateDirectory("Assets/Settings");
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            var rp = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(rp, rpPath);
            AssetDatabase.AddObjectToAsset(rendererData, rp);
            AssetDatabase.SaveAssets();
            GraphicsSettings.defaultRenderPipeline = rp;
            QualitySettings.renderPipeline = rp;
            report.AppendLine("URP pipeline asset created & assigned (renderer persisted as sub-asset)");

            // 2. FBX 纹理提取与材质重映射
            const string fbxPath = "Assets/Models/coffee_shop.fbx";
            Directory.CreateDirectory("Assets/Models/Textures");
            var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            mi.ExtractTextures("Assets/Models/Textures");
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            mi.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Local);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            report.AppendLine("textures extracted & materials remapped");

            AssetDatabase.SaveAssets();

            // 3. 场景：新建、实例化 FBX
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var shop = (GameObject)Object.Instantiate(prefab);
            shop.name = "CoffeeShop";

            var all = shop.GetComponentsInChildren<Transform>(true);

            // 4. 材质转 URP：手动改 shader（官方批处理转换器在本版本有 2D 升级器无默认构造的 bug）
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            Directory.CreateDirectory("Assets/Models/Materials");
            var matCache = new System.Collections.Generic.Dictionary<string, Material>();
            Material ToUrp(Material src)
            {
                if (src == null || litShader == null) return src;
                if (src.shader == litShader) return src;
                if (matCache.TryGetValue(src.name, out var m)) return m;
                var nm = new Material(litShader) { name = src.name };
                if (src.HasProperty("_MainTex")) nm.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                if (src.HasProperty("_Color")) nm.SetColor("_BaseColor", src.GetColor("_Color"));
                AssetDatabase.CreateAsset(nm, AssetDatabase.GenerateUniqueAssetPath($"Assets/Models/Materials/{src.name}.mat"));
                matCache[src.name] = nm;
                return nm;
            }
            int converted = 0;
            foreach (var r in shop.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var nm = ToUrp(mats[i]);
                    if (nm != mats[i]) { mats[i] = nm; converted++; }
                }
                r.sharedMaterials = mats;
            }
            AssetDatabase.SaveAssets();
            report.AppendLine($"renderer material slots converted to URP: {converted}");
            var shaderNames = shop.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null)
                .Select(m => m.shader.name).Distinct();
            report.AppendLine("shaders in scene: " + string.Join(", ", shaderNames));

            // 材质诊断：自发光是导致全场惨白的首要嫌疑（FBX 导入常把 diffuse 也塞进 emission）
            var distinctMats = shop.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            int emissiveCount = 0;
            foreach (var m in distinctMats)
            {
                bool em = m.IsKeywordEnabled("_EMISSION");
                if (em) emissiveCount++;
                report.AppendLine($"mat {m.name}: emission={em} emColor={(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor").ToString("F2") : "-")} baseMap={(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "null")}");
            }
            report.AppendLine($"emissive materials: {emissiveCount}/{distinctMats.Count}");
            report.AppendLine($"ambient: mode={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity:F2} skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "null")}");

            // 5. 通道轴自动判定：两柜在哪根水平轴上不相叠，哪根就是通道方向
            Transform Find(string n) => all.First(t => t.name == n);
            var backR = Find("Back_Counter").GetComponent<Renderer>();
            var frontR = Find("Bar_Counter").GetComponent<Renderer>();
            Bounds bb = backR.bounds, fb = frontR.bounds;
            float gapZplus  = fb.min.z - bb.max.z;   // 前柜在 +Z 侧
            float gapZminus = bb.min.z - fb.max.z;   // 前柜在 -Z 侧
            float gapXplus  = fb.min.x - bb.max.x;   // 前柜在 +X 侧
            float gapXminus = bb.min.x - fb.max.x;   // 前柜在 -X 侧
            report.AppendLine($"gaps: z+={gapZplus:F2} z-={gapZminus:F2} x+={gapXplus:F2} x-={gapXminus:F2}");

            float best = Mathf.Max(gapZplus, gapZminus, gapXplus, gapXminus);
            Vector3 dir =
                best == gapZplus ? Vector3.forward :
                best == gapZminus ? Vector3.back :
                best == gapXplus ? Vector3.right : Vector3.left;
            float shift = Mathf.Max(0f, TargetAisle - best);
            report.AppendLine($"aisle axis={dir} current={best:F2}m shift={shift:F2}m");

            int moved = 0;
            foreach (var t in all)
            {
                if (FrontGroup.Contains(t.name)) { t.position += dir * shift; moved++; }
            }
            report.AppendLine($"front group moved ({moved}/{FrontGroup.Length} objects)");

            // 6. 校验通道宽度
            float newAisle = dir == Vector3.forward ? frontR.bounds.min.z - backR.bounds.max.z
                    : dir == Vector3.back ? backR.bounds.min.z - frontR.bounds.max.z
                    : dir == Vector3.right ? frontR.bounds.min.x - backR.bounds.max.x
                    : backR.bounds.min.x - frontR.bounds.max.x;
            report.AppendLine($"aisle_width_m={newAisle:F2} (target>={TargetAisle:F2})");

            // 6.5 碰撞体烘焙（02 票）：按每个网格的局部包围盒烘一个 BoxCollider。
            //     跳过天花板以上与细杆件，避免吊灯/画框/挂杆把角色挡在过道里。
            var skipPrefix = new[] { "Ceiling", "Pendant", "Painting", "Lightbox", "Hood" };
            // 门框是"环形"网格（两侧立框 + 门头以上白墙），按整体包围盒烘一个 box 会把整个门洞封死。
            // 门扇 / 固定亮子 / 门槛条都是独立对象，各自有包围盒，不需要门框兜底。
            // 左上那扇常闭门（Door_locked）反过来 —— 它就是整体一件，必须烘，才能把门洞堵住。
            var skipExact = new[] { "Door_entrance" };
            int colliderCount = 0;

            // 诊断：横向两个轴都"大"的对象，用来确认斜墙的实际包围盒尺寸
            foreach (var r in shop.GetComponentsInChildren<Renderer>(true))
            {
                var s = r.bounds.size;
                if (s.x > 1.5f && s.z > 1.5f)
                    report.AppendLine($"  big-xz {r.name} size=({s.x:F2},{s.y:F2},{s.z:F2})");
            }

            foreach (var mf in shop.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var go = mf.gameObject;
                if (skipPrefix.Any(p => go.name.StartsWith(p, System.StringComparison.Ordinal))) continue;
                if (skipExact.Contains(go.name)) continue;
                if (go.GetComponent<Collider>() != null) continue;

                var lb = mesh.bounds;
                var ls = mf.transform.lossyScale;
                var rend = mf.GetComponent<Renderer>();
                // 高度判定用世界包围盒：对象可能带旋转，局部 Y 轴不等于世界 Y 轴
                float bottomWorld = rend != null ? rend.bounds.min.y : mf.transform.TransformPoint(lb.center).y - lb.extents.y * Mathf.Abs(ls.y);
                if (bottomWorld > 2.2f) continue;                                  // 头顶以上
                if (lb.size.x * Mathf.Abs(ls.x) < 0.10f && lb.size.z * Mathf.Abs(ls.z) < 0.10f) continue;  // 细杆

                // 斜置的整面墙（东北角斜窗那段）按轴对齐包围盒烘，盒子会从一个墙角铺到另一个墙角，
                // 把阳光区一半的通道封死——实测 6 个窗边座位连起身站位都找不到。
                // 这类"又长又宽又立着"的网格改用沿主轴旋转的 OBB。
                if (rend != null && NeedsOrientedBox(rend.bounds))
                {
                    BakeOrientedBox(go, mf, mesh, shop.transform);
                    colliderCount++;
                    continue;
                }

                var bc = go.AddComponent<BoxCollider>();
                bc.center = lb.center;
                bc.size = lb.size;
                // 薄片（地板、地台、单面墙）必须给厚度：零厚度的碰撞体会让 CharacterController
                // 常驻穿透状态，isGrounded 恒为 false、水平位移被整体卡死（本次踩到的坑）。
                // 注意 FBX 子物体常带 100 倍缩放、且根节点绕 X 转了 -90°，
                // 所以「薄」要按世界高度判断，增量也要换算回局部单位。
                Vector3 upInLocal = mf.transform.InverseTransformDirection(Vector3.up);
                int upAxis = 0;
                float bestAxis = 0f;
                for (int i = 0; i < 3; i++)
                    if (Mathf.Abs(upInLocal[i]) > bestAxis) { bestAxis = Mathf.Abs(upInLocal[i]); upAxis = i; }

                float worldThickY = lb.size[upAxis] * Mathf.Abs(ls[upAxis]);
                if (worldThickY < 0.06f)
                {
                    float localAdd = 0.25f / Mathf.Max(1e-4f, Mathf.Abs(ls[upAxis]));
                    var sz = bc.size; sz[upAxis] += localAdd; bc.size = sz;
                    // 局部轴可能反向（FBX 根节点 -90°X），按符号把加厚量补到下方，保证顶面高度不变
                    var ct = bc.center;
                    ct[upAxis] -= Mathf.Sign(upInLocal[upAxis]) * localAdd * 0.5f;
                    bc.center = ct;
                }
                colliderCount++;
            }
            report.AppendLine($"box colliders baked: {colliderCount}");

            // 7. 灯光、相机与截图机位
            var sunGO = new GameObject("Directional Light");
            var sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.8f;
            sunGO.transform.rotation = Quaternion.Euler(50, -30, 0);

            // 全局 Volume：ACES 色调映射压住过曝（Tonemapping 覆盖必须作为子资产持久化，否则重载后丢失）
            const string profilePath = "Assets/Settings/GlobalVolumeProfile.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) != null)
                AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            AssetDatabase.CreateAsset(profile, profilePath);
            AssetDatabase.AddObjectToAsset(tone, profile);
            AssetDatabase.SaveAssets();
            var volGO = new GameObject("Global Volume");
            var vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
            report.AppendLine($"volume profile assigned: {(vol.sharedProfile != null)} guid={AssetDatabase.AssetPathToGUID(profilePath)}");

            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.18f, 0.18f, 0.2f);
            var camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;   // URP 相机默认不开后处理，ACES 全靠它

            // 室内店没有直射天光：默认天空盒环境光强度 1.0 会把白墙全打爆，压到室内水平
            RenderSettings.ambientIntensity = 0.25f;
            report.AppendLine("ambientIntensity set to 0.25 (interior baseline)");

            // 门外地台（Porch）会把整体包围盒往店外拉，店面取景与居中照旧用店内包围盒
            var renderers = shop.GetComponentsInChildren<Renderer>()
                .Where(r => r.name != "Porch").ToArray();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            // 7.4 玩家角色 + 第三人称跟随镜头（02 票）+ 门外出生点（03 票）
            //     外观是程序化占位体，接 Quaternius 模型时整体替换 Player/Visual 子物体即可，
            //     角色控制与镜头代码不感知外观。
            // 入口在右下侧（方案 12.2「主入口位于右下侧门，左上双开门保持关闭」）。
            // 会开的那一扇单独成一件、原点落在铰链轴上；门槛条给出门洞的精确中心与净宽。
            var leafT = all.FirstOrDefault(t => t.name == "Door_entrance_Leaf");
            var thrT = all.FirstOrDefault(t => t.name == "Door_entrance_Threshold");
            var frameT = all.FirstOrDefault(t => t.name == "Door_entrance");
            var panelT = all.FirstOrDefault(t => t.name == "Door_entrance_Panel");
            var porchT = all.FirstOrDefault(t => t.name == "Porch");

            Vector3 spawn = bounds.center; spawn.y = 0.06f;
            Vector3 facing = Vector3.forward;
            Vector3 doorCenterW = bounds.center; doorCenterW.y = 0f;
            Vector3 interiorDir = Vector3.forward;
            Vector3 interiorAnchor = bounds.center; interiorAnchor.y = 0.06f;
            float doorHalfWidth = 0.75f;

            if (leafT != null && thrT != null)
            {
                // 门槛条就是门洞本身：中心 = 门洞中心，较长的那条水平边 = 净宽
                Bounds tb = thrT.GetComponent<Renderer>().bounds;
                doorCenterW = new Vector3(tb.center.x, 0f, tb.center.z);
                doorHalfWidth = Mathf.Max(tb.size.x, tb.size.z) * 0.5f;
                Vector3 lateralAxis = tb.size.x >= tb.size.z ? Vector3.right : Vector3.forward;

                // 门洞法线：垂直于门槛。正负不用包围盒中心猜（店面是多边形，那方向是斜的），
                // 由门外地台定：从地台指向门的那一侧就是"往店内"。
                Vector3 nrm = Vector3.Cross(Vector3.up, lateralAxis).normalized;
                if (porchT != null && porchT.GetComponent<Renderer>() != null)
                {
                    Bounds pb = porchT.GetComponent<Renderer>().bounds;
                    Vector3 toDoor = doorCenterW - new Vector3(pb.center.x, 0f, pb.center.z);
                    toDoor.y = 0f;
                    if (toDoor.sqrMagnitude > 1e-4f && Vector3.Dot(toDoor, nrm) < 0f) nrm = -nrm;
                }

                if (nrm.sqrMagnitude > 1e-4f)
                {
                    nrm.Normalize();
                    interiorDir = nrm;
                    facing = nrm;                          // 出生时正对着店门
                    spawn = doorCenterW - nrm * 1.75f;     // 站在门外地台上
                    spawn.y = 0.06f;
                    report.AppendLine($"door normal={nrm} width={doorHalfWidth * 2f:F2}m");

                    // 店内视点：从店面中心向外螺旋找一块不压家具的空位。
                    // 不能贴着门口取（门两侧就是书墙，第三人称镜头会被顶到 1m 内，构图没法看）。
                    var floorRO = all.FirstOrDefault(t => t.name == "Floor");
                    Vector3 mid = floorRO != null && floorRO.GetComponent<Renderer>() != null
                        ? floorRO.GetComponent<Renderer>().bounds.center
                        : bounds.center;
                    Vector3 midFlat = new Vector3(mid.x, 0.06f, mid.z);

                    Physics.SyncTransforms();
                    bool found = false;
                    for (float rad = 0f; rad <= 4.0f && !found; rad += 0.30f)
                    {
                        int steps = rad < 0.01f ? 1 : 8;
                        for (int k = 0; k < steps && !found; k++)
                        {
                            float ang = k * Mathf.PI * 2f / steps;
                            Vector3 cand = midFlat + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rad;
                            Vector3 a = new Vector3(cand.x, 0.50f, cand.z);
                            Vector3 b = new Vector3(cand.x, 1.40f, cand.z);
                            if (Physics.CheckCapsule(a, b, 0.32f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            interiorAnchor = new Vector3(cand.x, 0.06f, cand.z);
                            report.AppendLine($"interior anchor at r={rad:F1}m ang={ang * Mathf.Rad2Deg:F0}deg");
                            found = true;
                        }
                    }
                    if (!found) report.AppendLine("interior anchor: NOT FOUND, using shop center");

                    // 出生点自检：被门扇/墙压住就往外退（门外地台进深 3.0m，最多退到 2.65m）
                    for (float back = 0f; back <= 0.9f; back += 0.2f)
                    {
                        Vector3 p = doorCenterW - nrm * (1.75f + back); p.y = 0.06f;
                        Vector3 a = new Vector3(p.x, 0.50f, p.z);
                        Vector3 b = new Vector3(p.x, 1.40f, p.z);
                        if (Physics.CheckCapsule(a, b, 0.32f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        spawn = p;
                        break;
                    }
                }
            }

            Material MakeMat(string path, Color c)
            {
                AssetDatabase.DeleteAsset(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var m = new Material(litShader) { name = Path.GetFileNameWithoutExtension(path) };
                m.SetColor("_BaseColor", c);
                AssetDatabase.CreateAsset(m, path);
                return m;
            }
            var matBody = MakeMat("Assets/Models/Materials/PlayerBody.mat", new Color(0.80f, 0.76f, 0.68f));
            var matSkin = MakeMat("Assets/Models/Materials/PlayerSkin.mat", new Color(0.85f, 0.69f, 0.55f));

            var playerGO = new GameObject("Player");
            playerGO.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(facing, Vector3.up));
            var visual = new GameObject("Visual").transform;
            visual.SetParent(playerGO.transform, false);

            GameObject Prim(PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat, string name)
            {
                var g = GameObject.CreatePrimitive(type);
                g.name = name;
                g.transform.SetParent(visual, false);
                g.transform.localPosition = localPos;
                g.transform.localScale = scale;
                var col = g.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);      // 碰撞交给 CharacterController
                g.GetComponent<Renderer>().sharedMaterial = mat;
                return g;
            }
            Prim(PrimitiveType.Capsule, new Vector3(0f, 0.42f, 0f), new Vector3(0.34f, 0.42f, 0.34f), matBody, "PlayerVisual_Legs");   // 腿（坐下时收起来）
            Prim(PrimitiveType.Capsule, new Vector3(0f, 1.04f, 0f), new Vector3(0.44f, 0.40f, 0.38f), matBody, "PlayerVisual_Body");   // 身
            Prim(PrimitiveType.Sphere, new Vector3(0f, 1.50f, 0f), new Vector3(0.26f, 0.28f, 0.26f), matSkin, "PlayerVisual_Head");    // 头
            Prim(PrimitiveType.Cube, new Vector3(0f, 1.12f, 0.185f), new Vector3(0.13f, 0.15f, 0.03f), matSkin, "PlayerVisual_Facing"); // 朝向标

            var charCtl = playerGO.AddComponent<CharacterController>();
            charCtl.height = 1.66f;
            charCtl.radius = 0.28f;
            charCtl.center = new Vector3(0f, 0.83f, 0f);
            charCtl.skinWidth = 0.03f;
            charCtl.stepOffset = 0.2f;
            charCtl.slopeLimit = 50f;

            var pivotGO = new GameObject("CameraPivot");
            pivotGO.transform.SetParent(playerGO.transform, false);
            pivotGO.transform.localPosition = new Vector3(0f, 1.05f, 0f);

            var spawnGO = new GameObject("PlayerSpawn");
            spawnGO.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(facing, Vector3.up));
            var interiorGO = new GameObject("InteriorAnchor");
            interiorGO.transform.SetPositionAndRotation(interiorAnchor, Quaternion.LookRotation(facing, Vector3.up));

            var router = playerGO.AddComponent<CoffeeShop.Controls.PlayerInputRouter>();
            var motor = playerGO.AddComponent<CoffeeShop.Player.PlayerMotor>();
            motor.Bind(router, camGO.transform, pivotGO.transform);

            var follow = camGO.AddComponent<CoffeeShop.Cameras.ThirdPersonCameraRig>();
            follow.Bind(playerGO.transform, pivotGO.transform, router);
            follow.ConfigureInitialView(Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, 35f);

            var fader = camGO.AddComponent<CoffeeShop.Cameras.CameraOcclusionFader>();
            fader.Bind(camGO.transform, playerGO.transform);
            report.AppendLine($"player spawn=({spawn.x:F2}, {spawn.z:F2}) facing=({facing.x:F2}, {facing.z:F2})");

            // 7.5 入口门 + 门外地台围挡（03 票）
            if (leafT != null && thrT != null)
            {
                var doorGO = new GameObject("EntranceDoor");
                doorGO.transform.position = doorCenterW;
                var door = doorGO.AddComponent<CoffeeShop.Interaction.EntranceDoor>();
                door.leaf = leafT;
                door.doorCenter = doorCenterW;
                door.interiorDir = interiorDir;
                door.halfWidth = doorHalfWidth;
                door.player = playerGO.transform;
                door.Bind(router);

                var targets = new System.Collections.Generic.List<Collider>();
                foreach (var t in new[] { leafT, panelT, thrT })
                {
                    if (t == null) continue;
                    var c = t.GetComponent<Collider>();
                    if (c != null) targets.Add(c);
                }
                door.clickTargets = targets.ToArray();

                var bell = doorGO.AddComponent<AudioSource>();
                bell.playOnAwake = false;
                bell.spatialBlend = 1f;
                bell.rolloffMode = AudioRolloffMode.Linear;
                bell.minDistance = 2f;
                bell.maxDistance = 30f;
                door.bellSource = bell;

                // 门外没有外立面也就没有路灯，门口会是一片死黑。
                // 门头补一盏暖光——照片里门头上方本来就有灯，顺带把门外地台照亮。
                var lampGO = new GameObject("EntranceLamp");
                lampGO.transform.position = doorCenterW - interiorDir.normalized * 1.10f + Vector3.up * 2.90f;
                var lamp = lampGO.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.range = 9f;
                lamp.intensity = 4f;
                lamp.color = new Color(1f, 0.88f, 0.72f);
                lamp.shadows = LightShadows.None;

                report.AppendLine($"entrance door: center=({doorCenterW.x:F2}, {doorCenterW.z:F2}) " +
                                  $"halfWidth={doorHalfWidth:F2} clickTargets={targets.Count}");

                // 门外地台三面不可见围挡：玩家不会从平台边缘掉进空场景。
                // 放在独立的 Boundary 层：角色会撞上，但交互射线和镜头避让都跳过它。
                if (porchT != null && porchT.GetComponent<Renderer>() != null)
                {
                    int boundaryLayer = EnsureLayer("Boundary");
                    Bounds pb = porchT.GetComponent<Renderer>().bounds;
                    Vector3 nDir = interiorDir.normalized;
                    Vector3 lat = Vector3.Cross(Vector3.up, nDir).normalized;
                    float extN = Mathf.Abs(nDir.x) * pb.extents.x + Mathf.Abs(nDir.z) * pb.extents.z;
                    float extL = Mathf.Abs(lat.x) * pb.extents.x + Mathf.Abs(lat.z) * pb.extents.z;
                    var guardGO = new GameObject("PorchBoundary");
                    guardGO.layer = boundaryLayer;

                    void Guard(Vector3 normal, Vector3 center, float width)
                    {
                        var g = new GameObject("guard");
                        g.layer = boundaryLayer;
                        g.transform.SetParent(guardGO.transform, false);
                        g.transform.SetPositionAndRotation(center, Quaternion.LookRotation(normal, Vector3.up));
                        var bc = g.AddComponent<BoxCollider>();
                        bc.size = new Vector3(width, 3f, 0.2f);
                    }
                    Vector3 pc = pb.center; pc.y = 1.5f;
                    Guard(nDir, pc - nDir * extN + nDir * 0.1f, extL * 2f);
                    Guard(lat, pc + lat * extL, extN * 2f);
                    Guard(-lat, pc - lat * extL, extN * 2f);
                    report.AppendLine($"porch boundary guards=3 layer={boundaryLayer} " +
                                      $"extentN={extN:F2} extentL={extL:F2}");
                }
            }

            // 7.6 座位系统（04 票）：配置驱动，场景里逐把椅子解析出坐下点/朝向/桌面点。
            //     配置只写"绑哪个对象、朝哪边"，坐标全部现算，家具挪位后不用改 JSON。
            const string seatCfg = "Assets/Resources/Config/seats.json";
            if (File.Exists(seatCfg)) AssetDatabase.ImportAsset(seatCfg, ImportAssetOptions.ForceUpdate);

            var seatGO = new GameObject("Seating");
            var seating = seatGO.AddComponent<CoffeeShop.Seating.SeatingManager>();
            seating.shopRoot = shop.transform;
            seating.Build();                       // 编辑期先解析一次，好把结果写进报告
            report.AppendLine("seating: " + seating.Describe());
            foreach (var s in seating.Seats)
                report.AppendLine("  seat " + s);
            if (seating.Missing.Count > 0)
                report.AppendLine("seating missing anchors: " + string.Join(", ", seating.Missing));
            foreach (var err in seating.Errors) report.AppendLine("seating config error: " + err);

            var seatInteract = playerGO.AddComponent<CoffeeShop.Seating.SeatInteraction>();
            seatInteract.Bind(seating, motor, router, visual);
            report.AppendLine($"seat interact: reach={seatInteract.reachRadius:F2}m " +
                              $"nearestFree={(seating.NearestFree(spawn, 99f) != null ? "yes" : "no")}");

            var audioListener = camGO.GetComponent<AudioListener>();
            if (audioListener == null) camGO.AddComponent<AudioListener>();

            var rigGO = new GameObject("ScreenshotRig");
            var rig = rigGO.AddComponent<ScreenshotRig>();
            rig.player = motor;
            rig.followRig = follow;
            rig.fader = fader;
            rig.spawnPoint = spawnGO.transform;
            rig.interiorPoint = interiorGO.transform;
            rig.door = Object.FindFirstObjectByType<CoffeeShop.Interaction.EntranceDoor>();
            // 门口落脚区机位：站在店内正对门 3.3m，一眼看到门、门口空地、以及阳光区长桌收头
            rig.camPosEntry = doorCenterW + interiorDir * 3.3f + Vector3.up * 1.6f;
            rig.camLookEntry = doorCenterW + Vector3.up * 0.85f;
            // 吧台机位：站在通道正中（两柜之间），沿吧台纵向（X）偏一点看过去，能同时看到前柜、通道、后柜
            var aisleCenter = (frontR.bounds.center + backR.bounds.center) * 0.5f;
            rig.camPosBar = aisleCenter + new Vector3(2.8f, 0.7f, 0f);
            rig.camLookBar = aisleCenter + new Vector3(-1.5f, -0.4f, 0f);
            // 全景机位：接近俯视（无吊顶楼板遮挡），看得到店内布局
            rig.camPosAll = bounds.center + new Vector3(0.5f, 11f, 4.5f);
            rig.camLookAll = bounds.center;
            // 后吧台正视（顾客视角，对照"吧台和做咖啡的地方(近景).jpg"）；取咖啡机与最右端热水机的中点取景
            var cmT = all.FirstOrDefault(t => t.name == "Coffee_Machine");
            var wbT = all.FirstOrDefault(t => t.name == "Water_Boiler");
            if (cmT != null)
            {
                var cmc = cmT.GetComponent<Renderer>().bounds.center;
                float midX = wbT != null ? (cmc.x + wbT.GetComponent<Renderer>().bounds.center.x) / 2f : cmc.x - 1.5f;
                rig.camPosEq = new Vector3(midX, 1.6f, cmc.z - 3.7f);
                rig.camLookEq = new Vector3(midX, 0.95f, cmc.z + 0.1f);
            }
            // 吧台凳特写（从顾客侧后方看靠背）
            var stoolT = all.FirstOrDefault(t => t.name == "Bar_Stool_1");
            if (stoolT != null)
            {
                var sc = stoolT.GetComponent<Renderer>().bounds.center;
                rig.camPosStool = sc + new Vector3(0.55f, 0.45f, -0.95f);
                rig.camLookStool = sc + new Vector3(0f, 0.15f, 0f);
            }
            // 矮凳特写：站到"圆桌→椅子"连线的延长线上回看，修对时镜头正对靠背环
            var chairT = all.FirstOrDefault(t => t.name.StartsWith("Round2_Chair"));
            var tableT = all.FirstOrDefault(t => t.name == "Round_Table_2");
            if (chairT != null && tableT != null)
            {
                var cc = chairT.GetComponent<Renderer>().bounds.center;
                var tc = tableT.GetComponent<Renderer>().bounds.center;
                var back = (cc - tc); back.y = 0; back = back.normalized;
                rig.camPosChair = cc + back * 1.1f + new Vector3(0, 0.55f, 0);
                rig.camLookChair = cc + new Vector3(0f, 0.05f, 0f);
            }
            // 未找到的对象用全景机位兜底，避免相机落在原点
            if (rig.camPosEq == Vector3.zero) { rig.camPosEq = rig.camPosAll; rig.camLookEq = rig.camLookAll; }
            if (rig.camPosStool == Vector3.zero) { rig.camPosStool = rig.camPosAll; rig.camLookStool = rig.camLookAll; }
            if (rig.camPosChair == Vector3.zero) { rig.camPosChair = rig.camPosAll; rig.camLookChair = rig.camLookAll; }

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
            AssetDatabase.SaveAssets();
            report.AppendLine("scene saved: Assets/Scenes/Main.unity (screenshots via PlayCapture)");

            File.WriteAllText("setup_report.txt", report.ToString());
            Debug.Log(report.ToString());
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            File.WriteAllText("setup_report.txt", "EXCEPTION: " + e + "\n\n" + report);
            EditorApplication.Exit(2);
        }
    }

    /// <summary>
    /// 判断一个网格是否"斜着摆的大件"：横向两个轴都超过 1.8m、且立着有一定高度。
    /// 店里只有东北角那段斜窗墙符合（斜边使 AABB 面积远大于实际墙体，4.49×4.43 的包围盒
    /// 对应实际只有 5.5m 长、0.6m 厚的墙）。
    /// </summary>
    static bool NeedsOrientedBox(Bounds worldBounds)
    {
        var s = worldBounds.size;
        return s.x > 1.8f && s.z > 1.8f && s.y > 1.5f && s.y < 6f;
    }

    /// <summary>
    /// 沿网格顶点主方向烘一个旋转的 BoxCollider。
    /// 主方向用水平面内顶点的协方差主轴（PCA）求，不需要知道墙的走向。
    /// 碰撞体挂在 FBX 根下而不是原节点下——子节点带 100 倍缩放，挂进去尺寸会翻百倍。
    /// </summary>
    static void BakeOrientedBox(GameObject go, MeshFilter mf, Mesh mesh, Transform root)
    {
        var verts = mesh.vertices;
        int n = verts.Length;
        if (n < 3) return;

        var w = new Vector3[n];
        for (int i = 0; i < n; i++) w[i] = mf.transform.TransformPoint(verts[i]);

        Vector3 min = w[0], max = w[0];
        Vector2 mean = Vector2.zero;
        for (int i = 0; i < n; i++)
        {
            min = Vector3.Min(min, w[i]);
            max = Vector3.Max(max, w[i]);
            mean += new Vector2(w[i].x, w[i].z);
        }
        mean /= n;

        float sxx = 0f, sxz = 0f, szz = 0f;
        for (int i = 0; i < n; i++)
        {
            float dx = w[i].x - mean.x, dz = w[i].z - mean.y;
            sxx += dx * dx; sxz += dx * dz; szz += dz * dz;
        }
        float theta = 0.5f * Mathf.Atan2(2f * sxz, sxx - szz);
        Vector3 axis = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
        Vector3 perp = Vector3.Cross(Vector3.up, axis);

        float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            float a = Vector3.Dot(w[i], axis);
            float b = Vector3.Dot(w[i], perp);
            a0 = Mathf.Min(a0, a); a1 = Mathf.Max(a1, a);
            b0 = Mathf.Min(b0, b); b1 = Mathf.Max(b1, b);
        }

        Vector3 center = axis * ((a0 + a1) * 0.5f)
                       + perp * ((b0 + b1) * 0.5f)
                       + Vector3.up * ((min.y + max.y) * 0.5f);

        var colGO = new GameObject(go.name + "_ObbCollider");
        colGO.layer = go.layer;
        colGO.transform.SetParent(root, true);
        colGO.transform.SetPositionAndRotation(center, Quaternion.LookRotation(axis, Vector3.up));
        var bc = colGO.AddComponent<BoxCollider>();
        // LookRotation(axis) 让局部 +Z 对齐 axis、+X 对齐 perp
        bc.size = new Vector3(b1 - b0, max.y - min.y, a1 - a0);
        Debug.Log($"[collider] OBB for {go.name}: size={bc.size} axis={axis}");
    }

    /// <summary>
    /// 确保存在一个自定义层并返回层号（前 8 个是 Unity 内置层，不能动）。
    /// 找不到空位就退回 Default(0)。
    /// </summary>
    static int EnsureLayer(string name)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return 0;
        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null) return 0;

        for (int i = 8; i < layers.arraySize; i++)
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (el.stringValue == name) return i;
        }
        for (int i = 8; i < layers.arraySize; i++)
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = name;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }
        }
        return 0;
    }
}
