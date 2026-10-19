using PAO.UI.V2;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PAO.Sandbox
{
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
        private float chargeTime, nextShot;
        private bool waitForRelease;
        private GameObject chargedBubble;
        private LineRenderer preview;
        private Material bubbleMaterial, previewMaterial;
        private BubbleSandbox world;
        private BubbleTypeSwitcher switcher;
        private readonly RaycastHit[] hits = new RaycastHit[64];
        public string ToolName => switcher != null && switcher.Current == BubbleType.Sticky ? "浮空" :
            switcher != null && switcher.Current == BubbleType.Bomb ? "破坏" : Paint ? "染色" : "建造";
        public Color BrushColor => switcher != null && switcher.Current == BubbleType.Bomb ? new Color(1, .45f, .3f) :
            switcher != null && switcher.Current == BubbleType.Sticky ? new Color(.45f, .8f, 1) : SelectedColor;

        private void Start()
        {
            world = BubbleSandbox.Instance; switcher = GetComponent<BubbleTypeSwitcher>();
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
        private void Update()
        {
            if (world == null) return;
            if (UIInput.Blocked || Time.timeScale <= 0)
            { CancelCharge(); waitForRelease = true; preview.enabled = false; return; }
            bool held = UIInput.Held(UIInput.Charge);
            if (waitForRelease)
            { if (held) return; waitForRelease = false; }
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
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
            if (Input.GetKeyDown(KeyCode.B) && UIRuntime.Instance != null) { UIRuntime.Instance.ShowSandboxWorkshop(); return; }
            UpdateTarget(); DrawPreview();
            if (Cursor.lockState != CursorLockMode.Locked && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            { CancelCharge(); return; }
            bool pressed = UIInput.Down(UIInput.Charge);
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
            if (!HasTarget) { world.Notify("瞄准场地或造物；所有创造都要留在光框内。" ); return false; }
            GameObject bubble = CreateVisual(MouthPosition(), Mathf.Lerp(.3f, .8f, charge));
            // Shared material colors are captured per shot with a property block.
            var tint = new MaterialPropertyBlock(); tint.SetColor("_BaseColor", BrushColor);
            bubble.GetComponent<Renderer>().SetPropertyBlock(tint);
            bubble.AddComponent<SandboxBubbleShot>().Initialize(world, TargetCell, TargetPoint,
                switcher != null ? switcher.Current : BubbleType.Bouncy, Diameter, Shape, ColorIndex, Paint, charge,
                HasCustomColor ? (Color?)SelectedColor : null);
            return true;
        }
        private void UpdateTarget()
        {
            Camera camera = Camera.main; HasTarget = false;
            if (camera == null) return;
            Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f));
            int count = Physics.RaycastNonAlloc(ray, hits, 38, ~(1 << 2), QueryTriggerInteraction.Ignore);
            int nearest = -1; float distance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].transform.IsChildOf(transform) || hits[i].transform == transform) continue;
                if (hits[i].distance < distance) { nearest = i; distance = hits[i].distance; }
            }
            bool build = switcher != null && switcher.Current == BubbleType.Bouncy && !Paint;
            if (nearest >= 0)
            {
                RaycastHit hit = hits[nearest]; SandboxPiece piece = hit.collider.GetComponent<SandboxPiece>();
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
        private void DrawPreview()
        {
            preview.enabled = HasTarget; if (!HasTarget) return;
            previewMaterial.color = BrushColor;
            Vector3 c = world.CellCenter(TargetCell); float r = world.cellSize * Diameter * .5f;
            Vector3[] points = {
                new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,-1,1), new Vector3(-1,-1,1), new Vector3(-1,-1,-1),
                new Vector3(-1,1,-1), new Vector3(1,1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(1,1,1),
                new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1), new Vector3(-1,-1,1), new Vector3(-1,1,1), new Vector3(-1,1,-1) };
            for (int i = 0; i < points.Length; i++) preview.SetPosition(i, c + points[i] * r);
        }
        private void OnDisable() { CancelCharge(); if (preview != null) preview.enabled = false; }
        private void OnDestroy()
        { if (bubbleMaterial != null) Destroy(bubbleMaterial); if (previewMaterial != null) Destroy(previewMaterial); }
    }
}
