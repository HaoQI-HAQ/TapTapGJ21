#if UNITY_EDITOR
using System.Collections;
using System.IO;
using PAO.UI.V2;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.Sandbox
{
    public sealed partial class SandboxVerificationRunner
    {
        private bool PreviewContainsAll(SandboxPreviewView preview)
        {
            foreach (MeshRenderer renderer in preview.Objects.GetComponentsInChildren<MeshRenderer>())
            {
                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    Vector3 screen = preview.ViewCamera.WorldToViewportPoint(point);
                    if (screen.z <= 0 || screen.x < .01f || screen.x > .99f || screen.y < .01f || screen.y > .99f) return false;
                }
            }
            return true;
        }

        private IEnumerator VerifyModelPreview(SandboxBrush brush)
        {
            UIRuntime runtime = UIRuntime.Instance;
            string saved = File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null;
            world.ClearWorld(); brush.Selection.Clear();
            Vector3Int p = new Vector3Int(12, 3, 7), far = new Vector3Int(44, 33, 21);
            Color tint = new Color32(230, 52, 86, 255);
            world.Build(p, 1, SandboxShape.Block, 0, false, tint);
            world.Build(far, 1, SandboxShape.Bubble, 2, false);
            SandboxPiece original = PieceAt(p); brush.Selection.Toggle(original);
            Material originalMaterial = original.GetComponent<MeshRenderer>().sharedMaterial;
            PlayerController movement = brush.GetComponent<PlayerController>();
            bool enabled = movement.enabled;
            Vector3 playerPosition = movement.transform.position;
            Camera source = Camera.main; Vector3 cameraPosition = source.transform.position;
            Quaternion cameraRotation = source.transform.rotation;
            int count = world.Count, revision = world.Revision, scenes = SceneManager.sceneCount;
            Button entry = null;
            foreach (Button button in runtime.GetComponentsInChildren<Button>())
                if (button.name == "Open creation preview") entry = button;
            Check(entry != null && entry.GetComponentInChildren<Text>().text.Contains("F6"),
                "Gameplay exposes a dedicated F6 creation-preview entry.");
            entry.onClick.Invoke();
            yield return null; yield return null;
            SandboxPreviewView preview = runtime.GetComponentInChildren<SandboxPreviewView>();
            Check(preview != null && runtime.IsSandboxPreview && runtime.IsBlockingGameplay && Time.timeScale == 0 && !movement.enabled,
                "Preview opens independently and suspends player movement and edits.");
            Check(preview.ObjectCount == count && preview.Objects.gameObject.scene != world.gameObject.scene && SceneManager.sceneCount == scenes + 1 &&
                preview.Objects.GetComponentsInChildren<Collider>().Length == 0 && preview.Objects.GetComponentsInChildren<LineRenderer>().Length == 0,
                "Preview includes only actual creation meshes in a separate scene, without walls, physics, player or red frames.");
            Check(preview.ContentBounds.Contains(world.CellCenter(p)) && preview.ContentBounds.Contains(world.CellCenter(far)) && PreviewContainsAll(preview),
                "Auto framing includes far-apart low and high creations instead of centering the empty arena.");
            bool paints = false, pearl = false;
            foreach (MeshRenderer renderer in preview.Objects.GetComponentsInChildren<MeshRenderer>())
            {
                Material material = renderer.sharedMaterial;
                paints |= material.GetColor("_BaseColor") == tint && material.GetFloat("_Pearl") == 0;
                pearl |= material.GetFloat("_Pearl") == 1 && material.shader.name == "PAO/SandboxPreviewSurface";
            }
            Check(paints && pearl, "Preview supplies ceramic and pearl materials while retaining exact custom paint.");
            Check(!world.WorldBounds.Contains(preview.ViewCamera.transform.position) && preview.ViewCamera.GetComponent<PlayerController>() == null,
                "Preview camera is independent of the arena bounds and gameplay collision controller.");
            float distance = preview.Distance;
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, delta = new Vector2(350, 120) };
            preview.OnDrag(pointer);
            Check(preview.Distance == distance, "Orbiting past the arena walls does not pull the preview camera inward.");
            Vector3 before = preview.ViewCamera.transform.position;
            pointer.button = PointerEventData.InputButton.Middle; pointer.delta = new Vector2(180, -30);
            preview.OnDrag(pointer); pointer.scrollDelta = new Vector2(0, 2); preview.OnScroll(pointer);
            Check(preview.ViewCamera.transform.position != before && preview.Distance < distance,
                "Preview pan and zoom move the camera while gameplay time is paused.");
            preview.SetView(0, 0); Check(PreviewContainsAll(preview), "Front view reframes the complete creation.");
            preview.SetView(90, 0); Check(PreviewContainsAll(preview), "Side view reframes the complete creation.");
            preview.SetView(0, 89); Check(PreviewContainsAll(preview), "Top view reframes the complete creation.");
            bool grid = preview.GridVisible; preview.ToggleGrid();
            Check(preview.GridVisible != grid, "The optional preview ground can be hidden.");
            preview.SetView(35, 24);
            yield return Capture("creat-model-preview-spread.png");
            foreach (Button button in runtime.GetComponentsInChildren<Button>())
                if (button.name == "Close creation preview") { button.onClick.Invoke(); break; }
            // Compare at the resume boundary, before normal player and camera updates run again.
            Check(!runtime.IsSandboxPreview && !runtime.IsBlockingGameplay && Time.timeScale > 0 && movement.enabled == enabled &&
                movement.transform.position == playerPosition && source.transform.position == cameraPosition && source.transform.rotation == cameraRotation,
                "Leaving preview restores gameplay and the exact player and camera state.");
            Check(world.Count == count && world.Revision == revision && brush.Selection.Contains(original) &&
                original.GetComponent<MeshRenderer>().sharedMaterial == originalMaterial && original.Data.PaintColor == tint,
                "Preview does not modify creations, original materials, undo history or the selection.");
            yield return null; yield return null;
            Check(SceneManager.sceneCount == scenes && runtime.GetComponentInChildren<SandboxPreviewView>() == null,
                "Leaving preview releases its separate scene and viewing resources.");

            world.ClearWorld(); brush.Selection.Clear();
            // A small authored-by-build sculpture verifies framing uses the occupied shape's scale.
            for (int x = 15; x <= 24; x++)
            {
                world.Build(new Vector3Int(x, 0, 12), 1, SandboxShape.Block, 7, false);
                world.Build(new Vector3Int(x, 6, 12), 1, SandboxShape.Bubble, (x - 15) % 8, false);
            }
            for (int y = 1; y < 6; y++)
                foreach (int x in new[] { 15, 24 }) world.Build(new Vector3Int(x, y, 12), 1, SandboxShape.Block, 0, false);
            for (int y = 1; y < 5; y++) world.Build(new Vector3Int(19, y, 12), 1, SandboxShape.Bubble, 3, false);
            runtime.ShowPause(); runtime.ShowSandboxPreview();
            yield return null; yield return null;
            preview = runtime.GetComponentInChildren<SandboxPreviewView>();
            Check(preview.ObjectCount == world.Count && PreviewContainsAll(preview),
                "Entering from pause reads the latest actual work and frames a compact sculpture.");
            yield return Capture("creat-model-preview.png");
            yield return Capture("creat-model-preview-compact.png", 1280, 720);
            Check(PreviewContainsAll(preview), "Automatic framing adapts to a resized preview viewport.");
            runtime.Back(); yield return null; yield return null;
            world.ClearWorld();
            runtime.ShowSandboxPreview(); yield return null; yield return null;
            preview = runtime.GetComponentInChildren<SandboxPreviewView>();
            Check(preview.ObjectCount == 0 && !float.IsNaN(preview.Distance) && preview.Distance > 0,
                "An empty creation opens a valid preview with a helpful empty state.");
            yield return Capture("creat-model-preview-empty.png");
            runtime.Back(); yield return null; yield return null;
            Check((saved == null && !File.Exists(world.SavePath)) || (saved != null && File.ReadAllText(world.SavePath) == saved),
                "Independent preview leaves the real saved creation untouched.");
            Check(UnityEditor.ShaderUtil.GetShaderMessages(Resources.Load<Shader>("SandboxPreviewSurface")).Length == 0,
                "Preview material shader compiles without diagnostics.");
        }
    }
}
#endif
