using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 机关交互的一键搭建工具。
    ///
    /// 【最快试玩】
    ///   菜单 PAO → 关卡 → 机关 → 打开或生成演示场景，然后按 Play。
    ///   演示场景里已经放好玩家、拉杆、门和门边镜头，靠近拉杆按 E 就能看完整流程。
    ///
    /// 【接到自己的模型上】
    ///   不用跑这个工具。按《机关交互使用说明》里的层级摆好，
    ///   把 Lever Interactable / Mechanism Door 拖到对应物体上即可。
    /// </summary>
    public static class MechanismSetupTools
    {
        public const string DemoScenePath = "Assets/GameMain/Scenes/MechanismDemo.unity";
        private const string MaterialFolder = "Assets/GameMain/Art/Materials/MechanismDemo";

        [MenuItem("PAO/关卡/机关/打开或生成演示场景", false, 60)]
        public static void OpenOrCreateDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) != null)
            {
                EditorSceneManager.OpenScene(DemoScenePath);
                return;
            }

            BuildDemo();
        }

        [MenuItem("PAO/关卡/机关/重新生成演示场景", false, 61)]
        public static void RebuildDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!Application.isBatchMode &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) != null &&
                !EditorUtility.DisplayDialog(
                    "重新生成演示场景",
                    "会覆盖 " + DemoScenePath + "。\n\n你自己在演示场景里改的东西会丢失，确定吗？",
                    "覆盖",
                    "取消"))
            {
                return;
            }

            BuildDemo();
        }

        [MenuItem("PAO/关卡/机关/为选中的门创建门边镜头", false, 62)]
        public static void CreateShotForSelectedDoor()
        {
            GameObject selected = Selection.activeGameObject;

            if (selected == null)
            {
                EditorUtility.DisplayDialog("创建门边镜头", "先在 Hierarchy 里选中一扇门（挂着 Mechanism Door 的物体）。", "好");
                return;
            }

            MechanismDoor door = selected.GetComponentInParent<MechanismDoor>();
            if (door == null)
            {
                door = selected.GetComponentInChildren<MechanismDoor>();
            }

            if (door == null)
            {
                EditorUtility.DisplayDialog("创建门边镜头", "选中的物体上没有找到 Mechanism Door 组件。", "好");
                return;
            }

            Bounds bounds = CalculateBounds(door.gameObject);
            Vector3 center = bounds.center;

            // 从门正面退开，再稍微偏到一侧，构图比正对着门更像游戏镜头。
            float distance = Mathf.Max(3.5f, bounds.extents.magnitude * 2.4f);
            Vector3 position = center
                               - door.transform.forward * distance
                               + door.transform.right * 1.8f
                               + Vector3.up * Mathf.Max(0.8f, bounds.extents.y * 0.7f);

            GameObject cameraObject = new GameObject("Door Shot Camera", typeof(Camera));
            Undo.RegisterCreatedObjectUndo(cameraObject, "Create door shot camera");
            cameraObject.transform.position = position;
            cameraObject.transform.LookAt(center + Vector3.up * 0.25f);

            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.07f, 0.12f, 1f);
            AddUniversalCameraData(cameraObject);

            GameObject lookTarget = new GameObject("Door Look Target");
            Undo.RegisterCreatedObjectUndo(lookTarget, "Create door look target");
            lookTarget.transform.position = center + Vector3.up * 0.25f;

            GameObject rig = new GameObject("Door Camera Shot");
            Undo.RegisterCreatedObjectUndo(rig, "Create door camera shot");
            MechanismCameraShot shot = rig.AddComponent<MechanismCameraShot>();

            SerializedObject shotProperties = new SerializedObject(shot);
            SetObject(shotProperties, "m_ShotCamera", camera);
            SetObject(shotProperties, "m_LookAt", lookTarget.transform);
            shotProperties.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject doorProperties = new SerializedObject(door);
            SetObject(doorProperties, "m_OpenCameraShot", shot);
            doorProperties.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(door.gameObject.scene);
            Selection.activeGameObject = rig;
        }

        /// <summary>批处理入口：生成演示场景后退出。只给命令行用。</summary>
        public static void BuildDemoForBatch()
        {
            try
            {
                BuildDemo();
                AssetDatabase.SaveAssets();
                Debug.Log("[MechanismSetup] Demo scene generated: " + DemoScenePath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        public static void BuildDemo()
        {
            EnsureFolder(MaterialFolder);

            Material floorMaterial = GetOrCreateMaterial("M_MechanismFloor", new Color(0.055f, 0.09f, 0.11f), 0.05f, 0.25f);
            Material metalMaterial = GetOrCreateMaterial("M_MechanismMetal", new Color(0.32f, 0.36f, 0.39f), 0.75f, 0.55f);
            Material darkMetalMaterial = GetOrCreateMaterial("M_MechanismDarkMetal", new Color(0.12f, 0.15f, 0.17f), 0.7f, 0.4f);
            Material accentMaterial = GetOrCreateMaterial("M_MechanismAccent", new Color(0.95f, 0.52f, 0.18f), 0.25f, 0.5f);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.23f, 0.28f, 0.32f, 1f);

            GameObject ground = CreatePrimitive(
                PrimitiveType.Plane,
                "Ground",
                null,
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 1f, 4f),
                floorMaterial,
                Quaternion.identity);

            ground.isStatic = true;

            GameObject sunObject = new GameObject("Directional Light", typeof(Light));
            sunObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.92f, 0.82f, 1f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;

            // ---------- 玩家 ----------
            GameObject player = new GameObject("Player", typeof(CharacterController), typeof(PlayerController));
            player.transform.position = new Vector3(0f, 1f, -4.5f);
            player.tag = "Player";

            CharacterController character = player.GetComponent<CharacterController>();
            character.height = 1.8f;
            character.radius = 0.35f;
            character.center = Vector3.zero;
            character.skinWidth = 0.04f;

            GameObject playerVisual = CreatePrimitive(
                PrimitiveType.Capsule,
                "Player Visual",
                player.transform,
                Vector3.zero,
                new Vector3(0.8f, 0.9f, 0.8f),
                accentMaterial,
                Quaternion.identity);
            RemoveCollider(playerVisual);

            GameObject cameraPivot = new GameObject("CameraPivot");
            cameraPivot.transform.SetParent(player.transform, false);
            cameraPivot.transform.localPosition = new Vector3(0f, 1.5f, 0f);

            GameObject playerCameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            playerCameraObject.tag = "MainCamera";
            playerCameraObject.transform.SetParent(cameraPivot.transform, false);
            playerCameraObject.transform.localPosition = new Vector3(0f, 0f, -4.5f);

            Camera playerCamera = playerCameraObject.GetComponent<Camera>();
            playerCamera.fieldOfView = 55f;
            playerCamera.nearClipPlane = 0.05f;
            playerCamera.farClipPlane = 200f;
            playerCamera.clearFlags = CameraClearFlags.SolidColor;
            playerCamera.backgroundColor = new Color(0.025f, 0.07f, 0.12f, 1f);
            AddUniversalCameraData(playerCameraObject);

            PlayerController controller = player.GetComponent<PlayerController>();
            SerializedObject controllerProperties = new SerializedObject(controller);
            SetObject(controllerProperties, "m_CameraPivot", cameraPivot.transform);
            SetObject(controllerProperties, "m_CameraTransform", playerCameraObject.transform);
            controllerProperties.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 拉杆 ----------
            GameObject lever = new GameObject("Lever");
            lever.transform.position = new Vector3(2.2f, 0f, 0.5f);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Pedestal",
                lever.transform,
                new Vector3(0f, 0.6f, 0f),
                new Vector3(1.2f, 1.2f, 0.6f),
                darkMetalMaterial,
                Quaternion.identity);

            CreatePrimitive(
                PrimitiveType.Cylinder,
                "Gear",
                lever.transform,
                new Vector3(0f, 1.2f, 0f),
                new Vector3(0.95f, 0.12f, 0.95f),
                accentMaterial,
                Quaternion.Euler(90f, 0f, 0f));

            GameObject hinge = CreateEmpty("Hinge", lever.transform, new Vector3(0f, 1.2f, 0f));

            // 手柄用一个空物体当“旋转根”，模型都挂在它下面。
            // 这样手柄的缩放不会把子物体（配重球）拉变形。
            GameObject handlePivot = CreateEmpty("Handle", hinge.transform, Vector3.zero);
            handlePivot.transform.localRotation = Quaternion.Euler(0f, 0f, -25f);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Handle Bar",
                handlePivot.transform,
                new Vector3(0f, 0.75f, 0f),
                new Vector3(0.16f, 1.5f, 0.16f),
                metalMaterial,
                Quaternion.identity);

            CreatePrimitive(
                PrimitiveType.Sphere,
                "Handle Knob",
                handlePivot.transform,
                new Vector3(0f, 1.5f, 0f),
                new Vector3(0.3f, 0.3f, 0.3f),
                accentMaterial,
                Quaternion.identity);

            GameObject promptAnchor = CreateEmpty("PromptAnchor", lever.transform, new Vector3(0f, 2.55f, 0f));

            GameObject leverLightObject = new GameObject("Lever Light", typeof(Light));
            leverLightObject.transform.position = new Vector3(2.2f, 2.4f, 0.5f);
            Light leverLight = leverLightObject.GetComponent<Light>();
            leverLight.type = LightType.Point;
            leverLight.color = new Color(1f, 0.72f, 0.38f, 1f);
            leverLight.intensity = 2.5f;
            leverLight.range = 6f;

            // ---------- 门 ----------
            GameObject doorRoot = new GameObject("Door");
            doorRoot.transform.position = new Vector3(5.6f, 0f, 3.2f);

            GameObject doorHinge = CreateEmpty("Hinge", doorRoot.transform, Vector3.zero);

            // 门板同样用一个空物体当旋转根，门板和门把手都挂在它下面。
            GameObject doorLeaf = CreateEmpty("Door Leaf", doorHinge.transform, Vector3.zero);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Door Panel",
                doorLeaf.transform,
                new Vector3(1.1f, 1.5f, 0f),
                new Vector3(2.2f, 3f, 0.18f),
                metalMaterial,
                Quaternion.identity);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Door Handle",
                doorLeaf.transform,
                new Vector3(1.82f, 1.5f, -0.14f),
                new Vector3(0.08f, 0.55f, 0.08f),
                accentMaterial,
                Quaternion.identity);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Door Frame Left",
                doorRoot.transform,
                new Vector3(0f, 1.55f, -0.06f),
                new Vector3(0.18f, 3.1f, 0.28f),
                darkMetalMaterial,
                Quaternion.identity);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Door Frame Right",
                doorRoot.transform,
                new Vector3(2.2f, 1.55f, -0.06f),
                new Vector3(0.18f, 3.1f, 0.28f),
                darkMetalMaterial,
                Quaternion.identity);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Door Frame Top",
                doorRoot.transform,
                new Vector3(1.1f, 3.12f, -0.06f),
                new Vector3(2.56f, 0.2f, 0.28f),
                darkMetalMaterial,
                Quaternion.identity);

            // ---------- 门边镜头 ----------
            Vector3 doorCenter = new Vector3(6.7f, 1.5f, 3.2f);

            GameObject shotCameraObject = new GameObject("Door Shot Camera", typeof(Camera));
            shotCameraObject.transform.position = new Vector3(3.2f, 2.6f, -1.2f);
            shotCameraObject.transform.LookAt(doorCenter + Vector3.up * 0.15f);

            Camera shotCamera = shotCameraObject.GetComponent<Camera>();
            shotCamera.enabled = false;
            shotCamera.fieldOfView = 45f;
            shotCamera.nearClipPlane = 0.05f;
            shotCamera.farClipPlane = 200f;
            shotCamera.clearFlags = CameraClearFlags.SolidColor;
            shotCamera.backgroundColor = new Color(0.025f, 0.07f, 0.12f, 1f);
            AddUniversalCameraData(shotCameraObject);

            GameObject lookTarget = new GameObject("Door Look Target");
            lookTarget.transform.position = doorCenter + Vector3.up * 0.15f;

            GameObject shotRig = new GameObject("Door Camera Shot");
            MechanismCameraShot shot = shotRig.AddComponent<MechanismCameraShot>();

            SerializedObject shotProperties = new SerializedObject(shot);
            SetObject(shotProperties, "m_ShotCamera", shotCamera);
            SetObject(shotProperties, "m_LookAt", lookTarget.transform);
            SetFloat(shotProperties, "m_Duration", 3.5f);
            SetBool(shotProperties, "m_ReturnToGameplay", true);
            SetBool(shotProperties, "m_LockPlayerControl", true);
            SetInt(shotProperties, "m_DepthBoost", 20);
            shotProperties.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 组件参数 ----------
            MechanismDoor door = doorRoot.AddComponent<MechanismDoor>();
            SerializedObject doorProperties = new SerializedObject(door);
            SetObject(doorProperties, "m_MovingPart", doorLeaf.transform);
            SetObject(doorProperties, "m_Hinge", doorHinge.transform);
            SetEnum(doorProperties, "m_Motion", 0);
            SetVector(doorProperties, "m_RotateAxis", Vector3.up);
            SetFloat(doorProperties, "m_OpenAngle", 100f);
            SetFloat(doorProperties, "m_Delay", 0.5f);
            SetFloat(doorProperties, "m_Duration", 1.2f);
            SetBool(doorProperties, "m_StartOpen", false);
            SetObject(doorProperties, "m_OpenCameraShot", shot);
            doorProperties.ApplyModifiedPropertiesWithoutUndo();

            LeverInteractable leverInteractable = lever.AddComponent<LeverInteractable>();
            SerializedObject leverProperties = new SerializedObject(leverInteractable);
            SetFloat(leverProperties, "m_InteractDistance", 3.2f);
            SetObject(leverProperties, "m_PromptAnchor", promptAnchor.transform);
            SetObject(leverProperties, "m_RotatingPart", handlePivot.transform);
            SetObject(leverProperties, "m_Hinge", hinge.transform);
            SetVector(leverProperties, "m_Axis", Vector3.forward);
            SetFloat(leverProperties, "m_Angle", 60f);
            SetFloat(leverProperties, "m_Duration", 0.35f);
            SetBool(leverProperties, "m_ToggleBack", false);
            SetObject(leverProperties, "m_TargetDoor", door);
            leverProperties.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, DemoScenePath);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = lever;
            Debug.Log("[MechanismSetup] 演示场景已生成：" + DemoScenePath +
                      "。按 Play，WASD 走到拉杆旁按 E。");
        }

        private static Bounds CalculateBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(target.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Quaternion localRotation)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            if (material != null)
            {
                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = material;
                }
            }

            return go;
        }

        private static GameObject CreateEmpty(string name, Transform parent, Vector3 localPosition)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go;
        }

        private static void RemoveCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static void AddUniversalCameraData(GameObject cameraObject)
        {
            UniversalAdditionalCameraData data = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            if (data == null)
            {
                data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            }

            data.renderType = CameraRenderType.Base;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }

            parent = parent.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Material GetOrCreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader == null)
                {
                    Debug.LogError("[MechanismSetup] 找不到 URP Lit / Standard 着色器，演示场景会用默认材质。");
                    return null;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetObject(SerializedObject target, string field, UnityEngine.Object value)
        {
            SerializedProperty property = target.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("[MechanismSetup] 找不到字段：" + field);
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject target, string field, float value)
        {
            SerializedProperty property = target.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("[MechanismSetup] 找不到字段：" + field);
                return;
            }

            property.floatValue = value;
        }

        private static void SetBool(SerializedObject target, string field, bool value)
        {
            SerializedProperty property = target.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("[MechanismSetup] 找不到字段：" + field);
                return;
            }

            property.boolValue = value;
        }

        private static void SetInt(SerializedObject target, string field, int value)
        {
            SerializedProperty property = target.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("[MechanismSetup] 找不到字段：" + field);
                return;
            }

            property.intValue = value;
        }

        private static void SetEnum(SerializedObject target, string field, int value)
        {
            SerializedProperty property = target.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("[MechanismSetup] 找不到字段：" + field);
                return;
            }

            property.enumValueIndex = value;
        }

        private static void SetVector(SerializedObject target, string field, Vector3 value)
        {
            SerializedProperty property = target.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("[MechanismSetup] 找不到字段：" + field);
                return;
            }

            property.vector3Value = value;
        }
    }
}
