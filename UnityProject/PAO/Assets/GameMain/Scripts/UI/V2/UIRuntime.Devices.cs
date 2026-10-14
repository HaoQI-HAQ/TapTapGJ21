using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private float nextDpadNavigation;
        private void UpdateControllerTabs()
        {
            if (pages.Count == 0 || capturePage != null || navigationResumeFrame > 0) return;
            if (settingsSession != null && pages[pages.Count - 1] == settingsSession.page)
            {
                int direction = UIInput.ControllerDown(ControllerControl.LB) ? -1 : UIInput.ControllerDown(ControllerControl.RB) ? 1 : 0;
                if (direction != 0)
                {
                    settingsSession.tab = (settingsSession.tab + direction + 6) % 6;
                    settingsSession.inputOptions = false;
                    RenderSettings(settingsSession);
                    settingsSession.tabs.GetChild(settingsSession.tab).GetComponent<Button>().Select();
                }
            }
            // The legacy UI module handles the left stick; D-pad navigation uses the same UI move events.
            Vector2 move = UIInput.ControllerHeld(ControllerControl.DpadUp) ? Vector2.up :
                UIInput.ControllerHeld(ControllerControl.DpadDown) ? Vector2.down :
                UIInput.ControllerHeld(ControllerControl.DpadLeft) ? Vector2.left :
                UIInput.ControllerHeld(ControllerControl.DpadRight) ? Vector2.right : Vector2.zero;
            bool pressed = UIInput.ControllerDown(ControllerControl.DpadUp) || UIInput.ControllerDown(ControllerControl.DpadDown) ||
                UIInput.ControllerDown(ControllerControl.DpadLeft) || UIInput.ControllerDown(ControllerControl.DpadRight);
            if (move == Vector2.zero) { nextDpadNavigation = 0; return; }
            if (!pressed && Time.unscaledTime < nextDpadNavigation) return;
            nextDpadNavigation = Time.unscaledTime + (pressed ? 0.4f : 0.12f);
            if (EventSystem.current == null) return;
            if (EventSystem.current.currentSelectedGameObject == null) pages[pages.Count - 1].RestoreFocus();
            var data = new AxisEventData(EventSystem.current) { moveVector = move,
                moveDir = move.y > 0 ? MoveDirection.Up : move.y < 0 ? MoveDirection.Down : move.x > 0 ? MoveDirection.Right : MoveDirection.Left };
            ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, data, ExecuteEvents.moveHandler);
        }

        private void HandleDeviceDisconnect()
        {
            if (!UIInput.ConsumeDisconnect()) return;
            CancelBindingCapture(true);
            if (IsGameplay && !IsBlockingGameplay) ShowPause();
            UIPage page = Push(T("手柄已断开", "Controller disconnected"), "", true, UIPageDesign.Controls);
            ui.Label(page.Content, T("游戏保持暂停。重新连接手柄，或使用键鼠继续。你的绑定不会丢失。",
                "The game stays paused. Reconnect your controller or continue with keyboard and mouse. Your bindings are preserved."), 26);
            ui.Button(page.Footer, T("使用键鼠", "Use keyboard"), () => { UIInput.UseKeyboard(); Pop(); });
            ui.Button(page.Footer, T("检查连接", "Check connection"), () =>
            { if (UIInput.ControllerConnected) Pop(); else Toast(T("尚未检测到手柄，请检查连接。", "No controller detected. Please check its connection.")); }, true);
            Finish(page);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (UISettings.Current == null) return;
            AudioListener.pause = UISettings.Current.muteUnfocused && !focused;
        }

        private void DrawDeviceDiagram(Transform parent, bool controller)
        {
            RectTransform row = ui.Grid(controller ? "Controller layout" : "Keyboard layout", parent, 2, 360);
            Image diagram = ui.Surface("Device diagram", row, Config.mist, true);
            UIWidgets.Size(diagram.transform, 138, 430, 0);
            diagram.raycastTarget = false;
            RectTransform drawing = ui.Rect("Fitted device illustration", diagram.transform);
            drawing.anchorMin = drawing.anchorMax = new Vector2(.5f, .5f); drawing.sizeDelta = new Vector2(430, 138);
            drawing.gameObject.AddComponent<UIDeviceDiagramFit>();
            if (controller)
            {
                var outline = ui.Rect("Controller outline", drawing).gameObject.AddComponent<UIControllerOutline>();
                UIWidgets.Stretch(outline.rectTransform, 6); outline.color = Config.text; outline.raycastTarget = false;
                DiagramBadge(drawing, "LB / LT", new Vector2(-135, 48), new Vector2(92, 30));
                DiagramBadge(drawing, "RB / RT", new Vector2(135, 48), new Vector2(92, 30));
                DiagramBadge(drawing, "LS", new Vector2(-110, 12), new Vector2(56, 56), true);
                DiagramBadge(drawing, "RS", new Vector2(60, -34), new Vector2(50, 50), true);
                DiagramBadge(drawing, "Y", new Vector2(139, 14), new Vector2(28, 28), true);
                DiagramBadge(drawing, "X", new Vector2(111, -14), new Vector2(28, 28), true);
                DiagramBadge(drawing, "B", new Vector2(167, -14), new Vector2(28, 28), true);
                DiagramBadge(drawing, "A", new Vector2(139, -42), new Vector2(28, 28), true);
                DiagramBadge(drawing, "+", new Vector2(-57, -37), new Vector2(35, 35));
            }
            else
            {
                DiagramBadge(drawing, UIInput.KeyName(UIInput.Forward), new Vector2(-106, 40), new Vector2(60, 44));
                DiagramBadge(drawing, UIInput.KeyName(UIInput.Left), new Vector2(-176, -14), new Vector2(60, 44));
                DiagramBadge(drawing, UIInput.KeyName(UIInput.Backward), new Vector2(-106, -14), new Vector2(60, 44));
                DiagramBadge(drawing, UIInput.KeyName(UIInput.Right), new Vector2(-36, -14), new Vector2(60, 44));
                DiagramBadge(drawing, UIInput.KeyName(UIInput.Jump), new Vector2(-106, -52), new Vector2(200, 30));
                DiagramBadge(drawing, "Mouse", new Vector2(139, 0), new Vector2(92, 114), true);
            }
            Text description = ui.Label(row, controller ? T("手柄配置\n摇杆控制移动与视角\n按钮绑定独立保存", "Controller configuration\nSticks move and look\nIndependent button bindings") :
                T("键鼠配置\n鼠标控制视角\n键盘与鼠标按键均可修改", "Keyboard and mouse\nMouse controls look\nRebind keys and mouse buttons"), 24);
            UIWidgets.Size(description.transform, 138, -1, 1);
        }
        private void DiagramBadge(Transform parent, string label, Vector2 position, Vector2 size, bool round = false)
        {
            Image badge = round ? ui.Rect(label, parent).gameObject.AddComponent<UICircleImage>() : ui.Surface(label, parent, Config.paper, true);
            badge.color = Config.paper; badge.raycastTarget = false;
            Anchor(badge.rectTransform, new Vector2(0.5f, 0.5f), position, size, new Vector2(0.5f, 0.5f));
            Text text = ui.Label(badge.transform, label, 18, Config.text, TextAnchor.MiddleCenter); UIWidgets.Stretch(text.rectTransform, 2);
        }
    }

    public sealed class UIDeviceDiagramFit : MonoBehaviour
    {
        private void LateUpdate()
        {
            float width = ((RectTransform)transform.parent).rect.width;
            transform.localScale = Vector3.one * Mathf.Min(1, Mathf.Max(0, width - 12) / 430f);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIControllerOutline : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Vector2[] shape = { new Vector2(-0.36f, 0.38f), new Vector2(0.36f, 0.38f), new Vector2(0.44f, 0.14f),
                new Vector2(0.46f, -0.46f), new Vector2(0.29f, -0.44f), new Vector2(0.18f, -0.15f),
                new Vector2(-0.18f, -0.15f), new Vector2(-0.29f, -0.44f), new Vector2(-0.46f, -0.46f), new Vector2(-0.44f, 0.14f) };
            Rect rect = rectTransform.rect;
            helper.AddVert(Vector3.zero, UIDesignPalette.Cream, Vector2.zero);
            foreach (Vector2 point in shape) helper.AddVert(new Vector3(point.x * rect.width, point.y * rect.height), UIDesignPalette.Cream, Vector2.zero);
            for (int i = 0; i < shape.Length; i++) helper.AddTriangle(0, i + 1, (i + 1) % shape.Length + 1);
            for (int i = 0; i < shape.Length; i++)
            {
                Vector2 a = Vector2.Scale(shape[i], rect.size), b = Vector2.Scale(shape[(i + 1) % shape.Length], rect.size);
                UIPearlGraphic.Stroke(helper, a, b, 2, color);
            }
        }
    }
}
