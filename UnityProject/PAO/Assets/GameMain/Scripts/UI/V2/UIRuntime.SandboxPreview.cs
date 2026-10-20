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
            ClearPages();
            UIPage page = Push("作品预览", "", false, UIPageDesign.Pause);
            page.transform.Find("Page").gameObject.SetActive(false);
            RawImage viewport = ui.Rect("Creation preview viewport", page.transform).gameObject.AddComponent<RawImage>();
            UIWidgets.Stretch(viewport.rectTransform);
            sandboxPreview = viewport.gameObject.AddComponent<SandboxPreviewView>();
            sandboxPreview.Initialize(world);
            UIWorkPreviewTheatre.Create(ui, page, sandboxPreview, viewport, ShowSandboxSaveWork, ShowSandboxArchives, Back);
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
