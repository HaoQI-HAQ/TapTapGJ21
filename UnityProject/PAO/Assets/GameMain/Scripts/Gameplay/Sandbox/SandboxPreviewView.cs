using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace PAO.Sandbox
{
    /// <summary>A materialled snapshot in its own scene, with an orbit camera independent of gameplay physics.</summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class SandboxPreviewView : MonoBehaviour, IDragHandler, IScrollHandler
    {
        private static readonly Vector3 StageOrigin = new Vector3(10000, 10000, 10000);
        private const int PreviewLayer = 30;
        private readonly List<Material> materials = new List<Material>();
        private Scene scene;
        private GameObject stage, ground;
        private Mesh groundMesh, fogMesh;
        private Material fogMaterial;
        private RawImage surface;
        private RenderTexture target;
        private Vector3 pivot;
        private float yaw = 35, pitch = 24, distance;
        private float insetTop, insetBottom;
        private bool disposed, autoFrame;
        public Camera ViewCamera { get; private set; }
        public Bounds ContentBounds { get; private set; }
        public int ObjectCount { get; private set; }
        public bool GridVisible => ground != null && ground.activeSelf;
        public float Distance => distance;
        public float Yaw => yaw;
        public float Pitch => pitch;
        public Transform Objects { get; private set; }

        public void Initialize(BubbleSandbox world)
        {
            surface = GetComponent<RawImage>();
            surface.color = Color.white;
            scene = SceneManager.CreateScene("Creat independent preview " + GetInstanceID());
            stage = new GameObject("Creation preview stage");
            SceneManager.MoveGameObjectToScene(stage, scene);
            stage.transform.position = StageOrigin;
            Objects = new GameObject("Actual creations only").transform;
            Objects.SetParent(stage.transform, false);
            SandboxPiece[] pieces = world.GetComponentsInChildren<SandboxPiece>();
            bool hasBounds = false;
            foreach (SandboxPiece piece in pieces)
            {
                if (!piece.gameObject.activeInHierarchy) continue;
                Bounds cell = new Bounds(world.CellCenter(piece.Data.position), Vector3.one * world.cellSize);
                if (!hasBounds) { ContentBounds = cell; hasBounds = true; }
                else { Bounds bounds = ContentBounds; bounds.Encapsulate(cell); ContentBounds = bounds; }
            }
            if (!hasBounds) ContentBounds = new Bounds(world.CellCenter(Vector3Int.zero), Vector3.one * world.cellSize * 2);
            Shader shader = Resources.Load<Shader>("SandboxPreviewSurface");
            var paints = new Dictionary<Color, Material>();
            foreach (SandboxPiece piece in pieces)
            {
                if (!piece.gameObject.activeInHierarchy) continue;
                MeshFilter source = piece.GetComponent<MeshFilter>();
                if (source == null || source.sharedMesh == null) continue;
                Color paint = piece.Data.PaintColor;
                // Alpha distinguishes pearl and ceramic in the cache; all surfaces remain opaque.
                Color key = paint; key.a = piece.Data.shape == SandboxShape.Bubble ? 1 : 0;
                if (!paints.TryGetValue(key, out Material material))
                {
                    material = MakeMaterial(shader, paint, piece.Data.shape == SandboxShape.Bubble ? 1 : 0);
                    paints.Add(key, material);
                }
                var copy = new GameObject("Preview " + piece.Data.position, typeof(MeshFilter), typeof(MeshRenderer));
                copy.layer = PreviewLayer; copy.transform.SetParent(Objects, false);
                copy.transform.localPosition = world.CellCenter(piece.Data.position) - ContentBounds.center;
                copy.transform.localScale = Vector3.one * world.cellSize;
                copy.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                copy.GetComponent<MeshRenderer>().sharedMaterial = material;
                ObjectCount++;
            }
            BuildGround(shader);
            var cameraObject = new GameObject("Independent preview camera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);
            ViewCamera = cameraObject.GetComponent<Camera>(); ViewCamera.enabled = false;
            ViewCamera.cullingMask = 1 << PreviewLayer;
            ViewCamera.clearFlags = CameraClearFlags.SolidColor;
            ViewCamera.backgroundColor = new Color(.012f, .015f, .019f, 1);
            ViewCamera.fieldOfView = 40; ViewCamera.nearClipPlane = .03f;
            ViewCamera.farClipPlane = Mathf.Max(500, ContentBounds.size.magnitude * 20);
            ViewCamera.allowHDR = false; ViewCamera.allowMSAA = true;
            var data = ViewCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; data.renderShadows = false;
            data.requiresColorTexture = false; data.requiresDepthTexture = true; data.volumeLayerMask = 0;
            BuildAtmosphere();
            EnsureTarget(); FrameAll(); Render();
        }

        private Material MakeMaterial(Shader shader, Color color, float pearl)
        {
            var material = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit"));
            material.name = pearl > .5f ? "Preview / pearl" : "Preview / ceramic";
            material.SetColor("_BaseColor", color); material.SetFloat("_Pearl", pearl);
            material.enableInstancing = true; materials.Add(material); return material;
        }

        private void BuildGround(Shader shader)
        {
            ground = new GameObject("Preview grid ground", typeof(MeshFilter), typeof(MeshRenderer));
            ground.layer = PreviewLayer; ground.transform.SetParent(stage.transform, false);
            float radius = Mathf.Max(8, ContentBounds.extents.magnitude * 2);
            float y = -ContentBounds.extents.y - .05f;
            groundMesh = new Mesh { name = "Preview ground mesh" };
            groundMesh.vertices = new[] { new Vector3(-radius, y, -radius), new Vector3(-radius, y, radius),
                new Vector3(radius, y, radius), new Vector3(radius, y, -radius) };
            groundMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; groundMesh.RecalculateNormals();
            ground.GetComponent<MeshFilter>().sharedMesh = groundMesh;
            Material material = MakeMaterial(shader, new Color(.025f, .028f, .032f), 0);
            material.SetFloat("_Ground", 1); material.SetFloat("_GroundRadius", radius);
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void BuildAtmosphere()
        {
            Shader shader = Resources.Load<Shader>("SandboxPreviewAtmosphere");
            if (shader == null) return;
            float height = Mathf.Max(2, ContentBounds.size.y);
            float radius = Mathf.Max(8, ContentBounds.extents.magnitude * 2);
            float floor = -ContentBounds.extents.y - .05f;
            Vector3 min = new Vector3(-radius, floor, -radius);
            Vector3 max = new Vector3(radius, floor + height * 1.85f, radius);
            var fog = new GameObject("Preview ground mist and spotlight", typeof(MeshFilter), typeof(MeshRenderer));
            fog.layer = PreviewLayer; fog.transform.SetParent(ground.transform, false);
            fogMesh = new Mesh { name = "Preview atmosphere bounds" };
            fogMesh.vertices = new[] {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z) };
            fogMesh.triangles = new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,4,7, 0,7,3,
                1,2,6, 1,6,5, 3,7,6, 3,6,2, 0,1,5, 0,5,4 };
            fogMesh.RecalculateBounds(); fog.GetComponent<MeshFilter>().sharedMesh = fogMesh;
            fogMaterial = new Material(shader) { name = "Preview / low mist and warm spotlight" };
            fogMaterial.SetVector("_StageOrigin", StageOrigin);
            fogMaterial.SetVector("_BoundsMin", min); fogMaterial.SetVector("_BoundsMax", max);
            fogMaterial.SetFloat("_FloorY", floor); fogMaterial.SetFloat("_ContentHeight", height);
            fogMaterial.SetFloat("_StageRadius", radius);
            materials.Add(fogMaterial);
            MeshRenderer renderer = fog.GetComponent<MeshRenderer>(); renderer.sharedMaterial = fogMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SetFramingInsets(float top, float bottom)
        {
            top = Mathf.Clamp(top, 0, .32f); bottom = Mathf.Clamp(bottom, 0, .32f);
            if (Mathf.Approximately(insetTop, top) && Mathf.Approximately(insetBottom, bottom)) return;
            insetTop = top; insetBottom = bottom;
            if (autoFrame) FrameAll();
        }

        public void FrameAll()
        {
            if (ViewCamera == null) return;
            pivot = Vector3.zero;
            Quaternion inverse = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0));
            float tangent = Mathf.Tan(ViewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            float vertical = tangent * (1 - insetTop - insetBottom);
            float horizontal = tangent * ViewCamera.aspect;
            float fit = 1;
            Vector3 extents = ContentBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = inverse * Vector3.Scale(extents, new Vector3(x, y, z));
                fit = Mathf.Max(fit, Mathf.Abs(point.x) / horizontal - point.z, Mathf.Abs(point.y) / vertical - point.z);
            }
            distance = fit * 1.18f + ViewCamera.nearClipPlane;
            autoFrame = true; UpdateCamera();
        }

        public void SetView(float horizontal, float vertical)
        { yaw = horizontal; pitch = vertical; FrameAll(); }
        public void ToggleGrid() { if (ground != null) ground.SetActive(!ground.activeSelf); }
        public void Orbit(Vector2 delta)
        {
            yaw += delta.x * .35f; pitch = Mathf.Clamp(pitch - delta.y * .35f, -89, 89);
            autoFrame = false; UpdateCamera();
        }
        public void Pan(Vector2 delta)
        {
            if (ViewCamera == null) return;
            float scale = 2 * distance * Mathf.Tan(ViewCamera.fieldOfView * .5f * Mathf.Deg2Rad) /
                Mathf.Max(1, surface.rectTransform.rect.height);
            pivot -= (ViewCamera.transform.right * delta.x + ViewCamera.transform.up * delta.y) * scale;
            autoFrame = false; UpdateCamera();
        }
        public void Zoom(float steps)
        {
            distance = Mathf.Clamp(distance * Mathf.Exp(-steps * .14f), .15f, Mathf.Max(1000, ContentBounds.size.magnitude * 12));
            autoFrame = false; UpdateCamera();
        }
        public void OnDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Middle || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                Pan(data.delta);
            else Orbit(data.delta);
        }
        public void OnScroll(PointerEventData data) { Zoom(data.scrollDelta.y); }
        private void UpdateCamera()
        {
            if (ViewCamera == null) return;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            float offset = (insetTop - insetBottom) * distance * Mathf.Tan(ViewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            Vector3 aim = pivot + rotation * Vector3.up * offset;
            ViewCamera.transform.SetPositionAndRotation(StageOrigin + aim - rotation * Vector3.forward * distance, rotation);
            ViewCamera.farClipPlane = Mathf.Max(500, distance + ContentBounds.size.magnitude * 4);
        }
        private void Update()
        {
            CanvasGroup page = GetComponentInParent<CanvasGroup>();
            if (page != null && !page.interactable) return;
            if (Input.GetKeyDown(KeyCode.F)) FrameAll();
        }
        private void LateUpdate() { if (!disposed && ViewCamera != null) { EnsureTarget(); Render(); } }
        private void EnsureTarget()
        {
            if (surface == null || ViewCamera == null) return;
            Canvas canvas = surface.canvas;
            float scale = canvas != null ? canvas.scaleFactor : 1;
            int width = Mathf.Clamp(Mathf.RoundToInt(surface.rectTransform.rect.width * scale), 64, 2560);
            int height = Mathf.Clamp(Mathf.RoundToInt(surface.rectTransform.rect.height * scale), 64, 1600);
            if (target != null && target.width == width && target.height == height) return;
            ReleaseTarget();
            target = new RenderTexture(width, height, 24) { name = "Creation preview", antiAliasing = 4 };
            target.Create(); surface.texture = target; ViewCamera.targetTexture = target;
            ViewCamera.aspect = (float)width / height;
            if (autoFrame) FrameAll();
        }
        public void Render()
        {
            if (fogMaterial != null) fogMaterial.SetFloat("_PreviewTime",
                PAO.UI.V2.UISettings.Current != null && PAO.UI.V2.UISettings.Current.reducedMotion ? 0 : Time.unscaledTime * .035f);
            if (ViewCamera != null && target != null) ViewCamera.Render();
        }
        private void ReleaseTarget()
        {
            if (ViewCamera != null) ViewCamera.targetTexture = null;
            if (surface != null) surface.texture = null;
            if (target != null) { target.Release(); Destroy(target); target = null; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; ReleaseTarget();
            if (stage != null) stage.SetActive(false);
            if (Application.isPlaying && scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            else if (stage != null) Destroy(stage);
            foreach (Material material in materials) if (material != null) Destroy(material);
            materials.Clear(); if (groundMesh != null) Destroy(groundMesh);
            if (fogMesh != null) Destroy(fogMesh);
        }
        private void OnDisable() { Dispose(); }
        private void OnDestroy() { Dispose(); }
    }
}
