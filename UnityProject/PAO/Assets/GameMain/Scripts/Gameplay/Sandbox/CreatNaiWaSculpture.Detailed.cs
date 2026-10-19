#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PAO.Sandbox
{
    internal sealed partial class CreatNaiWaSculpture
    {
        private static readonly Color LaughYellow = new Color32(247, 201, 58, 255);
        private static readonly Color LaughCream = new Color32(242, 224, 184, 255);
        private static readonly Color LaughHand = new Color32(106, 97, 72, 255);
        private static readonly Color LaughFinger = new Color32(119, 109, 81, 255);
        private static readonly Color LaughDark = new Color32(35, 24, 20, 255);
        private static readonly Color LaughLip = new Color32(133, 79, 38, 255);
        private static readonly Color LaughTooth = new Color32(255, 248, 225, 255);
        private static readonly Color LaughTongue = new Color32(168, 106, 101, 255);
        private static readonly Vector3 LaughHead = new Vector3(-4.6f, 53.1f, -5.3f);
        private static readonly Quaternion LaughHeadRotation = Quaternion.Euler(-12, 0, 13);

        private SculptureData MakeDetailedSculpture()
        {
            var data = new SculptureData { dimensions = BubbleSandbox.DefaultDimensions * 2,
                cellSize = 1, origin = BubbleSandbox.DefaultOrigin };
            var volume = new Dictionary<Vector3Int, Color>();
            Quaternion inverseHead = Quaternion.Inverse(LaughHeadRotation);
            for (int y = 0; y < 69; y++)
            for (int z = -22; z <= 21; z++)
            for (int x = -25; x <= 25; x++)
            {
                var p = new Vector3(x, y, z);
                Vector3 q = inverseHead * (p - LaughHead);
                bool belly = Ellipsoid(p, new Vector3(1, 32.5f, 2.3f), new Vector3(16.5f, 21, 12.4f));
                bool back = Ellipsoid(p, new Vector3(2.7f, 44.2f, 4.2f), new Vector3(13.7f, 12.1f, 10.2f));
                bool neck = Ellipsoid(p, new Vector3(-1.5f, 45.7f, -1.2f), new Vector3(11.2f, 11, 10));
                bool head = Ellipsoid(q, Vector3.zero, new Vector3(10.2f, 11.4f, 9.3f)) ||
                    Ellipsoid(q, new Vector3(0, -4.5f, -.8f), new Vector3(8.8f, 7.3f, 8.8f));
                bool legs = Capsule(p, new Vector3(-7.8f, 16, 3), new Vector3(-11, 4, -1), 4.7f) ||
                    Capsule(p, new Vector3(9.5f, 16, 4), new Vector3(12, 4, 1.3f), 4.9f) ||
                    Ellipsoid(p, new Vector3(-11.7f, 1.6f, -3.6f), new Vector3(4.7f, 2.4f, 5.6f)) ||
                    Ellipsoid(p, new Vector3(12, 1.6f, -1.5f), new Vector3(4.9f, 2.4f, 5.7f));
                bool tail = Capsule(p, new Vector3(1, 19, 12), new Vector3(4.5f, 11.5f, 18), 2.5f);
                bool arms = false, palms = false, fingers = false;
                foreach (int side in new[] { -1, 1 })
                {
                    float lift = side < 0 ? 1 : 0;
                    arms |= Capsule(p, new Vector3(side * 12.4f, 45 + lift, 1),
                        new Vector3(side * 18.2f, 34.2f + lift, -1), 4.2f);
                    arms |= Capsule(p, new Vector3(side * 18.2f, 34.2f + lift, -1),
                        new Vector3(side * 7.1f, 25.4f + lift, -12), 3.2f);
                    palms |= Ellipsoid(p, new Vector3(side * 5.7f, 24.5f + lift, -13), new Vector3(3.5f, 4.4f, 2.7f));
                    // Four separate rounded fingers curl inwards across the lower belly.
                    for (int finger = 0; finger < 4; finger++)
                    {
                        float fy = 26.5f - finger * 1.85f + lift;
                        Vector3 a = new Vector3(side * 6.3f, fy + .6f, -14.7f);
                        Vector3 b = new Vector3(side * 3.3f, fy - .65f, -16.1f);
                        Vector3 c = new Vector3(side * (1.1f + finger * .24f), fy - 1.15f, -15.4f);
                        fingers |= Capsule(p, a, b, .82f) || Capsule(p, b, c, .82f);
                    }
                    fingers |= Capsule(p, new Vector3(side * 6.8f, 27.2f + lift, -13.7f),
                        new Vector3(side * 2.6f, 29.1f + lift, -14.6f), 1.1f);
                }
                bool solid = belly || back || neck || head || legs || tail || arms || palms || fingers;
                Color paint = LaughYellow;
                // The cream patch follows the front surface instead of coloring a flat rectangular slice.
                float creamShape = (x - .3f) * (x - .3f) / (14.2f * 14.2f) +
                    (y - 31.4f) * (y - 31.4f) / (16.2f * 16.2f);
                if (belly && !arms && creamShape <= 1 && z < -1) paint = LaughCream;

                float faceFront = DetailedFaceFront(q.x, q.y);
                bool faceSurface = head && q.z <= faceFront + 1.65f;
                if (faceSurface)
                {
                    float eyeX = Mathf.Abs(q.x) - 5.15f;
                    if (Mathf.Abs(eyeX) <= 3.1f)
                    {
                        float arch = 3.8f + 1.2f * (1 - eyeX * eyeX / (3.1f * 3.1f));
                        if (Mathf.Abs(q.y - arch) < .57f) paint = LaughDark;
                        else if (q.y > arch + .57f && q.y < arch + 1.6f)
                            paint = new Color32(255, 216, 91, 255);
                    }
                    float u = q.x / 6.55f;
                    if (Mathf.Abs(u) < 1)
                    {
                        float upper = -1.6f + 1.6f * u * u;
                        float lower = -2.2f - 7 * Mathf.Sqrt(1 - u * u);
                        if (q.y < upper + .55f && q.y > lower - .55f)
                        {
                            paint = LaughLip;
                            if (q.y < upper && q.y > lower)
                            {
                                paint = LaughDark;
                                // A scalloped row of individual teeth follows the curved smile.
                                float nearest = Mathf.Round((q.x + 5.4f) / 1.8f) * 1.8f - 5.4f;
                                float toothDrop = 1.55f - .30f * Mathf.Abs(u);
                                if (q.y > upper - toothDrop && Mathf.Abs(q.x - nearest) < .68f) paint = LaughTooth;
                                if (q.y < lower + .62f && Mathf.Abs(u) < .79f) paint = LaughTooth;
                                if (q.y > lower + .65f && q.y < lower + 1.7f && Mathf.Abs(u) < .51f) paint = LaughTongue;
                            }
                        }
                    }
                }
                if (palms) paint = LaughHand;
                if (fingers) paint = LaughFinger;
                if (!solid) continue;
                volume.Add(new Vector3Int(x + 48, y, z + 24), paint);
            }
            // Keep a two-bubble-thick shell: visible detail stays intact, while the interior is editable and lightweight.
            Vector3Int[] neighbors = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right,
                new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            foreach (KeyValuePair<Vector3Int, Color> cell in volume)
            {
                bool surface = false;
                foreach (Vector3Int direction in neighbors)
                    if (!volume.ContainsKey(cell.Key + direction) || !volume.ContainsKey(cell.Key + direction * 2))
                    { surface = true; break; }
                if (!surface) continue;
                data.cells.Add(new SandboxCellData { position = cell.Key, color = 11, customColor = true,
                    tint = cell.Value, shape = SandboxShape.Bubble });
            }
            if (data.cells.Count == 0) throw new InvalidDataException("Detailed sculpture is empty.");
            return data;
        }

        private static float DetailedFaceFront(float x, float y)
        {
            float forehead = 1 - x * x / (10.2f * 10.2f) - y * y / (11.4f * 11.4f);
            float chin = 1 - x * x / (8.8f * 8.8f) - (y + 4.5f) * (y + 4.5f) / (7.3f * 7.3f);
            float a = forehead >= 0 ? -9.3f * Mathf.Sqrt(forehead) : 999;
            float b = chin >= 0 ? -.8f - 8.8f * Mathf.Sqrt(chin) : 999;
            return Mathf.Min(a, b);
        }

        private void VerifyFineArchive(SculptureData detailed, SandboxWorkArchive archive)
        {
            string autosave = File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null;
            var standard = new SculptureData { dimensions = BubbleSandbox.DefaultDimensions,
                cellSize = 2, origin = BubbleSandbox.DefaultOrigin };
            var calibration = new Vector3Int(4, 3, 5);
            standard.cells.Add(new SandboxCellData { position = calibration, color = 11, shape = SandboxShape.Bubble });
            if (!world.RestoreWorldJson(JsonUtility.ToJson(standard)) || world.cellSize != 2 ||
                world.dimensions != BubbleSandbox.DefaultDimensions || world.Count != 1 ||
                world.CellCenter(calibration) != new Vector3(-39, 7, -13))
                throw new InvalidDataException("The original 2m lattice did not restore exactly.");
            if (!archive.Load(world, work.Id, out string loadError) || world.cellSize != 1 ||
                world.dimensions != BubbleSandbox.DefaultDimensions * 2 || world.Count != detailed.cells.Count ||
                world.WorldBounds.size != new Vector3(96, 72, 48))
                throw new InvalidDataException(loadError ?? "Fine work did not reload inside the original physical arena.");
            foreach (SandboxCellData cell in detailed.cells)
                if (!world.TryGetCell(cell.position, out SandboxCellData restored) || restored.shape != cell.shape ||
                    restored.PaintColor != cell.PaintColor) throw new InvalidDataException("Fine geometry or color changed during archive loading.");
            if ((autosave == null && File.Exists(world.SavePath)) ||
                (autosave != null && File.ReadAllText(world.SavePath) != autosave))
                throw new InvalidDataException("The rolling autosave changed during sculpture authoring.");
        }
    }
}
#endif
