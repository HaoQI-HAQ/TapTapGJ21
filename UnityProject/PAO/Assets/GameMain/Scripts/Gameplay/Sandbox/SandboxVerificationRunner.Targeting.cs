#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using PAO.UI.V2;
using UnityEngine;

namespace PAO.Sandbox
{
    public sealed partial class SandboxVerificationRunner
    {
        private IEnumerator VerifyUpwardTargeting(SandboxBrush brush)
        {
            string savedWorld = File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null;
            PlayerController movement = brush.GetComponent<PlayerController>();
            CharacterController controller = brush.GetComponent<CharacterController>();
            bool movementEnabled = movement.enabled;
            Vector3 playerPosition = brush.transform.position;
            Camera camera = Camera.main;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            MethodInfo aim = typeof(SandboxBrush).GetMethod("UpdateTarget", BindingFlags.NonPublic | BindingFlags.Instance);
            movement.enabled = false;
            brush.Selection.Clear(); world.ClearWorld();
            brush.SelectTool(SandboxTool.Build);
            if (brush.Paint) brush.TogglePaint();
            while (brush.Diameter != 1) brush.CycleSize();
            PlaceFeet(controller, new Vector3(-7, .15f, -5));
            Vector3Int elevated = new Vector3Int(20, 5, 18);
            world.Build(elevated, 1, SandboxShape.Bubble, 0, false);
            SandboxPiece piece = PieceAt(elevated);
            camera.transform.position = new Vector3(-7, -2, -11);
            camera.transform.LookAt(piece.transform.position);
            Physics.SyncTransforms();
            void PrepareView() => CameraOcclusionParticles.Prepare(new Vector3(-7, 1.8f, -5),
                camera.transform.position, .4f, ~(1 << 2), brush.transform, null);
            try
            {
                float until = Time.realtimeSinceStartup + .4f;
                do
                {
                    PrepareView();
                    yield return null;
                } while (Time.realtimeSinceStartup < until);
                int faded = 0;
                foreach (CameraOcclusionParticles effect in world.GetComponentsInChildren<CameraOcclusionParticles>())
                    if (effect.IsObstructing && effect.Fade > .95f) faded++;
                Check(faded >= 2 && Physics.Raycast(camera.ViewportPointToRay(new Vector3(.5f, .5f)),
                    out RaycastHit solid, 38, ~(1 << 2), QueryTriggerInteraction.Ignore) &&
                    solid.collider.GetComponent<SandboxPiece>() == null,
                    "Low upward view reproduces the dissolved floor and foundation remaining physical ray blockers.");
                aim.Invoke(brush, null);
                Check(brush.HoveredPiece == piece && brush.HasTarget,
                    "Upward crosshair reaches the visible elevated bubble through camera-dissolved floor and foundation.");
                brush.SelectTool(SandboxTool.Select); aim.Invoke(brush, null);
                Check(brush.ToggleTargetSelection() && brush.Selection.Contains(piece),
                    "Selection mode adds the elevated bubble aimed at from below the floor.");
                yield return Capture("creat-upward-selection.png");
                brush.Selection.Clear();
                brush.SelectTool(SandboxTool.Build);
                PrepareView();
                Check(brush.MoveVertical(1) && brush.Tool == SandboxTool.Build,
                    "Q action targets an elevated bubble while retaining build mode.");
                yield return new WaitForSeconds(1.2f);
                Check(piece.Data.position == elevated + Vector3Int.up,
                    "Upward Q action moves the aimed bubble after its travelling bubble arrives.");
                camera.transform.LookAt(piece.transform.position); PrepareView(); aim.Invoke(brush, null);
                Check(brush.MoveVertical(-1), "E action can also target the elevated bubble from below.");
                yield return new WaitForSeconds(1.2f);
                Check(piece.Data.position == elevated, "Upward E action returns the elevated bubble by one cell.");

                PrepareView(); brush.AimUnderfoot();
                Check(brush.HasTarget && brush.HoveredPiece == null && brush.TargetCell.y == 0 &&
                    Mathf.Abs(brush.TargetPoint.y) < .02f,
                    "Underfoot placement still detects solid ground while its camera effect is active.");

                CameraOcclusionParticles.ReleaseAll();
                aim.Invoke(brush, null);
                Check(brush.HoveredPiece == null,
                    "A solid floor still blocks view targeting when it is not dissolved for the camera.");
                CameraOcclusionParticles.Prepare(new Vector3(-7, 1.8f, -5), camera.transform.position,
                    .4f, ~(1 << 2), brush.transform, null);
                Vector3Int blocker = new Vector3Int(20, 2, 12);
                world.Build(blocker, 1, SandboxShape.Block, 3, false);
                Physics.SyncTransforms(); aim.Invoke(brush, null);
                Check(brush.HoveredPiece == PieceAt(blocker),
                    "An ordinary visible foreground block remains the nearest target before an elevated bubble.");

                world.ClearWorld(); CameraOcclusionParticles.ReleaseAll();
                Vector3Int high = new Vector3Int(20, 29, 20);
                world.Build(high, 1, SandboxShape.Bubble, 4, false);
                camera.transform.position = new Vector3(-7, 2, -11);
                camera.transform.LookAt(world.CellCenter(high)); Physics.SyncTransforms();
                brush.SelectTool(SandboxTool.Select); aim.Invoke(brush, null);
                Check(Vector3.Distance(camera.transform.position, world.CellCenter(high)) > 60 &&
                    brush.HoveredPiece == PieceAt(high) && brush.ToggleTargetSelection(),
                    "A high creation more than 60 metres away is selectable within the enlarged sandbox.");
                Check(savedWorld == (File.Exists(world.SavePath) ? File.ReadAllText(world.SavePath) : null),
                    "Upward targeting verification leaves the player's saved creations unchanged.");
            }
            finally
            {
                brush.Selection.Clear(); world.ClearWorld(); CameraOcclusionParticles.ReleaseAll();
                controller.enabled = false; brush.transform.position = playerPosition; controller.enabled = true;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                movement.enabled = movementEnabled; brush.SelectTool(SandboxTool.Build);
                Physics.SyncTransforms();
            }
        }
    }
}
#endif
