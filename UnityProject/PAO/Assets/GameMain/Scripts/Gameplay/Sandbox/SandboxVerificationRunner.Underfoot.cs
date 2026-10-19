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
        private void PlaceFeet(CharacterController controller, Vector3 feet)
        {
            Bounds body = controller.bounds;
            Vector3 offset = feet - new Vector3(body.center.x, body.min.y, body.center.z);
            controller.enabled = false;
            controller.transform.position += offset;
            controller.enabled = true;
            Physics.SyncTransforms();
        }
        private IEnumerator VerifyUnderfootPlacement(SandboxBrush brush)
        {
            string save = File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null;
            PlayerController movement = brush.GetComponent<PlayerController>();
            CharacterController controller = brush.GetComponent<CharacterController>();
            bool movementEnabled = movement.enabled;
            Vector3 originalPosition = brush.transform.position;
            Camera camera = Camera.main;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            MethodInfo updateTarget = typeof(SandboxBrush).GetMethod("UpdateTarget", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo drawPreview = typeof(SandboxBrush).GetMethod("DrawPreview", BindingFlags.NonPublic | BindingFlags.Instance);
            movement.enabled = false;
            brush.SelectTool(SandboxTool.Build);
            if (brush.Paint) brush.TogglePaint();
            while (brush.Diameter != 1) brush.CycleSize();
            if (brush.Shape != SandboxShape.Block) brush.ToggleShape();
            brush.SelectHexColor("#E63456");
            world.ClearWorld();
            Vector3Int ground = new Vector3Int(24, 0, 8);
            Vector3 center = world.CellCenter(ground);
            PlaceFeet(controller, new Vector3(center.x, .15f, center.z));
            brush.AimUnderfoot();
            Vector3 before = brush.transform.position;
            Check(brush.HasTarget && brush.TargetCell == ground && !brush.CanPlaceTarget && !brush.Fire(0) && world.Count == 0,
                "Standing in the intended cell blocks foot placement instead of pushing or teleporting the player.");
            drawPreview.Invoke(brush, null);
            LineRenderer preview = world.transform.Find("Brush footprint preview").GetComponent<LineRenderer>();
            Check(preview.enabled && preview.sharedMaterial.color.r > .9f && preview.sharedMaterial.color.g < .3f,
                "An obstructed placement is shown with a red preview.");
            Check(brush.transform.position == before, "A blocked placement leaves the player's exact position unchanged.");

            PlaceFeet(controller, new Vector3(center.x, 2.3f, center.z));
            brush.AimUnderfoot();
            Check(brush.TargetCell == ground && brush.CanPlaceTarget,
                "Jumping keeps the target attached to the floor face below, rather than following the player's height.");
            before = brush.transform.position;
            Check(brush.Fire(0) && world.Count == 0 && brush.transform.position == before,
                "Quick placement still blows a travelling bubble and waits for arrival.");
            brush.SelectColor(1);
            yield return new WaitForSeconds(.8f);
            Check(world.TryGetCell(ground, out SandboxCellData built) && built.customColor &&
                built.PaintColor == (Color)new Color32(230, 52, 86, 255) && brush.transform.position == before,
                "The released bubble builds the captured color beneath the player without changing player position.");
            movement.enabled = true;
            yield return new WaitForSeconds(.5f);
            movement.enabled = false;
            brush.AimUnderfoot();
            Check(controller.bounds.min.y > 1.8f && controller.bounds.min.y < 2.2f &&
                brush.TargetCell == ground + Vector3Int.up && !brush.CanPlaceTarget,
                "Normal gravity lands the player on the placed block and the next layer stays blocked until another jump.");

            movement.BounceUp(2.8f); movement.enabled = true;
            float deadline = Time.time + 1.5f;
            while (Time.time < deadline && controller.bounds.min.y < 4.2f) yield return null;
            brush.AimUnderfoot();
            Check(brush.TargetCell == ground + Vector3Int.up && brush.CanPlaceTarget && brush.Fire(0),
                "A real controller jump clears the next layer for a Minecraft-style tower placement.");
            yield return new WaitForSeconds(.8f);
            movement.enabled = false;
            Check(world.Count == 2 && controller.bounds.min.y > 3.8f && controller.bounds.min.y < 4.2f &&
                Mathf.Abs(controller.bounds.center.x - center.x) < .05f && Mathf.Abs(controller.bounds.center.z - center.z) < .05f,
                "The player naturally lands on the second tower block without any scripted relocation.");
            Check(world.Undo() && world.Count == 1, "The last tower placement is undone as one stroke.");
            PlaceFeet(controller, new Vector3(center.x, 2.05f, center.z));
            before = brush.transform.position;
            Check(!world.Redo() && world.CanRedo && world.Count == 1 && brush.transform.position == before,
                "Redo refuses to restore a block inside the player and preserves the redo entry.");
            PlaceFeet(controller, new Vector3(center.x, 4.3f, center.z));
            before = brush.transform.position;
            Check(world.Redo() && world.Count == 2 && brush.transform.position == before,
                "Redo succeeds after the player clears the cell and never teleports the player.");

            world.ClearWorld();
            PlaceFeet(controller, new Vector3(center.x, 2.3f, center.z));
            brush.AimUnderfoot(); brush.Fire(0);
            PlaceFeet(controller, new Vector3(center.x, .15f, center.z));
            before = brush.transform.position;
            yield return new WaitForSeconds(.8f);
            Check(world.Count == 0 && brush.transform.position == before,
                "Bubble arrival rechecks the player's current position and cancels if they fell back into the target.");
            PlaceFeet(controller, new Vector3(center.x, 2.3f, center.z));
            brush.AimUnderfoot(); brush.Fire(0);
            PlaceFeet(controller, new Vector3(center.x + 8, .15f, center.z));
            before = brush.transform.position;
            yield return new WaitForSeconds(.8f);
            Check(world.TryGetCell(ground, out _) && brush.transform.position == before,
                "Walking away after release does not redirect the shot or pull the player back.");

            world.ClearWorld();
            brush.CycleSize();
            PlaceFeet(controller, new Vector3(center.x, .15f, center.z));
            brush.AimUnderfoot();
            Check(!brush.CanPlaceTarget && !brush.Fire(0) && world.Count == 0,
                "Large brushes reject the whole stroke when any new cell intersects the player.");
            PlaceFeet(controller, new Vector3(center.x, 6.3f, center.z));
            brush.AimUnderfoot();
            before = brush.transform.position;
            Check(brush.TargetCell == ground + Vector3Int.up && brush.CanPlaceTarget && brush.Fire(0),
                "A large underfoot brush remains anchored to the supporting surface when the player has enough clearance.");
            yield return new WaitForSeconds(.8f);
            Check(world.Count == 27 && brush.transform.position == before,
                "A clear three-cell brush places all 27 cells without relocating the player.");

            world.ClearWorld();
            while (brush.Diameter != 1) brush.CycleSize();
            PlaceFeet(controller, new Vector3(center.x + 8, .15f, center.z));
            Vector3Int faceCell = new Vector3Int(18, 4, 8);
            world.Build(faceCell, 1, SandboxShape.Block, 0, false);
            bool faces = true;
            foreach (Vector3Int direction in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up,
                Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
            {
                camera.transform.position = world.CellCenter(faceCell) + (Vector3)direction * 6;
                camera.transform.LookAt(world.CellCenter(faceCell));
                Physics.SyncTransforms(); updateTarget.Invoke(brush, null);
                faces &= brush.HasTarget && brush.TargetCell == faceCell + direction && brush.CanPlaceTarget;
            }
            Check(faces, "All six block faces target their correct adjacent grid cell.");
            Check(!world.CanBuild(faceCell, 1, SandboxShape.Block) && world.Build(faceCell, 1, SandboxShape.Block, 7, false) == 0 &&
                world.TryGetCell(faceCell, out built) && built.color == 0,
                "Occupied cells cannot be overwritten by placement.");
            Check(world.Build(new Vector3Int(24, 36, 8), 1, SandboxShape.Block, 0, false) == 0,
                "Minecraft-style placement still obeys the sandbox ceiling.");
            brush.TogglePaint(); brush.AimUnderfoot();
            Check(!brush.TargetingUnderfoot && !brush.HasTarget, "Painting does not activate underfoot building.");
            brush.TogglePaint();
            Check((save == null && !File.Exists(world.SavePath)) || (save != null && File.ReadAllText(world.SavePath) == save),
                "Placement verification leaves the player's real save untouched.");

            world.ClearWorld();
            PlaceFeet(controller, new Vector3(center.x + 8, .15f, center.z));
            world.Build(ground, 1, SandboxShape.Block, 1, false);
            PlaceFeet(controller, new Vector3(center.x, 2.05f, center.z));
            brush.AimUnderfoot(); drawPreview.Invoke(brush, null);
            camera.transform.position = center + new Vector3(8, 8, -11);
            camera.transform.LookAt(new Vector3(center.x, 3, center.z));
            UIRuntime.Instance.SendMessage("RefreshSandboxHud");
            yield return Capture("creat-underfoot-blocked.png");
            PlaceFeet(controller, new Vector3(center.x, 4.3f, center.z));
            brush.AimUnderfoot(); drawPreview.Invoke(brush, null);
            UIRuntime.Instance.SendMessage("RefreshSandboxHud");
            yield return Capture("creat-underfoot.png");
            yield return Capture("creat-underfoot-compact.png", 1280, 720);

            world.ClearWorld();
            controller.enabled = false; brush.transform.position = originalPosition; controller.enabled = true;
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            movement.enabled = movementEnabled;
            brush.SelectColor(0);
            if (brush.Shape != SandboxShape.Bubble) brush.ToggleShape();
            Physics.SyncTransforms();
        }
    }
}
#endif
