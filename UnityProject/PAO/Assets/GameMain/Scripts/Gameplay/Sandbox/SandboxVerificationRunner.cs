#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PAO.UI.V2;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.Sandbox
{
    /// <summary>Runs only when requested by the editor setup tool; never ships in a player.</summary>
    public sealed partial class SandboxVerificationRunner : MonoBehaviour
    {
        public bool PreviewOnly;
        public bool ExpansionOnly;
        public bool UnderfootOnly;
        public bool TargetingOnly;
        public bool DestructionOnly;
        public bool ModelPreviewOnly;
        public bool ArchivesOnly;
        private readonly List<string> results = new List<string>();
        private readonly List<string> errors = new List<string>();
        private string output;
        private BubbleSandbox world;
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            output = Path.GetFullPath("output/CreatValidation"); Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
        }
        private void OnLog(string condition, string trace, LogType type)
        { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(condition + "\n" + trace); }
        private void Check(bool success, string message)
        { results.Add((success ? "PASS " : "FAIL ") + message); Debug.Log("[Creat verify] " + results[results.Count - 1]); }
        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(2);
            world = BubbleSandbox.Instance;
            if (world == null) { Check(false, "Creat has a sandbox controller."); Finish(); yield break; }
            world.restoreSavedWorld = false;
            SandboxBrush inputBrush = FindFirstObjectByType<SandboxBrush>();
            inputBrush.enabled = false;
            // Programmatic checks should not consume mouse/keyboard input from the editor user.
            UIRuntime.Instance.enabled = false;
            var inputModule = FindFirstObjectByType<UnityEngine.EventSystems.StandaloneInputModule>();
            if (inputModule != null) inputModule.enabled = false;
            if (ExpansionOnly) { yield return VerifyExpansion(); Finish(); yield break; }
            if (ArchivesOnly)
            {
                yield return VerifyWorkArchives(inputBrush);
                Check(errors.Count == 0, "Work archives run without Unity runtime errors.");
                Finish(); yield break;
            }
            if (ModelPreviewOnly)
            {
                yield return VerifyModelPreview(inputBrush);
                Check(errors.Count == 0, "Independent model preview runs without Unity runtime errors.");
                Finish(); yield break;
            }
            if (DestructionOnly)
            {
                yield return VerifySelectedDestruction(inputBrush);
                Check(errors.Count == 0, "Selected destruction runs without Unity runtime errors.");
                Finish(); yield break;
            }
            if (TargetingOnly)
            {
                yield return VerifyUpwardTargeting(inputBrush);
                Check(errors.Count == 0, "Upward targeting runs without Unity runtime errors.");
                Finish(); yield break;
            }
            if (UnderfootOnly)
            {
                yield return VerifyUnderfootPlacement(inputBrush);
                Check(errors.Count == 0, "Underfoot construction runs without Unity runtime errors.");
                Finish(); yield break;
            }
            if (PreviewOnly) { yield return CapturePages(); Finish(); yield break; }
            Check(UIRuntime.Instance != null && UIRuntime.Instance.IsGameplay, "Creat boots the integrated gameplay UI.");
            Check(FindFirstObjectByType<SandboxBrush>() != null && FindFirstObjectByType<BubbleLauncher>() == null,
                "Sandbox brush replaces the resource-consuming launcher only in Creat.");
            string pieceGuid = UnityEditor.AssetDatabase.AssetPathToGUID("Assets/GameMain/Scripts/Gameplay/Sandbox/SandboxPiece.cs");
            Check(!File.ReadAllText("Assets/GameMain/Scenes/Creat.unity").Contains(pieceGuid), "The authored sandbox starts empty.");
            yield return VerifyUnderfootPlacement(inputBrush);
            yield return VerifyUpwardTargeting(inputBrush);
            yield return VerifySelectionAndLower(inputBrush);
            yield return VerifyModelPreview(inputBrush);
            yield return VerifyWorkArchives(inputBrush);
            CameraOcclusionVerification.Run();
            yield return new WaitUntil(() => FindFirstObjectByType<CameraOcclusionVerification>() == null);
            // A real user save may already have been restored on entry. Keep verification edits in memory only.
            world.ClearWorld();
            Vector3Int p = new Vector3Int(8, 0, 8);
            Check(world.Build(p, 1, SandboxShape.Block, 0, false) == 1, "A build bubble creates a solid cell.");
            Check(world.Build(p, 1, SandboxShape.Block, 1, false) == 0 && world.Count == 1, "Repeated placement does not overwrite or duplicate a cell.");
            Check(world.Build(p, 3, SandboxShape.Bubble, 3, true) == 1 && world.Count == 1,
                "Paint changes existing cells without creating surrounding geometry.");
            Check(world.TryGetCell(p, out SandboxCellData state) && state.color == 3, "Paint uses the selected palette color.");
            world.Undo(); Check(world.TryGetCell(p, out state) && state.color == 0, "Undo restores the previous paint.");
            world.Redo(); Check(world.TryGetCell(p, out state) && state.color == 3, "Redo restores the painted result.");
            world.Build(p + Vector3Int.right, 1, SandboxShape.Bubble, 2, false);
            world.Build(p + Vector3Int.up * 3, 1, SandboxShape.Block, 2, false);
            Check(world.Lift(p, 3) == 0, "Lift rejects an occupied destination without moving part of a structure.");
            world.DestroyBrush(p + Vector3Int.up * 3, 1, SandboxShape.Block);
            world.Build(p + Vector3Int.up * 2, 1, SandboxShape.Block, 2, false);
            Check(world.Lift(p, 3) == 0, "Lift also rejects an obstacle along its travel path.");
            world.DestroyBrush(p + Vector3Int.up * 2, 1, SandboxShape.Block);
            Check(world.Lift(p, 2) == 2 && world.TryGetCell(p + Vector3Int.up * 2, out state),
                "Float moves a connected structure together and reserves its destination cells.");
            yield return new WaitForSeconds(1.1f);
            world.Undo(); Check(world.TryGetCell(p, out state), "Undo returns floated cells to their original coordinates.");
            Check(world.DestroyBrush(p, 1, SandboxShape.Block) == 1 && world.Count == 1, "Destroy removes the local stroke only.");
            world.Undo(); Check(world.Count == 2, "Destruction can be undone.");
            world.ClearWorld(); Check(world.Count == 0, "Clear empties the sandbox.");
            world.Undo(); Check(world.Count == 2, "Clear can be undone as one edit.");
            Vector3Int ceiling = new Vector3Int(0, world.dimensions.y - 1, 0);
            world.Build(ceiling, 1, SandboxShape.Bubble, 0, false);
            Check(world.Lift(ceiling, 1) == 0 && world.TryGetCell(ceiling, out state), "The ceiling constrains floated creations.");
            int before = world.Count;
            Check(world.Build(new Vector3Int(-4, 0, 0), 1, SandboxShape.Block, 0, false) == 0 && world.Count == before,
                "Building outside the finite volume is rejected.");
            world.Build(Vector3Int.zero, 5, SandboxShape.Block, 1, false);
            bool bounded = true;
            foreach (SandboxPiece piece in world.GetComponentsInChildren<SandboxPiece>()) bounded &= world.Contains(piece.Data.position);
            Check(bounded, "Large edge strokes stay inside the volume.");
            Check(BubbleSandbox.Palette.Length == 40 && BubbleSandbox.ColorNames.Length == 40 &&
                BubbleSandbox.Palette[0] == (Color)new Color32(104, 222, 206, 255) &&
                BubbleSandbox.Palette[7] == (Color)new Color32(236, 245, 238, 255),
                "Forty presets preserve the original palette indices.");
            Vector3Int customCell = new Vector3Int(20, 0, 20);
            Color custom = new Color32(18, 52, 86, 255), other = new Color32(230, 84, 157, 255);
            world.Build(customCell, 1, SandboxShape.Block, 39, false);
            Check(world.TryGetCell(customCell, out state) && state.color == 39 && state.PaintColor == BubbleSandbox.Palette[39],
                "The last new preset builds with its actual color.");
            Check(world.Build(customCell, 1, SandboxShape.Bubble, 0, true, custom) == 1 &&
                world.TryGetCell(customCell, out state) && state.customColor && state.PaintColor == custom && state.shape == SandboxShape.Block,
                "Custom paint keeps geometry and stores the exact RGB color.");
            Check(world.Build(customCell, 1, SandboxShape.Block, 1, true, custom) == 0,
                "Painting with the same custom RGB does not create an undo entry.");
            world.Undo(); Check(world.TryGetCell(customCell, out state) && !state.customColor && state.color == 39,
                "Undo restores a preset after custom painting.");
            world.Redo(); Check(world.TryGetCell(customCell, out state) && state.PaintColor == custom,
                "Redo restores the exact custom color.");
            world.Lift(customCell, 1);
            Check(world.TryGetCell(customCell + Vector3Int.up, out state) && state.PaintColor == custom,
                "Float preserves custom color data.");
            world.Undo();
            MaterialPropertyBlock tint = new MaterialPropertyBlock();
            foreach (SandboxPiece piece in world.GetComponentsInChildren<SandboxPiece>())
                if (piece.Data.position == customCell) piece.GetComponent<Renderer>().GetPropertyBlock(tint);
            Check(tint.GetColor("_BaseColor") == custom, "Custom creation renderers display the stored color without cloning materials.");
            Check(inputBrush.SelectHexColor("#123456") && inputBrush.SelectedColor == custom && inputBrush.HasCustomColor,
                "HEX input selects the exact RGB value.");
            Check(!inputBrush.SelectHexColor("invalid") && inputBrush.SelectedColor == custom,
                "Invalid HEX input preserves the selected color.");
            Check(inputBrush.SelectHexColor("ffffff") && inputBrush.SelectedColor == Color.white &&
                inputBrush.SelectHexColor("000000") && inputBrush.SelectedColor == Color.black,
                "HEX input supports black, white and an optional hash prefix.");
            inputBrush.SelectColor(39); inputBrush.SelectColor(inputBrush.ColorIndex + 1);
            Check(!inputBrush.HasCustomColor && inputBrush.ColorIndex == 0, "Preset cycling wraps across all forty colors.");
            string save = Path.Combine(output, "test-world.json");
            int savedCount = world.Count;
            Check(world.SaveWorld(save), "World serialization succeeds.");
            world.ClearWorld();
            Check(world.LoadWorld(save) && world.Count == savedCount, "Loading restores geometry, colors and shapes.");
            Check(world.TryGetCell(customCell, out state) && state.customColor && state.PaintColor == custom,
                "Saving and loading round-trips the exact custom RGB.");
            string legacy = Path.Combine(output, "legacy-world.json");
            AtomicJsonFile.Write(legacy, "{\"version\":1,\"dimensions\":" + DimensionsJson() +
                ",\"cellSize\":" + world.cellSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"origin\":" + JsonUtility.ToJson(world.origin) +
                ",\"cells\":[{\"position\":{\"x\":8,\"y\":0,\"z\":8},\"color\":7,\"shape\":1}]}");
            Check(world.LoadWorld(legacy) && world.Count == 1 && world.TryGetCell(p, out state) &&
                !state.customColor && state.PaintColor == BubbleSandbox.Palette[7], "Existing version-one saves retain their original colors.");
            world.LoadWorld(save);
            string invalidTint = Path.Combine(output, "invalid-tint.json");
            // Use a deliberately out-of-range serialized tint; validation must happen before world replacement.
            string invalidPayload = "{\"version\":2,\"dimensions\":" + DimensionsJson() +
                ",\"cellSize\":" + world.cellSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"origin\":" + JsonUtility.ToJson(world.origin) + ",\"cells\":[" +
                JsonUtility.ToJson(new SandboxCellData { position = customCell, color = 0, customColor = true, tint = new Color(-1, 0, 0, 1) }) + "]}";
            AtomicJsonFile.Write(invalidTint, invalidPayload);
            Check(!world.LoadWorld(invalidTint) && world.Count == savedCount && world.TryGetCell(customCell, out state) && state.PaintColor == custom,
                "Invalid custom colors cannot replace the current work.");
            File.WriteAllText(Path.Combine(output, "invalid.json"), "{\"version\":999}");
            Check(!world.LoadWorld(Path.Combine(output, "invalid.json")) && world.Count == savedCount, "An invalid save preserves the current work.");
            string corrupt = Path.Combine(output, "corrupt-world.json");
            File.WriteAllText(corrupt, "{\"format\":1,\"payload\":\"bad\",\"checksum\":\"bad\"}");
            Check(!world.LoadWorld(corrupt) && world.Count == savedCount &&
                world.TryGetCell(customCell, out state) && state.PaintColor == custom,
                "A rolling save with a broken checksum returns failure without replacing the current work.");
            world.ClearWorld();
            Vector3Int shotCell = new Vector3Int(12, 0, 12);
            GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(shot.GetComponent<Collider>());
            shot.transform.position = world.CellCenter(shotCell) + Vector3.back * 15;
            inputBrush.SelectCustomColor(custom);
            shot.AddComponent<SandboxBubbleShot>().Initialize(world, shotCell, world.CellCenter(shotCell), BubbleType.Bouncy, 1, SandboxShape.Bubble, 4, false, 0, inputBrush.SelectedColor);
            inputBrush.SelectCustomColor(other);
            Check(world.Count == 0, "A travelling bubble does not edit before arrival.");
            UIRuntime.Instance.ShowPause();
            yield return new WaitForSecondsRealtime(.8f);
            Check(world.Count == 0 && Time.timeScale == 0, "Pause freezes travelling bubble edits.");
            UIRuntime.Instance.Back();
            yield return new WaitForSeconds(1);
            Check(world.Count == 1, "The blown bubble creates its captured stroke on arrival.");
            Check(world.TryGetCell(shotCell, out state) && state.customColor && state.PaintColor == custom,
                "A travelling bubble retains its captured custom color after the brush changes.");
            // Unlimited supply is tested through more strokes than the normal bomb/resource limits.
            for (int i = 0; i < 80; i++) world.Build(new Vector3Int(i % 20, 0, i / 20 + 14), 1, SandboxShape.Bubble, i % 8, false);
            Check(world.Count == 81, "Eighty consecutive strokes require no bubble energy or ammunition.");
            yield return new WaitForSeconds(11);
            Check(world.Count == 81, "Creations outlive ordinary bubble lifetimes.");
            world.ClearWorld();
            BuildShowcase();
            inputBrush.enabled = true;
            world.Notify("创造没有配方。让泡泡成为你的桥、花园与浮岛。" );
            UIRuntime.Instance.SendMessage("RefreshSandboxHud");
            yield return new WaitForSeconds(.4f);
            yield return Capture("creat-gameplay.png");
            UIRuntime.Instance.ShowSandboxWorkshop(); yield return new WaitForSecondsRealtime(.7f);
            Check(UIRuntime.Instance.IsBlockingGameplay && Time.timeScale == 0, "The palette workshop blocks gameplay while open.");
            int swatchCount = 0; Button mixer = null, lastPreset = null;
            foreach (Button button in UIRuntime.Instance.GetComponentsInChildren<Button>())
            {
                if (button.name.StartsWith("Palette ")) swatchCount++;
                if (button.name == "Palette 39") lastPreset = button;
                if (button.name == "Free color mixer") mixer = button;
            }
            Check(swatchCount == 40 && mixer != null, "The workshop exposes forty swatches and free-color controls.");
            lastPreset.onClick.Invoke();
            Check(inputBrush.ColorIndex == 39 && !inputBrush.HasCustomColor, "Clicking a swatch selects its preset.");
            Check(lastPreset.GetComponentInChildren<Text>().color == Color.white,
                "Dark swatches retain readable white labels after focus and page animations.");
            yield return Capture("creat-workshop.png");
            mixer.onClick.Invoke(); yield return new WaitForSecondsRealtime(.9f);
            InputField hexField = UIRuntime.Instance.GetComponentInChildren<InputField>();
            Check(hexField.IsInteractable(), "Free-color controls accept input after the page opens.");
            hexField.text = "#123456"; hexField.onEndEdit.Invoke(hexField.text);
            Check(inputBrush.SelectedColor == custom && inputBrush.HasCustomColor, "The HEX field applies a custom brush color.");
            hexField.text = "#GGGGGG"; hexField.onEndEdit.Invoke(hexField.text);
            Check(inputBrush.SelectedColor == custom, "Invalid HEX entered in the workshop keeps the valid selection.");
            Slider red = null;
            foreach (Slider slider in UIRuntime.Instance.GetComponentsInChildren<Slider>()) if (slider.name == "RGB 0") red = slider;
            red.value = 230;
            Check(inputBrush.SelectedColor == (Color)new Color32(230, 52, 86, 255) && hexField.text == "#E63456",
                "Dragging RGB synchronizes the brush and HEX field without rebuilding the page.");
            yield return Capture("creat-color-mixer.png");
            UIRuntime.Instance.Back();
            Check(inputBrush.HasCustomColor && inputBrush.ColorName == "#E63456", "Closing free-color controls keeps the custom brush.");
            UIRuntime.Instance.Back();
            yield return new WaitForSecondsRealtime(.3f);
            Check(Time.timeScale > 0, "Closing the workshop restores gameplay time.");
            UnityEngine.SceneManagement.SceneManager.LoadScene(UIRuntime.Instance.Config.menuScene);
            yield return new WaitForSecondsRealtime(1.2f);
            UIRuntime.Instance.SendMessage("ShowMenuActions");
            yield return new WaitForSecondsRealtime(1);
            yield return Capture("creat-menu-entry.png");
            Check(errors.Count == 0, "No Unity runtime errors occurred.");
            Finish();
        }
        [Serializable] private sealed class BoundsFixture
        {
            public int version = 2;
            public Vector3Int dimensions = new Vector3Int(24, 12, 24);
            public float cellSize = 2;
            public Vector3 origin = new Vector3(-24, 0, -24);
            public List<SandboxCellData> cells = new List<SandboxCellData>();
        }
        private IEnumerator VerifyExpansion()
        {
            string playerSave = File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null;
            world.ClearWorld();
            Bounds bounds = world.WorldBounds;
            Check(bounds.size == new Vector3(96, 72, 48) && world.cellSize == 2,
                "The arena is 96 by 48 meters with a 72-meter ceiling and unchanged 2-meter cells.");
            Check(bounds.size.x * bounds.size.z == 48 * 48 * 2 && world.Capacity == 6912 * 6,
                "Floor area doubles, height triples, and build capacity grows to 41472 cells.");
            Physics.SyncTransforms();
            Check(Physics.Raycast(new Vector3(44, 20, 18), Vector3.down, out RaycastHit floor, 25) &&
                floor.collider.name.StartsWith("Creation floor") && Mathf.Abs(floor.point.y) < .01f,
                "The newly added floor area has a solid floor at ground level.");
            Check(Physics.Raycast(new Vector3(0, 40, 0), Vector3.right, out RaycastHit east, 60) && Mathf.Abs(east.point.x - 48) < .01f,
                "The east wall is at 48 meters and no old wall blocks the expanded area.");
            Check(Physics.Raycast(new Vector3(0, 40, 0), Vector3.left, out RaycastHit west, 60) && Mathf.Abs(west.point.x + 48) < .01f,
                "The west wall is at minus 48 meters.");
            Check(Physics.Raycast(new Vector3(44, 40, 0), Vector3.forward, out RaycastHit north, 30) && Mathf.Abs(north.point.z - 24) < .01f,
                "Side walls span the entire expanded width and height.");
            Check(Physics.Raycast(new Vector3(44, 20, 18), Vector3.up, out RaycastHit ceiling, 60) && Mathf.Abs(ceiling.point.y - 72) < .01f,
                "The physical ceiling is at 72 meters, with no old ceiling at 24 meters.");
            Vector3Int high = new Vector3Int(47, 35, 23);
            Check(world.Build(high, 1, SandboxShape.Block, 15, false) == 1 && world.Contains(high),
                "Construction works at the newly added far corner above the old ceiling.");
            Check(world.Lift(high, 1) == 0 && !world.Contains(high + Vector3Int.up) &&
                !world.Contains(new Vector3Int(48, 0, 0)) && !world.Contains(new Vector3Int(0, 0, 24)),
                "Building and floating still respect all new outer limits.");
            Check(world.Build(new Vector3Int(0, 20, 0), 1, SandboxShape.Block, 0, false) == 1,
                "Construction also works in the added western area.");

            Color custom = new Color32(230, 52, 86, 255);
            var original = new BoundsFixture();
            original.cells.Add(new SandboxCellData { position = Vector3Int.zero, color = 0, shape = SandboxShape.Block });
            original.cells.Add(new SandboxCellData { position = new Vector3Int(23, 11, 23), color = 7, shape = SandboxShape.Bubble });
            original.cells.Add(new SandboxCellData { position = new Vector3Int(12, 4, 8), color = 0, customColor = true, tint = custom, shape = SandboxShape.Block });
            string originalPath = Path.Combine(output, "original-bounds-v2.json");
            AtomicJsonFile.Write(originalPath, JsonUtility.ToJson(original));
            string originalBytes = File.ReadAllText(originalPath);
            bool migrated = world.LoadWorld(originalPath) && world.Count == original.cells.Count;
            foreach (SandboxCellData source in original.cells)
            {
                Vector3Int target = source.position + new Vector3Int(12, 0, 0);
                migrated &= world.TryGetCell(target, out SandboxCellData loaded) && loaded.shape == source.shape && loaded.PaintColor == source.PaintColor &&
                    world.CellCenter(target) == original.origin + ((Vector3)source.position + Vector3.one * .5f) * original.cellSize;
            }
            Check(migrated, "Original version-two creations keep their positions, sizes, shapes and custom colors after expansion.");
            Check(File.ReadAllText(originalPath) == originalBytes, "Loading an original save leaves its source file unchanged.");
            string expandedPath = Path.Combine(output, "expanded-world.json");
            world.Build(high, 1, SandboxShape.Block, 0, false, custom);
            Check(world.SaveWorld(expandedPath), "Expanded worlds serialize successfully.");
            world.ClearWorld();
            Check(world.LoadWorld(expandedPath) && world.TryGetCell(high, out SandboxCellData expanded) && expanded.PaintColor == custom &&
                world.TryGetCell(new Vector3Int(12, 0, 0), out _),
                "Expanded saves restore high cells and never shift migrated creations a second time.");
            int count = world.Count;
            original.cells[0] = new SandboxCellData { position = new Vector3Int(24, 0, 0), color = 0 };
            string invalidPath = Path.Combine(output, "invalid-original-bounds.json");
            AtomicJsonFile.Write(invalidPath, JsonUtility.ToJson(original));
            Check(!world.LoadWorld(invalidPath) && world.Count == count,
                "A cell outside its original saved bounds is rejected before replacing the current work.");
            string versionOne = Path.Combine(output, "original-bounds-v1.json");
            AtomicJsonFile.Write(versionOne, "{\"version\":1,\"dimensions\":{\"x\":24,\"y\":12,\"z\":24},\"cellSize\":2," +
                "\"origin\":{\"x\":-24,\"y\":0,\"z\":-24},\"cells\":[{\"position\":{\"x\":23,\"y\":11,\"z\":23},\"color\":7,\"shape\":1}]}");
            Check(world.LoadWorld(versionOne) && world.Count == 1 && world.TryGetCell(new Vector3Int(35, 11, 23), out SandboxCellData v1) && v1.color == 7,
                "Original version-one saves also migrate with their existing palette colors.");
            world.ClearWorld();
            for (int y = 0; y < 36; y++)
            {
                world.Build(new Vector3Int(4, y, 18), 1, SandboxShape.Block, y % 8, false);
                world.Build(new Vector3Int(43, y, 18), 1, SandboxShape.Bubble, 15, false);
            }
            for (int x = 5; x < 43; x++) world.Build(new Vector3Int(x, 34, 18), 1, SandboxShape.Bubble, 0, false);
            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player != null) player.enabled = false;
            Camera view = Camera.main;
            view.transform.position = new Vector3(104, 104, -126);
            view.transform.rotation = Quaternion.LookRotation(new Vector3(0, 30, 0) - view.transform.position);
            RenderSettings.fog = false;
            world.Notify("底面积 ×2 · 高度 ×3 · 41472 格自由创造空间");
            UIRuntime.Instance.SendMessage("RefreshSandboxHud");
            yield return null;
            yield return Capture("creat-expanded-arena.png");
            Check((playerSave == null && !File.Exists(world.SavePath)) || (playerSave != null && File.ReadAllText(world.SavePath) == playerSave),
                "Expansion verification never modifies the player's real save.");
            Check(errors.Count == 0, "Expanded arena runs without Unity runtime errors.");
        }
        private IEnumerator CapturePages()
        {
            SandboxBrush brush = FindFirstObjectByType<SandboxBrush>();
            brush.SelectColor(39);
            UIRuntime.Instance.ShowSandboxWorkshop();
            yield return new WaitForSecondsRealtime(1);
            yield return Capture("creat-workshop.png");
            foreach (Button button in UIRuntime.Instance.GetComponentsInChildren<Button>())
                if (button.name == "Free color mixer") { button.onClick.Invoke(); break; }
            yield return new WaitForSecondsRealtime(1);
            InputField hex = UIRuntime.Instance.GetComponentInChildren<InputField>();
            hex.text = "#E63456"; hex.onEndEdit.Invoke(hex.text);
            yield return Capture("creat-color-mixer.png");
            UIRuntime.Instance.Back(); UIRuntime.Instance.Back();
            UnityEngine.SceneManagement.SceneManager.LoadScene(UIRuntime.Instance.Config.menuScene);
            yield return new WaitForSecondsRealtime(1.2f);
            UIRuntime.Instance.SendMessage("ShowMenuActions");
            yield return new WaitForSecondsRealtime(1);
            yield return Capture("creat-menu-entry.png");
            Check(errors.Count == 0, "UI previews render without Unity runtime errors.");
        }
        private string DimensionsJson() => "{\"x\":" + world.dimensions.x + ",\"y\":" + world.dimensions.y + ",\"z\":" + world.dimensions.z + "}";
        private void BuildShowcase()
        {
            for (int x = 6; x <= 17; x++) for (int z = 9; z <= 14; z++)
                if (x == 6 || x == 17 || z == 9 || z == 14) world.Build(new Vector3Int(x, 0, z), 1, SandboxShape.Bubble, (x + z) % 8, false);
            for (int y = 1; y <= 4; y++) foreach (int x in new[] { 7, 16 })
                world.Build(new Vector3Int(x, y, 13), 1, SandboxShape.Block, y % 2 == 0 ? 2 : 7, false);
            for (int x = 7; x <= 16; x++) world.Build(new Vector3Int(x, 5, 13), 1, SandboxShape.Bubble, x % 8, false);
            world.Build(new Vector3Int(12, 2, 18), 5, SandboxShape.Bubble, 0, false);
            world.Build(new Vector3Int(12, 4, 18), 3, SandboxShape.Bubble, 3, false);
        }
        private IEnumerator Capture(string name, int width = 1600, int height = 900)
        {
            Camera view = Camera.main;
            Canvas canvas = UIRuntime.Instance.GetComponentInChildren<Canvas>();
            var target = new RenderTexture(width, height, 24); target.Create();
            RenderTexture previousTarget = view.targetTexture;
            view.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = view; canvas.planeDistance = 1;
            yield return null; yield return null;
            // The disabled runtime does not normally refit after the temporary render size changes.
            Canvas.ForceUpdateCanvases();
            if (BubbleSandbox.Instance != null) UIRuntime.Instance.SendMessage("RefreshSandboxHud");
            Canvas.ForceUpdateCanvases(); view.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
            RenderTexture.active = previous;
            canvas.worldCamera = null; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            view.targetTexture = previousTarget; Destroy(texture); Destroy(target);
        }
        private void Finish()
        {
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(Path.Combine(output, ArchivesOnly ? "archives-results.txt" : ModelPreviewOnly ? "model-preview-results.txt" : DestructionOnly ? "destruction-results.txt" : TargetingOnly ? "targeting-results.txt" : UnderfootOnly ? "underfoot-results.txt" : ExpansionOnly ? "expansion-results.txt" : PreviewOnly ? "preview-results.txt" : "results.txt"), string.Join("\n", results) + "\n" + string.Join("\n", errors));
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
