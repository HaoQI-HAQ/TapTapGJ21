using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PAO.Sandbox
{
    /// <summary>Explicit, toggleable selection. Frames follow the actual moving pieces without changing their materials.</summary>
    public sealed class SandboxSelection : MonoBehaviour
    {
        private readonly Dictionary<SandboxPiece, LineRenderer> selected = new Dictionary<SandboxPiece, LineRenderer>();
        private readonly List<SandboxPiece> removed = new List<SandboxPiece>();
        private Material frameMaterial;
        public int Count { get { Prune(); return selected.Count; } }
        public bool Contains(SandboxPiece piece) => piece != null && selected.ContainsKey(piece);

        public bool Toggle(SandboxPiece piece)
        {
            Prune();
            if (piece == null || !piece.gameObject.activeInHierarchy || BubbleSandbox.Instance == null ||
                !piece.transform.IsChildOf(BubbleSandbox.Instance.transform)) return false;
            if (selected.TryGetValue(piece, out LineRenderer frame))
            {
                if (frame != null) Destroy(frame.gameObject);
                selected.Remove(piece);
                return false;
            }
            if (frameMaterial == null)
            {
                frameMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                frameMaterial.name = "Sandbox selection red frame";
                frameMaterial.color = new Color(1, .12f, .16f);
            }
            var outline = new GameObject("Selected · red frame", typeof(LineRenderer));
            outline.transform.SetParent(piece.transform, false);
            LineRenderer line = outline.GetComponent<LineRenderer>();
            line.sharedMaterial = frameMaterial; line.useWorldSpace = false;
            line.widthMultiplier = .032f; line.numCornerVertices = 3; line.numCapVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            Vector3[] corners = {
                new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,-1,1), new Vector3(-1,-1,1), new Vector3(-1,-1,-1),
                new Vector3(-1,1,-1), new Vector3(1,1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(1,1,1),
                new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1), new Vector3(-1,-1,1), new Vector3(-1,1,1), new Vector3(-1,1,-1) };
            Bounds bounds = piece.GetComponent<MeshFilter>().sharedMesh.bounds;
            for (int i = 0; i < corners.Length; i++) corners[i] = bounds.center + Vector3.Scale(corners[i], bounds.extents + Vector3.one * .018f);
            line.positionCount = corners.Length; line.SetPositions(corners);
            selected.Add(piece, line);
            return true;
        }

        public SandboxPiece[] Snapshot()
        {
            Prune();
            var pieces = new SandboxPiece[selected.Count]; selected.Keys.CopyTo(pieces, 0); return pieces;
        }

        public void Transfer(SandboxPiece source, SandboxPiece survivor)
        {
            if (!Contains(source)) return;
            Toggle(source);
            if (!Contains(survivor)) Toggle(survivor);
        }

        public void Clear()
        {
            foreach (LineRenderer frame in selected.Values) if (frame != null) Destroy(frame.gameObject);
            selected.Clear();
        }

        private void Prune()
        {
            removed.Clear();
            foreach (var item in selected)
                if (item.Key == null || !item.Key.gameObject.activeInHierarchy)
                {
                    if (item.Value != null) Destroy(item.Value.gameObject);
                    removed.Add(item.Key);
                }
            foreach (SandboxPiece piece in removed) selected.Remove(piece);
        }

        private void LateUpdate() { Prune(); }
        private void OnDestroy() { Clear(); if (frameMaterial != null) Destroy(frameMaterial); }
    }
}
