using System;
using UnityEngine;

namespace PAO.UI.V2
{
    public enum ControllerControl { None, A, B, X, Y, LB, RB, View, Menu, LeftStick, RightStick, LT, RT, DpadUp, DpadDown, DpadLeft, DpadRight }

    public static class UIInput
    {
        private static readonly KeyCode[] DefaultBindings = UISettingsData.DefaultKeys();
        private static readonly ControllerControl[] DefaultPad = UISettingsData.DefaultController();
        private static readonly KeyCode[] Keyboard = (KeyCode[])Enum.GetValues(typeof(KeyCode));
        public const int Interact = 3, Journal = 4, Help = 5, Forward = 6, Backward = 7, Left = 8, Right = 9,
            Jump = 10, Sprint = 11, Ride = 12, Charge = 13, Detonate = 14, PreviousBubble = 15, NextBubble = 16;
        private static readonly bool[] held = new bool[17], down = new bool[17];
        private static int polledFrame = -1;
        private static float devicePoll;
        private static bool disconnect;
        public static bool ControllerActive { get; private set; }
        public static bool ControllerConnected { get; private set; }
        public static string ControllerName { get; private set; } = "";
        public static event Action DeviceChanged;
        public static bool UsesV2 => UIRuntime.Instance != null && UIRuntime.Instance.IsGameplay;
        public static bool Blocked => UIRuntime.Instance != null && UIRuntime.Instance.BlocksInputThisFrame;
        public static bool AnyControllerHeld { get { Poll(); return Array.Exists(held, value => value); } }
#if UNITY_EDITOR
        internal static Func<ControllerControl, bool> VerificationHeld;
        internal static Func<string, float> VerificationAxis;
        internal static Func<string[]> VerificationDevices;
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Array.Clear(held, 0, held.Length); Array.Clear(down, 0, down.Length);
            polledFrame = -1; devicePoll = 0; disconnect = ControllerActive = ControllerConnected = false;
            ControllerName = ""; DeviceChanged = null;
#if UNITY_EDITOR
            VerificationHeld = null; VerificationAxis = null; VerificationDevices = null;
#endif
        }
        public static void Poll()
        {
            if (polledFrame == Time.frameCount) return;
            polledFrame = Time.frameCount;
            bool padInput = false;
            for (int i = 1; i < held.Length; i++)
            {
                bool value = ReadControl((ControllerControl)i);
                down[i] = value && !held[i]; held[i] = value; padInput |= down[i];
            }
            padInput |= Mathf.Abs(Axis("PAOMoveX")) > 0.45f || Mathf.Abs(Axis("PAOMoveY")) > 0.45f ||
                        Mathf.Abs(Axis("PAOLookX")) > 0.45f || Mathf.Abs(Axis("PAOLookY")) > 0.45f;
            bool keyboardInput = Mathf.Abs(Input.GetAxisRaw("Mouse X")) + Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.2f;
            if (Input.anyKeyDown)
                foreach (KeyCode key in Keyboard)
                    if (key > KeyCode.None && key <= KeyCode.Mouse6 && Input.GetKeyDown(key)) { keyboardInput = true; break; }
            if (padInput) SetDevice(true); else if (keyboardInput) SetDevice(false);
            if (Time.unscaledTime >= devicePoll)
            {
                devicePoll = Time.unscaledTime + 0.5f;
                string[] names = Input.GetJoystickNames();
#if UNITY_EDITOR
                if (VerificationDevices != null) names = VerificationDevices();
#endif
                string name = Array.Find(names, n => !string.IsNullOrWhiteSpace(n)) ?? "";
                bool connected = name.Length > 0;
                if (ControllerConnected && !connected && ControllerActive) disconnect = true;
                if (ControllerConnected != connected || ControllerName != name)
                { ControllerConnected = connected; ControllerName = name; DeviceChanged?.Invoke(); }
            }
        }
        private static void SetDevice(bool pad)
        { if (ControllerActive != pad) { ControllerActive = pad; DeviceChanged?.Invoke(); } }
        public static void UseKeyboard() { SetDevice(false); }
        public static bool ConsumeDisconnect() { bool result = disconnect; disconnect = false; return result; }
        private static float Axis(string name)
        {
#if UNITY_EDITOR
            if (VerificationAxis != null) return VerificationAxis(name);
#endif
            return Input.GetAxisRaw(name);
        }
        private static bool ReadControl(ControllerControl control)
        {
#if UNITY_EDITOR
            if (VerificationHeld != null && control <= ControllerControl.RightStick) return VerificationHeld(control);
#endif
            if (control >= ControllerControl.A && control <= ControllerControl.RightStick)
                return Input.GetKey((KeyCode)((int)KeyCode.JoystickButton0 + (int)control - 1));
            float threshold = UISettings.Current != null ? UISettings.Current.triggerThreshold : 0.5f;
            if (control == ControllerControl.LT) return Axis("PAOTriggerL") > threshold;
            if (control == ControllerControl.RT) return Axis("PAOTriggerR") > threshold;
            if (control == ControllerControl.DpadUp) return Axis("PAODpadY") > 0.5f;
            if (control == ControllerControl.DpadDown) return Axis("PAODpadY") < -0.5f;
            if (control == ControllerControl.DpadLeft) return Axis("PAODpadX") < -0.5f;
            if (control == ControllerControl.DpadRight) return Axis("PAODpadX") > 0.5f;
            return false;
        }
        public static bool ControllerDown(ControllerControl control)
        { Poll(); return control > ControllerControl.None && (int)control < down.Length && down[(int)control]; }
        public static bool ControllerHeld(ControllerControl control)
        { Poll(); return control > ControllerControl.None && (int)control < held.Length && held[(int)control]; }
        public static bool IsBindable(KeyCode key) => Enum.IsDefined(typeof(KeyCode), key) && key > KeyCode.None && key <= KeyCode.Mouse6 &&
            key != KeyCode.Escape && key != KeyCode.Return && key != KeyCode.KeypadEnter && key != KeyCode.Tab &&
            key != KeyCode.UpArrow && key != KeyCode.DownArrow && key != KeyCode.LeftArrow && key != KeyCode.RightArrow;
        public static bool CanBindController(int action, ControllerControl control)
        {
            if (action < 0 || action >= DefaultPad.Length || !Enum.IsDefined(typeof(ControllerControl), control)) return false;
            if (action >= Forward && action <= Right) return control == ControllerControl.None;
            if (control == ControllerControl.Menu) return false;
            return control != ControllerControl.None || action <= Help;
        }
        public static KeyCode Binding(int action) => action < 0 || action >= DefaultBindings.Length ? KeyCode.None :
            UISettings.Current != null && UISettings.Current.keys != null && action < UISettings.Current.keys.Length ? UISettings.Current.keys[action] : DefaultBindings[action];
        public static ControllerControl PadBinding(int action) => action < 0 || action >= DefaultPad.Length ? ControllerControl.None :
            UISettings.Current != null && UISettings.Current.controller != null && action < UISettings.Current.controller.Length ? UISettings.Current.controller[action] : DefaultPad[action];
        public static bool Down(int action, KeyCode legacyController = KeyCode.None) => !Blocked &&
            (Input.GetKeyDown(Binding(action)) || ControllerDown(PadBinding(action)));
        public static bool Held(int action, KeyCode legacyController = KeyCode.None) => !Blocked &&
            (Input.GetKey(Binding(action)) || ControllerHeld(PadBinding(action)));
        public static Vector2 ApplyDeadzone(Vector2 value, float deadzone)
        {
            float magnitude = Mathf.Min(1, value.magnitude);
            return magnitude <= deadzone ? Vector2.zero : value.normalized * ((magnitude - deadzone) / (1 - deadzone));
        }
        public static Vector2 Move()
        {
            if (Blocked) return Vector2.zero;
            float x = (Held(Right) ? 1 : 0) - (Held(Left) ? 1 : 0), y = (Held(Forward) ? 1 : 0) - (Held(Backward) ? 1 : 0);
            bool swap = UISettings.Current != null && UISettings.Current.swapSticks;
            Vector2 stick = new Vector2(Axis(swap ? "PAOLookX" : "PAOMoveX"), Axis(swap ? "PAOLookY" : "PAOMoveY"));
            return Vector2.ClampMagnitude(new Vector2(x, y) + ApplyDeadzone(stick, UISettings.Current?.moveDeadzone ?? 0.19f), 1);
        }
        public static Vector2 Look()
        {
            if (Blocked) return Vector2.zero;
            bool swap = UISettings.Current != null && UISettings.Current.swapSticks;
            Vector2 stick = ApplyDeadzone(new Vector2(Axis(swap ? "PAOMoveX" : "PAOLookX"), Axis(swap ? "PAOMoveY" : "PAOLookY")), UISettings.Current?.lookDeadzone ?? 0.19f);
            if (UISettings.Current != null) { stick *= UISettings.Current.controllerSensitivity; if (UISettings.Current.controllerInvertY) stick.y *= -1; }
            return stick;
        }
        public static bool ToggleCharge => ControllerActive ? UISettings.Current.controllerToggleCharge : UISettings.Current.toggleCharge;
        public static string ControlName(ControllerControl control)
        {
            switch (control)
            {
                case ControllerControl.None: return UISettings.Text("未绑定", "Unbound");
                case ControllerControl.LeftStick: return "LS";
                case ControllerControl.RightStick: return "RS";
                case ControllerControl.DpadUp: return UISettings.Text("方向上", "D-pad Up");
                case ControllerControl.DpadDown: return UISettings.Text("方向下", "D-pad Down");
                case ControllerControl.DpadLeft: return UISettings.Text("方向左", "D-pad Left");
                case ControllerControl.DpadRight: return UISettings.Text("方向右", "D-pad Right");
                default: return control.ToString();
            }
        }
        public static string KeyName(int action) => KeyName(Binding(action));
        public static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Mouse0: return UISettings.Text("鼠标左键", "Left mouse");
                case KeyCode.Mouse1: return UISettings.Text("鼠标右键", "Right mouse");
                case KeyCode.Mouse2: return UISettings.Text("鼠标中键", "Middle mouse");
                case KeyCode.Space: return UISettings.Text("空格", "Space");
                case KeyCode.LeftShift: return "Shift";
            }
            return key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 ? ((int)key - (int)KeyCode.Alpha0).ToString() : key.ToString();
        }
        public static string Prompt(int action) => ControllerActive ? ControlName(PadBinding(action)) : KeyName(action);
        public static string MovementHint() => ControllerActive ? UISettings.Text("摇杆", "Stick") :
            KeyName(Forward) + "/" + KeyName(Left) + "/" + KeyName(Backward) + "/" + KeyName(Right);
        public static string ActionName(int action)
        {
            string[] zh = { "浮粘泡泡", "弹力泡泡", "炸弹泡泡", "情境交互", "意识日志", "教学帮助", "向前移动", "向后移动", "向左移动", "向右移动", "跳跃", "加速", "乘坐与跳出", "蓄力与发射", "引爆泡泡", "上一个泡泡", "下一个泡泡" };
            string[] en = { "Sticky bubble", "Bouncy bubble", "Bomb bubble", "Context interaction", "Journal", "Tutorial help", "Move forward", "Move backward", "Move left", "Move right", "Jump", "Sprint", "Ride & exit bubble", "Charge & launch", "Detonate bubble", "Previous bubble", "Next bubble" };
            return UISettings.Text(zh[action], en[action]);
        }
    }
}
