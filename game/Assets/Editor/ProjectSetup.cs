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
    static readonly string[] FrontGroup = { "Bar_Counter", "Cashier", "Bar_Stool_1", "Bar_Stool_2", "Bar_Stool_3", "Bar_Rack" };
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
            report.AppendLine($"front group moved ({moved}/6 objects)");

            // 6. 校验通道宽度
            float newAisle = dir == Vector3.forward ? frontR.bounds.min.z - backR.bounds.max.z
                    : dir == Vector3.back ? backR.bounds.min.z - frontR.bounds.max.z
                    : dir == Vector3.right ? frontR.bounds.min.x - backR.bounds.max.x
                    : backR.bounds.min.x - frontR.bounds.max.x;
            report.AppendLine($"aisle_width_m={newAisle:F2} (target>={TargetAisle:F2})");

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

            var renderers = shop.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            var rigGO = new GameObject("ScreenshotRig");
            var rig = rigGO.AddComponent<ScreenshotRig>();
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
}
