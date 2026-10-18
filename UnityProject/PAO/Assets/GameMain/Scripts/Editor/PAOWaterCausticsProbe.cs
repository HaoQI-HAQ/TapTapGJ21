using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 焦散排查工具：截图 + 场景报告 + wengjie 水面修复。
    ///
    /// 背景：wengjie 的头顶水面是 8 块 URP SimpleLit 半透明平面（没有焦散/波纹），
    /// Thalassophobia 的焦散方向光在 URP 里又是「附加方向光 cookie 不渲染」的坑，
    /// 所以游戏里看不到任何焦散亮线。项目里其实已经有一套 URP 可用的
    /// UnderwaterCeiling（Map4 同款焦散亮线），这里负责把它用到 wengjie。
    /// </summary>
    public static class PAOWaterCausticsProbe
    {
        private const string kOutDir     = "Docs/_caustics";
        private const string kScenePath  = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kWaterRoot  = "Wengjie_WaterDetail";
        private const string kCausticsGo = "Caustics";

        // ------------------------------------------------------------------
        // 截图
        // ------------------------------------------------------------------
        [MenuItem("Tools/PAO/Probe/Capture Views", priority = 900)]
        public static void CaptureViews()
        {
            Directory.CreateDirectory(kOutDir);

            Scene scene = SceneManager.GetActiveScene();
            Camera cam = Camera.main;
            if (cam == null)
            {
                Camera[] cameras = Object.FindObjectsOfType<Camera>();
                if (cameras.Length > 0) cam = cameras[0];
            }
            if (cam == null)
            {
                Debug.LogError("[Probe] 当前场景没有相机，无法截图。");
                return;
            }

            Vector3 pos = cam.transform.position;
            Quaternion rot = cam.transform.rotation;
            float yaw = rot.eulerAngles.y;

            Capture("00_main_camera", pos, rot, 1600, 900);
            // 抬头 60°：看头顶水面（截图里的那片蓝）
            Capture("01_look_up", pos, Quaternion.Euler(300f, yaw, 0f), 1600, 900);
            // 贴地视角：看地面焦散
            Capture("02_ground", new Vector3(0f, 4f, -20f), Quaternion.Euler(335f, 0f, 0f), 1600, 900);
            // 俯视：看整片地面（焦散覆盖面积）
            Capture("03_overview", new Vector3(0f, 55f, 20f), Quaternion.Euler(55f, 0f, 0f), 1600, 900);
            // 专门看丁达尔光柱：站在河岸往 +Z 抬头（第一束 Sunshaft 在 (0,22,0)）
            Capture("04_sunshaft", new Vector3(0f, 6f, -30f), Quaternion.Euler(320f, 0f, 0f), 1600, 900);

            Debug.Log("[Probe] 截图完成 -> " + Path.GetFullPath(kOutDir) + "（场景 " + scene.name + "）");
        }

        private static void Capture(string name, Vector3 pos, Quaternion rot, int w, int h)
        {
            Camera source = Camera.main;
            var go = new GameObject("__PAO_ProbeCamera");
            go.hideFlags = HideFlags.HideAndDontSave;

            var cam = go.AddComponent<Camera>();
            if (source != null) cam.CopyFrom(source);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.cullingMask = ~0;

            UniversalAdditionalCameraData data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;

            // 地面焦散是挂在「玩家相机」前方的一块全屏 Quad，临时相机拍不到它。
            // 这里把它临时挪到临时相机上，拍完再挪回去（不能复制 —— 复制会和
            // 原来那块叠在一起，加法混合下亮度翻倍）。
            GameObject quad = FindCausticsQuad();
            Transform quadParent = null;
            Vector3 quadPos = Vector3.zero;
            Quaternion quadRot = Quaternion.identity;
            Vector3 quadScale = Vector3.one;
            if (quad != null)
            {
                quadParent = quad.transform.parent;
                quadPos = quad.transform.localPosition;
                quadRot = quad.transform.localRotation;
                quadScale = quad.transform.localScale;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, 1.5f);
                quad.transform.localRotation = Quaternion.identity;
                quad.transform.localScale = new Vector3(8f, 8f, 1f);
            }
            else
            {
                Debug.LogWarning("[Probe] 场景里没有 PAO_CausticsScreen，截图不含地面焦散。");
            }

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.sRGB);
            rt.name = "__PAO_ProbeRT";
            rt.Create();

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(kOutDir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;

            cam.targetTexture = null;
            if (quad != null && quadParent != null)
            {
                quad.transform.SetParent(quadParent, false);
                quad.transform.localPosition = quadPos;
                quad.transform.localRotation = quadRot;
                quad.transform.localScale = quadScale;
            }
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }

        private static GameObject FindCausticsQuad()
        {
            foreach (MeshRenderer r in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (r != null && r.gameObject.name == "PAO_CausticsScreen")
                    return r.gameObject;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 大物体报告（找出画面里那片蓝色"透镜"到底是什么）
        // ------------------------------------------------------------------
        [MenuItem("Tools/PAO/Probe/Big Renderers", priority = 902)]
        public static void BigRendererReport()
        {
            Scene scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine("[Probe] 大物体清单（尺寸 > 30 米）——场景 " + scene.name);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    Vector3 size = r.bounds.size;
                    if (Mathf.Max(size.x, size.y, size.z) < 30f) continue;

                    Material m = r.sharedMaterial;
                    string mn = m != null ? m.name : "null";
                    string sn = m != null && m.shader != null ? m.shader.name : "null";
                    string color = "n/a";
                    if (m != null && m.HasProperty("_BaseColor"))
                    {
                        Color c = m.GetColor("_BaseColor");
                        color = "(" + c.r.ToString("F2") + "," + c.g.ToString("F2") + "," +
                                c.b.ToString("F2") + ",a" + c.a.ToString("F2") + ")";
                    }
                    sb.AppendLine("  " + r.gameObject.name + "  root=" + root.name +
                                  "  pos=" + r.bounds.center + "  size=" + size +
                                  "  mat=" + mn + "  shader=" + sn + "  base=" + color);
                }
            }

            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------
        // 场景报告
        // ------------------------------------------------------------------
        [MenuItem("Tools/PAO/Probe/Scene Report", priority = 901)]
        public static void Report()
        {
            Scene scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine("[Probe] 场景 " + scene.name + " @ " + scene.path);

            Camera cam = Camera.main;
            if (cam != null)
                sb.AppendLine("  Main Camera: " + cam.transform.position + " / " + cam.transform.eulerAngles);

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool has = false;
            int water = 0, caustic = 0, total = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    total++;
                    if (has) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; has = true; }

                    Material m = r.sharedMaterial;
                    string mn = m != null ? m.name : "null";
                    string sn = m != null && m.shader != null ? m.shader.name : "null";
                    string n = r.gameObject.name;
                    if (n.Contains("Water") || mn.Contains("Water") || sn.Contains("Ceiling"))
                    {
                        water++;
                        sb.AppendLine("  [水] " + n + " pos=" + r.transform.position +
                                      " mat=" + mn + " shader=" + sn +
                                      " size=" + r.bounds.size);
                    }
                    if (mn.Contains("Caustic") || n.Contains("Caustic") || sn.Contains("Caustic"))
                    {
                        caustic++;
                        sb.AppendLine("  [焦散资产] " + n + " mat=" + mn + " shader=" + sn);
                    }
                }
            }

            sb.AppendLine("  Renderer 总数=" + total + "，水面=" + water + "，焦散资产=" + caustic);
            if (has)
                sb.AppendLine("  关卡包围盒 center=" + bounds.center + " size=" + bounds.size + " maxY=" + bounds.max.y);

            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
                sb.AppendLine("  URP 资产=" + urp.name);

            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------
        // wengjie 水面修复：换成项目自有 UnderwaterCeiling（Map4 同款焦散亮线）
        // ------------------------------------------------------------------
        [MenuItem("Tools/PAO/Probe/Fix Wengjie Water", priority = 140)]
        public static void FixWengjieWaterSurface()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Probe] Play 模式里不能改场景，先退出 Play。");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            // ① 删掉 8 块 SimpleLit 平面水面（flat，没有焦散）
            GameObject waterDetail = GameObject.Find(kWaterRoot);
            int removedBlocks = 0;
            if (waterDetail != null)
            {
                removedBlocks = waterDetail.transform.childCount;
                Object.DestroyImmediate(waterDetail);
            }

            // ② 删掉那个不生效的 Thalassophobia 焦散方向光
            GameObject caustics = GameObject.Find(kCausticsGo);
            if (caustics != null && caustics.GetComponent<Light>() != null)
                Object.DestroyImmediate(caustics);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // ③ 用项目自带的 UnderwaterCeiling 重建头顶水面（含焦散亮线 + 裙边）
            PAO.Underwater.EditorTools.UnderwaterCeilingSetup.BuildCurrentScene();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Probe] wengjie 水面已换成焦散亮线版本：删掉 " + removedBlocks +
                      " 块 SimpleLit 平面 + 1 个失效焦散方向光。");
        }
        // ------------------------------------------------------------------
        // 地面焦散：相机前方全屏 Quad + PAO/CausticsScreen（加法叠加）
        // ------------------------------------------------------------------
        [MenuItem("Tools/PAO/Probe/Add Ground Caustics", priority = 141)]
        public static void AddGroundCaustics()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Probe] Play 模式里不能改场景，先退出 Play。");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            Material mat = EnsureCausticsScreenMaterial();
            if (mat == null) return;

            // 水面高度：优先取 UnderwaterCeiling 顶盖的 Y
            float waterY = 86f;
            GameObject ceiling = GameObject.Find("Water_Ceiling");
            if (ceiling != null)
            {
                var uc = ceiling.GetComponent<PAO.Underwater.UnderwaterCeiling>();
                if (uc != null) waterY = ceiling.transform.position.y;
            }
            mat.SetFloat("_WaterY", waterY);

            // 相机前方的全屏 Quad
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[Probe] 找不到 Main Camera。");
                return;
            }

            Transform old = cam.transform.Find("PAO_CausticsScreen");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "PAO_CausticsScreen";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(cam.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 1.5f);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(8f, 8f, 1f);

            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Probe] 地面焦散已挂到相机前方：水面 Y=" + waterY + "，材质=" +
                      AssetDatabase.GetAssetPath(mat));
        }

        private static Material EnsureCausticsScreenMaterial()
        {
            const string matPath = "Assets/GameMain/Art/Materials/M_PAOCausticsScreen.mat";
            AssetDatabase.Refresh();

            Shader shader = Shader.Find("PAO/CausticsScreen");
            if (shader == null)
            {
                Debug.LogError("[Probe] 找不到 shader PAO/CausticsScreen —— 先看 Console 里的 shader 编译错误。");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "M_PAOCausticsScreen" };
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[Probe] 新建材质 " + matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }

            return mat;
        }

        // ------------------------------------------------------------------
        // 一键：水面顶盖 + 地面焦散
        // ------------------------------------------------------------------
        [MenuItem("Tools/PAO/Probe/Fix All Caustics", priority = 139)]
        public static void FixAll()
        {
            FixWengjieWaterSurface();
            AddGroundCaustics();
            Debug.Log("[Probe] 焦散一套（水面亮线 + 地面亮线）已落盘。");
        }

        // ------------------------------------------------------------------
        // 角色补光：玩家预制体挂一盏只照 Player 层的相机补光灯
        // ------------------------------------------------------------------
        private const string kPlayerPrefabPath = "Assets/GameMain/Art/Models/takoyaki.prefab";
        private const string kPlayerLayerName  = "Player";
        private const string kFillLightName    = "CharacterFillLight";

        [MenuItem("Tools/PAO/Probe/Setup Character Fill Light", priority = 150)]
        public static void SetupCharacterFillLight()
        {
            int layer = EnsurePlayerLayer();
            if (layer < 0)
            {
                Debug.LogError("[FillLight] 找不到/创建不了 Player 层。");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(kPlayerPrefabPath);
            if (root == null)
            {
                Debug.LogError("[FillLight] 打不开预制体 " + kPlayerPrefabPath);
                return;
            }

            try
            {
                // ① 玩家渲染器放进 Player 层
                int placed = 0;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    r.gameObject.layer = layer;
                    placed++;
                }

                // ② 相机下方挂补光灯（挂到 Main Camera 上：灯在镜头位置，
                //    永远从"玩家看得见的那一侧"打过去，所以任何角度都有补光）
                Transform pivot = FindDeepTransform(root.transform, "CameraPivot");
                if (pivot == null)
                {
                    Debug.LogError("[FillLight] 预制体里找不到 CameraPivot。");
                    return;
                }

                Transform anchor = FindDeepTransform(pivot, "Main Camera");
                if (anchor == null) anchor = pivot;

                // 先清掉整个预制体里所有旧的补光灯（包括前几版留在 CameraPivot 下的），
                // 否则会叠两盏，角色头顶被照出一个高光热点。
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name == kFillLightName)
                        Object.DestroyImmediate(t.gameObject);
                }

                var lightGo = new GameObject(kFillLightName);
                lightGo.transform.SetParent(anchor, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.5f, -1.0f);
                lightGo.transform.localRotation = Quaternion.identity;

                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1.0f, 0.95f, 0.89f);
                light.intensity = 1.8f;
                light.range = 30f;
                light.shadows = LightShadows.None;
                light.cullingMask = 1 << layer;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.bounceIntensity = 0f;
                light.renderMode = LightRenderMode.ForcePixel;

                PrefabUtility.SaveAsPrefabAsset(root, kPlayerPrefabPath);
                Debug.Log("[FillLight] 玩家预制体已更新：Player 层=" + layer +
                          "，补光=" + kFillLightName + "（点光 2.0 / 范围 25 / 只照 Player；渲染器 " +
                          placed + " 个已进层）。");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int EnsurePlayerLayer()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;

            var so = new SerializedObject(assets[0]);
            SerializedProperty layers = so.FindProperty("layers");
            if (layers == null) return -1;

            for (int i = 6; i < layers.arraySize; i++)
            {
                SerializedProperty sp = layers.GetArrayElementAtIndex(i);
                if (sp.stringValue == kPlayerLayerName) return i;
            }
            for (int i = 6; i < layers.arraySize; i++)
            {
                SerializedProperty sp = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue))
                {
                    sp.stringValue = kPlayerLayerName;
                    so.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    return i;
                }
            }
            return -1;
        }

        private static Transform FindDeepTransform(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
        // ------------------------------------------------------------------
        // Play 状态体检：查“角色不能动”这类问题（在 Play 中点这个菜单）
        // ------------------------------------------------------------------
        [MenuItem("Tools/PAO/Probe/Play State", priority = 903)]
        public static void PlayState()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Probe] Play 状态体检 " + System.DateTime.Now.ToString("HH:mm:ss"));
            sb.AppendLine("  isPlaying = " + EditorApplication.isPlaying +
                          "，paused(编辑器) = " + EditorApplication.isPaused +
                          "，isFocused = " + Application.isFocused);
            sb.AppendLine("  Time.timeScale = " + Time.timeScale +
                          "，frameCount = " + Time.frameCount +
                          "，unscaledTime = " + Time.unscaledTime.ToString("F2"));
            sb.AppendLine("  Cursor.lockState = " + Cursor.lockState +
                          "，visible = " + Cursor.visible);

            var runtime = PAO.UI.V2.UIRuntime.Instance;
            sb.AppendLine("  UIRuntime = " + (runtime != null ? "存在" : "null"));
            if (runtime != null)
            {
                sb.AppendLine("  IsBlockingGameplay = " + runtime.IsBlockingGameplay +
                              "，BlocksInputThisFrame = " + runtime.BlocksInputThisFrame);
            }

            PlayerController player = Object.FindObjectOfType<PlayerController>();
            sb.AppendLine("  PlayerController = " + (player != null ? "存在" : "null"));
            if (player != null)
            {
                sb.AppendLine("    enabled = " + player.enabled +
                              "，activeAndEnabled = " + player.isActiveAndEnabled +
                              "，pos = " + player.transform.position +
                              "，ReleaseCursorForMenus = " + player.ReleaseCursorForMenus +
                              "，CursorWanted = " + player.CursorWanted);
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null)
                    sb.AppendLine("    CharacterController.enabled = " + cc.enabled +
                                  "，isGrounded = " + cc.isGrounded + "，velocity = " + cc.velocity);
            }

            int canvases = 0;
            foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
                if (c.isActiveAndEnabled && c.gameObject.activeInHierarchy) canvases++;
            sb.AppendLine("  激活 Canvas 数 = " + canvases);

            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------
        // Play 状态体检结束
        // ------------------------------------------------------------------
    }
}
