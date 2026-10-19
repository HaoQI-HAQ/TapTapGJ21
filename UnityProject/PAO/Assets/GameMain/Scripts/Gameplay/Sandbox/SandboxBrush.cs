using PAO.UI.V2;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PAO.Sandbox
{
    public enum SandboxTool { Lift, Build, Destroy, Lower, Select }
    /// <summary>Captures a brush at release; its travelling bubble applies the edit on arrival.</summary>
    public sealed class SandboxBrush : MonoBehaviour
    {
        public int ColorIndex { get; private set; }
        public bool HasCustomColor { get; private set; }
        private Color customColor;
        public Color SelectedColor => HasCustomColor ? customColor : BubbleSandbox.Palette[ColorIndex];
        public string ColorName => HasCustomColor ? "#" + ColorUtility.ToHtmlStringRGB(customColor) : BubbleSandbox.ColorNames[ColorIndex];
        public int Diameter { get; private set; } = 1;
        public SandboxShape Shape { get; private set; }
        public bool Paint { get; private set; }
        public bool Continuous { get; set; }
        public bool IsCharging { get; private set; }
        public float ChargeProgress => Mathf.Clamp01(chargeTime / 1.2f);
        public bool HasTarget { get; private set; }
        public Vector3Int TargetCell { get; private set; }
        public Vector3 TargetPoint { get; private set; }
        public bool TargetingUnderfoot { get; private set; }
        public bool CanPlaceTarget => HasTarget && (Tool != SandboxTool.Build || Paint || world.CanBuild(TargetCell, Diameter, Shape));
        public SandboxPiece HoveredPiece { get; private set; }
        public SandboxSelection Selection { get; private set; }
        private SandboxTool? extraTool;
        private bool originalSwitcherEnabled;
        public SandboxTool Tool => extraTool ?? (SandboxTool)(switcher != null ? (int)switcher.Current : 1);
        private float chargeTime, nextShot;
        private bool waitForRelease;
        private GameObject chargedBubble;
        private LineRenderer preview;
        private Material bubbleMaterial, previewMaterial;
        private BubbleSandbox world;
        private BubbleTypeSwitcher switcher;
        private RaycastHit[] hits = new RaycastHit[64];
        public string ToolName => Tool == SandboxTool.Lift ? "浮空" : Tool == SandboxTool.Lower ? "下浮" :
            Tool == SandboxTool.Select ? "选中" : Tool == SandboxTool.Destroy ? "破坏" : Paint ? "染色" : "建造";
        public Color BrushColor => Tool == SandboxTool.Destroy ? new Color(1, .45f, .3f) :
            Tool == SandboxTool.Lift ? new Color(.45f, .8f, 1) : Tool == SandboxTool.Lower ? new Color(.72f, .6f, 1) :
            Tool == SandboxTool.Select ? new Color(1, .12f, .16f) : SelectedColor;

        private void Start()
        {
            world = BubbleSandbox.Instance; switcher = GetComponent<BubbleTypeSwitcher>();
            if (switcher != null) { originalSwitcherEnabled = switcher.enabled; switcher.enabled = false; }
            Selection = gameObject.AddComponent<SandboxSelection>();
            SelectTool(SandboxTool.Build);
            bubbleMaterial = new Material(world.creationMaterial); bubbleMaterial.name = "Brush bubble";
            previewMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            GameObject outline = new GameObject("Brush footprint preview"); outline.transform.SetParent(world.transform, false);
            preview = outline.AddComponent<LineRenderer>(); preview.sharedMaterial = previewMaterial;
            preview.useWorldSpace = true; preview.widthMultiplier = .035f; preview.positionCount = 16;
            preview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        public void SelectColor(int index)
        { int count = BubbleSandbox.Palette.Length; ColorIndex = (index % count + count) % count; HasCustomColor = false; }
        public void SelectCustomColor(Color value)
        {
            customColor = (Color32)new Color(Mathf.Clamp01(value.r), Mathf.Clamp01(value.g), Mathf.Clamp01(value.b), 1);
            HasCustomColor = true;
        }
        public bool SelectHexColor(string value)
        {
            string hex = (value ?? "").Trim().TrimStart('#');
            if (hex.Length != 6 || !ColorUtility.TryParseHtmlString("#" + hex, out Color color)) return false;
            SelectCustomColor(color); return true;
        }
        public void CycleSize() { Diameter = Diameter == 1 ? 3 : Diameter == 3 ? 5 : 1; }
        public void ToggleShape() { Shape = Shape == SandboxShape.Bubble ? SandboxShape.Block : SandboxShape.Bubble; }
        public void TogglePaint() { Paint = !Paint; }
        public void SelectTool(SandboxTool tool)
        {
            CancelCharge();
            if (switcher == null) switcher = GetComponent<BubbleTypeSwitcher>();
            extraTool = (int)tool >= 3 ? tool : (SandboxTool?)null;
            if ((int)tool < 3 && switcher != null) switcher.SetType((BubbleType)tool);
            waitForRelease = UIInput.Held(UIInput.Charge);
        }
        public bool ToggleTargetSelection()
        {
            if (Selection == null || HoveredPiece == null) { world.Notify("将十字准心对准一个造物，再点击选中。" ); return false; }
            bool added = Selection.Toggle(HoveredPiece);
            world.Notify((added ? "已选中" : "已取消选中") + " · 共 " + Selection.Count + " 件");
            return added;
        }
        public void ClearSelection() { if (Selection != null) Selection.Clear(); world.Notify("已清除全部选择。" ); }
        public bool MoveSelected(int direction)
        {
            if (Selection == null || Selection.Count == 0) { world.Notify("先用选中模式选择造物。" ); return false; }
            return FireSelection(0, direction < 0, Selection.Snapshot());
        }
        public bool MoveVertical(int direction)
        {
            CancelCharge();
            if (UIInput.Blocked || Time.timeScale <= 0) return false;
            if (Selection != null && Selection.Count > 0) return MoveSelected(direction);
            UpdateTarget();
            if (HoveredPiece == null) { world.Notify("选中造物，或将十字准心对准造物，再按 Q 上浮 / E 下浮。" ); return false; }
            GameObject bubble = CreateVisual(MouthPosition(), .3f);
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", direction < 0 ? new Color(.72f, .6f, 1) : new Color(.45f, .8f, 1));
            bubble.GetComponent<Renderer>().SetPropertyBlock(tint);
            bubble.AddComponent<SandboxBubbleShot>().Initialize(world, HoveredPiece.Data.position, HoveredPiece.transform.position,
                BubbleType.Sticky, Diameter, Shape, ColorIndex, false, 0, null, direction < 0);
            return true;
        }
        private void CycleTool(int direction)
        {
            SandboxTool[] tools = { SandboxTool.Build, SandboxTool.Destroy, SandboxTool.Select };
            int current = System.Array.IndexOf(tools, Tool);
            SelectTool(tools[(Mathf.Max(0, current) + direction + tools.Length) % tools.Length]);
        }
        private void Update()
        {
            if (world == null) return;
            if (UIInput.Blocked || Time.timeScale <= 0)
            { CancelCharge(); waitForRelease = true; preview.enabled = false; return; }
            bool held = UIInput.Held(UIInput.Charge);
            if (waitForRelease)
            { if (held) return; waitForRelease = false; }
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool clearSelection = Input.GetKeyDown(KeyCode.R);
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.PageUp) || UIInput.ControllerDown(UIInput.PadBinding(0))) MoveVertical(1);
            else if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.PageDown)) MoveVertical(-1);
            else if (Input.GetKeyDown(KeyCode.Alpha1) || UIInput.ControllerDown(UIInput.PadBinding(1))) SelectTool(SandboxTool.Build);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || UIInput.ControllerDown(UIInput.PadBinding(2))) SelectTool(SandboxTool.Destroy);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectTool(SandboxTool.Select);
            else
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (UIInput.ControllerDown(UIInput.PadBinding(UIInput.PreviousBubble)) || scroll < -.05f) CycleTool(-1);
                else if (UIInput.ControllerDown(UIInput.PadBinding(UIInput.NextBubble)) || scroll > .05f) CycleTool(1);
            }
            if (clearSelection) ClearSelection();
            if (control && Input.GetKeyDown(KeyCode.Z)) world.Undo();
            if (control && Input.GetKeyDown(KeyCode.Y)) world.Redo();
            if (!control && Input.GetKeyDown(KeyCode.C)) SelectColor(ColorIndex + 1);
            if (Input.GetKeyDown(KeyCode.V)) SelectColor(ColorIndex - 1);
            if (Input.GetKeyDown(KeyCode.X)) CycleSize();
            if (Input.GetKeyDown(KeyCode.G)) ToggleShape();
            if (Input.GetKeyDown(KeyCode.P)) TogglePaint();
            if (Input.GetKeyDown(KeyCode.T)) { Continuous = !Continuous; CancelCharge(); }
            if (Input.GetKeyDown(KeyCode.F5)) world.SaveWorld();
            if (Input.GetKeyDown(KeyCode.Home)) world.Respawn();
            if (Input.GetKeyDown(KeyCode.F6) && UIRuntime.Instance != null) { UIRuntime.Instance.ShowSandboxPreview(); return; }
            if (Input.GetKeyDown(KeyCode.B) && UIRuntime.Instance != null) { UIRuntime.Instance.ShowSandboxWorkshop(); return; }
            UpdateTarget(); DrawPreview();
            if (Cursor.lockState != CursorLockMode.Locked && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            { CancelCharge(); return; }
            bool pressed = UIInput.Down(UIInput.Charge);
            if (Tool == SandboxTool.Build && UIInput.Down(UIInput.Detonate))
            { CancelCharge(); Fire(0); return; }
            if (Tool == SandboxTool.Select)
            {
                CancelCharge();
                if (pressed) ToggleTargetSelection();
                return;
            }
            if (Continuous)
            {
                if (held && Time.time >= nextShot)
                { Fire(0); nextShot = Time.time + .22f; }
                return;
            }
            if (pressed)
            {
                if (UIInput.ToggleCharge && IsCharging) { Release(); return; }
                BeginCharge();
            }
            if (!IsCharging) return;
            chargeTime += Time.deltaTime;
            if (chargedBubble != null)
            {
                chargedBubble.transform.position = MouthPosition();
                chargedBubble.transform.localScale = Vector3.one * Mathf.Lerp(.25f, 1.1f, ChargeProgress);
                bubbleMaterial.color = BrushColor;
            }
            if (!UIInput.ToggleCharge && !held) Release();
        }
        private Vector3 MouthPosition() => transform.position + Vector3.up * 1.25f + transform.forward * .7f;
        private void BeginCharge()
        {
            CancelCharge(); IsCharging = true; chargeTime = 0;
            if (switcher != null) switcher.IsLocked = true;
            chargedBubble = CreateVisual(MouthPosition(), .25f);
        }
        private void Release() { float progress = ChargeProgress; CancelCharge(); Fire(progress); }
        private void CancelCharge()
        {
            IsCharging = false; chargeTime = 0;
            if (switcher != null) switcher.IsLocked = false;
            if (chargedBubble != null) Destroy(chargedBubble); chargedBubble = null;
        }
        private GameObject CreateVisual(Vector3 position, float size)
        {
            GameObject bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere); bubble.name = "Blown brush bubble";
            Collider collision = bubble.GetComponent<Collider>(); collision.enabled = false; Destroy(collision);
            bubble.transform.SetParent(world.transform, false); bubble.transform.position = position;
            bubble.transform.localScale = Vector3.one * size;
            bubble.GetComponent<Renderer>().sharedMaterial = bubbleMaterial; bubbleMaterial.color = BrushColor;
            return bubble;
        }
        public bool Fire(float charge)
        {
            if (Tool == SandboxTool.Select) return false;
            if ((Tool == SandboxTool.Lift || Tool == SandboxTool.Lower) && Selection != null && Selection.Count > 0)
                return FireSelection(charge, Tool == SandboxTool.Lower, Selection.Snapshot());
            if (Tool == SandboxTool.Destroy && Selection != null && Selection.Count > 0)
            {
                bool fired = FireSelection(charge, false, Selection.Snapshot(), BubbleType.Bomb);
                // One batch per press keeps continuous fire from spilling into unselected creations.
                if (fired) waitForRelease = true;
                return fired;
            }
            if (!HasTarget) { world.Notify("瞄准场地或造物；所有创造都要留在光框内。" ); return false; }
            if (!CanPlaceTarget) { world.Notify("红框内不能放置；脚下搭高时先跳起。" ); return false; }
            GameObject bubble = CreateVisual(MouthPosition(), Mathf.Lerp(.3f, .8f, charge));
            // Shared material colors are captured per shot with a property block.
            var tint = new MaterialPropertyBlock(); tint.SetColor("_BaseColor", BrushColor);
            bubble.GetComponent<Renderer>().SetPropertyBlock(tint);
            bubble.AddComponent<SandboxBubbleShot>().Initialize(world, TargetCell, TargetPoint,
                Tool == SandboxTool.Lower ? BubbleType.Sticky : (BubbleType)Tool, Diameter, Shape, ColorIndex, Paint, charge,
                HasCustomColor ? (Color?)SelectedColor : null, Tool == SandboxTool.Lower);
            return true;
        }
        private bool FireSelection(float charge, bool lower, SandboxPiece[] pieces, BubbleType ability = BubbleType.Sticky)
        {
            if (pieces.Length == 0) return false;
            Vector3 destination = Vector3.zero;
            foreach (SandboxPiece piece in pieces) destination += piece.transform.position;
            destination /= pieces.Length;
            GameObject bubble = CreateVisual(MouthPosition(), Mathf.Lerp(.3f, .8f, charge));
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", ability == BubbleType.Bomb ? new Color(1, .45f, .3f) :
                lower ? new Color(.72f, .6f, 1) : new Color(.45f, .8f, 1));
            bubble.GetComponent<Renderer>().SetPropertyBlock(tint);
            bubble.AddComponent<SandboxBubbleShot>().Initialize(world, pieces[0].Data.position, destination,
                ability, Diameter, Shape, ColorIndex, false, charge, null, lower, pieces);
            return true;
        }
        public void AimUnderfoot()
        {
            HasTarget = false; HoveredPiece = null;
            TargetingUnderfoot = Tool == SandboxTool.Build && !Paint;
            CharacterController controller = GetComponent<CharacterController>();
            if (!TargetingUnderfoot || world == null || controller == null || !controller.enabled) return;
            Bounds body = controller.bounds;
            // Aim at the supporting face, so jumping keeps the same intended cell below the player.
            Ray ray = new Ray(body.center + Vector3.up * .1f, Vector3.down);
            int nearest = NearestHit(ray);
            if (nearest < 0) return;
            RaycastHit hit = hits[nearest];
            SandboxPiece support = hit.collider.GetComponent<SandboxPiece>();
            HoveredPiece = support;
            Vector3Int bottom = support != null ? support.Data.position + Vector3Int.up :
                world.WorldToCell(hit.point + Vector3.up * .02f);
            int radius = Diameter / 2;
            TargetCell = bottom + Vector3Int.up * radius;
            TargetPoint = hit.point;
            HasTarget = world.Contains(TargetCell - Vector3Int.up * radius) &&
                world.Contains(TargetCell + Vector3Int.up * radius);
        }
        private void UpdateTarget()
        {
            if (Tool == SandboxTool.Build && !Paint && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
            { AimUnderfoot(); return; }
            Camera camera = Camera.main; HasTarget = false; HoveredPiece = null; TargetingUnderfoot = false;
            if (camera == null) return;
            Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f));
            int nearest = NearestHit(ray, true);
            bool build = Tool == SandboxTool.Build && !Paint;
            if (nearest >= 0)
            {
                RaycastHit hit = hits[nearest]; SandboxPiece piece = hit.collider.GetComponent<SandboxPiece>();
                HoveredPiece = piece;
                TargetPoint = hit.point;
                if (piece != null)
                {
                    Vector3 normal = hit.normal; Vector3Int direction;
                    float x = Mathf.Abs(normal.x), y = Mathf.Abs(normal.y), z = Mathf.Abs(normal.z);
                    if (x > y && x > z) direction = new Vector3Int(normal.x >= 0 ? 1 : -1, 0, 0);
                    else if (y > z) direction = new Vector3Int(0, normal.y >= 0 ? 1 : -1, 0);
                    else direction = new Vector3Int(0, 0, normal.z >= 0 ? 1 : -1);
                    TargetCell = piece.Data.position + (build ? direction : Vector3Int.zero);
                    HasTarget = world.Contains(TargetCell);
                }
                else if (build) { TargetCell = world.WorldToCell(hit.point + hit.normal * .02f); HasTarget = world.Contains(TargetCell); }
            }
            else if (build)
            {
                TargetPoint = ray.GetPoint(12); TargetCell = world.WorldToCell(TargetPoint); HasTarget = world.Contains(TargetCell);
            }
        }
        private int NearestHit(Ray ray, bool cameraTarget = false)
        {
            // Reach every cell in the arena, even when the third-person camera is outside its bounds.
            Bounds bounds = world.WorldBounds;
            float range = bounds.size.magnitude + Vector3.Distance(ray.origin, bounds.ClosestPoint(ray.origin)) + world.cellSize;
            int count;
            while ((count = Physics.RaycastNonAlloc(ray, hits, range, ~(1 << 2), QueryTriggerInteraction.Ignore)) == hits.Length)
                System.Array.Resize(ref hits, hits.Length * 2);
            int nearest = -1; float distance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].transform.IsChildOf(transform)) continue;
                // A low camera can sit below the dissolved floor. Only view targeting sees through it;
                // underfoot placement and player physics must still use the solid supporting surface.
                if (cameraTarget && hits[i].collider.TryGetComponent(out CameraOcclusionParticles particles) && particles.IsObstructing)
                    continue;
                if (hits[i].distance < distance) { nearest = i; distance = hits[i].distance; }
            }
            return nearest;
        }
        private void DrawPreview()
        {
            preview.enabled = HasTarget && Tool != SandboxTool.Select &&
                !(Tool == SandboxTool.Destroy && Selection != null && Selection.Count > 0);
            if (!preview.enabled) return;
            previewMaterial.color = CanPlaceTarget ? BrushColor : new Color(1, .18f, .2f);
            Vector3 c = world.CellCenter(TargetCell); float r = world.cellSize * Diameter * .5f;
            Vector3[] points = {
                new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,-1,1), new Vector3(-1,-1,1), new Vector3(-1,-1,-1),
                new Vector3(-1,1,-1), new Vector3(1,1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(1,1,1),
                new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1), new Vector3(-1,-1,1), new Vector3(-1,1,1), new Vector3(-1,1,-1) };
            for (int i = 0; i < points.Length; i++) preview.SetPosition(i, c + points[i] * r);
        }
        private void OnDisable() { CancelCharge(); if (preview != null) preview.enabled = false; }
        private void OnDestroy()
        {
            if (switcher != null) switcher.enabled = originalSwitcherEnabled;
            if (preview != null) Destroy(preview.gameObject);
            if (bubbleMaterial != null) Destroy(bubbleMaterial); if (previewMaterial != null) Destroy(previewMaterial);
        }
    }
}
