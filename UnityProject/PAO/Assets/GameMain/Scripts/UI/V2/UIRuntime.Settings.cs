using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private sealed class SettingsSession
        {
            public UISettingsData original, draft;
            public UIPage page;
            public RectTransform body, tabs;
            public int tab, quality, width, height;
            public FullScreenMode mode;
            public bool committed, inputOptions;
        }
        private SettingsSession settingsSession;
        private Action<ControllerControl> controllerRebind;
        private UIPage capturePage;
        private Text captureMessage;
        private float captureDeadline;
        private bool captureNeutral;
        private int navigationResumeFrame;
        private bool previousNavigation;
        private AudioSource audioPreview;

        public void ShowSettings()
        {
            if (settingsSession != null) return;
            var session = new SettingsSession { original = UISettings.Current.Copy(), draft = UISettings.Current.Copy(),
                quality = QualitySettings.GetQualityLevel(), width = Screen.width, height = Screen.height, mode = Screen.fullScreenMode };
            settingsSession = session;
            session.page = Push(T("游戏设置", "Settings"), T("即时预览 · 应用后保存 · 返回撤销未保存的调整", "Live preview · Apply to save · Back discards unsaved changes"), false, UIPageDesign.Settings);
            session.page.BeforeClose = () =>
            {
                CancelBindingCapture(false);
                if (!session.committed)
                {
                    UISettings.Apply(session.original, false);
                    if (QualitySettings.GetQualityLevel() != session.quality) QualitySettings.SetQualityLevel(session.quality);
                    RestoreDisplay(session.width, session.height, session.mode);
                }
                if (settingsSession == session) settingsSession = null;
            };
            // Tabs remain visible while the binding list scrolls.
            ScrollRect scroll = session.page.Content.GetComponentInParent<ScrollRect>();
            session.tabs = ui.Row("Settings tabs", scroll.transform.parent, 10);
            session.tabs.SetSiblingIndex(scroll.transform.GetSiblingIndex());
            UIWidgets.Size(session.tabs, 80).preferredHeight = -1;
            session.body = ui.Card("Settings workbench", session.page.Content);
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                ui.Button(session.tabs, TabName(i), () => { session.tab = index; session.inputOptions = false; RenderSettings(session); });
            }
            ui.Button(session.page.Footer, T("返回", "Back"), Pop);
            ui.Button(session.page.Footer, T("恢复全部默认", "Restore all defaults"), () => Confirm(T("恢复全部默认设置？", "Restore all defaults?"),
                T("键鼠、手柄及其他设置都会重置。应用后才会保存。", "Keyboard, controller and other settings will reset. Apply to save."),
                () => { session.draft = new UISettingsData(); PreviewSettings(session); RenderSettings(session); }));
            ui.Button(session.page.Footer, T("应用", "Apply"), () => ApplySettings(session), true);
            RenderSettings(session); Finish(session.page);
        }

        private static string TabName(int tab)
        {
            string[] zh = { "声音", "画面", "键鼠", "手柄", "辅助", "语言" };
            string[] en = { "Audio", "Video", "Keyboard", "Controller", "Accessibility", "Language" };
            return T(zh[tab], en[tab]);
        }

        private void PreviewSettings(SettingsSession session) { UISettings.Apply(session.draft, false); }

        private void RenderSettings(SettingsSession session, int focusAction = -1)
        {
            if (session != settingsSession || session.page == null) return;
            GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            Selectable[] previousControls = session.body.GetComponentsInChildren<Selectable>();
            int previousIndex = Array.FindIndex(previousControls, item => item.gameObject == focused);
            string previousName = previousIndex >= 0 ? focused.name : null;
            foreach (Transform child in session.body) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            for (int i = 0; i < session.tabs.childCount; i++)
            {
                Transform tab = session.tabs.GetChild(i);
                tab.GetComponent<Image>().color = Color.white;
                tab.GetComponent<UIButtonFeedback>().SetEmphasized(i == session.tab);
                Text label = tab.GetComponentInChildren<Text>(); label.text = TabName(i);
                label.color = ui.Ink;
            }
            var d = session.draft;
            Text[] pageLabels = session.page.GetComponentsInChildren<Text>(true);
            pageLabels[0].text = T("游戏设置", "Settings");
            pageLabels[1].text = T("即时预览 · 应用后保存 · 返回撤销未保存的调整", "Live preview · Apply to save · Back discards unsaved changes");
            string[] footer = { T("返回", "Back"), T("恢复全部默认", "Restore all defaults"), T("应用", "Apply") };
            for (int i = 0; i < session.page.Footer.childCount && i < footer.Length; i++)
                session.page.Footer.GetChild(i).GetComponentInChildren<Text>().text = footer[i];
            Action preview = () => PreviewSettings(session);
            ui.Emblem(session.body, session.tab == 2 || session.tab == 3 ? UIGuideSymbol.Move : UIGuideSymbol.Steer, TabName(session.tab));
            if (session.tab == 0)
            {
                ui.Slider(session.body, T("总音量", "Master volume"), d.master, 0, 1, v => { d.master = v; preview(); });
                ui.Slider(session.body, T("音乐", "Music"), d.music, 0, 1, v => { d.music = v; preview(); });
                ui.Slider(session.body, T("音效", "Sound effects"), d.effects, 0, 1, v => { d.effects = v; preview(); });
                ui.Toggle(session.body, T("后台静音", "Mute when unfocused"), d.muteUnfocused, v => { d.muteUnfocused = v; preview(); });
                ui.Toggle(session.body, T("声音文字提示", "Sound captions"), d.soundCaptions, v => { d.soundCaptions = v; preview(); });
                ui.Button(session.body, T("试听音效", "Test sound"), PlaySettingsSound);
            }
            else if (session.tab == 1)
            {
                string[] modes = { T("窗口", "Windowed"), T("无边框全屏", "Borderless"), T("独占全屏", "Exclusive fullscreen") };
                int mode = d.windowMode >= 0 ? d.windowMode : d.fullscreen ? 1 : 0;
                ui.Button(session.body, T("显示模式", "Display mode") + "  ·  " + modes[mode], () =>
                { d.windowMode = (mode + 1) % 3; d.fullscreen = d.windowMode != 0; RenderSettings(session); });
                string resolution = d.resolutionWidth > 0 ? d.resolutionWidth + " × " + d.resolutionHeight : T("保持当前", "Current");
                ui.Button(session.body, T("分辨率", "Resolution") + "  ·  " + resolution, () => ChooseResolution(session));
                string quality = d.quality < 0 ? T("保持当前", "Current") : QualitySettings.names[d.quality];
                ui.Button(session.body, T("画质", "Quality") + "  ·  " + quality, () =>
                { d.quality = (d.quality + 1) % QualitySettings.names.Length; preview(); RenderSettings(session); });
                ui.Toggle(session.body, T("垂直同步", "V-sync"), d.vSync > 0, v => { d.vSync = v ? 1 : 0; preview(); });
                ui.Button(session.body, T("帧率上限", "Frame limit") + "  ·  " + (d.frameLimit < 0 ? T("不限", "Unlimited") : d.frameLimit.ToString()), () =>
                { int[] rates = { 30, 60, 120, 144, -1 }; d.frameLimit = rates[(Array.IndexOf(rates, d.frameLimit) + 1) % rates.Length]; preview(); RenderSettings(session); });
                ui.Label(session.body, T("垂直同步开启时，帧率由显示器刷新率控制。分辨率与显示模式应用后需要确认，15 秒未确认会还原。",
                    "V-sync uses the display refresh rate. Resolution and display mode require confirmation; after 15 seconds they revert."), 21);
            }
            else if (session.tab == 2 || session.tab == 3)
            {
                bool controller = session.tab == 3;
                RectTransform sections = ui.Row("Input sections", session.body, 12);
                ui.Button(sections, T("操作绑定", "Bindings"), () => { session.inputOptions = false; RenderSettings(session); }, !session.inputOptions);
                ui.Button(sections, T("输入参数", "Input options"), () => { session.inputOptions = true; RenderSettings(session); }, session.inputOptions);
                ui.Button(sections, T("按键示意", "Control layout"), () => ShowInputDiagram(controller));
                if (session.inputOptions && controller)
                {
                    Text status = ui.Label(session.body, "", 21);
                    status.gameObject.AddComponent<UIControllerStatus>();
                    ui.Slider(session.body, T("手柄视角灵敏度", "Controller look sensitivity"), d.controllerSensitivity, 0.3f, 3, v => { d.controllerSensitivity = v; preview(); });
                    ui.Slider(session.body, T("移动摇杆死区", "Movement stick deadzone"), d.moveDeadzone, 0.05f, 0.5f, v => { d.moveDeadzone = v; preview(); });
                    ui.Slider(session.body, T("视角摇杆死区", "Look stick deadzone"), d.lookDeadzone, 0.05f, 0.5f, v => { d.lookDeadzone = v; preview(); });
                    ui.Slider(session.body, T("扳机触发阈值", "Trigger threshold"), d.triggerThreshold, 0.15f, 0.95f, v => { d.triggerThreshold = v; preview(); });
                    ui.Toggle(session.body, T("反转手柄垂直视角", "Invert controller Y"), d.controllerInvertY, v => { d.controllerInvertY = v; preview(); });
                    ui.Toggle(session.body, T("交换左右摇杆", "Swap movement and look sticks"), d.swapSticks, v => { d.swapSticks = v; preview(); });
                    ui.Toggle(session.body, T("手柄单击蓄力，再次单击发射", "Controller toggle charge"), d.controllerToggleCharge, v => { d.controllerToggleCharge = v; preview(); });
                    ui.Label(session.body, T("移动与视角使用摇杆。按钮、十字键与 LT/RT 扳机均可重映射。Menu 始终暂停；菜单内 A 确认、B 返回。",
                        "Sticks control movement and look. Buttons, D-pad and LT/RT triggers can be rebound. Menu always pauses; A confirms and B goes back in menus."), 21);
                }
                else if (session.inputOptions)
                {
                    ui.Slider(session.body, T("鼠标视角灵敏度", "Mouse look sensitivity"), d.sensitivity, 0.3f, 2, v => { d.sensitivity = v; preview(); });
                    ui.Toggle(session.body, T("反转鼠标垂直视角", "Invert mouse Y"), d.invertLook, v => { d.invertLook = v; preview(); });
                    ui.Toggle(session.body, T("单击蓄力，再次单击发射", "Toggle charge"), d.toggleCharge, v => { d.toggleCharge = v; preview(); });
                    ui.Label(session.body, T("点击绑定后按下新按键。鼠标按键也可绑定。Esc 返回、Enter 确认与方向导航始终保留。",
                        "Select a binding, then press a key or mouse button. Esc, Enter and menu navigation remain reserved."), 21);
                }
                else
                {
                    if (controller) ui.Label(session.body, "", 21).gameObject.AddComponent<UIControllerStatus>();
                    ui.Label(session.body, controller ? T("选择按钮改键 · 支持按钮、十字键和扳机 · Menu / Esc 取消", "Select a binding · Buttons, D-pad & triggers · Menu / Esc cancels") :
                        T("选择按键改键 · 支持键盘与鼠标按钮 · Esc 取消", "Select a binding · Keyboard & mouse buttons · Esc cancels"), 21);
                    int[] actions = controller ? new[] { 10, 11, 12, 13, 14, 15, 16, 0, 1, 2, 3, 4, 5 } :
                        new[] { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 0, 1, 2, 3, 4, 5 };
                    foreach (int action in actions)
                    {
                        RectTransform row = ui.Rect("Binding " + action, session.body); row.gameObject.AddComponent<UIBindingRowLayout>();
                        UIWidgets.Size(row, 80).preferredHeight = -1;
                        Text title = ui.Label(row, UIInput.ActionName(action), 24); UIWidgets.Size(title.transform, 72, -1, 1);
                        string binding = controller ? UIInput.ControlName(d.controller[action]) : UIInput.KeyName(d.keys[action]);
                        Button key = ui.Button(row, binding, () => BeginBindingCapture(session, action, controller));
                        key.gameObject.name = "Bind " + action; SizeBindingButton(key, 280);
                        Button reset = ui.Button(row, T("重置", "Reset"), () => ChangeBinding(session, action, controller,
                            UISettingsData.DefaultKeys()[action], UISettingsData.DefaultController()[action]));
                        reset.gameObject.name = "Reset " + action; SizeBindingButton(reset, 160);
                        if (controller && UIInput.CanBindController(action, ControllerControl.None))
                        {
                            Button clear = ui.Button(row, T("清除", "Clear"), () => ChangeBinding(session, action, true, KeyCode.None, ControllerControl.None));
                            clear.gameObject.name = "Clear " + action; SizeBindingButton(clear, 160);
                        }
                    }
                    ui.Label(session.body, controller ? T("固定菜单操作：Menu 暂停 · A 确认 · B 返回 · LB / RB 切换设置页。左右摇杆控制移动与视角。",
                        "Fixed menu controls: Menu pauses · A confirms · B goes back · LB / RB changes tabs. Sticks control movement and look.") :
                        T("固定菜单操作：Esc 返回 · Enter 确认 · 方向键导航。鼠标移动控制视角。",
                        "Fixed menu controls: Esc goes back · Enter confirms · Arrow keys navigate. Mouse movement controls look."), 21);
                }
                ui.Button(session.body, controller ? T("重置手柄绑定", "Reset controller bindings") : T("重置键鼠绑定", "Reset keyboard bindings"), () =>
                    Confirm(controller ? T("重置手柄？", "Reset controller?") : T("重置键鼠？", "Reset keyboard?"),
                    T("恢复本设备的绑定及输入参数，其他设备保持当前配置。应用后保存。", "Restore this device's bindings and input options. Other devices keep their configuration. Apply to save."),
                    () => { if (controller) d.ResetController(); else d.ResetKeyboard(); preview(); RenderSettings(session); }));
            }
            else if (session.tab == 4)
            {
                ui.Slider(session.body, T("文字大小", "Text size"), d.textScale, 1, 1.3f, v => { d.textScale = v; preview(); });
                ui.Slider(session.body, T("界面缩放", "UI scale"), d.uiScale, 0.85f, 1.2f, v => { d.uiScale = v; preview(); });
                ui.Toggle(session.body, T("高对比文字", "High contrast text"), d.highContrast, v => { d.highContrast = v; preview(); });
                string[] colorModes = { T("标准", "Standard"), T("红色觉辅助", "Protanopia"), T("绿色觉辅助", "Deuteranopia"), T("蓝色觉辅助", "Tritanopia") };
                ui.Button(session.body, T("色觉辅助", "Color vision") + "  ·  " + colorModes[d.colorVision], () =>
                { d.colorVision = (d.colorVision + 1) % colorModes.Length; preview(); RenderSettings(session); });
                ui.Toggle(session.body, T("减少动态效果", "Reduced motion"), d.reducedMotion, v => { d.reducedMotion = v; preview(); });
                ui.Toggle(session.body, T("显示字幕", "Subtitles"), d.subtitles, v => { d.subtitles = v; preview(); });
                ui.Slider(session.body, T("字幕背景不透明度", "Subtitle background opacity"), d.subtitleBackground, 0, 1, v => { d.subtitleBackground = v; preview(); });
                Image sample = ui.Surface("Subtitle preview", session.body, new Color(Config.ink.r, Config.ink.g, Config.ink.b, d.subtitleBackground), true);
                UIWidgets.Size(sample.transform, 80);
                sample.gameObject.AddComponent<UISubtitleBackground>().Configure(Config.ink);
                Text subtitle = ui.Label(sample.transform, T("字幕预览：水流正在引领你前行。", "Subtitle preview: the current guides you forward."), 24, Color.white, TextAnchor.MiddleCenter);
                UIWidgets.Stretch(subtitle.rectTransform, 12);
                ui.Toggle(session.body, T("情境教学", "Context tutorials"), d.tutorials, v => { d.tutorials = v; preview(); });
                ui.Toggle(session.body, T("详细文字提示", "Detailed text hints"), d.detailedHints, v => { d.detailedHints = v; preview(); });
                ui.Button(session.body, T("重置已读教学", "Reset tutorials"), () => Confirm(T("重新显示教学？", "Show tutorials again?"),
                    T("后续探索中会再次显示首次提示。", "First-time hints will appear again during exploration."),
                    () => { UIRunState.Progress.tutorialsSeen.Clear(); Toast(T("教学提示已重置", "Tutorial hints reset")); }));
            }
            else
            {
                ui.Label(session.body, T("语言切换即时预览。返回撤销，应用后保存。", "Preview a language immediately. Back cancels; Apply saves."), 24);
                foreach (int language in new[] { 0, 1, 2 })
                {
                    int value = language; string[] names = { "简体中文", "English", "Pseudo-localization" };
                    ui.Button(session.body, names[language], () => { d.language = value; preview(); RenderSettings(session); }, d.language == language);
                }
            }
            session.page.Refresh();
            ScrollRect ownerScroll = session.body.GetComponentInParent<ScrollRect>();
            if (focusAction < 0)
            {
                ownerScroll.verticalNormalizedPosition = 1;
                if (previousIndex >= 0)
                {
                    Selectable[] controls = session.body.GetComponentsInChildren<Selectable>();
                    Selectable target = Array.Find(controls, item => item.name == previousName);
                    if (target == null && controls.Length > 0) target = controls[Mathf.Min(previousIndex, controls.Length - 1)];
                    if (target != null) target.Select();
                }
            }
            else foreach (Button button in session.body.GetComponentsInChildren<Button>())
                if (button.name == "Bind " + focusAction) { button.Select(); break; }
        }

        private static void SizeBindingButton(Button button, float width)
        {
            LayoutElement layout = UIWidgets.Size(button.transform, 80, width, 0);
            layout.layoutPriority = 3; layout.preferredHeight = -1;
        }

        private void ShowInputDiagram(bool controller)
        {
            UIPage page = Push(controller ? T("手柄按键示意", "Controller layout") : T("键鼠按键示意", "Keyboard and mouse layout"), "", true, UIPageDesign.Controls);
            DrawDeviceDiagram(page.Content, controller);
            ui.Label(page.Content, controller ? T("LS / RS 是摇杆按下；LT / RT 是扳机。玩法按键可在手柄页修改，菜单始终使用 A 确认、B 返回、Menu 暂停。",
                "LS / RS are stick presses; LT / RT are triggers. Rebind gameplay controls on the Controller page. Menus always use A to confirm, B to go back and Menu to pause.") :
                T("鼠标控制视角；键盘和鼠标按钮可在键鼠页修改。菜单始终使用 Enter 确认、Esc 返回，方向键或 Tab 导航。",
                "Mouse movement controls look. Rebind keys and mouse buttons on the Keyboard page. Menus always use Enter to confirm, Esc to go back and arrows or Tab to navigate."), 24);
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        private void BeginBindingCapture(SettingsSession session, int action, bool controller)
        {
            CancelBindingCapture(false);
            capturePage = Push(T("设置绑定 · ", "Bind · ") + UIInput.ActionName(action), controller ?
                T("松开所有按钮后，按下新按钮或扳机。Menu / Esc 取消。", "Release all controls, then press a button or trigger. Menu / Esc cancels.") :
                T("松开当前按键后，按下新按键或鼠标按钮。Esc 取消。", "Release the current key, then press a key or mouse button. Esc cancels."), true, UIPageDesign.Controls);
            captureMessage = ui.Label(capturePage.Content, T("等待输入", "Waiting for input"), 28);
            captureDeadline = Time.unscaledTime + 12; captureNeutral = false; rebindOpenedFrame = Time.frameCount;
            if (navigationResumeFrame == 0) previousNavigation = EventSystem.current != null && EventSystem.current.sendNavigationEvents;
            navigationResumeFrame = 0;
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = false;
            if (controller) controllerRebind = value => ChangeBinding(session, action, true, KeyCode.None, value);
            else rebind = value => ChangeBinding(session, action, false, value, ControllerControl.None);
            capturePage.BeforeClose = () => CancelBindingCapture(false);
            ui.Button(capturePage.Footer, T("取消", "Cancel"), () => CancelBindingCapture(true)); Finish(capturePage);
        }

        private bool UpdateBindingCapture()
        {
            if (navigationResumeFrame > 0 && Time.frameCount > navigationResumeFrame && !UIInput.AnyControllerHeld && !Input.GetKey(KeyCode.Return))
            { if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = previousNavigation; navigationResumeFrame = 0; }
            if (capturePage == null) return false;
            if (Input.GetKeyDown(KeyCode.Escape) || UIInput.ControllerDown(ControllerControl.Menu) || Time.unscaledTime >= captureDeadline)
            { CancelBindingCapture(true); return true; }
            captureMessage.text = T("等待输入 · ", "Waiting · ") + Mathf.CeilToInt(captureDeadline - Time.unscaledTime) + " s";
            if (Time.frameCount <= rebindOpenedFrame) return true;
            if (!captureNeutral)
            { captureNeutral = !UIInput.AnyControllerHeld && !Input.anyKey; return true; }
            if (controllerRebind != null)
            {
                foreach (ControllerControl control in Enum.GetValues(typeof(ControllerControl)))
                    if (control != ControllerControl.None && control != ControllerControl.Menu && UIInput.ControllerDown(control))
                    { Action<ControllerControl> callback = controllerRebind; CancelBindingCapture(true); callback(control); return true; }
            }
            else foreach (KeyCode key in keyboardKeys)
            {
                if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6 && PointerOverCaptureButton()) continue;
                if (UIInput.IsBindable(key) && Input.GetKeyDown(key))
                { Action<KeyCode> callback = rebind; CancelBindingCapture(true); callback?.Invoke(key); return true; }
            }
            return true;
        }

        private void CancelBindingCapture(bool close)
        {
            UIPage page = capturePage;
            if (page == null) return;
            capturePage = null; rebind = null; controllerRebind = null; captureMessage = null;
            navigationResumeFrame = Time.frameCount + 1;
            if (close && pages.Count > 0 && pages[pages.Count - 1] == page) Pop();
        }

        private void ChangeBinding(SettingsSession session, int action, bool controller, KeyCode key, ControllerControl control)
        {
            if (session != settingsSession || session.page == null) return;
            if (controller ? !UIInput.CanBindController(action, control) : !UIInput.IsBindable(key)) return;
            int conflict = controller ? control == ControllerControl.None ? -1 : Array.IndexOf(session.draft.controller, control) : Array.IndexOf(session.draft.keys, key);
            if (controller && conflict >= 0 && conflict != action && !UIInput.CanBindController(conflict, session.draft.controller[action]))
            { Toast(T("该操作需要保留绑定，请先为它设置其他按钮。", "That action needs a binding. Assign it another control first.")); return; }
            Action bind = () =>
            {
                if (controller) session.draft.BindController(action, control); else session.draft.BindKeyboard(action, key);
                PreviewSettings(session); RenderSettings(session, action);
            };
            if (conflict >= 0 && conflict != action)
                Confirm(T("绑定已被占用", "Binding already in use"), UIInput.ActionName(conflict) + T("正在使用此按键。确认后交换两项绑定。", " uses this control. Confirm to swap both bindings."), bind);
            else bind();
        }

        private bool PointerOverCaptureButton()
        {
            if (EventSystem.current == null) return false;
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, hits);
            return hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() != null;
        }

        private void ChooseResolution(SettingsSession session)
        {
            UIPage choice = Push(T("选择分辨率", "Choose resolution"), "", true, UIPageDesign.Settings);
            var values = new System.Collections.Generic.List<Vector2Int> { new Vector2Int(Screen.width, Screen.height) };
            foreach (Resolution value in Screen.resolutions)
            {
                Vector2Int size = new Vector2Int(value.width, value.height);
                if (size.x >= 640 && size.y >= 360 && !values.Contains(size)) values.Add(size);
            }
            foreach (Vector2Int value in values)
                ui.Button(choice.Content, value.x + " × " + value.y, () => { session.draft.resolutionWidth = value.x; session.draft.resolutionHeight = value.y; Pop(); RenderSettings(session); });
            ui.Button(choice.Footer, T("返回", "Back"), Pop); Finish(choice);
        }

        private void ApplySettings(SettingsSession session)
        {
            if (settingsSession != session || pages[pages.Count - 1] != session.page) return;
            bool displayChanged = UISettings.DisplayMode(session.draft) != Screen.fullScreenMode ||
                (session.draft.resolutionWidth > 0 && (session.draft.resolutionWidth != Screen.width || session.draft.resolutionHeight != Screen.height));
            if (!displayChanged) { CommitSettings(session); return; }
            int width = Screen.width, height = Screen.height; FullScreenMode mode = Screen.fullScreenMode;
            UISettings.ApplyDisplay(session.draft);
            UIPage confirm = Push(T("保留画面设置？", "Keep display settings?"), "", true, UIPageDesign.Settings);
            Text countdown = ui.Label(confirm.Content, "", 28); bool keep = false;
            confirm.BeforeClose = () =>
            {
                if (keep) return;
                RestoreDisplay(width, height, mode);
                session.draft.resolutionWidth = session.original.resolutionWidth; session.draft.resolutionHeight = session.original.resolutionHeight;
                session.draft.windowMode = session.original.windowMode; session.draft.fullscreen = session.original.fullscreen;
                RenderSettings(session);
            };
            ui.Button(confirm.Footer, T("还原", "Revert"), Pop);
            ui.Button(confirm.Footer, T("保留并保存", "Keep and save"), () => { keep = true; Pop(); CommitSettings(session); }, true);
            Finish(confirm); StartCoroutine(DisplayConfirmation(confirm, countdown));
        }

        private IEnumerator DisplayConfirmation(UIPage page, Text countdown)
        {
            float until = Time.unscaledTime + 15;
            while (page != null && pages.Contains(page))
            {
                countdown.text = T("未确认将在 ", "Reverting in ") + Mathf.Max(0, Mathf.CeilToInt(until - Time.unscaledTime)) + T(" 秒后还原。", " seconds unless confirmed.");
                if (Time.unscaledTime >= until) { if (pages[pages.Count - 1] == page) Pop(); yield break; }
                yield return null;
            }
        }

        private void CommitSettings(SettingsSession session)
        {
            try { UISettings.Commit(session.draft); }
            catch (Exception) { Toast(T("设置保存失败，请重试。", "Settings could not be saved. Please retry.")); return; }
            session.committed = true; Pop(); RefreshVisiblePage(); Toast(T("设置已保存", "Settings saved"));
        }
        private static void RestoreDisplay(int width, int height, FullScreenMode mode)
        { if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode) Screen.SetResolution(width, height, mode); }

        private void PlaySettingsSound()
        {
            if (audioPreview == null)
            {
                audioPreview = gameObject.AddComponent<AudioSource>(); audioPreview.playOnAwake = false; audioPreview.spatialBlend = 0;
                int count = 11025; float[] samples = new float[count];
                for (int i = 0; i < count; i++) samples[i] = Mathf.Sin(i * 2 * Mathf.PI * 440 / 44100) * 0.18f * Mathf.Sin(Mathf.PI * i / count);
                audioPreview.clip = AudioClip.Create("Settings preview", count, 1, 44100, false); audioPreview.clip.SetData(samples, 0);
            }
            audioPreview.volume = UISettings.Current.effects; audioPreview.Play();
            if (UISettings.Current.soundCaptions) Toast(T("音效试听", "Sound preview"));
        }
    }

    public sealed class UIControllerStatus : MonoBehaviour
    {
        private void OnEnable() { UIInput.DeviceChanged += Refresh; UISettings.Changed += Refresh; Refresh(); }
        private void OnDisable() { UIInput.DeviceChanged -= Refresh; UISettings.Changed -= Refresh; }
        private void Refresh()
        {
            GetComponent<Text>().text = UIInput.ControllerConnected ? UISettings.Text("已连接 · ", "Connected · ") + UIInput.ControllerName :
                UISettings.Text("未连接手柄 · 可先调整配置，连接后即可使用", "Controller disconnected · You can configure bindings before connecting");
        }
    }
}
