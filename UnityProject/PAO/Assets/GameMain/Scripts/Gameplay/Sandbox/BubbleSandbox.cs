using System;
using System.Collections.Generic;
using System.IO;
using PAO.UI.V2;
using UnityEngine;

namespace PAO.Sandbox
{
    public enum SandboxShape { Bubble, Block }

    [Serializable]
    public struct SandboxCellData
    {
        public Vector3Int position;
        public int color;
        public bool customColor;
        public Color tint;
        public SandboxShape shape;
        public Color PaintColor => customColor ? tint : BubbleSandbox.Palette[color];
    }

    /// <summary>A finite lattice of permanent creations. Only arriving bubble shots edit it.</summary>
    [DefaultExecutionOrder(-600)]
    public sealed class BubbleSandbox : MonoBehaviour
    {
        public static BubbleSandbox Instance { get; private set; }
        public static readonly Vector3 DefaultOrigin = new Vector3(-48, 0, -24);
        public static readonly Vector3Int DefaultDimensions = new Vector3Int(48, 36, 24);
        public Vector3 origin = DefaultOrigin;
        public Vector3Int dimensions = DefaultDimensions;
        public float cellSize = 2;
        public Material creationMaterial;
        public bool restoreSavedWorld = true;
        public Vector3 spawn = new Vector3(0, .15f, -18);
        public static readonly Color[] Palette = {
            new Color32(104, 222, 206, 255), new Color32(112, 184, 246, 255),
            new Color32(179, 153, 239, 255), new Color32(246, 155, 189, 255),
            new Color32(249, 183, 112, 255), new Color32(246, 224, 143, 255),
            new Color32(155, 215, 143, 255), new Color32(236, 245, 238, 255),
            new Color32(235, 61, 71, 255), new Color32(255, 112, 96, 255),
            new Color32(255, 144, 40, 255), new Color32(255, 202, 40, 255),
            new Color32(186, 218, 56, 255), new Color32(58, 180, 92, 255),
            new Color32(24, 192, 205, 255), new Color32(45, 114, 235, 255),
            new Color32(77, 76, 202, 255), new Color32(142, 70, 210, 255),
            new Color32(210, 63, 181, 255), new Color32(240, 72, 136, 255),
            new Color32(128, 31, 53, 255), new Color32(151, 64, 39, 255),
            new Color32(128, 91, 44, 255), new Color32(122, 128, 46, 255),
            new Color32(32, 104, 66, 255), new Color32(26, 107, 111, 255),
            new Color32(35, 68, 114, 255), new Color32(78, 48, 111, 255),
            new Color32(255, 219, 218, 255), new Color32(255, 225, 189, 255),
            new Color32(220, 235, 189, 255), new Color32(194, 228, 247, 255),
            new Color32(255, 255, 255, 255), new Color32(210, 214, 220, 255),
            new Color32(149, 158, 170, 255), new Color32(92, 103, 119, 255),
            new Color32(48, 56, 70, 255), new Color32(18, 21, 28, 255),
            new Color32(189, 156, 125, 255), new Color32(99, 72, 58, 255)
        };
        // Keep the first eight entries stable so existing creations retain their colors.
        public static readonly string[] ColorNames = { "薄荷", "晴空", "薰衣草", "樱花", "蜜橘", "奶油", "嫩叶", "珍珠",
            "朱红", "珊瑚", "鲜橙", "金黄", "青柠", "草绿", "湖蓝", "宝蓝",
            "靛蓝", "紫罗兰", "洋红", "玫红", "酒红", "陶土", "赭石", "橄榄",
            "森林", "深青", "海军蓝", "葡萄", "浅桃", "杏仁", "芽白", "冰蓝",
            "纯白", "银灰", "雾灰", "石板", "炭灰", "墨黑", "沙棕", "可可" };
        private static readonly string[] EnglishColorNames = { "Mint", "Sky", "Lavender", "Blossom", "Tangerine", "Cream", "Leaf", "Pearl",
            "Vermilion", "Coral", "Orange", "Gold", "Lime", "Grass", "Lake", "Royal Blue",
            "Indigo", "Violet", "Magenta", "Rose", "Burgundy", "Terracotta", "Ochre", "Olive",
            "Forest", "Teal", "Navy", "Grape", "Peach", "Almond", "Sprout", "Ice Blue",
            "White", "Silver", "Mist", "Slate", "Charcoal", "Ink", "Sand", "Cocoa" };
        public static string ColorName(int index)
        {
            index = Mathf.Clamp(index, 0, ColorNames.Length - 1);
            return UISettings.Text(ColorNames[index], EnglishColorNames[index]);
        }
        private readonly Dictionary<Vector3Int, SandboxPiece> cells = new Dictionary<Vector3Int, SandboxPiece>();
        private readonly List<SandboxPiece> moving = new List<SandboxPiece>();
        private readonly List<Edit> undo = new List<Edit>(), redo = new List<Edit>();
        private readonly Material[] colors = new Material[Palette.Length];
        private MaterialPropertyBlock customTint;
        private static readonly Vector3Int[] Neighbors = { Vector3Int.up, Vector3Int.down, Vector3Int.left,
            Vector3Int.right, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
        private Transform creations;
        private bool dirty;
        private float saveAt;
        private PlayerController player;
        private SandboxPiece riding;
        private Vector3 ridingLastPosition;
        private sealed class Edit
        {
            public readonly List<SandboxCellData> before = new List<SandboxCellData>();
            public readonly List<SandboxCellData> after = new List<SandboxCellData>();
        }
        [Serializable] private sealed class WorldData
        {
            public int version = 2;
            public Vector3Int dimensions;
            public float cellSize;
            public Vector3 origin;
            public string workId, workName;
            public List<SandboxCellData> cells = new List<SandboxCellData>();
        }
        public int Count => cells.Count;
        public int Capacity => dimensions.x * dimensions.y * dimensions.z;
        public int Revision { get; private set; }
        public int ShotGeneration { get; private set; }
        public string CurrentWorkId { get; private set; }
        public string CurrentWorkName { get; private set; }
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        private string statusChinese = "吹一颗泡泡，让想象落地。";
        private string statusEnglish = "Blow a bubble and bring your imagination to life.";
        private object[] statusArguments;
        private string renderedStatus;
        private int renderedStatusLanguage = int.MinValue;
        public bool HasStatusMessage { get; private set; }
        public string Status
        {
            get
            {
                int language = UISettings.Current != null ? UISettings.Current.language : 0;
                if (renderedStatus != null && renderedStatusLanguage == language) return renderedStatus;
                string text = UISettings.Text(statusChinese, statusEnglish);
                renderedStatus = statusArguments == null || statusArguments.Length == 0 ? text : string.Format(text, statusArguments);
                renderedStatusLanguage = language;
                return renderedStatus;
            }
        }
        public string SavePath => Path.Combine(Application.persistentDataPath, "Creat", "sandbox.json");
        public Bounds WorldBounds => new Bounds(origin + (Vector3)dimensions * cellSize * .5f, (Vector3)dimensions * cellSize);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }
        private void Awake()
        {
            Instance = this;
            customTint = new MaterialPropertyBlock();
            creations = new GameObject("Bubble creations").transform;
            creations.SetParent(transform, false);
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = creationMaterial != null ? new Material(creationMaterial) :
                    new Material(Shader.Find("Universal Render Pipeline/Lit"));
                colors[i].name = "Sandbox / " + ColorNames[i];
                colors[i].color = Palette[i];
                colors[i].SetFloat("_Smoothness", .78f);
                colors[i].enableInstancing = true;
            }
        }
        private void Start()
        {
            player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                player.UseOcclusionParticles = true;
                foreach (MeshRenderer solid in GetComponentsInChildren<MeshRenderer>())
                    if (!solid.transform.IsChildOf(player.transform)) CameraOcclusionParticles.Attach(solid.gameObject);
            }
            saveAt = Time.unscaledTime + 30;
            if (restoreSavedWorld && File.Exists(SavePath)) LoadWorld();
        }
        private void Update()
        {
            if (restoreSavedWorld && dirty && Time.unscaledTime >= saveAt && !UIInput.Blocked)
            { SaveWorld(); saveAt = Time.unscaledTime + 30; }
        }
        private void LateUpdate()
        {
            if (player != null && player.transform.position.y < origin.y - 4) Respawn();
            for (int i = moving.Count - 1; i >= 0; i--)
            {
                SandboxPiece piece = moving[i];
                if (piece == null) { moving.RemoveAt(i); continue; }
                Vector3 target = CellCenter(piece.Data.position);
                piece.transform.position = Vector3.MoveTowards(piece.transform.position, target, cellSize * 2.5f * Time.deltaTime);
                if (Vector3.SqrMagnitude(piece.transform.position - target) < .0001f) moving.RemoveAt(i);
            }
            if (riding != null && player != null)
            {
                CharacterController controller = player.GetComponent<CharacterController>();
                if (controller.enabled) controller.Move(riding.transform.position - ridingLastPosition);
                ridingLastPosition = riding.transform.position;
                Bounds bounds = riding.GetComponent<Collider>().bounds;
                Vector3 feet = controller.bounds.min;
                if (Mathf.Abs(player.transform.position.x - bounds.center.x) > cellSize * .6f ||
                    Mathf.Abs(player.transform.position.z - bounds.center.z) > cellSize * .6f ||
                    Mathf.Abs(feet.y - bounds.max.y) > .65f || !moving.Contains(riding)) riding = null;
            }
        }
        public bool Contains(Vector3Int p) => p.x >= 0 && p.y >= 0 && p.z >= 0 &&
            p.x < dimensions.x && p.y < dimensions.y && p.z < dimensions.z;
        public Vector3Int WorldToCell(Vector3 world) => Vector3Int.FloorToInt((world - origin) / cellSize);
        public Vector3 CellCenter(Vector3Int p) => origin + ((Vector3)p + Vector3.one * .5f) * cellSize;
        public bool TryGetCell(Vector3Int p, out SandboxCellData data)
        {
            if (cells.TryGetValue(p, out SandboxPiece piece)) { data = piece.Data; return true; }
            data = default; return false;
        }
        public void Notify(string message) { Notify(message, message); }
        public void Notify(string chinese, string english, params object[] arguments)
        {
            statusChinese = chinese; statusEnglish = english;
            statusArguments = arguments;
            renderedStatus = null;
            HasStatusMessage = true;
        }
        public IEnumerable<Vector3Int> Footprint(Vector3Int center, int diameter, SandboxShape shape)
        {
            // The preview is a box. Shape chooses each created solid, not which cells the brush affects.
            int radius = Mathf.Clamp(diameter / 2, 0, 2);
            for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            for (int z = -radius; z <= radius; z++)
                yield return center + new Vector3Int(x, y, z);
        }
        private bool OverlapsPlayer(Vector3Int p)
        {
            if (player == null) return false;
            return new Bounds(CellCenter(p), Vector3.one * (cellSize * .96f)).Intersects(player.GetComponent<CharacterController>().bounds);
        }
        public bool CanBuild(Vector3Int center, int diameter, SandboxShape shape)
        {
            bool empty = false;
            foreach (Vector3Int p in Footprint(center, diameter, shape))
            {
                if (!Contains(p) || cells.ContainsKey(p)) continue;
                if (OverlapsPlayer(p)) return false;
                empty = true;
            }
            return empty;
        }
        public int Build(Vector3Int center, int diameter, SandboxShape shape, int color, bool paint, Color? tint = null)
        {
            color = Mathf.Clamp(color, 0, Palette.Length - 1);
            if (tint.HasValue && !ValidTint(tint.Value)) return 0;
            Color selected = tint ?? Palette[color];
            if (!paint && !CanBuild(center, diameter, shape))
            { Notify("目标格被占用或碰到玩家；脚下搭高时先跳起。", "That cell is occupied or touches the player. Jump before building underfoot."); return 0; }
            Edit edit = new Edit();
            foreach (Vector3Int p in Footprint(center, diameter, shape))
            {
                if (!Contains(p)) continue;
                if (cells.TryGetValue(p, out SandboxPiece piece))
                {
                    if (!paint || piece.Data.PaintColor == selected) continue;
                    edit.before.Add(piece.Data);
                    SandboxCellData recolored = piece.Data;
                    recolored.color = color; recolored.customColor = tint.HasValue; recolored.tint = selected;
                    edit.after.Add(recolored);
                }
                else if (!paint)
                    edit.after.Add(new SandboxCellData { position = p, color = color, shape = shape,
                        customColor = tint.HasValue, tint = selected });
            }
            if (edit.after.Count == 0)
            {
                Notify(paint ? "瞄准已有造物，用泡泡染上颜色。" : "这里已满、超出边界，或离你太近了。",
                    paint ? "Aim at an existing creation to paint it with bubbles." : "This area is full, out of bounds, or too close to you.");
                return 0;
            }
            Apply(edit, true); Remember(edit);
            Notify(paint ? "染色 {0} 格 · 泡泡无限供应" : "建造 {0} 格 · 泡泡无限供应",
                paint ? "Painted {0} cells · Unlimited bubbles" : "Built {0} cells · Unlimited bubbles", edit.after.Count);
            return edit.after.Count;
        }
        public int DestroyBrush(Vector3Int center, int diameter, SandboxShape shape)
        {
            Edit edit = new Edit();
            foreach (Vector3Int p in Footprint(center, diameter, shape))
                if (cells.TryGetValue(p, out SandboxPiece piece)) edit.before.Add(piece.Data);
            if (edit.before.Count == 0) { Notify("破坏泡泡只拆除你的造物。", "Destroy bubbles only remove your creations."); return 0; }
            Apply(edit, true); Remember(edit); Notify("拆除 {0} 格 · Ctrl+Z 可撤销", "Removed {0} cells · Ctrl+Z to undo", edit.before.Count);
            return edit.before.Count;
        }
        public int DestroySelection(SandboxPiece[] pieces)
        {
            if (pieces == null || pieces.Length == 0) { Notify("先用选中模式选择造物。", "Choose creations with the Select tool first."); return 0; }
            var positions = new HashSet<Vector3Int>();
            Edit edit = new Edit();
            foreach (SandboxPiece piece in pieces)
            {
                if (piece == null || !cells.TryGetValue(piece.Data.position, out SandboxPiece current) || current != piece)
                { Notify("选中的造物已改变，请重新选择。", "The selected creations have changed. Select them again."); return 0; }
                if (positions.Add(piece.Data.position)) edit.before.Add(piece.Data);
            }
            Apply(edit, true); Remember(edit);
            Notify("已拆除选中的 {0} 件造物 · Ctrl+Z 可整批恢复", "Removed {0} selected creations · Ctrl+Z to restore the batch", edit.before.Count);
            return edit.before.Count;
        }
        public int Lift(Vector3Int start, int levels)
        { return MoveConnected(start, Mathf.Clamp(levels, 1, 3)); }
        public int Lower(Vector3Int start, int levels)
        { return MoveConnected(start, -Mathf.Clamp(levels, 1, 3)); }
        private int MoveConnected(Vector3Int start, int levels)
        {
            if (!cells.ContainsKey(start))
            {
                Notify(levels > 0 ? "将浮空泡泡吹向相连的造物。" : "将下浮泡泡吹向相连的造物。",
                    levels > 0 ? "Blow a Lift bubble at connected creations." : "Blow a Lower bubble at connected creations.");
                return 0;
            }
            var connected = new HashSet<Vector3Int> { start };
            var queue = new Queue<Vector3Int>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector3Int p = queue.Dequeue();
                foreach (Vector3Int direction in Neighbors)
                    if (cells.ContainsKey(p + direction) && connected.Add(p + direction)) queue.Enqueue(p + direction);
            }
            return MoveCells(connected, levels);
        }
        public int MoveSelection(SandboxPiece[] pieces, int levels)
        {
            if (pieces == null || pieces.Length == 0) { Notify("先用选中模式选择造物。", "Choose creations with the Select tool first."); return 0; }
            var selected = new HashSet<Vector3Int>();
            foreach (SandboxPiece piece in pieces)
            {
                if (piece == null || !cells.TryGetValue(piece.Data.position, out SandboxPiece current) || current != piece)
                { Notify("选中的造物已改变，请重新选择。", "The selected creations have changed. Select them again."); return 0; }
                selected.Add(piece.Data.position);
            }
            return MoveCells(selected, levels);
        }
        private int MoveCells(HashSet<Vector3Int> connected, int levels)
        {
            if (levels == 0) return 0;
            if (moving.Count > 0) { Notify("等造物浮稳，再继续移动。", "Wait for the creations to settle before moving them again."); return 0; }
            int count = Mathf.Clamp(Mathf.Abs(levels), 1, 3);
            Vector3Int direction = levels > 0 ? Vector3Int.up : Vector3Int.down;
            Vector3Int offset = direction * count;
            SandboxPiece support = null;
            if (player != null)
                foreach (Vector3Int p in connected)
                {
                    SandboxPiece piece = cells[p]; Bounds b = piece.GetComponent<Collider>().bounds;
                    if (Mathf.Abs(player.transform.position.x - b.center.x) < cellSize * .5f &&
                        Mathf.Abs(player.transform.position.z - b.center.z) < cellSize * .5f &&
                        Mathf.Abs(player.GetComponent<CharacterController>().bounds.min.y - b.max.y) < .3f) support = piece;
                }
            // Collect every bubble touched along the sweep before changing anything. A later
            // blocked cell must reject the whole edit, including all potential merges.
            var absorbed = new Dictionary<Vector3Int, SandboxPiece>();
            foreach (Vector3Int p in connected)
                for (int step = 1; step <= count; step++)
                {
                    Vector3Int destination = p + direction * step;
                    bool occupied = !connected.Contains(destination) && cells.TryGetValue(destination, out _);
                    bool merge = occupied && cells[p].Data.shape == SandboxShape.Bubble &&
                        cells[destination].Data.shape == SandboxShape.Bubble;
                    if (!Contains(destination) || (occupied && !merge) || (support == null && OverlapsPlayer(destination)))
                    {
                        Notify(levels > 0 ? "上方空间不足：不能穿过地面、其他造物或角色。" : "下方空间不足：不能穿过地面、其他造物或角色。",
                            levels > 0 ? "Not enough space above. Creations cannot pass through the ground, other creations, or the player." :
                            "Not enough space below. Creations cannot pass through the ground, other creations, or the player.");
                        return 0;
                    }
                    if (merge && !absorbed.ContainsKey(destination)) absorbed.Add(destination, cells[p]);
                }
            riding = support; if (support != null) ridingLastPosition = support.transform.position;
            Edit edit = new Edit(); var pieces = new List<SandboxPiece>();
            foreach (Vector3Int p in connected)
            {
                SandboxPiece piece = cells[p]; pieces.Add(piece); edit.before.Add(piece.Data);
                SandboxCellData data = piece.Data; data.position += offset; edit.after.Add(data);
            }
            SandboxSelection selection = absorbed.Count > 0 ? FindFirstObjectByType<SandboxSelection>() : null;
            foreach (var item in absorbed)
            {
                SandboxPiece stationary = cells[item.Key]; edit.before.Add(stationary.Data);
                if (selection != null) selection.Transfer(stationary, item.Value);
                Remove(item.Key);
            }
            foreach (Vector3Int p in connected) cells.Remove(p);
            for (int i = 0; i < pieces.Count; i++)
            {
                pieces[i].Data = edit.after[i]; cells.Add(edit.after[i].position, pieces[i]); moving.Add(pieces[i]);
            }
            Remember(edit);
            if (absorbed.Count > 0)
                Notify(levels > 0 ? "托起 {0} 格造物 · 上升 {1} 米 · 已合并 {2} 颗泡泡" : "下浮 {0} 格造物 · 下降 {1} 米 · 已合并 {2} 颗泡泡",
                    levels > 0 ? "Lifted {0} cells · Up {1} m · Merged {2} bubbles" : "Lowered {0} cells · Down {1} m · Merged {2} bubbles",
                    connected.Count, count * cellSize, absorbed.Count);
            else
                Notify(levels > 0 ? "托起 {0} 格造物 · 上升 {1} 米" : "下浮 {0} 格造物 · 下降 {1} 米",
                    levels > 0 ? "Lifted {0} cells · Up {1} m" : "Lowered {0} cells · Down {1} m", connected.Count, count * cellSize);
            return connected.Count;
        }
        private void Remember(Edit edit)
        {
            undo.Add(edit); if (undo.Count > 64) undo.RemoveAt(0); redo.Clear(); Changed();
        }
        private void Changed() { dirty = true; Revision++; saveAt = Mathf.Min(saveAt, Time.unscaledTime + 30); }
        private void Apply(Edit edit, bool forward)
        {
            foreach (SandboxCellData data in forward ? edit.before : edit.after) Remove(data.position);
            foreach (SandboxCellData data in forward ? edit.after : edit.before) Add(data);
        }
        private void Add(SandboxCellData data)
        {
            if (cells.ContainsKey(data.position)) Remove(data.position);
            GameObject go = GameObject.CreatePrimitive(data.shape == SandboxShape.Bubble ? PrimitiveType.Sphere : PrimitiveType.Cube);
            go.name = "Bubble " + data.position; go.transform.SetParent(creations, false);
            go.transform.position = CellCenter(data.position); go.transform.localScale = Vector3.one * cellSize;
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = colors[data.customColor ? 0 : data.color];
            if (data.customColor)
            {
                customTint.SetColor("_BaseColor", data.tint);
                renderer.SetPropertyBlock(customTint);
            }
            SandboxPiece piece = go.AddComponent<SandboxPiece>(); piece.Data = data; cells.Add(data.position, piece);
            CameraOcclusionParticles.Attach(go);
        }
        private void Remove(Vector3Int p)
        {
            if (!cells.TryGetValue(p, out SandboxPiece piece)) return;
            cells.Remove(p); moving.Remove(piece);
            if (piece != null) { piece.gameObject.SetActive(false); Destroy(piece.gameObject); }
        }
        public bool Undo()
        {
            if (!CanUndo) return false;
            ShotGeneration++;
            Edit edit = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            Apply(edit, false); redo.Add(edit); Changed(); Notify("已撤销上一笔。", "Undid the last edit."); return true;
        }
        public bool Redo()
        {
            if (!CanRedo) return false;
            Edit edit = redo[redo.Count - 1];
            foreach (SandboxCellData cell in edit.after)
                if (!cells.ContainsKey(cell.position) && OverlapsPlayer(cell.position))
                { Notify("重做的位置碰到玩家，请先离开或跳起。", "The redo would touch the player. Move away or jump first."); return false; }
            ShotGeneration++;
            redo.RemoveAt(redo.Count - 1);
            Apply(edit, true); undo.Add(edit); Changed(); Notify("已重做上一笔。", "Redid the last edit."); return true;
        }
        public void ClearWorld()
        {
            ShotGeneration++;
            if (Count == 0) return;
            Edit edit = new Edit(); foreach (SandboxPiece piece in cells.Values) edit.before.Add(piece.Data);
            Apply(edit, true); Remember(edit); Notify("场地已清空 · Ctrl+Z 可恢复。", "Cleared the arena · Ctrl+Z to restore.");
        }
        public bool SaveWorld(string path = null)
        {
            try
            {
                AtomicJsonFile.Write(path ?? SavePath, CaptureWorldJson());
                dirty = false; Notify("沙盒作品已保存。", "Sandbox creation saved."); return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { Notify("保存失败，请检查存储空间。", "Save failed. Check your storage space."); Debug.LogWarning("[Creat] Save failed: " + e.Message); return false; }
        }
        public bool LoadWorld(string path = null)
        {
            try
            {
                return RestoreWorldJson(AtomicJsonFile.Read(path ?? SavePath));
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException)
            { Notify("读取失败，当前作品已保留。", "Load failed. Your current creation has been kept."); Debug.LogWarning("[Creat] Load failed: " + e.Message); return false; }
        }
        internal string CaptureWorldJson()
        {
            WorldData data = new WorldData { dimensions = dimensions, cellSize = cellSize, origin = origin,
                workId = CurrentWorkId, workName = CurrentWorkName };
            foreach (SandboxPiece piece in cells.Values) data.cells.Add(piece.Data);
            return JsonUtility.ToJson(data);
        }
        internal bool RestoreWorldJson(string json, bool scheduleAutosave = false)
        {
            try
            {
                WorldData data = JsonUtility.FromJson<WorldData>(json);
                if (data == null || (data.version != 1 && data.version != 2) || data.cells == null)
                    throw new InvalidDataException("Incompatible sandbox save.");
                // A detailed work may use 1m bubbles within the same physical 96m x 72m x 48m arena.
                // Only these two bounded lattices are accepted; never resize from arbitrary save data.
                bool standardArena = origin == DefaultOrigin &&
                    ((cellSize == 2 && dimensions == DefaultDimensions) ||
                     (cellSize == 1 && dimensions == DefaultDimensions * 2));
                bool compatibleResolution = standardArena && data.origin == DefaultOrigin &&
                    ((data.cellSize == 2 && data.dimensions == DefaultDimensions) ||
                     (data.cellSize == 1 && data.dimensions == DefaultDimensions * 2));
                bool originalVolume = data.dimensions == new Vector3Int(24, 12, 24) &&
                    data.origin == new Vector3(-24, 0, -24) && data.cellSize == 2 && standardArena;
                Vector3Int targetDimensions = originalVolume ? DefaultDimensions : data.dimensions;
                float targetCellSize = originalVolume ? 2 : data.cellSize;
                Vector3 targetOrigin = originalVolume ? DefaultOrigin : data.origin;
                if (!originalVolume && !compatibleResolution &&
                    (data.cellSize != cellSize || data.dimensions != dimensions || data.origin != origin))
                    throw new InvalidDataException("Incompatible sandbox bounds.");
                if (data.cells.Count > targetDimensions.x * targetDimensions.y * targetDimensions.z)
                    throw new InvalidDataException("Sandbox capacity exceeded.");
                // Rebase original saves without moving or scaling any creations in world space.
                Vector3Int offset = originalVolume ? new Vector3Int(12, 0, 0) : Vector3Int.zero;
                var occupied = new HashSet<Vector3Int>();
                for (int i = 0; i < data.cells.Count; i++)
                {
                    SandboxCellData cell = data.cells[i];
                    Vector3Int source = cell.position;
                    cell.position += offset;
                    if (source.x < 0 || source.y < 0 || source.z < 0 || source.x >= data.dimensions.x ||
                        source.y >= data.dimensions.y || source.z >= data.dimensions.z ||
                        cell.position.x < 0 || cell.position.y < 0 || cell.position.z < 0 ||
                        cell.position.x >= targetDimensions.x || cell.position.y >= targetDimensions.y ||
                        cell.position.z >= targetDimensions.z || cell.color < 0 || cell.color >= Palette.Length ||
                        (cell.customColor && (data.version < 2 || !ValidTint(cell.tint))) ||
                        (cell.shape != SandboxShape.Bubble && cell.shape != SandboxShape.Block) || !occupied.Add(cell.position))
                        throw new InvalidDataException("Invalid cell in sandbox save.");
                    data.cells[i] = cell;
                }
                // Validate the complete file before replacing the current work.
                ClearWorld();
                dimensions = targetDimensions; cellSize = targetCellSize; origin = targetOrigin;
                foreach (SandboxCellData cell in data.cells) Add(cell);
                CurrentWorkId = Guid.TryParseExact(data.workId, "N", out _) ? data.workId : null;
                CurrentWorkName = SandboxWorkArchive.ValidateName(data.workName, out string name, out _) ? name : null;
                undo.Clear(); redo.Clear(); dirty = scheduleAutosave; Revision++;
                if (scheduleAutosave) saveAt = Time.unscaledTime + 30;
                Respawn(); Notify("已恢复 {0} 格泡泡造物。", "Restored {0} bubble cells.", Count); return true;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException)
            { Notify("读取失败，当前作品已保留。", "Load failed. Your current creation has been kept."); Debug.LogWarning("[Creat] Load failed: " + e.Message); return false; }
        }
        private static bool ValidTint(Color value) => value.r >= 0 && value.r <= 1 &&
            value.g >= 0 && value.g <= 1 && value.b >= 0 && value.b <= 1 && value.a == 1;
        internal void SetCurrentWork(string id, string name)
        {
            CurrentWorkId = id;
            CurrentWorkName = SandboxWorkArchive.ValidateName(name, out string normalized, out _) ? normalized : null;
            dirty = true;
        }
        public void Respawn()
        {
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;
            CharacterController controller = player.GetComponent<CharacterController>();
            Vector3 position = spawn;
            // A saved structure can occupy the entry. Place the player above that column.
            Vector3Int column = WorldToCell(spawn);
            for (int y = 0; y < dimensions.y; y++)
                if (cells.ContainsKey(new Vector3Int(column.x, y, column.z))) position.y = origin.y + (y + 1) * cellSize + .15f;
            position.y += (controller.height * .5f - controller.center.y) * player.transform.lossyScale.y;
            bool wasEnabled = controller.enabled; controller.enabled = false;
            player.transform.position = position; controller.enabled = wasEnabled; riding = null;
        }
        private void OnApplicationQuit() { if (restoreSavedWorld && dirty) SaveWorld(); }
        private void OnDestroy()
        {
            if (player != null) player.UseOcclusionParticles = false;
            CameraOcclusionParticles.ReleaseAll();
            if (Instance == this) Instance = null;
            foreach (Material material in colors) if (material != null) Destroy(material);
        }
    }
}
