#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using PAO.UI.V2;
using UnityEngine;

namespace PAO.Sandbox
{
    public sealed partial class SandboxVerificationRunner
    {
        private IEnumerator VerifyBrushVolume(SandboxBrush brush)
        {
            string savedWorld = File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null;
            PlayerController movement = brush.GetComponent<PlayerController>();
            CharacterController controller = brush.GetComponent<CharacterController>();
            bool movementEnabled = movement.enabled;
            Vector3 playerPosition = brush.transform.position;
            Camera camera = Camera.main;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            SandboxTool previousTool = brush.Tool;
            SandboxShape previousShape = brush.Shape;
            int previousDiameter = brush.Diameter;
            MethodInfo aim = typeof(SandboxBrush).GetMethod("UpdateTarget", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo preview = typeof(SandboxBrush).GetMethod("DrawPreview", BindingFlags.NonPublic | BindingFlags.Instance);
            Vector3Int center = new Vector3Int(18, 8, 14);
            Color tint = new Color32(16, 122, 232, 255);
            movement.enabled = false; brush.Selection.Clear(); world.ClearWorld();
            PlaceFeet(controller, new Vector3(-35, .15f, -20));
            try
            {
                foreach (int size in new[] { 3, 5 })
                {
                    world.ClearWorld();
                    int radius = size / 2, count = size * size * size;
                    Vector3Int corner = center + Vector3Int.one * radius;
                    Vector3Int outside = center + Vector3Int.right * (radius + 1);
                    Check(world.Build(center, size, SandboxShape.Bubble, 0, false) == count && world.Count == count &&
                        world.TryGetCell(corner, out SandboxCellData data) && data.shape == SandboxShape.Bubble,
                        size + "-cell round-bubble brush fills the whole preview box, including its corners.");
                    world.Build(outside, 1, SandboxShape.Block, 4, false);
                    Check(world.Build(center, size, SandboxShape.Bubble, 0, true, tint) == count &&
                        world.TryGetCell(corner, out data) && data.PaintColor == tint &&
                        world.TryGetCell(outside, out data) && data.color == 4 && !data.customColor,
                        size + "-cell paint affects the entire box while preserving the adjacent outside cell.");
                    Check(world.Undo() && world.TryGetCell(corner, out data) && !data.customColor &&
                        world.Redo() && world.TryGetCell(corner, out data) && data.PaintColor == tint,
                        size + "-cell volume paint remains one reversible edit.");
                    Check(world.DestroyBrush(center, size, SandboxShape.Bubble) == count && world.Count == 1 &&
                        world.TryGetCell(outside, out _),
                        size + "-cell destruction removes all box cells, including corners, and preserves the outside cell.");
                    Check(world.Undo() && world.Count == count + 1 && world.TryGetCell(corner, out data) &&
                        data.PaintColor == tint && world.Redo() && world.Count == 1,
                        size + "-cell volume destruction supports whole-stroke undo and redo.");
                }

                world.ClearWorld();
                PlaceFeet(controller, world.CellCenter(center + Vector3Int.one) - Vector3.up * .8f);
                Check(!world.CanBuild(center, 3, SandboxShape.Bubble) &&
                    world.Build(center, 3, SandboxShape.Bubble, 0, false) == 0 && world.Count == 0,
                    "A player occupying a box corner blocks the entire round-bubble build stroke.");
                PlaceFeet(controller, new Vector3(-35, .15f, -20));
                world.Build(center, 1, SandboxShape.Bubble, 0, false);
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    world.Build(center + new Vector3Int(x, y, z), 1,
                        x > 0 ? SandboxShape.Bubble : SandboxShape.Block, 3, false);
                world.Build(center + Vector3Int.right * 2, 1, SandboxShape.Bubble, 4, false);
                world.Build(center + Vector3Int.left * 2, 1, SandboxShape.Block, 5, false);
                while (brush.Diameter != 3) brush.CycleSize();
                if (brush.Shape != SandboxShape.Bubble) brush.ToggleShape();
                brush.SelectTool(SandboxTool.Destroy);
                camera.transform.position = world.CellCenter(center) + Vector3.back * 18;
                camera.transform.LookAt(world.CellCenter(center)); Physics.SyncTransforms();
                aim.Invoke(brush, null); preview.Invoke(brush, null);
                LineRenderer outline = world.transform.Find("Brush footprint preview").GetComponent<LineRenderer>();
                Bounds previewBounds = new Bounds(outline.GetPosition(0), Vector3.zero);
                for (int i = 1; i < outline.positionCount; i++) previewBounds.Encapsulate(outline.GetPosition(i));
                Check(brush.HasTarget && brush.TargetCell == center && outline.enabled &&
                    previewBounds.center == world.CellCenter(center) && previewBounds.size == Vector3.one * 6,
                    "The actual aimed three-cell destruction preview encloses exactly a six-metre cube.");
                UIRuntime.Instance.SendMessage("RefreshSandboxHud");
                yield return Capture("creat-brush-volume.png");
                yield return Capture("creat-brush-volume-compact.png", 1280, 720);
                Check(brush.Fire(0) && world.Count == 11,
                    "Volume destruction blows a travelling bubble before changing the nine objects inside the box.");
                while (brush.Diameter != 1) brush.CycleSize();
                brush.ToggleShape();
                yield return new WaitForSeconds(.8f);
                Check(world.Count == 2 && world.TryGetCell(center + Vector3Int.right * 2, out _) &&
                    world.TryGetCell(center + Vector3Int.left * 2, out _),
                    "The released round-bubble stroke removes all eight box corners and its center despite later brush changes.");
                Check(world.Undo() && world.Count == 11 && world.Redo() && world.Count == 2,
                    "Actual volume shots undo and redo as one complete box edit.");
                Check(savedWorld == (File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null),
                    "Volume brush verification leaves the player's saved creations unchanged.");
            }
            finally
            {
                brush.Selection.Clear(); world.ClearWorld();
                while (brush.Diameter != previousDiameter) brush.CycleSize();
                if (brush.Shape != previousShape) brush.ToggleShape();
                controller.enabled = false; brush.transform.position = playerPosition; controller.enabled = true;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                movement.enabled = movementEnabled; brush.SelectTool(previousTool); Physics.SyncTransforms();
            }
        }
    }
}
#endif
