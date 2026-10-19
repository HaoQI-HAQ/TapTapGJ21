#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using PAO.UI.V2;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.Sandbox
{
    public sealed partial class SandboxVerificationRunner
    {
        private SandboxPiece PieceAt(Vector3Int cell)
        {
            foreach (SandboxPiece piece in world.GetComponentsInChildren<SandboxPiece>())
                if (piece.gameObject.activeInHierarchy && piece.Data.position == cell) return piece;
            return null;
        }

        private IEnumerator VerifySelectionAndLower(SandboxBrush brush)
        {
            world.ClearWorld();
            Vector3Int p = new Vector3Int(14, 4, 8), q = p + Vector3Int.right;
            world.Build(p, 1, SandboxShape.Block, 0, false);
            world.Build(q, 1, SandboxShape.Bubble, 3, false);
            SandboxPiece first = PieceAt(p), second = PieceAt(q);
            Material original = first.GetComponent<Renderer>().sharedMaterial;
            Check(world.Lower(p, 2) == 2 && world.TryGetCell(p + Vector3Int.down * 2, out _),
                "Lower moves the connected structure down and reserves its destinations.");
            yield return new WaitForSeconds(1.1f);
            world.Undo();
            Check(world.TryGetCell(p, out _) && world.TryGetCell(q, out _), "Undo restores a lowered structure as one edit.");
            world.Redo();
            Check(world.TryGetCell(p + Vector3Int.down * 2, out _), "Redo restores the lowered coordinates.");
            world.Undo();
            first = PieceAt(p); second = PieceAt(q); original = first.GetComponent<Renderer>().sharedMaterial;
            Check(world.Lower(p, 5) == 2, "Lower clamps charged movement to three cells.");
            yield return new WaitForSeconds(1.4f);
            Check(world.Lower(p + Vector3Int.down * 3, 2) == 0, "Lower cannot pass through the creation floor.");
            world.Undo(); first = PieceAt(p); second = PieceAt(q);
            world.Build(p + Vector3Int.down * 2, 1, SandboxShape.Block, 1, false);
            Check(world.Lower(p, 3) == 0 && world.TryGetCell(p, out _) && world.TryGetCell(q, out _),
                "Lower rejects an obstacle anywhere along the path without partially moving the group.");
            world.DestroyBrush(p + Vector3Int.down * 2, 1, SandboxShape.Block);

            SandboxSelection selection = brush.Selection;
            Check(selection != null && selection.Toggle(first) && selection.Count == 1,
                "Selection adds the clicked creation.");
            LineRenderer frame = first.GetComponentInChildren<LineRenderer>();
            Check(frame != null && frame.sharedMaterial.color.r > .9f && frame.sharedMaterial.color.g < .3f &&
                first.GetComponent<Renderer>().sharedMaterial == original,
                "Selected creation receives a red frame while retaining its original paint material.");
            Check(!selection.Toggle(first) && selection.Count == 0, "Clicking the same creation again cancels selection.");
            selection.Toggle(first); selection.Toggle(second);
            Check(selection.Count == 2, "Multiple creations can be selected independently.");
            brush.SelectTool(SandboxTool.Lower);
            Check(brush.Tool == SandboxTool.Lower && brush.ToolName == "下浮", "Lower is an independent sandbox tool.");
            brush.SelectTool(SandboxTool.Select);
            Check(brush.ToolName == "选中" && !brush.Fire(0), "Selection mode never creates or destroys a bubble stroke.");
            Check(world.MoveSelection(new[] { first }, -1) == 1 && selection.Count == 2,
                "A selected creation moves independently of its unselected connected neighbor.");
            yield return new WaitForSeconds(.6f);
            Check(first.Data.position == p + Vector3Int.down && second.Data.position == q &&
                first.GetComponentInChildren<LineRenderer>().transform.parent == first.transform,
                "Red selection frames follow the actual moving objects.");
            Check(world.MoveSelection(selection.Snapshot(), 1) == 2, "A multiple selection moves together.");
            yield return new WaitForSeconds(.6f);
            Check(first.Data.position == p && second.Data.position == q + Vector3Int.up, "Selected positions preserve their relative offsets.");
            Vector3Int shotStart = first.Data.position;
            Check(brush.MoveVertical(-1) && brush.Tool == SandboxTool.Select,
                "Vertical action starts a lower bubble for the captured selection without leaving selection mode.");
            selection.Clear();
            Check(first.Data.position == shotStart, "Selected lower waits for the travelling bubble to arrive.");
            yield return new WaitForSeconds(1.3f);
            Check(first.Data.position == shotStart + Vector3Int.down && selection.Count == 0,
                "Changing the live selection does not redirect an already fired lower bubble.");

            selection.Toggle(first);
            world.DestroyBrush(first.Data.position, 1, SandboxShape.Block);
            Check(selection.Count == 0, "Destroyed or undone objects are removed from the selection.");
            world.ClearWorld();
            yield return VerifySelectedDestruction(brush);
            yield return VerifyBubbleMerging(brush);

            // Exercise center-ray targeting against the actual camera and draw the new HUD for review.
            Camera camera = Camera.main;
            Vector3 cameraPosition = camera.transform.position; Quaternion cameraRotation = camera.transform.rotation;
            Vector3Int visual = new Vector3Int(24, 2, 8);
            world.Build(visual, 1, SandboxShape.Block, 0, false);
            world.Build(visual + Vector3Int.right * 2, 1, SandboxShape.Bubble, 3, false);
            first = PieceAt(visual); second = PieceAt(visual + Vector3Int.right * 2);
            PlayerController movement = brush.GetComponent<PlayerController>();
            bool movementEnabled = movement.enabled; movement.enabled = false;
            camera.transform.position = world.CellCenter(visual) + new Vector3(0, .8f, -9);
            camera.transform.LookAt(first.transform.position);
            Physics.SyncTransforms();
            typeof(SandboxBrush).GetMethod("UpdateTarget", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(brush, null);
            Check(brush.HoveredPiece == first && brush.ToggleTargetSelection() && selection.Count == 1,
                "Screen-center crosshair selects the nearest visible creation.");
            selection.Toggle(second);
            UIRuntime.Instance.SendMessage("RefreshSandboxHud");
            int arms = 0, tools = 0; bool selectionHud = false;
            foreach (Transform child in UIRuntime.Instance.GetComponentsInChildren<Transform>())
            {
                if (child.name.StartsWith("Crosshair arm ")) arms++;
                if (child.name.StartsWith("Sandbox tool ")) tools++;
                if (child.name == "Selection HUD") selectionHud = child.gameObject.activeInHierarchy;
            }
            Check(arms == 4 && tools == 3 && selectionHud,
                "HUD exposes the crosshair and three bottom modes without duplicate vertical controls.");
            Button up = null, down = null;
            foreach (Button button in UIRuntime.Instance.GetComponentsInChildren<Button>())
            {
                if (button.name == "Selection action 0") up = button;
                if (button.name == "Selection action 1") down = button;
            }
            Check(up != null && down != null && up.GetComponentInChildren<Text>().text == "Q  上浮" &&
                down.GetComponentInChildren<Text>().text == "E  下浮" && up.interactable && down.interactable,
                "Upper vertical controls show their Q and E shortcuts and work with a selection.");
            yield return Capture("creat-selection-hud.png");
            yield return Capture("creat-selection-hud-compact.png", 1280, 720);
            brush.ClearSelection();
            UIRuntime.Instance.SendMessage("RefreshSandboxHud");
            Check(up.gameObject.activeInHierarchy && up.interactable && down.interactable,
                "Upper vertical controls stay visible and usable for the aimed creation without a selection.");
            yield return Capture("creat-selection-empty.png");
            brush.SelectTool(SandboxTool.Build);
            Check(brush.MoveVertical(1) && brush.Tool == SandboxTool.Build,
                "Aimed vertical action works from build mode without changing the current mode.");
            yield return new WaitForSeconds(1.2f);
            Check(first.Data.position == visual + Vector3Int.up,
                "Aimed vertical action moves the hit creation rather than the adjacent build target.");
            world.Undo();
            first = PieceAt(visual); selection.Toggle(first);
            down.onClick.Invoke();
            yield return new WaitForSeconds(1.2f);
            Check(first.Data.position == visual + Vector3Int.down,
                "Clicking the relocated lower HUD button performs the same vertical action.");
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            movement.enabled = movementEnabled;
            brush.SelectTool(SandboxTool.Build);
            world.ClearWorld();
        }

        private IEnumerator VerifyBubbleMerging(SandboxBrush brush)
        {
            SandboxSelection selection = brush.Selection;
            selection.Clear(); world.ClearWorld();
            Vector3Int p = new Vector3Int(14, 5, 8), below = p + Vector3Int.down;
            Color movingTint = new Color32(16, 122, 232, 255), targetTint = new Color32(246, 47, 82, 255);
            world.Build(p, 1, SandboxShape.Bubble, 0, false, movingTint);
            world.Build(below, 1, SandboxShape.Bubble, 3, false, targetTint);
            SandboxPiece survivor = PieceAt(p), target = PieceAt(below);
            selection.Toggle(survivor); selection.Toggle(target);
            Check(world.MoveSelection(new[] { survivor }, -1) == 1 && world.Count == 1 &&
                PieceAt(below) == survivor && !world.TryGetCell(p, out _) && !target.gameObject.activeSelf,
                "Overlapping selected and stationary bubbles merge immediately into one live cell.");
            Check(selection.Count == 1 && selection.Contains(survivor) && survivor.Data.PaintColor == movingTint,
                "Merging retains the moving bubble's custom paint and one red selection frame.");
            Check(world.Undo() && world.Count == 2 && world.TryGetCell(p, out SandboxCellData original) &&
                original.PaintColor == movingTint && world.TryGetCell(below, out SandboxCellData restored) &&
                restored.PaintColor == targetTint,
                "One undo restores both pre-merge bubbles and their distinct custom colors even during animation.");
            Check(world.Redo() && world.Count == 1 && world.TryGetCell(below, out restored) && restored.PaintColor == movingTint,
                "Redo restores a single merged bubble with the surviving paint.");
            string mergedSave = System.IO.Path.Combine(output, "merged-world.json");
            Check(world.SaveWorld(mergedSave) && world.LoadWorld(mergedSave) && world.Count == 1 &&
                world.TryGetCell(below, out restored) && restored.PaintColor == movingTint,
                "A merged bubble saves and reloads as exactly one cell.");

            selection.Clear(); world.ClearWorld();
            world.Build(p, 1, SandboxShape.Bubble, 0, false);
            world.Build(below, 1, SandboxShape.Bubble, 3, false);
            survivor = PieceAt(p); target = PieceAt(below); selection.Toggle(target);
            Check(world.MoveSelection(new[] { survivor }, -1) == 1 && selection.Count == 1 && selection.Contains(survivor),
                "A selected absorbed bubble transfers its red frame to the survivor.");
            world.Undo(); selection.Clear(); world.ClearWorld();

            world.Build(p, 1, SandboxShape.Bubble, 0, false);
            world.Build(p + Vector3Int.up * 2, 1, SandboxShape.Bubble, 2, false);
            Check(world.Lift(p, 2) == 1 && world.Count == 1 && world.TryGetCell(p + Vector3Int.up * 2, out _),
                "Aimed connected lift merges a separated bubble at its destination.");
            yield return new WaitForSeconds(1.1f);
            world.ClearWorld();

            for (int step = 0; step <= 3; step++) world.Build(p + Vector3Int.up * step, 1, SandboxShape.Bubble, step, false);
            survivor = PieceAt(p);
            Check(world.MoveSelection(new[] { survivor }, 3) == 1 && world.Count == 1 &&
                PieceAt(p + Vector3Int.up * 3) == survivor,
                "A multi-cell vertical move absorbs bubbles along its path and leaves one destination bubble.");
            world.Undo();
            Check(world.Count == 4, "One undo restores every bubble absorbed along the path.");
            world.ClearWorld();

            world.Build(p, 1, SandboxShape.Bubble, 0, false);
            world.Build(below, 1, SandboxShape.Bubble, 3, false);
            world.Build(p + Vector3Int.down * 2, 1, SandboxShape.Block, 1, false);
            survivor = PieceAt(p); int revision = world.Revision;
            Check(world.MoveSelection(new[] { survivor }, -3) == 0 && world.Count == 3 && world.Revision == revision &&
                survivor.Data.position == p && world.TryGetCell(below, out _),
                "A block after a potential merge rejects the entire edit without consuming any bubble.");
            world.ClearWorld();
            world.Build(p, 1, SandboxShape.Block, 0, false);
            world.Build(below, 1, SandboxShape.Bubble, 3, false);
            Check(world.MoveSelection(new[] { PieceAt(p) }, -1) == 0 && world.Count == 2,
                "Blocks cannot merge with bubbles.");
            world.ClearWorld(); selection.Clear();
        }
    }
}
#endif
