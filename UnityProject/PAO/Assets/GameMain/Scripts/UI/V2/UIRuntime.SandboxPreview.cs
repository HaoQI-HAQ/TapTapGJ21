using PAO.Sandbox;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private SandboxPreviewView sandboxPreview;
        public bool IsSandboxPreview => sandboxPreview != null;

        public void ShowSandboxPreview()
        {
            BubbleSandbox world = BubbleSandbox.Instance;
            if (world == null || sandboxPreview != null || loading) return;
            // Replace sandbox menus with a separate viewport while keeping the usual pause/resume lifecycle.
            ClearPages();
            UIPage page = Push("作品预览", "", false, UIPageDesign.Pause);
            page.GetComponent<Image>().color = Color.white;
            RectTransform body = (RectTransform)page.transform.Find("Page");
            body.gameObject.SetActive(false);
            RawImage viewport = ui.Rect("Creation preview viewport", page.transform).gameObject.AddComponent<RawImage>();
            UIWidgets.Stretch(viewport.rectTransform);
            viewport.rectTransform.offsetMin = new Vector2(0, 106);
            viewport.rectTransform.offsetMax = new Vector2(0, -124);
            viewport.transform.SetAsFirstSibling();
            sandboxPreview = viewport.gameObject.AddComponent<SandboxPreviewView>();
            sandboxPreview.Initialize(world);
            RectTransform header = ui.Rect("Preview header", page.transform);
            header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one;
            header.pivot = Vector2.up; header.sizeDelta = new Vector2(0, 124); header.anchoredPosition = Vector2.zero;
            Text title = SandboxLabel(header, "作品预览  /  " + world.Count + " 件造物", 28,
                new Vector2(28, -15), new Vector2(520, 46));
            title.raycastTarget = false;
            SandboxLabel(header, world.Count == 0 ? "场地还没有造物，返回建造后再来预览。" : "按实际摆放自动取景 · 自由查看整件作品", 18,
                new Vector2(28, -70), new Vector2(560, 32));
            Button save = ui.Button(header, "保存作品", ShowSandboxSaveWork);
            save.name = "Save preview work";
            Anchor((RectTransform)save.transform, Vector2.one, new Vector2(-466, -32), new Vector2(180, 54), Vector2.one);
            FitSandboxActionLabel(save);
            Button archives = ui.Button(header, "作品存档", ShowSandboxArchives);
            archives.name = "Open preview work archives";
            Anchor((RectTransform)archives.transform, Vector2.one, new Vector2(-272, -32), new Vector2(180, 54), Vector2.one);
            FitSandboxActionLabel(archives);
            Button back = ui.Button(header, "Esc  返回建造", Back);
            back.name = "Close creation preview";
            Anchor((RectTransform)back.transform, Vector2.one, new Vector2(-28, -32), new Vector2(230, 54), Vector2.one);
            FitSandboxActionLabel(back);
            RectTransform footer = ui.Rect("Preview controls", page.transform);
            footer.anchorMin = Vector2.zero; footer.anchorMax = Vector2.right;
            footer.pivot = Vector2.zero; footer.sizeDelta = new Vector2(0, 106); footer.anchoredPosition = Vector2.zero;
            string[] labels = { "F  取景全部", "透视", "正面", "侧面", "顶视", "地面 开/关" };
            System.Action[] actions = {
                () => sandboxPreview.FrameAll(), () => sandboxPreview.SetView(35, 24),
                () => sandboxPreview.SetView(0, 0), () => sandboxPreview.SetView(90, 0),
                () => sandboxPreview.SetView(0, 89), () => sandboxPreview.ToggleGrid() };
            for (int i = 0; i < labels.Length; i++)
            {
                Button button = ui.Button(footer, labels[i], actions[i]);
                RectTransform rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2((float)i / labels.Length, 1);
                rect.anchorMax = new Vector2((float)(i + 1) / labels.Length, 1);
                rect.pivot = Vector2.up; rect.offsetMin = new Vector2(12, -54); rect.offsetMax = new Vector2(-12, -6);
                FitSandboxActionLabel(button);
            }
            Text help = ui.Label(footer, "拖动旋转  ·  滚轮缩放  ·  中键 / Shift+拖动平移  ·  F 重新取景", 18, Config.ink, TextAnchor.MiddleCenter);
            help.raycastTarget = false; UIWidgets.Stretch(help.rectTransform);
            help.rectTransform.offsetMin = new Vector2(12, 8); help.rectTransform.offsetMax = new Vector2(-12, -60);
            page.BeforeClose = () =>
            {
                if (sandboxPreview != null) sandboxPreview.Dispose();
                sandboxPreview = null;
            };
            page.Show(); page.SetVisible(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
