#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.Sandbox
{
    /// <summary>Opt-in, editable bubble sculpture, saved as a named work without replacing autosave.</summary>
    [InitializeOnLoad]
    internal static class CreatNaiWaRequest
    {
        private const string RequestPath = "Library/CreatNaiWa.request";
        private const string Pending = "PAO.Creat.NaiWa.Pending";
        private const string Restore = "PAO.Creat.NaiWa.Restore";
        private const string PreviousScene = "PAO.Creat.NaiWa.PreviousScene";

        static CreatNaiWaRequest()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Restore, false))
                {
                    EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousScene, ""));
                    SessionState.SetBool(Restore, false);
                }
            };
        }

        [MenuItem("PAO/Creat/拼搭参考图奶娃", false, 30)]
        private static void Request() => File.WriteAllText(RequestPath, "build");
        [MenuItem("PAO/Creat/拼搭抱肚大笑奶娃（精细版）", false, 31)]
        private static void RequestDetailed() => File.WriteAllText(RequestPath, "detailed");

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(RequestPath) && !SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool("PAO.Creat.NaiWa.Detailed", File.ReadAllText(RequestPath).Trim() == "detailed");
                File.Delete(RequestPath);
                SessionState.SetBool(Pending, true);
                File.WriteAllText("Library/CreatNaiWa.status", "STARTING");
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.SetString(PreviousScene, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                    SessionState.SetBool(Restore, true);
                    EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/GameMain/Scenes/Creat.unity");
                    EditorApplication.isPlaying = true;
                }
            }
            if (!EditorApplication.isPlaying || !SessionState.GetBool(Pending, false)) return;
            BubbleSandbox world = BubbleSandbox.Instance;
            if (world == null || UIRuntime.Instance == null) return;
            SessionState.SetBool(Pending, false);
            CreatNaiWaSculpture builder = new GameObject("Reference NaiWa sculpture builder").AddComponent<CreatNaiWaSculpture>();
            builder.Detailed = SessionState.GetBool("PAO.Creat.NaiWa.Detailed", false);
        }
    }

    internal sealed partial class CreatNaiWaSculpture : MonoBehaviour
    {
        internal bool Detailed { get; set; }
        private string Folder => Detailed ? "output/CreatNaiWaDetailed" : "output/CreatNaiWa";
        private string WorkName => Detailed ? "奶娃 · 抱肚大笑精细版" : "奶娃 · 捧脸小黄龙";
        private static readonly Color Yellow = new Color32(255, 191, 20, 255);
        private static readonly Color Cream = new Color32(255, 226, 153, 255);
        private static readonly Color Green = new Color32(29, 94, 70, 255);
        private static readonly Color Ink = new Color32(24, 36, 28, 255);
        private static readonly Color Mouth = new Color32(117, 62, 21, 255);
        private static readonly Color Blush = new Color32(240, 152, 33, 255);
        private BubbleSandbox world;
        private SandboxWorkInfo work;

        [Serializable]
        private sealed class SculptureData
        {
            public int version = 2;
            public Vector3Int dimensions;
            public float cellSize;
            public Vector3 origin;
            public List<SandboxCellData> cells = new List<SandboxCellData>();
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            yield return new WaitForSecondsRealtime(.8f);
            if (!BuildAndSave()) yield break;
            if (UIRuntime.Instance.IsSandboxPreview) UIRuntime.Instance.Back();
            UIRuntime.Instance.ShowSandboxPreview();
            yield return null; yield return null;
            SandboxPreviewView preview = UIRuntime.Instance.GetComponentInChildren<SandboxPreviewView>();
            if (preview == null) { Fail(new InvalidOperationException("Creation preview did not open.")); yield break; }
            // The sculpture faces -Z, matching the preview's front-view convention.
            preview.SetView(0, Detailed ? 4 : 3);
            yield return CapturePreview(preview, "naiwa-front.png");
            preview.SetView(Detailed ? 22 : -18, 8);
            yield return CapturePreview(preview, "naiwa-three-quarter.png");
            preview.SetView(90, 5);
            yield return CapturePreview(preview, "naiwa-side.png");
            preview.SetView(Detailed ? 20 : -15, 6);
            yield return CaptureScreen("naiwa-in-creat.png");
            string report = "DONE\nName: " + work.Name + "\nCells: " + world.Count +
                "\nArchive: " + Path.Combine(new SandboxWorkArchive().DirectoryPath, work.Id + ".json") +
                "\nWorld: " + Path.GetFullPath(Folder + "/naiwa-world.json") +
                "\nPreview: " + Path.GetFullPath(Folder + "/naiwa-front.png") +
                "\nNamed archive reload: PASS\nAutosave: preserved\n";
            if (Detailed) report += "Fine bubble spacing: 1m (standard: 2m)\nStandard/fine lattice round-trip: PASS\n";
            File.WriteAllText(Folder + "/result.txt", report);
            File.WriteAllText("Library/CreatNaiWa.status", report);
            world.Notify("奶娃已拼好 · 已保存独立作品 · Esc 返回继续编辑");
            Debug.Log("[Creat NaiWa] " + report);
        }

        private bool BuildAndSave()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                world = BubbleSandbox.Instance;
                if (world == null) throw new InvalidOperationException("Open Creat first.");
                var archive = new SandboxWorkArchive();
                // Preserve any live unsaved creation as an independent backup before showing the sculpture.
                if (UIRuntime.Instance.IsSandboxPreview) UIRuntime.Instance.Back();
                if (world.Count > 0 && !File.Exists(Folder + "/previous-work.json"))
                {
                    File.WriteAllText(Folder + "/previous-work.json", world.CaptureWorldJson());
                    if (!archive.SaveNew(world, "拼奶娃前的作品 " + DateTime.Now.ToString("MM-dd HHmm"), out _, out string backupError))
                        throw new IOException(backupError);
                }
                world.restoreSavedWorld = false;
                SculptureData data = Detailed ? MakeDetailedSculpture() : MakeSculpture();
                string json = JsonUtility.ToJson(data);
                File.WriteAllText(Folder + "/naiwa-world.json", json);
                if (!world.RestoreWorldJson(json)) throw new InvalidDataException("Sculpture was rejected by the sandbox.");
                string idPath = Folder + "/archive-id.txt";
                string existing = File.Exists(idPath) ? File.ReadAllText(idPath).Trim() : null;
                List<SandboxWorkInfo> works = archive.List(out _);
                work = works.Find(item => item.Id == existing && item.Name == WorkName);
                if (work != null)
                {
                    if (!archive.Overwrite(world, work.Id, out string overwriteError)) throw new IOException(overwriteError);
                }
                else
                {
                    if (!archive.SaveNew(world, WorkName, out work, out string saveError)) throw new IOException(saveError);
                    File.WriteAllText(idPath, work.Id);
                }
                if (!archive.Load(world, work.Id, out string loadError) || world.Count != data.cells.Count)
                    throw new InvalidDataException(loadError ?? "Named work did not round-trip.");
                if (Detailed) VerifyFineArchive(data, archive);
                return true;
            }
            catch (Exception error) { Fail(error); return false; }
        }

        private SculptureData MakeSculpture()
        {
            var data = new SculptureData { dimensions = world.dimensions, cellSize = world.cellSize, origin = world.origin };
            for (int y = 0; y < 35; y++)
            for (int z = -9; z <= 10; z++)
            for (int x = -11; x <= 11; x++)
            {
                Vector3 p = new Vector3(x, y, z);
                bool body = Ellipsoid(p, new Vector3(0, 17.6f, 0), new Vector3(7.4f, 9.2f, 5.6f));
                bool head = Ellipsoid(p, new Vector3(0, 28.1f, -.2f), new Vector3(5.6f, 5.9f, 4.8f));
                bool shoulder = Ellipsoid(p, new Vector3(0, 23.6f, .1f), new Vector3(6.3f, 4.8f, 4.8f));
                bool jaw = Ellipsoid(p, new Vector3(0, 25.5f, -1.5f), new Vector3(5.1f, 3.5f, 4));
                bool nose = Ellipsoid(p, new Vector3(0, 27.1f, -4.5f), new Vector3(2.5f, 1.65f, 1.5f));
                bool legs = false, arms = false, hands = false;
                foreach (int side in new[] { -1, 1 })
                {
                    legs |= Ellipsoid(p, new Vector3(side * 3.3f, 6, .6f), new Vector3(2.75f, 5.6f, 3));
                    legs |= Ellipsoid(p, new Vector3(side * 3.4f, 1.3f, -.8f), new Vector3(2.9f, 1.8f, 3.9f));
                    arms |= Capsule(p, new Vector3(side * 5.6f, 24.1f, .1f), new Vector3(side * 7.4f, 21.2f, -1.3f), 2.25f);
                    arms |= Capsule(p, new Vector3(side * 7.4f, 21.2f, -1.3f), new Vector3(side * 2.8f, 24.2f, -5.7f), 1.75f);
                    hands |= Ellipsoid(p, new Vector3(side * 2.1f, 24.5f, -6), new Vector3(1.75f, 2, 1.6f));
                }
                bool tail = Capsule(p, new Vector3(0, 11, 4.4f), new Vector3(0, 8.1f, 8.2f), 1.8f);
                Color paint = Yellow;
                bool solid = body || head || shoulder || jaw || nose || legs || arms || hands || tail;
                if (body && !arms && !hands && z <= -3 && x * x / 35.5f + (y - 16.2f) * (y - 16.2f) / 39.5f <= 1)
                    paint = Cream;
                // Eyes protrude from the curved forehead; dark pupils and white catchlights face the viewer.
                foreach (int side in new[] { -1, 1 })
                {
                    if (Ellipsoid(p, new Vector3(side * 2.7f, 30, -4.2f), new Vector3(1.45f, 1.9f, 1.25f)))
                    { solid = true; paint = Green; }
                    if (Ellipsoid(p, new Vector3(side * 2.6f, 30.1f, -5.1f), new Vector3(.78f, 1.25f, .8f)))
                    { solid = true; paint = Ink; }
                    if (x == side * 2 && y == 31 && z == -5) { solid = true; paint = Color.white; }
                    if (head && z <= -4 && x == side * 4 && y == 27) paint = Blush;
                }
                if (Ellipsoid(p, new Vector3(0, 25.3f, -5.7f), new Vector3(1.2f, .85f, 1)))
                { solid = true; paint = Mouth; }
                // A small smiling line remains visible between the two hands.
                if (hands) paint = Yellow;
                if (!solid) continue;
                Vector3Int position = new Vector3Int(x + 24, y, z + 12);
                if (!world.Contains(position)) throw new InvalidDataException("Sculpture exceeds the creation volume.");
                data.cells.Add(new SandboxCellData { position = position, color = 11, customColor = true, tint = paint, shape = SandboxShape.Bubble });
            }
            return data;
        }

        private static bool Ellipsoid(Vector3 point, Vector3 center, Vector3 radius)
        {
            Vector3 d = point - center;
            return d.x * d.x / (radius.x * radius.x) + d.y * d.y / (radius.y * radius.y) + d.z * d.z / (radius.z * radius.z) <= 1;
        }
        private static bool Capsule(Vector3 point, Vector3 a, Vector3 b, float radius)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude);
            return (point - a - ab * t).sqrMagnitude <= radius * radius;
        }

        private IEnumerator CapturePreview(SandboxPreviewView preview, string name)
        {
            yield return null;
            Camera camera = preview.ViewCamera;
            RenderTexture previous = camera.targetTexture;
            float aspect = camera.aspect;
            var target = new RenderTexture(1600, 1600, 24) { antiAliasing = 4 };
            target.Create(); camera.targetTexture = target; camera.aspect = 1; preview.FrameAll();
            camera.Render(); WriteImage(target, name);
            camera.targetTexture = previous; camera.aspect = aspect; preview.FrameAll();
            target.Release(); Destroy(target);
        }

        private IEnumerator CaptureScreen(string name)
        {
            Camera camera = Camera.main;
            Canvas canvas = UIRuntime.Instance.GetComponentInChildren<Canvas>();
            RenderTexture previous = camera.targetTexture;
            var target = new RenderTexture(1920, 1080, 24); target.Create();
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = Mathf.Max(1, camera.nearClipPlane + .1f);
            yield return null; yield return null;
            Canvas.ForceUpdateCanvases(); camera.Render(); WriteImage(target, name);
            canvas.worldCamera = null; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = previous; target.Release(); Destroy(target);
        }

        private void WriteImage(RenderTexture target, string name)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Folder + "/" + name, pixels.EncodeToPNG());
            RenderTexture.active = previous; Destroy(pixels);
        }
        private static void Fail(Exception error)
        {
            File.WriteAllText("Library/CreatNaiWa.status", "FAILED\n" + error);
            Debug.LogException(error);
        }
    }
}
#endif
