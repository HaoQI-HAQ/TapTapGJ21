using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        public void ShowConstruction(BuildSite site)
        {
            if (site == null || IsBlockingGameplay) return;
            UIPage page = Push(T("让水流成为路径", "Shape a path through the current"),
                T("意识能量 ", "Consciousness energy ") + UIRunState.Progress.buildTokens);
            if (site.recipes.Length == 0) ui.Label(page.Content, T("这个建构点暂无可用配方。", "No recipes are available at this site."));
            foreach (BuildRecipe recipe in site.recipes)
            {
                if (recipe == null) continue;
                BuildRecipe selected = recipe;
                string reason = site.CanPlace(recipe);
                ui.Label(page.Content, recipe.title + "  ·  " + T("消耗 ", "Cost ") + recipe.cost, 28);
                ui.Label(page.Content, recipe.description, 24);
                if (reason != null) ui.Label(page.Content, reason, 22);
                ui.Button(page.Content, T("预览建构", "Preview construction"), () => ShowBuildPreview(site, selected), false, reason == null);
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop);
            ui.Button(page.Footer, T("拆除并回收", "Remove & recover"), () => Confirm(T("拆除这段路径？", "Remove this path?"),
                T("拆除后返还建构所用的意识能量。", "The consciousness energy used to build it will be returned."),
                () => { if (site.Remove()) { Pop(); Toast(T("建构已拆除", "Construction removed")); } else Toast(T("这里没有可拆除的建构。", "There is no construction to remove.")); }));
            Finish(page);
        }

        private void ShowBuildPreview(BuildSite site, BuildRecipe recipe)
        {
            if (site == null || site.CanPlace(recipe) != null) return;
            UIPage catalogue = pages[pages.Count - 1];
            UIPage page = Push(recipe.title, T("预览不会消耗能量。确认后完成建构。", "Preview uses no energy. Confirm to build."), true);
            catalogue.SetVisible(false);
            RectTransform panel = page.transform.Find("Page") as RectTransform;
            panel.anchorMin = new Vector2(0.63f, 0.08f); panel.anchorMax = new Vector2(0.98f, 0.92f);
            // Wireframe bounds preview avoids executing scripts, physics or audio on the source prefab.
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 2);
            Renderer[] renderers = recipe.prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            }
            GameObject preview = new GameObject("Construction placement preview");
            preview.transform.position = site.placement.position;
            preview.transform.rotation = site.placement.rotation;
            var lines = preview.AddComponent<LineRenderer>();
            lines.useWorldSpace = false; lines.widthMultiplier = 0.04f;
            lines.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            lines.material.color = Config.teal; lines.positionCount = 16;
            Vector3 half = bounds.size * 0.5f;
            Vector3[] corners = {
                new Vector3(-half.x, -half.y, -half.z), new Vector3(half.x, -half.y, -half.z),
                new Vector3(half.x, -half.y, half.z), new Vector3(-half.x, -half.y, half.z),
                new Vector3(-half.x, half.y, -half.z), new Vector3(half.x, half.y, -half.z),
                new Vector3(half.x, half.y, half.z), new Vector3(-half.x, half.y, half.z) };
            int[] path = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };
            for (int i = 0; i < path.Length; i++) lines.SetPosition(i, corners[path[i]]);
            Material material = lines.material;
            page.BeforeClose = () =>
            {
                if (preview != null) Destroy(preview);
                if (material != null) Destroy(material);
                if (catalogue != null) catalogue.SetVisible(true);
            };
            float rotation = 0;
            Text orientation = ui.Label(page.Content, T("方向 ", "Rotation ") + "0°", 28);
            ui.Label(page.Content, T("✓  落点可用", "✓  Valid placement"), 28, Config.teal);
            ui.Button(page.Content, T("旋转 90°", "Rotate 90°"), () =>
            {
                rotation = (rotation + 90) % 360;
                preview.transform.rotation = site.placement.rotation * Quaternion.Euler(0, rotation, 0);
                orientation.text = T("方向 ", "Rotation ") + rotation + "°";
            });
            ui.Button(page.Footer, T("取消", "Cancel"), Pop);
            ui.Button(page.Footer, T("确认建构", "Build"), () =>
            {
                if (site == null) { Pop(); return; }
                string reason = site.CanPlace(recipe);
                if (reason != null) { Toast(reason); return; }
                if (site.Place(recipe, rotation))
                { Pop(); Pop(); Toast(T("路径已建立 · ", "Path created · ") + recipe.description); }
            }, true); Finish(page);
        }
    }
}
