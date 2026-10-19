#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using PAO.UI.V2;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.Sandbox
{
    public sealed partial class SandboxVerificationRunner
    {
        private IEnumerator VerifySelectedDestruction(SandboxBrush brush)
        {
            string savedWorld = File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null;
            PlayerController movement = brush.GetComponent<PlayerController>();
            bool movementEnabled = movement.enabled;
            Camera camera = Camera.main;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            MethodInfo aim = typeof(SandboxBrush).GetMethod("UpdateTarget", BindingFlags.NonPublic | BindingFlags.Instance);
            SandboxSelection selection = brush.Selection;
            SandboxTool previousTool = brush.Tool;
            movement.enabled = false; selection.Clear(); world.ClearWorld();
            Vector3Int p = new Vector3Int(18, 4, 12), q = p + Vector3Int.right * 8 + Vector3Int.up * 3;
            Color tint = new Color32(230, 52, 86, 255);
            try
            {
                world.Build(p, 1, SandboxShape.Block, 0, false, tint);
                world.Build(q, 1, SandboxShape.Bubble, 3, false);
                world.Build(p + Vector3Int.right, 1, SandboxShape.Bubble, 2, false);
                world.Build(q + Vector3Int.right, 1, SandboxShape.Block, 4, false);
                SandboxPiece first = PieceAt(p), second = PieceAt(q), neighbor = PieceAt(p + Vector3Int.right);
                selection.Toggle(first); selection.Toggle(second);
                while (brush.Diameter != 5) brush.CycleSize();
                if (brush.Shape != SandboxShape.Block) brush.ToggleShape();
                brush.SelectTool(SandboxTool.Destroy);
                Check(selection.Count == 2 && brush.Tool == SandboxTool.Destroy,
                    "Switching to destroy mode retains a multi-selection of distant blocks and bubbles.");
                camera.transform.position = (first.transform.position + second.transform.position) * .5f + new Vector3(0, 2, -27);
                camera.transform.LookAt((first.transform.position + second.transform.position) * .5f);
                Physics.SyncTransforms(); aim.Invoke(brush, null);
                UIRuntime.Instance.SendMessage("RefreshSandboxHud");
                bool selectionHud = false, batchHint = false;
                foreach (Transform child in UIRuntime.Instance.GetComponentsInChildren<Transform>())
                    if (child.name == "Selection HUD") selectionHud = child.gameObject.activeInHierarchy;
                foreach (Text text in UIRuntime.Instance.GetComponentsInChildren<Text>())
                    if (text.text.Contains("将拆除已选中的 2 件造物")) batchHint = true;
                Check(!selectionHud && batchHint, "Destroy mode hides the selection-only action bar while keeping the selected count and batch destruction hint.");
                yield return Capture("creat-destroy-selection.png");
                yield return Capture("creat-destroy-selection-compact.png", 1280, 720);
                camera.transform.LookAt(camera.transform.position + Vector3.up);
                Physics.SyncTransforms(); aim.Invoke(brush, null);
                Check(!brush.HasTarget && brush.Fire(.8f) && world.Count == 4,
                    "Batch destruction fires without a crosshair target and waits for its travelling bubble.");
                selection.Clear(); selection.Toggle(neighbor);
                yield return new WaitForSeconds(.8f);
                Check(world.Count == 2 && !world.TryGetCell(p, out _) && !world.TryGetCell(q, out _) &&
                    world.TryGetCell(p + Vector3Int.right, out _) && world.TryGetCell(q + Vector3Int.right, out _),
                    "One shot destroys every captured selected object while preserving unselected neighbors even with a five-cell brush.");
                Check(selection.Count == 1 && selection.Contains(neighbor),
                    "Changing selection in flight does not redirect destruction or clear newly selected objects.");
                Check(world.Undo() && world.Count == 4 && world.TryGetCell(p, out SandboxCellData restored) &&
                    restored.shape == SandboxShape.Block && restored.customColor && restored.PaintColor == tint &&
                    world.TryGetCell(q, out restored) && restored.shape == SandboxShape.Bubble && restored.color == 3,
                    "One undo restores the entire destroyed batch with its exact positions, shapes and colors.");
                Check(world.Redo() && world.Count == 2 && !world.TryGetCell(p, out _) && !world.TryGetCell(q, out _),
                    "One redo removes the complete selected batch again.");
                world.Undo();
                Check(brush.MoveVertical(1), "Q action remains available for a selection while in destroy mode.");
                yield return new WaitForSeconds(1.2f);
                Check(world.Count == 4 && neighbor.Data.position == p + Vector3Int.right + Vector3Int.up,
                    "Vertical selection actions in destroy mode move objects instead of destroying them.");

                selection.Clear(); world.ClearWorld();
                while (brush.Diameter != 1) brush.CycleSize();
                world.Build(p, 1, SandboxShape.Bubble, 0, false);
                world.Build(p + Vector3Int.right, 1, SandboxShape.Block, 2, false);
                camera.transform.position = world.CellCenter(p) + Vector3.back * 8;
                camera.transform.LookAt(world.CellCenter(p)); Physics.SyncTransforms(); aim.Invoke(brush, null);
                Check(brush.HasTarget && brush.Fire(0), "Without a selection, destroy still fires at the aimed brush footprint.");
                yield return new WaitForSeconds(.8f);
                Check(world.Count == 1 && !world.TryGetCell(p, out _) && world.TryGetCell(p + Vector3Int.right, out _),
                    "Ordinary local destruction still preserves nearby creations outside its footprint.");
                world.Undo();
                first = PieceAt(p); neighbor = PieceAt(p + Vector3Int.right);
                Check(world.DestroySelection(new[] { first, null }) == 0 && world.Count == 2,
                    "An invalid batch member rejects the whole destruction without partial deletion.");
                Check(world.DestroySelection(new[] { first, first }) == 1 && world.Count == 1,
                    "Duplicate batch references create only one deletion and one history entry.");
                world.Undo();
                Check(world.DestroySelection(new[] { neighbor, first }) == 0 && world.Count == 2,
                    "A stale captured object cannot delete its replacement or partially delete other members.");
                selection.Toggle(PieceAt(p)); selection.Toggle(PieceAt(p + Vector3Int.right));
                brush.Fire(0); world.Undo(); int countAfterUndo = world.Count;
                yield return new WaitForSeconds(.8f);
                Check(world.Count == countAfterUndo && world.TryGetCell(p, out _),
                    "Undo cancels in-flight batch destruction before it edits the restored world.");
                Check(savedWorld == (File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null),
                    "Selected destruction verification leaves the player's saved creations unchanged.");
            }
            finally
            {
                selection.Clear(); world.ClearWorld();
                while (brush.Diameter != 1) brush.CycleSize();
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                movement.enabled = movementEnabled; brush.SelectTool(previousTool);
            }
            yield return VerifyBrushVolume(brush);
        }
    }
}
#endif
