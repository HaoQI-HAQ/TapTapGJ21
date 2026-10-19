#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using PAO.UI.V2;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.Sandbox
{
    public sealed partial class SandboxVerificationRunner
    {
        [Serializable] private sealed class ArchiveFixture
        {
            public int version;
            public string id, name, worldJson;
            public int cellCount;
            public long updatedUtcTicks;
        }
        private Button ArchiveButton(string name)
        {
            Button result = null;
            foreach (Button button in UIRuntime.Instance.GetComponentsInChildren<Button>())
                if (button.name == name) result = button;
            return result;
        }
        private InputField ArchiveNameField()
        {
            InputField result = null;
            foreach (InputField field in UIRuntime.Instance.GetComponentsInChildren<InputField>())
                if (field.name == "Sandbox work name") result = field;
            return result;
        }
        private IEnumerator VerifyWorkArchives(SandboxBrush brush)
        {
            string savedWorld = File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null;
            string folder = Path.Combine(output, "ArchiveTests", Guid.NewGuid().ToString("N"));
            var archive = new SandboxWorkArchive(folder);
            UIRuntime runtime = UIRuntime.Instance;
            FieldInfo archiveField = typeof(UIRuntime).GetField("sandboxWorkArchive", BindingFlags.Instance | BindingFlags.NonPublic);
            object previousArchive = archiveField.GetValue(runtime);
            FieldInfo dirty = typeof(BubbleSandbox).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3Int p = new Vector3Int(10, 5, 8), q = p + new Vector3Int(3, 2, 1);
            Color tint = new Color32(230, 52, 86, 255);
            brush.Selection.Clear(); world.ClearWorld();
            archiveField.SetValue(runtime, archive);
            try
            {
                Check(archive.List(out string warning).Count == 0 && warning == null && !Directory.Exists(folder),
                    "A fresh work archive lists an empty state without creating or replacing any saves.");
                world.Build(p, 1, SandboxShape.Block, 0, false, tint);
                world.Build(q, 1, SandboxShape.Bubble, 3, false);
                bool wasDirty = (bool)dirty.GetValue(world);
                Check(archive.SaveNew(world, "  云端花园 / 一号  ", out SandboxWorkInfo first, out string error) &&
                    first.Name == "云端花园 / 一号" && first.CellCount == 2 &&
                    (bool)dirty.GetValue(world) == wasDirty,
                    "Named snapshots preserve Chinese names, exact counts and the pending autosave state.");
                string firstPath = Path.Combine(folder, first.Id + ".json");
                Check(File.Exists(firstPath) && File.ReadAllText(firstPath).Contains("\"checksum\""),
                    "Work data and metadata are committed together in one checksummed atomic file.");
                world.ClearWorld(); world.Build(new Vector3Int(22, 6, 17), 1, SandboxShape.Bubble, 4, false);
                Check(archive.SaveNew(world, "泡泡小屋", out SandboxWorkInfo second, out error),
                    "A second named work saves independently from the first.");
                string secondPath = Path.Combine(folder, second.Id + ".json"), secondBytes = File.ReadAllText(secondPath);
                var reopened = new SandboxWorkArchive(folder);
                var works = reopened.List(out warning);
                Check(works.Count == 2 && warning == null && works[0].Id == second.Id && works[1].Id == first.Id,
                    "Reopening the archive restores separate named works in newest-first order.");
                Check(reopened.Load(world, first.Id, out error) && world.Count == 2 &&
                    world.TryGetCell(p, out SandboxCellData data) && data.shape == SandboxShape.Block && data.PaintColor == tint &&
                    world.TryGetCell(q, out data) && data.shape == SandboxShape.Bubble && data.color == 3 &&
                    (bool)dirty.GetValue(world),
                    "Loading a work restores exact geometry and custom colors and schedules the rolling autosave.");
                world.Build(p + Vector3Int.up, 1, SandboxShape.Block, 7, false);
                Check(archive.Overwrite(world, first.Id, out error) && File.Exists(firstPath + ".bak") &&
                    File.ReadAllText(secondPath) == secondBytes && archive.List(out _).Count == 2,
                    "Overwrite updates only the chosen work, retains an atomic backup and does not create a new slot.");
                Check(archive.Rename(first.Id, "云端花园", out error) && archive.List(out _)[0].Name == "云端花园" &&
                    archive.Load(world, first.Id, out error) && world.Count == 3 && world.TryGetCell(p + Vector3Int.up, out _),
                    "Renaming changes the title while preserving all saved creation data.");
                Check(!archive.SaveNew(world, "   ", out _, out error) && !string.IsNullOrEmpty(error) &&
                    !archive.Rename(first.Id, new string('名', 33), out _) && !archive.SaveNew(world, "一\n二", out _, out _) &&
                    archive.List(out _).Count == 2,
                    "Empty, oversized and multiline names are rejected without changing any work slots.");
                Check(!archive.Load(world, "../sandbox", out _) && !archive.Delete("../sandbox", out _) &&
                    !archive.Overwrite(world, "../sandbox", out _) && world.Count == 3,
                    "Invalid slot identifiers cannot reach outside the archive or alter the current work.");
                string originalBytes = File.ReadAllText(firstPath);
                File.WriteAllText(firstPath, "{\"format\":1,\"payload\":\"bad\",\"checksum\":\"bad\"}");
                Check(!archive.Load(world, first.Id, out error) && world.Count == 3 && world.TryGetCell(p, out _) &&
                    archive.List(out warning).Count == 1 && !string.IsNullOrEmpty(warning),
                    "A corrupt archive is reported and cannot replace the current scene or hide other readable works.");
                File.WriteAllText(firstPath, originalBytes);
                ArchiveFixture invalid = JsonUtility.FromJson<ArchiveFixture>(AtomicJsonFile.Read(firstPath));
                invalid.worldJson = "{\"version\":99}";
                AtomicJsonFile.Write(firstPath, JsonUtility.ToJson(invalid));
                Check(!archive.Load(world, first.Id, out error) && world.Count == 3 && world.TryGetCell(p, out _),
                    "A valid checksum with invalid world contents is fully rejected before scene replacement.");
                File.WriteAllText(firstPath, originalBytes);
                string blocked = Path.Combine(folder, "blocked-root"); File.WriteAllText(blocked, "occupied");
                Check(!new SandboxWorkArchive(blocked).SaveNew(world, "不能写入", out _, out error) && world.Count == 3 &&
                    File.ReadAllText(firstPath) == originalBytes,
                    "A failed disk write reports failure and preserves both current creations and existing work files.");
                File.WriteAllText(firstPath + ".tmp", "unfinished");
                Check(archive.Delete(first.Id, out error) && !File.Exists(firstPath) && !File.Exists(firstPath + ".bak") &&
                    !File.Exists(firstPath + ".tmp") && archive.List(out _).Count == 1 && File.ReadAllText(secondPath) == secondBytes,
                    "Deleting a work removes its own main, backup and temporary files while preserving other works.");
                archive.SaveNew(world, "云端花园", out first, out _);

                runtime.ShowPause(); yield return new WaitForSecondsRealtime(.4f);
                Check(ArchiveButton("作品存档") != null && ArchiveButton("保存作品") != null,
                    "Sandbox pause exposes both named saving and the work archive.");
                ArchiveButton("作品存档").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
                Check(runtime.IsBlockingGameplay && Time.timeScale == 0 && ArchiveButton("Load work " + first.Id) != null &&
                    ArchiveButton("Overwrite work " + second.Id) != null && ArchiveButton("Load sandbox autosave") != null,
                    "The archive page pauses editing and exposes named work actions plus the existing rolling save.");
                yield return Capture("creat-work-archives.png");
                yield return Capture("creat-work-archives-compact.png", 1280, 720);
                ArchiveButton("Save new sandbox work").onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
                ArchiveNameField().text = "   "; ArchiveButton("Confirm sandbox work name").onClick.Invoke();
                Check(ArchiveNameField() != null && archive.List(out _).Count == 2,
                    "The save-name dialog keeps invalid input open without writing an empty-named work.");
                ArchiveNameField().text = "泡泡纪念馆";
                yield return Capture("creat-work-save-dialog.png");
                ArchiveButton("Confirm sandbox work name").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
                Check(archive.List(out _).Count == 3 && ArchiveNameField() == null &&
                    ArchiveButton("Save new sandbox work") != null && runtime.IsBlockingGameplay,
                    "Saving through the name dialog creates a new work and refreshes the archive without resuming gameplay.");
                ArchiveButton("Rename work " + second.Id).onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
                ArchiveNameField().text = "泡泡小屋 · 夜";
                ArchiveButton("Confirm sandbox work name").onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
                Check(archive.List(out _).Exists(work => work.Id == second.Id && work.Name == "泡泡小屋 · 夜"),
                    "The rename dialog updates a chosen work in the archive list.");
                ArchiveButton("Overwrite work " + second.Id).onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
                runtime.Back(); yield return null;
                Check(archive.List(out _).Find(work => work.Id == second.Id).CellCount == 1,
                    "Cancelling overwrite preserves the original stored work.");
                ArchiveButton("Overwrite work " + second.Id).onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
                ArchiveButton("确认").onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
                Check(archive.List(out _).Find(work => work.Id == second.Id).CellCount == 3,
                    "Confirmed overwrite refreshes the existing work with the current creation count.");
                ArchiveButton("Delete work " + second.Id).onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
                runtime.Back(); yield return null;
                Check(archive.List(out _).Count == 3, "Cancelling delete leaves every named work intact.");
                ArchiveButton("Delete work " + second.Id).onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
                ArchiveButton("确认").onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
                Check(archive.List(out _).Count == 2 && world.Count == 3,
                    "Confirmed delete removes only the chosen archive and preserves the current scene.");
                runtime.Back(); yield return null;
                int scenes = SceneManager.sceneCount;
                runtime.ShowSandboxPreview(); yield return new WaitForSecondsRealtime(.4f);
                SandboxPreviewView preview = runtime.GetComponentInChildren<SandboxPreviewView>();
                Check(ArchiveButton("Save preview work") != null && ArchiveButton("Open preview work archives") != null,
                    "Independent creation preview exposes save and archive entries.");
                yield return Capture("creat-preview-save-entry.png");
                yield return Capture("creat-preview-save-entry-compact.png", 1280, 720);
                ArchiveButton("Save preview work").onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
                ArchiveNameField().text = "预览里的作品";
                ArchiveButton("Confirm sandbox work name").onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
                Check(archive.List(out _).Count == 3 && runtime.IsSandboxPreview && preview.ObjectCount == 3 && Time.timeScale == 0,
                    "Saving from preview returns to the same independent preview and retains paused gameplay.");
                ArchiveButton("Open preview work archives").onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
                world.Build(p + Vector3Int.right, 1, SandboxShape.Bubble, 6, false);
                ArchiveButton("Load work " + first.Id).onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
                runtime.Back(); yield return null;
                Check(world.Count == 4 && runtime.IsBlockingGameplay,
                    "Cancelling load preserves the current unsaved creation and returns to the archive.");
                ArchiveButton("Load work " + first.Id).onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
                ArchiveButton("确认").onClick.Invoke(); yield return null; yield return null;
                Check(world.Count == 3 && !world.TryGetCell(p + Vector3Int.right, out _) &&
                    !runtime.IsBlockingGameplay && !runtime.IsSandboxPreview && Time.timeScale > 0 && SceneManager.sceneCount == scenes,
                    "Confirmed loading restores the saved work, disposes the prior preview and resumes building.");
                Check(savedWorld == (File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null),
                    "All archive verification preserves the player's real rolling save and uses an isolated work folder.");
            }
            finally
            {
                while (runtime.IsBlockingGameplay) runtime.Back();
                archiveField.SetValue(runtime, previousArchive);
                brush.Selection.Clear(); world.ClearWorld();
            }
        }
    }
}
#endif
