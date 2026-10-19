#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PAO.Sandbox
{
    /// <summary>On-demand physics and render regression checks, isolated from the player's creations and saves.</summary>
    public sealed class CameraOcclusionVerification : MonoBehaviour
    {
        private readonly List<string> results = new List<string>();
        private readonly List<string> errors = new List<string>();
        private readonly List<Material> materials = new List<Material>();
        private PlayerController player;
        private SandboxBrush brush;
        private bool playerEnabled, brushEnabled;
        private GameObject fixture;
        private Camera view;
        private const int Layer = 30;
        private const int Mask = 1 << Layer;
        private string output;

        [MenuItem("PAO/Creat/验证镜头粒子遮挡")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode before camera verification.");
            if (FindFirstObjectByType<CameraOcclusionVerification>() == null)
                new GameObject("Camera occlusion verification").AddComponent<CameraOcclusionVerification>();
        }

        private void Check(bool pass, string message)
        {
            results.Add((pass ? "PASS " : "FAIL ") + message);
            Debug.Log("[Camera particles] " + results[results.Count - 1]);
        }

        private void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        }

        private IEnumerator Start()
        {
            output = Path.GetFullPath("output/CameraOcclusionValidation");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
            player = FindFirstObjectByType<PlayerController>();
            brush = FindFirstObjectByType<SandboxBrush>();
            playerEnabled = player != null && player.enabled;
            brushEnabled = brush != null && brush.enabled;
            if (player != null) player.enabled = false;
            if (brush != null) brush.enabled = false;
            fixture = new GameObject("Isolated camera particle fixtures");
            Vector3 basis = new Vector3(10000, 10000, 10000);
            fixture.transform.position = basis;
            var cameraObject = new GameObject("Verification camera", typeof(Camera));
            cameraObject.transform.SetParent(fixture.transform);
            view = cameraObject.GetComponent<Camera>(); view.enabled = false;
            view.transform.position = basis + new Vector3(0, 2, -6);
            view.transform.rotation = Quaternion.identity;
            view.cullingMask = Mask; view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.025f, .06f, .08f); view.farClipPlane = 30;
            view.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            Vector3 focus = basis + new Vector3(0, 2, 0);
            GameObject subject = Solid("Visible subject", focus, Vector3.one * 1.3f, new Color(1, .24f, .035f), false);
            GameObject wall = Solid("Occluding wall", basis + new Vector3(0, 2, -3), new Vector3(5, 4, .5f), new Color(.16f, .33f, .36f));
            var effect = wall.GetComponent<CameraOcclusionParticles>();
            Material original = wall.GetComponent<Renderer>().sharedMaterial;
            Physics.SyncTransforms();
            Color blocked = Capture("01-solid-wall.png");
            yield return FadeAt(focus, .3f);
            Check(effect.Fade > .95f, "Wall between camera and focus dissolves into particles.");
            Check(wall.GetComponent<Collider>().enabled && Physics.Raycast(view.transform.position, Vector3.forward, 4, Mask),
                "Camera fade preserves solid wall physics.");
            Check(wall.GetComponent<Renderer>().sharedMaterial.shader.name == "PAO/CameraOcclusionParticles" &&
                wall.GetComponent<Renderer>().sharedMaterial.renderQueue == 3000,
                "Faded wall uses the transparent particle pass without writing an opaque depth mask.");
            Color revealed = Capture("02-particle-wall.png");
            Check(revealed.r > blocked.r + .12f && revealed.r > revealed.b + .3f,
                "Rendered subject is visible through the particle wall with its actual orange color.");

            // The camera/focus are unchanged; removing the obstacle must restore its exact material.
            wall.transform.position += Vector3.right * 10; Physics.SyncTransforms();
            yield return FadeAt(focus, .65f);
            Check(effect.Fade == 0 && wall.GetComponent<Renderer>().sharedMaterial == original,
                "Leaving the sightline restores the original material and shadow settings.");

            wall.transform.position = view.transform.position; Physics.SyncTransforms();
            yield return FadeAt(focus, .3f);
            Check(effect.Fade > .95f, "Camera already inside a wall is handled by overlap detection.");
            wall.transform.position += Vector3.right * 10; Physics.SyncTransforms();
            yield return FadeAt(focus, .65f);
            wall.transform.position = basis + new Vector3(0, 2, -3); wall.GetComponent<Collider>().isTrigger = true;
            Physics.SyncTransforms(); yield return FadeAt(focus, .3f);
            Check(effect.Fade == 0, "Trigger volumes do not turn into particles.");
            wall.GetComponent<Collider>().isTrigger = false;
            Physics.SyncTransforms();
            yield return FadeAt(focus, .3f, 0);
            Check(effect.Fade == 0, "Camera collision layer mask is respected.");
            wall.SetActive(false);

            // Reproduce the screenshot's low-angle floor/foundation occlusion, including two overlapping solids.
            GameObject floor = Solid("Floor", basis, new Vector3(12, .4f, 12), new Color(.16f, .33f, .36f));
            GameObject foundation = Solid("Foundation", basis + Vector3.down * .65f, new Vector3(13, 1, 13), new Color(.06f, .17f, .21f));
            view.transform.position = basis + new Vector3(0, -2, -6);
            view.transform.LookAt(focus); Physics.SyncTransforms();
            yield return FadeAt(focus, .3f);
            Check(floor.GetComponent<CameraOcclusionParticles>().Fade > .95f && foundation.GetComponent<CameraOcclusionParticles>().Fade > .95f,
                "Low camera angle dissolves both floor and foundation, including layered occluders.");
            Capture("03-low-angle-floor.png");
            floor.GetComponent<CameraOcclusionParticles>().enabled = false;
            Check(floor.GetComponent<Renderer>().sharedMaterial.shader.name != "PAO/CameraOcclusionParticles",
                "Disabling the effect immediately restores the solid material.");
            CameraOcclusionParticles.ReleaseAll();
            Check(foundation.GetComponent<CameraOcclusionParticles>().Fade == 0, "Scene cleanup releases all fading materials.");
            yield return VerifySandboxView();
            Check(ShaderUtil.GetShaderMessages(Resources.Load<Shader>("CameraOcclusionParticles")).Length == 0,
                "Particle shader compiles without diagnostics.");
            Check(errors.Count == 0, "Verification completes without Unity errors.");
            File.WriteAllText(Path.Combine(output, "results.txt"), string.Join("\n", results) + "\n" + string.Join("\n", errors));
            Destroy(gameObject);
        }

        private IEnumerator VerifySandboxView()
        {
            if (player == null || BubbleSandbox.Instance == null) yield break;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(PlayerController);
            Transform pivot = (Transform)type.GetField("m_CameraPivot", flags).GetValue(player);
            Transform cameraTransform = (Transform)type.GetField("m_CameraTransform", flags).GetValue(player);
            Camera actual = cameraTransform.GetComponent<Camera>();
            Vector3 pivotPosition = pivot.position, localPosition = cameraTransform.localPosition;
            Vector3 playerPosition = player.transform.position;
            CharacterController controller = player.GetComponent<CharacterController>();
            bool controllerEnabled = controller != null && controller.enabled;
            Quaternion pivotRotation = pivot.rotation;
            float currentDistance = (float)type.GetField("m_CurrentCameraDistance", flags).GetValue(player);
            bool originalMode = player.UseOcclusionParticles;
            Camera fixtureView = view;
            try
            {
                float height = (float)type.GetField("m_PivotHeight", flags).GetValue(player);
                float distance = (float)type.GetField("m_CameraDistance", flags).GetValue(player);
                // Earlier construction checks may leave the player above a creation. Ground this
                // fixture independently so its low-angle ray always crosses the real floor.
                if (controller != null) controller.enabled = false;
                player.transform.position = BubbleSandbox.Instance.spawn + Vector3.up *
                    (controller != null ? (controller.height * .5f - controller.center.y) * player.transform.lossyScale.y : 0);
                pivot.position = player.transform.position + Vector3.up * height;
                pivot.rotation = Quaternion.Euler(-32, 0, 0);
                cameraTransform.localPosition = new Vector3(0, 0, -distance);
                type.GetField("m_CurrentCameraDistance", flags).SetValue(player, distance);
                Physics.SyncTransforms();
                view = actual;
                Capture("04-creat-before.png");
                player.UseOcclusionParticles = true;
                MethodInfo update = type.GetMethod("UpdateCameraDistance", flags);
                float until = Time.realtimeSinceStartup + .4f;
                do { update.Invoke(player, null); yield return null; } while (Time.realtimeSinceStartup < until);
                Capture("05-creat-particles.png");
                int dissolved = 0;
                foreach (CameraOcclusionParticles effect in BubbleSandbox.Instance.GetComponentsInChildren<CameraOcclusionParticles>())
                    if (effect.Fade > .95f) dissolved++;
                Check(dissolved >= 2, "Actual Creat player camera dissolves the obstructing floor and foundation at a low angle.");
            }
            finally
            {
                CameraOcclusionParticles.ReleaseAll();
                player.UseOcclusionParticles = originalMode;
                player.transform.position = playerPosition;
                pivot.SetPositionAndRotation(pivotPosition, pivotRotation);
                cameraTransform.localPosition = localPosition;
                type.GetField("m_CurrentCameraDistance", flags).SetValue(player, currentDistance);
                if (controller != null) controller.enabled = controllerEnabled;
                Physics.SyncTransforms();
                view = fixtureView;
            }
        }

        private IEnumerator FadeAt(Vector3 focus, float seconds, int mask = Mask)
        {
            float until = Time.realtimeSinceStartup + seconds;
            do
            {
                CameraOcclusionParticles.Prepare(focus, view.transform.position, .4f, mask,
                    player != null ? player.transform : transform, null);
                yield return null;
            } while (Time.realtimeSinceStartup < until);
        }

        private GameObject Solid(string name, Vector3 position, Vector3 scale, Color color, bool fade = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.layer = Layer; go.transform.SetParent(fixture.transform);
            go.transform.position = position; go.transform.localScale = scale;
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = color; materials.Add(material);
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (fade) CameraOcclusionParticles.Attach(go);
            return go;
        }

        private Color Capture(string name)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = view.targetTexture;
            RenderTexture render = RenderTexture.GetTemporary(960, 540, 24);
            view.targetTexture = render; view.Render(); RenderTexture.active = render;
            var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply();
            Color center = texture.GetPixel(480, 270);
            File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
            view.targetTexture = previousTarget; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(render); Destroy(texture);
            return center;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            CameraOcclusionParticles.ReleaseAll();
            if (fixture != null) Destroy(fixture);
            foreach (Material material in materials) if (material != null) Destroy(material);
            if (player != null) player.enabled = playerEnabled;
            if (brush != null) brush.enabled = brushEnabled;
        }
    }
}
#endif
